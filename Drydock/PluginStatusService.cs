using Dalamud.Plugin;
using Item = Lumina.Excel.Sheets.Item;
using SubmarineExploration = Lumina.Excel.Sheets.SubmarineExploration;
using SubmarinePart = Lumina.Excel.Sheets.SubmarinePart;
using SubmarineRank = Lumina.Excel.Sheets.SubmarineRank;
using Drydock.Core.Fleet;
using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock;

internal sealed class DalamudPluginHost(IDalamudPluginInterface pi) : IPluginHost
{
    public InstalledPluginInfo? Find(string internalName)
    {
        foreach (var p in pi.InstalledPlugins)
        {
            if (string.Equals(p.InternalName, internalName, StringComparison.Ordinal))
                return new InstalledPluginInfo(p.InternalName, p.IsLoaded, p.Version?.ToString());
        }
        return null;
    }
}

// a sqlite file also counts its -wal, that's where the recent writes land
internal sealed class PluginConfigsProbe(string pluginConfigsDir) : IDataFileProbe
{
    public string PathFor(string relativePath) =>
        Path.Combine(pluginConfigsDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public DateTime? GetLastWriteUtc(string relativePath)
    {
        var path = PathFor(relativePath);
        DateTime? best = null;
        foreach (var p in new[] { path, path + "-wal" })
        {
            try
            {
                if (File.Exists(p))
                {
                    var t = File.GetLastWriteTimeUtc(p);
                    if (best is null || t > best) best = t;
                }
            }
            // the owning plugin can delete or lock it between Exists and the stat
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return best;
    }
}

// Checks run off the game thread. Windows only ever read the last result.
internal sealed class PluginStatusService : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;
    private readonly PluginChecker checker;
    private readonly PluginConfigsProbe probe;
    private readonly HistoryStore history;
    private readonly RefreshGate gate = new();
    private readonly ReadCache<FleetRead> fleetCache = new();
    private readonly ReadCache<AutoRetainerRead> arCache = new();
    private readonly ReadCache<LootRead> lootCache = new();
    private readonly ReadCache<InventoryRead> inventoryCache = new();
    private readonly ReadCache<KitPriceRead> kitPriceCache = new();
    private readonly AutoRetainerIpc arIpc;
    private (SourceKey?, SourceKey?, SourceKey?, SourceKey?, int, string)? historyInputs;
    private bool historyDirty = true;
    private readonly CancellationTokenSource cts = new();
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private Task? worker;
    private Task? background;

    private readonly SnapshotPublisher publisher;
    private int fullPending;
    private HistoryState historyState = new(null, null, null);

    private sealed record HistoryState(IReadOnlyList<HistoryRow>? Rows, string? Error, Calibration? Calibration);

    /// <summary>One picture of everything read and derived; a window reads it once per frame and uses that local.</summary>
    public StatusSnapshot Snapshot => publisher.Current;

    public IReadOnlyList<PluginCheckResult> Results => Snapshot.Inputs.Results;
    public FleetRead? Fleet => Snapshot.Inputs.Fleet;
    public AutoRetainerRead? AutoRetainer => Snapshot.Inputs.AutoRetainer;
    public LootRead? Loot => Snapshot.Inputs.Loot;
    /// <summary>Null unless Allagan Tools is ready and its inventory file was read.</summary>
    public InventoryRead? Inventory => Snapshot.Inputs.Inventory;
    /// <summary>Null unless CashFlow is ready or stale and its purchases were read.</summary>
    public KitPriceRead? KitPrice => Snapshot.Inputs.KitPrice;

    /// <summary>The repair kit price line: where the figure comes from, and why it is the setting when it is.</summary>
    public string KitPriceText => Snapshot.KitPriceText;

    /// <summary>Fires on a worker thread, not the game thread.</summary>
    public event Action? Refreshed;

    public bool RequiredReady => Results.Count > 0 && !Results.Any(r => r.BlocksStartup);

    public PluginStatusService(IDalamudPluginInterface pluginInterface, Configuration configuration)
    {
        pi = pluginInterface;
        config = configuration;
        // GetPluginConfigDirectory() is .../pluginConfigs/Drydock; the others are its siblings.
        probe = new PluginConfigsProbe(Path.GetDirectoryName(pi.GetPluginConfigDirectory().TrimEnd('/', '\\'))!);
        history = new HistoryStore(Path.Combine(pi.GetPluginConfigDirectory(), "history.db"));
        checker = new PluginChecker(new DalamudPluginHost(pi), probe, () => DateTime.UtcNow);
        arIpc = new AutoRetainerIpc(pi, Plugin.Framework);
        publisher = new SnapshotPublisher(CaptureSettings(), () => DateTime.UtcNow, TimeZoneInfo.Local);
        pi.ActivePluginsChanged += OnActivePluginsChanged;
    }

    public string PathFor(PluginRequirement r) => probe.PathFor(r.ConfigPath);

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => Refresh();

    public void Refresh()
    {
        Volatile.Write(ref fullPending, 1);
        Start();
    }

    /// <summary>
    /// A setting changed: rebuilds the derived figures from the reads already held and publishes a new snapshot, without
    /// touching a file. Call it on the thread that changed the setting.
    /// </summary>
    public void Recompute()
    {
        publisher.SetSettings(CaptureSettings());
        Start();
    }

    private ViewSettings CaptureSettings() => new(
        config.ExcludedCharacters.ToHashSet(StringComparer.Ordinal), config.SilentDays, config.LowCeruleum, config.LowRepairKits, config.RunwayDays,
        config.ResendCadenceHours, config.TargetRank, config.GilTarget, config.FuelPrice, config.KitPrice, config.FuelPerSector, config.KitsPerSector,
        config.UseMeasuredRates, config.MilestoneStep);

    private void Start()
    {
        if (cts.IsCancellationRequested || !gate.TryStart()) return;
        var ct = cts.Token;
        worker = Task.Run(() =>
        {
            do
            {
                var full = Interlocked.Exchange(ref fullPending, 0) == 1;
                try
                {
                    if (full) RunOnce(ct);
                    else Publish(null);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Drydock status check failed");
                }
                if (ct.IsCancellationRequested) return;
                if (!full) continue;
                try { Refreshed?.Invoke(); }
                catch (Exception ex) { Plugin.Log.Error(ex, "Drydock refresh handler failed"); }
            }
            while (gate.FinishAndAskAgain());
        });
    }

    // The one place a snapshot is made: the inputs with the update applied and the history as it stands, under the settings of now.
    private void Publish(Func<SourceInputs, SourceInputs>? update)
    {
        publisher.Publish(inputs =>
        {
            if (update is not null) inputs = update(inputs);
            var h = Volatile.Read(ref historyState);
            return inputs with { HistoryRows = h.Rows, HistoryError = h.Error, Calibration = h.Calibration };
        });
    }

    // Disk work the draw thread asked for; Dispose waits for it like for the refresh.
    private Task<string?> Background(Func<string?> work)
    {
        var task = Task.Run(work);
        background = task;
        return task;
    }

    private static SourceKey KeyOf(PluginCheckResult r, long setting) => new(r.DataLastWriteUtc, r.Status, setting);

    // A source is read again only when its file's time, its status or the setting behind it moved.
    private static T? Cached<T>(ReadCache<T> cache, PluginCheckResult r, long setting, Func<T> read) where T : class
    {
        if (!TabGates.Usable(r))
        {
            cache.Clear();
            return null;
        }
        return cache.Get(KeyOf(r, setting), read);
    }

    private void RunOnce(CancellationToken ct)
    {
        var staleDays = Math.Clamp(config.StaleDays, 1, 365);
        var results = checker.CheckAll(TimeSpan.FromDays(staleDays));
        ct.ThrowIfCancellationRequested();
        var st = results.First(r => r.Requirement == PluginRequirements.SubmarineTracker);
        var ar = results.First(r => r.Requirement == PluginRequirements.AutoRetainer);
        var ag = results.First(r => r.Requirement == PluginRequirements.AllaganTools);
        var cf = results.First(r => r.Requirement == PluginRequirements.CashFlow);
        var today = DateOnly.FromDateTime(DateTime.Now);
        // each reader fails on its own: one that throws takes down its own figures and nothing else
        var fleet = Cached(fleetCache, st, staleDays,
            () => Guard(() => SubmarineTrackerDb.Read(PathFor(st.Requirement)), e => new FleetRead(null, e)));
        var arFile = Cached(arCache, ar, staleDays,
            () => Guard(() => AutoRetainerConfig.Read(PathFor(ar.Requirement)), e => new AutoRetainerRead(null, e)));
        ct.ThrowIfCancellationRequested();
        // asked on every refresh, cached file or not: the flag AutoRetainer holds now wins over the one it last wrote
        var arRead = arFile?.Characters is null ? arFile : AutoRetainerConfig.WithLiveExclusions(arFile, arIpc.ReadExclusions(ct));
        var arExcludedKey = string.Join(",", arRead?.Characters?.Where(c => c.ExcludeWorkshop).Select(c => c.Cid) ?? []);
        ct.ThrowIfCancellationRequested();
        var loot = Cached(lootCache, st, staleDays,
            () => Guard(() => SubmarineTrackerDb.ReadLoot(PathFor(st.Requirement), DateTime.UtcNow), e => new LootRead(null, e)));
        var inventory = Cached(inventoryCache, ag, staleDays,
            () => Guard(() => InventoryToolsCsv.Read(PathFor(ag.Requirement)), e => new InventoryRead(null, e)));
        ct.ThrowIfCancellationRequested();
        // the median is taken relative to today, so the day is part of the key
        var kitPrice = Cached(kitPriceCache, cf, today.DayNumber,
            () => Guard(() => CashFlowDb.ReadKitPrice(PathFor(cf.Requirement), DateTime.Now, TimeZoneInfo.Local), e => new KitPriceRead(null, 0, e)));
        ct.ThrowIfCancellationRequested();
        var held = Snapshot.Inputs;
        var salvageNames = held.SalvageNames ?? LoadSalvageNames();
        var (gameData, gameDataError, sectors) = (held.GameData, held.GameDataError, held.Sectors);
        if (gameData is null)
        {
            (gameData, gameDataError) = LoadGameData();
            if (gameData is not null) sectors = gameData.SectorLabels();
        }
        var joined = SnapshotBuilder.Join(fleet, arRead, today, TimeZoneInfo.Local);
        ct.ThrowIfCancellationRequested();
        RecordHistory(results, fleet, loot, arRead, joined, inventory, salvageNames, (fleetCache.Key, lootCache.Key, arCache.Key, inventoryCache.Key, today.DayNumber, arExcludedKey), ct);
        Publish(i => i with
        {
            Results = results,
            Fleet = fleet,
            AutoRetainer = arRead,
            Loot = loot,
            Inventory = inventory,
            KitPrice = kitPrice,
            LastRefreshLocal = DateTime.Now,
            SalvageNames = salvageNames,
            Sectors = sectors,
            GameData = gameData,
            GameDataError = gameDataError,
        });
    }

    /// <summary>Removes every stored day and reads the file again, off the calling thread; the task's result is the error, or null.</summary>
    public Task<string?> DeleteHistoryAsync() => Background(() =>
    {
        lock (historyLock)
        {
            var error = history.DeleteAll();
            historyInputs = null;
            historyDirty = true;
            ReadHistory(error);
            Publish(null);
            return error;
        }
    });

    /// <summary>For a corrupt file or one from a newer Drydock: renames it to history.db.bak and starts a new one, off the calling thread. The task's result is the error, or null.</summary>
    public Task<string?> MoveHistoryAsideAsync() => Background(() =>
    {
        lock (historyLock)
        {
            var error = history.MoveAside();
            historyInputs = null;
            historyDirty = true;
            if (error is null)
            {
                historyRows = null;
                calibration = null;
            }
            ReadHistory(error);
            Publish(null);
            return error;
        }
    });

    /// <summary>What kind of problem the History tab has, so it can offer to move the file aside only when that helps.</summary>
    public HistoryFailure HistoryFailure => history.LastFailure;

    // only touched under historyLock; historyState is what the snapshot reads
    private readonly object historyLock = new();
    private IReadOnlyList<HistoryRow>? historyRows;
    private string? historyError;
    private Calibration? calibration;

    // Today's row after each refresh that read a fresh, non-empty fleet and loot; the last write of the day wins.
    // Failures are kept for the History tab and never reach the game.
    private void RecordHistory(IReadOnlyList<PluginCheckResult> results, FleetRead? fleet, LootRead? loot, AutoRetainerRead? arRead, TrackingRead? joined, InventoryRead? inventory,
        IReadOnlyList<SalvageItem> salvageNames, (SourceKey?, SourceKey?, SourceKey?, SourceKey?, int, string) inputs, CancellationToken ct)
    {
        try
        {
            lock (historyLock)
            {
                ct.ThrowIfCancellationRequested();
                string? writeError = null;
                // the row is a function of these inputs: with none moved there is nothing to build or write
                if (historyInputs == inputs && historyError is null && historyRows is not null) return;
                if (HistoryRecorder.ShouldRecord(results, fleet, loot))
                {
                    // a stale AutoRetainer or Allagan Tools file feeds nothing into today's row
                    var ar = HistoryRecorder.FreshOnly(results, PluginRequirements.AutoRetainer, arRead);
                    var inv = HistoryRecorder.FreshOnly(results, PluginRequirements.AllaganTools, inventory);
                    var tracking = ar?.Characters is null ? null : joined;
                    // characters AutoRetainer excludes from deployables record no hulls, loot or supplies; their gil stays
                    var arExcluded = CharacterFilter.WithAutoRetainer(new HashSet<string>(), arRead?.Characters);
                    SalvageBacklog? backlog = null;
                    // an inventory file whose owner column no longer looks right feeds nothing into the row
                    if (inv?.Rows is { } invRows && ar?.Characters is { } chars && InventoryToolsCsv.OwnersLookRight(invRows, chars.Select(c => c.Cid)))
                        backlog = BacklogMath.Compute(invRows, salvageNames, chars, new HashSet<string>());
                    var row = HistoryRecorder.Build(
                        DateOnly.FromDateTime(DateTime.Now), DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        CharacterFilter.Apply(fleet!.Fleets!, arExcluded), CharacterFilter.Apply(loot!.Rows!, arExcluded),
                        SalvageTable.DefaultPrices, ar?.Characters, tracking, backlog, TimeZoneInfo.Local,
                        (loot.LifetimeVoyages, loot.LifetimeSectors));
                    ct.ThrowIfCancellationRequested();
                    var before = history.Writes;
                    writeError = history.UpsertIfChanged(row);
                    if (writeError is not null)
                        Plugin.Log.Warning("Drydock could not write its history: " + writeError);
                    else if (history.Writes != before) historyDirty = true;
                }
                ReadHistory(writeError);
                if (writeError is null && historyError is null) historyInputs = inputs;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (historyLock)
            {
                historyError = ex.GetType().Name + ": " + ex.Message;
                Volatile.Write(ref historyState, new(historyRows, historyError, calibration));
            }
            Plugin.Log.Error(ex, "Drydock history failed");
        }
    }

    private void ReadHistory(string? earlierError = null)
    {
        if (!historyDirty && earlierError is null && historyError is null && historyRows is not null) return;
        var read = history.ReadAll();
        historyDirty = read.Rows is null;
        historyError = earlierError ?? read.Error;
        if (read.Rows is { } rows)
        {
            historyRows = rows;
            try { calibration = CalibrationMath.Compute(rows); }
            catch (Exception ex)
            {
                calibration = null;
                Plugin.Log.Warning(ex, "Drydock could not calibrate from its history");
            }
        }
        Volatile.Write(ref historyState, new(historyRows, historyError, calibration));
    }

    private static readonly LogOnce readerErrors = new();

    // A reader that keeps throwing is logged once per distinct message, not on every refresh.
    private static T Guard<T>(Func<T> read, Func<string, T> failed)
    {
        try { return read(); }
        catch (Exception ex)
        {
            var message = ex.GetType().Name + ": " + ex.Message;
            if (readerErrors.First(message))
                Plugin.Log.Warning(ex, "Drydock reader failed");
            return failed(message);
        }
    }

    // The game's own item names, with the collector's English ones where the sheet is unavailable.
    private static IReadOnlyList<SalvageItem> LoadSalvageNames()
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (sheet is null) return SalvageTable.Defaults;
            return SalvageTable.Defaults
                .Select(i => sheet.TryGetRow(i.Id, out var row) && row.Name.ToString() is { Length: > 0 } n ? i with { Name = n } : i)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Drydock could not read item names");
            return SalvageTable.Defaults;
        }
    }

