using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <summary>The settings the derived figures depend on, copied out of the configuration so a worker never reads a list the draw thread edits.</summary>
public sealed record ViewSettings(
    IReadOnlySet<string> Excluded,
    int SilentDays,
    int LowCeruleum,
    int LowRepairKits,
    int RunwayDays,
    int ResendCadenceHours,
    int TargetRank,
    long GilTarget,
    int FuelPrice,
    int KitPrice,
    double FuelPerSector,
    double KitsPerSector,
    bool UseMeasuredRates,
    long MilestoneStep = ProjectionMath.DefaultMilestoneStep)
{
    public WarningThresholds Thresholds => new(SilentDays, LowCeruleum, LowRepairKits, RunwayDays);
}

/// <summary>Everything the sources gave, as read; the derived figures are built from this and the settings, never the other way round.</summary>
public sealed record SourceInputs(
    IReadOnlyList<PluginCheckResult> Results,
    FleetRead? Fleet,
    AutoRetainerRead? AutoRetainer,
    LootRead? Loot,
    InventoryRead? Inventory,
    KitPriceRead? KitPrice,
    DateTime? LastRefreshLocal,
    IReadOnlyList<SalvageItem>? SalvageNames,
    IReadOnlyDictionary<uint, SectorInfo> Sectors,
    SubmarineGameData? GameData,
    string? GameDataError,
    IReadOnlyList<HistoryRow>? HistoryRows,
    string? HistoryError,
    Calibration? Calibration)
{
    public static SourceInputs Empty { get; } = new([], null, null, null, null, null, null, null, Routes.DefaultSectors, null, null, null, null, null);
}

/// <summary>What each tab's gate says, decided once per snapshot.</summary>
public sealed record SnapshotGates(
    TabGate Fleet,
    TabGate Tracking,
    TabGate Ticked,
    TabGate Loot,
    TabGate Production,
    TabGate Projection,
    TabGate Gil,
    TabGate Levelling);

public sealed record LevellingView(
    LevellingPlan Plan,
    int ExpUnknown,
    int Unestimated,
    DateOnly? LastEta,
    string? FallbackNote,
    int Under50,
    int From50To74,
    int From75To84,
    int From85);

/// <param name="Holdings">Null without AutoRetainer.</param>
/// <param name="NoGil">Where the start comes from when AutoRetainer's gil cannot be read.</param>
/// <param name="Result">Null when the start position has no gil figure to stand on.</param>
public sealed record ProjectionView(
    HoldingsSummary? Holdings,
    ProjectionStartView? NoGil,
    bool HasPlan,
    ProjectionResult? Result,
    float[] CurveValues,
    int[] CurveDays,
    string CurveOverlay);

/// <param name="Values">Empty below two points, which the tab says as "Not enough data yet".</param>
public sealed record HistoryChartData(string Title, DateOnly[] Dates, float[] Values, int[] Days, string Overlay)
{
    public bool Enough => Values.Length >= 2;
}

public sealed record HistoryView(
    IReadOnlyList<HistoryRow> Rows,
    string? Since,
    string? WhyNotRecording,
    IReadOnlyList<HistoryChartData> Charts);

// everything a frame draws, built on the worker and swapped in whole so a frame never mixes old and new
public sealed record StatusSnapshot(long Version, DateOnly Today, SourceInputs Inputs, ViewSettings Settings, SourceReads Reads)
{
    public IReadOnlyList<PluginCheckResult> Results => Inputs.Results;

    public SnapshotGates Gates { get; init; } = null!;

    public IReadOnlyList<(PluginRequirement Requirement, string Note)> StaleNotes { get; init; } = [];
    public string RefreshedText { get; init; } = "";
    public string? NeedsText { get; init; }

    /// <summary>The ticked characters' companies; null until the fleet was read.</summary>
    public IReadOnlyList<FreeCompanyFleet>? TickedFleets { get; init; }
    public IReadOnlyList<SubmarineRow> TickedSubs { get; init; } = [];
    public int? FarmingCount { get; init; }
    public IReadOnlyDictionary<string, int?> SentToday { get; init; } = new Dictionary<string, int?>();

    public RateChoice Rates { get; init; } = null!;
    public string RatesText { get; init; } = "";
    public KitPriceChoice KitPriceChoice { get; init; } = null!;
    public string KitPriceText { get; init; } = "";

    public SalvageEconomics? Economics { get; init; }
    /// <summary>The economics the Overview shows: null behind a closed loot gate.</summary>
    public SalvageEconomics? TileEconomics { get; init; }
    public string? TileEconomicsReason { get; init; }
    public float[] DailyValues { get; init; } = [];
    public string DailyRange { get; init; } = "";
    public long? ModelledNet { get; init; }
    public IReadOnlyList<FleetWarning>? Warnings { get; init; }

    public SalvageBacklog? Backlog { get; init; }
    public string? BacklogReason { get; init; }
    public string? SuppliesNote { get; init; }
    public string? BacklogByCharacterNote { get; init; }
    public IReadOnlyList<string> TrackingNotes { get; init; } = [];

    public HoldingsSummary? Holdings { get; init; }
    public LevellingView? Levelling { get; init; }
    public ProjectionView? Projection { get; init; }
    public HistoryView? History { get; init; }
    /// <summary>Null behind a closed Ticked gate, where the Overview draws the gate instead.</summary>
    public OverviewView? Overview { get; init; }

    public TrackingRead? Tracking => Reads.Tracking;
}

// pure on purpose: a recompute from the cached reads has to land on the same numbers as a fresh read
public static class SnapshotBuilder
{
    public static TrackingRead? Join(FleetRead? fleet, AutoRetainerRead? autoRetainer, DateOnly today, TimeZoneInfo zone) =>
        autoRetainer?.Characters is { } characters
            ? FleetTracking.JoinAll(fleet?.Fleets, characters, FleetTracking.DayKey(today, zone))
            : null;

    public static StatusSnapshot Build(SourceInputs inputs, ViewSettings s, long version, DateTime nowUtc, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        var excluded = s.Excluded;
        var results = inputs.Results;
        var tracking = Join(inputs.Fleet, inputs.AutoRetainer, today, zone);
        var reads = new SourceReads(results, inputs.Fleet, inputs.AutoRetainer, inputs.Loot, tracking);

        var plan = Plan(inputs, s, today, nowUtc);
        var rates = RateChoice.Choose(inputs.Calibration, s.UseMeasuredRates, s.FuelPerSector, s.KitsPerSector);
        var kitChoice = KitPriceChoice.Choose(inputs.KitPrice, s.KitPrice);
        var economics = Economics(inputs, tracking, s, rates, kitChoice.Price, today, zone);

        var gates = new SnapshotGates(
            TabGates.Fleet(reads),
            TabGates.Tracking(reads),
            TabGates.Ticked(reads, excluded),
            TabGates.Loot(reads, excluded),
            TabGates.Production(reads, excluded),
            TabGates.Projection(reads, excluded, economics),
            TabGates.Gil(reads, excluded),
            TabGates.Levelling(reads, excluded, inputs.GameData is not null, inputs.GameDataError));

        var tileEconomics = gates.Loot.IsOpen ? economics : null;
        var backlogReason = TabGates.BacklogReason(results, inputs.Inventory, inputs.AutoRetainer);
        SalvageBacklog? backlog = null;
        if (backlogReason is null)
        {
            // no AutoRetainer: every owner falls into the "Other" group and the ticks cannot apply
            backlog = BacklogMath.Compute(inputs.Inventory!.Rows!, inputs.SalvageNames ?? SalvageTable.Defaults, inputs.AutoRetainer?.Characters ?? [], excluded);
        }
        var holdings = inputs.AutoRetainer?.Characters is { } characters ? HoldingsMath.Compute(characters, backlog, excluded) : null;

        var ticked = inputs.Fleet?.Fleets is { } fleets ? CharacterFilter.Apply(fleets, excluded) : null;
        var subs = ticked is null ? [] : ticked.SelectMany(f => f.Submarines).ToList();
        var sent = new Dictionary<string, int?>(StringComparer.Ordinal);
        if (tracking is not null)
        {
            foreach (var c in tracking.Characters) sent.TryAdd(c.Name, c.SentToday);
        }

        var stale = new List<(PluginRequirement, string)>();
        foreach (var r in results)
        {
            if (TabGates.StaleNotes([r], r.Requirement) is [var note]) stale.Add((r.Requirement, note));
        }
        var problems = results.Where(r => r.BlocksStartup).ToList();

        var g = SalvageMath.FormatGil;
        var farmingCount = OverviewTiles.FarmingCount(subs);
        var warnings = gates.Ticked.IsOpen ? FleetWarnings.Compute(tracking!, s.Thresholds, excluded, tileEconomics) : null;
        var projection = gates.Projection.IsOpen ? ProjectionOf(reads, s, economics!, holdings, backlog, plan, today, g) : null;
        var tileReason = TabGates.TileReason(gates.Loot) ?? (tileEconomics is null ? "reading" : null);
        var suppliesNote = TabGates.SuppliesNote(reads);
        return new StatusSnapshot(version, today, inputs, s, reads)
        {
            Gates = gates,
            StaleNotes = stale,
            RefreshedText = inputs.LastRefreshLocal is { } last ? $"refreshed {last:HH:mm:ss}" : "not refreshed",
            NeedsText = problems.Count > 0 ? "Needs: " + string.Join(", ", problems.Select(p => p.Requirement.DisplayName)) : null,
            TickedFleets = ticked,
            TickedSubs = subs,
            FarmingCount = farmingCount,
            SentToday = sent,
            Rates = rates,
            RatesText = rates.Text,
            KitPriceChoice = kitChoice,
            KitPriceText = KitPriceChoice.Explain(inputs.KitPrice, TabGates.Usable(TabGates.Result(results, PluginRequirements.CashFlow)), s.KitPrice),
            Economics = economics,
            TileEconomics = tileEconomics,
            TileEconomicsReason = tileReason,
            DailyValues = economics?.Daily.Select(d => (float)d.Value).ToArray() ?? [],
            DailyRange = economics is { Daily: { Count: > 0 } daily } ? DateLabels.Range(daily[0].Date, daily[^1].Date, today) : "",
            ModelledNet = gates.Production.IsOpen ? economics?.NetPerDay : null,
            Warnings = warnings,
            Backlog = backlog,
            BacklogReason = backlogReason,
            SuppliesNote = suppliesNote,
            BacklogByCharacterNote = TabGates.BacklogByCharacterNote(results),
            TrackingNotes = TabGates.TrackingNotes(results),
            Holdings = holdings,
            Levelling = plan is null ? null : LevellingOf(plan, results, subs),
            Projection = projection,
            History = HistoryOf(inputs, today, g),
            Overview = gates.Ticked.IsOpen
                ? OverviewCards.Build(subs, farmingCount, tracking, s, tileEconomics, tileReason, gates.Gil, holdings, backlogReason, projection?.Result, suppliesNote, warnings, today)
                : null,
        };
    }