    // The three submarine sheets, copied into plain records so Core never sees Lumina. Retried on the next
    // refresh while it fails; the Levelling tab shows the error meanwhile.
    private static (SubmarineGameData? Data, string? Error) LoadGameData()
    {
        try
        {
            var rankSheet = Plugin.DataManager.GetExcelSheet<SubmarineRank>();
            var sectorSheet = Plugin.DataManager.GetExcelSheet<SubmarineExploration>();
            var partSheet = Plugin.DataManager.GetExcelSheet<SubmarinePart>();
            if (rankSheet is null) return (null, "the SubmarineRank sheet is not available");
            if (sectorSheet is null) return (null, "the SubmarineExploration sheet is not available");
            if (partSheet is null) return (null, "the SubmarinePart sheet is not available");

            var ranks = rankSheet
                .Select(r => new RankInfo((int)r.RowId, r.ExpToNext, r.Capacity, r.SurveillanceBonus, r.RetrievalBonus, r.SpeedBonus, r.RangeBonus, r.FavorBonus))
                .ToList();
            var sectors = sectorSheet
                .Select(s => new SectorData(
                    s.RowId, (int)s.Map.RowId, s.Location.ToString(), s.ExpReward, s.SurveyDurationmin, s.X, s.Y, s.Z,
                    s.CeruleumTankReq, s.RankReq, s.StartingPoint))
                .ToList();
            var parts = partSheet
                .Select(p => new PartData(p.RowId, p.Class, p.Slot, p.Surveillance, p.Retrieval, p.Speed, p.Range, p.Favor, p.RepairMaterials))
                .ToList();

            var data = new SubmarineGameData(ranks, sectors, parts);
            if (data.MaxRank == 0) return (null, "the SubmarineRank sheet has no ranks");
            if (data.Sectors.Count == 0) return (null, "the SubmarineExploration sheet is empty");
            return (data, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Drydock could not read the submarine sheets");
            return (null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Stops the worker: no more refreshes, no handler call and no history write once this returns (or after the timeout).</summary>
    public void Dispose()
    {
        pi.ActivePluginsChanged -= OnActivePluginsChanged;
        Refreshed = null;
        cts.Cancel();
        try
        {
            Task[] running = [worker ?? Task.CompletedTask, background ?? Task.CompletedTask];
            if (!Task.WaitAll(running, StopTimeout))
                Plugin.Log.Warning("Drydock's worker did not stop within {0} s", StopTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Drydock's worker ended with an error");
        }
        cts.Dispose();
    }
}