    private static LevellingPlan? Plan(SourceInputs inputs, ViewSettings s, DateOnly today, DateTime nowUtc)
    {
        if (inputs.GameData is not { } data || inputs.Fleet?.Fleets is not { } fleets) return null;
        return LevellingPlanner.Compute(
            CharacterFilter.Apply(fleets, s.Excluded), inputs.AutoRetainer?.Characters ?? [], data, today, s.TargetRank, s.ResendCadenceHours,
            inputs.Loot?.Rows is { } rows ? CharacterFilter.Apply(rows, s.Excluded) : null,
            nowUtc,
            labels: inputs.Sectors);
    }

    private static SalvageEconomics? Economics(SourceInputs inputs, TrackingRead? tracking, ViewSettings s, RateChoice rates, int kitPrice, DateOnly today, TimeZoneInfo zone)
    {
        if (inputs.Loot is not { Rows: { } loot } lootRead || inputs.Fleet?.Fleets is not { } fleets)
            return null;
        // null tracking (no AutoRetainer): the stock and runway are then unknown, everything else comes from the loot
        return SalvageMath.ComputeFor(
            loot,
            lootRead.HasSectors,
            fleets,
            tracking,
            s.Excluded,
            new EconomicsSettings(rates.FuelPerSector, rates.KitsPerSector, s.FuelPrice, kitPrice, s.RunwayDays),
            today,
            zone);
    }

    private static LevellingView LevellingOf(LevellingPlan plan, IReadOnlyList<PluginCheckResult> results, IReadOnlyList<SubmarineRow> subs)
    {
        var entries = plan.Entries;
        var expUnknown = entries.Count(x => x.ExpUnknown);
        var late = entries.Where(e => e.Estimate?.EtaWorst is not null).Select(e => e.Estimate!.EtaWorst!.Value).ToList();
        return new LevellingView(
            plan,
            expUnknown,
            entries.Count(e => e.Estimate is null),
            late.Count > 0 ? late.Max() : null,
            TabGates.LevellingFallbackNote(results, expUnknown),
            subs.Count(x => x.Rank < 50),
            subs.Count(x => x.Rank is >= 50 and < 75),
            subs.Count(x => x.Rank is >= 75 and < 85),
            subs.Count(x => x.Rank >= 85));
    }

    private static ProjectionView ProjectionOf(
        SourceReads reads, ViewSettings s, SalvageEconomics e, HoldingsSummary? holdings, SalvageBacklog? backlog, LevellingPlan? plan, DateOnly today,
        Func<double, string> g)
    {
        // AutoRetainer carries the gil; without it the projection starts from unsold salvage (Allagan Tools) or from 0
        ProjectionStartView? noGil = null;
        long position;
        if (holdings is not null)
        {
            if (holdings.NoLiquidData) return new(holdings, null, plan is not null, null, [], [], "");
            position = holdings.Position;
        }
        else
        {
            noGil = ProjectionStart.WithoutGil(backlog);
            position = noGil.Position;
        }

        var subs = CharacterFilter.Apply(reads.Fleet!.Fleets!, s.Excluded).SelectMany(f => f.Submarines).ToList();
        var farming = subs.Count(x => x.RouteClass == RouteClass.Farming);
        // worst-case ETA on purpose. if it's off, the hull starts farming early, which is the better way to be wrong.
        // TODO: maybe draw the best case as a second line once the ranges have a week of history to check against
        var etas = plan is not null
            ? plan.Entries.Select(x => x.Estimate?.DaysWorst is { } d ? (double)d : double.PositiveInfinity).ToList()
            : Enumerable.Repeat(double.PositiveInfinity, subs.Count - farming).ToList();
        var m = e.Model;
        var result = ProjectionMath.Compute(
            position, farming, etas, m.PerHull, m.SecPerEnd, m.SecPerLvl, m.CostPerSector,
            farming + etas.Count, m.PerDay, m.UpkeepDay, s.GilTarget, today, s.MilestoneStep);

        var values = result.Curve.Select(c => (float)c.Value).ToArray();
        var overlay = $"{DateLabels.Range(result.Curve[0].Date, result.Curve[^1].Date, today)}, {g(values.Min())} to {g(values.Max())}";
        return new(holdings, noGil, plan is not null, result, values, result.Curve.Select(c => c.Date.DayNumber).ToArray(), overlay);
    }

    private static HistoryView? HistoryOf(SourceInputs inputs, DateOnly today, Func<double, string> g)
    {
        if (inputs.HistoryRows is not { } rows) return null;
        var why = HistoryRecorder.WhyNotRecording(inputs.Results, inputs.Fleet, inputs.Loot);
        var charts = new List<HistoryChartData>();
        if (rows.Count >= 2)
        {
            var range = DateLabels.Range(rows[0].DateValue, rows[^1].DateValue, today);
            if (rows.All(r => r.Position.HasValue))
                charts.Add(Chart("Position (gil and unsold salvage)", rows.Select(r => (r.DateValue, (float)r.Position!.Value)), range, g));
            else
                charts.Add(Chart("Liquid gil (position needs Allagan Tools on every day)", rows.Where(r => r.Liquid.HasValue).Select(r => (r.DateValue, (float)r.Liquid!.Value)), range, g));
            charts.Add(Chart("Hulls farming", rows.Select(r => (r.DateValue, (float)r.Farming)), range, g));
            charts.Add(Chart("Ceruleum stock", rows.Where(r => r.Ceruleum.HasValue).Select(r => (r.DateValue, (float)r.Ceruleum!.Value)), range, g));
            charts.Add(Chart("Repair kit stock", rows.Where(r => r.Kits.HasValue).Select(r => (r.DateValue, (float)r.Kits!.Value)), range, g));
            charts.Add(Chart("Salvage per day", rows.Select(r => (r.DateValue, (float)r.SalvageDay)), range, g));
        }
        return new(rows, rows.Count > 0 ? DateLabels.Short(rows[0].DateValue, today) : null, why, charts);
    }

    private static HistoryChartData Chart(string title, IEnumerable<(DateOnly Date, float Value)> series, string range, Func<double, string> g)
    {
        var points = series.ToArray();
        if (points.Length < 2) return new(title, [], [], [], "");
        var values = points.Select(p => p.Value).ToArray();
        return new(title, points.Select(p => p.Date).ToArray(), values, points.Select(p => p.Date.DayNumber).ToArray(), $"{range}, {g(values.Min())} to {g(values.Max())}");
    }
}

/// <summary>Holds the current snapshot and publishes the next with one reference assignment; the version only ever goes up.</summary>
public sealed class SnapshotPublisher
{
    private readonly object gate = new();
    private readonly Func<DateTime> utcNow;
    private readonly TimeZoneInfo zone;
    private StatusSnapshot snapshot;
    private ViewSettings settings;

    public SnapshotPublisher(ViewSettings settings, Func<DateTime> utcNow, TimeZoneInfo zone)
    {
        this.settings = settings;
        this.utcNow = utcNow;
        this.zone = zone;
        snapshot = SnapshotBuilder.Build(SourceInputs.Empty, settings, 0, utcNow(), zone);
    }

    public StatusSnapshot Current => Volatile.Read(ref snapshot);

    public void SetSettings(ViewSettings s) => Volatile.Write(ref settings, s);

    /// <summary>Derives a snapshot from the held inputs (changed by <paramref name="update"/> when given) and the latest settings.</summary>
    public StatusSnapshot Publish(Func<SourceInputs, SourceInputs>? update = null)
    {
        lock (gate)
        {
            var inputs = snapshot.Inputs;
            if (update is not null) inputs = update(inputs);
            var next = SnapshotBuilder.Build(inputs, Volatile.Read(ref settings), snapshot.Version + 1, utcNow(), zone);
            Volatile.Write(ref snapshot, next);
            return next;
        }
    }
}
