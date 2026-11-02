using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Drydock.Core.Fleet;
using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Windows;

// Every tab asks Core's TabGates what it may show. A closed gate draws its lines and nothing else, so a source that is
// missing, off, empty, broken or unticked is explained in words and never drawn as a 0.
public sealed class MainWindow : Window
{
    private readonly PluginStatusService status;
    private readonly Action openSetup;
    private readonly Configuration configuration;
    private readonly Action onExclusionsChanged;
    private DateTime lastAutoCheck = DateTime.MinValue;
    private readonly LogOnce loggedDrawErrors = new();

    internal MainWindow(PluginStatusService status, Configuration configuration, Action openSetup, Action openConfig, Action onExclusionsChanged) : base("Drydock###DrydockMain")
    {
        this.status = status;
        this.configuration = configuration;
        this.openSetup = openSetup;
        this.onExclusionsChanged = onExclusionsChanged;
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => openConfig(),
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
        Size = new Vector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // Fixed pixel sizes follow the UI scale; Dalamud scales Size and SizeConstraints itself.
    internal static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public override void OnOpen() => status.Refresh();

    public override void Draw()
    {
        var snap = status.Snapshot;
        // the date rolling over changes what "today" means in every figure; the rebuild reuses the reads
        if (snap.Today != DateOnly.FromDateTime(DateTime.Now)) status.Recompute();

        // subs get resent while the window is open; the countdowns alone don't see that
        if (DateTime.UtcNow - lastAutoCheck > TimeSpan.FromMinutes(1))
        {
            lastAutoCheck = DateTime.UtcNow;
            status.Refresh();
        }

        SafeDraw("status", snap, DrawStatusStrip);
        ImGui.Separator();
        using var tabs = ImRaii.TabBar("drydock-tabs");
        if (!tabs) return;
        Tab("Overview", snap, DrawOverview);
        Tab("Fleet", snap, DrawFleet);
        Tab("Levelling", snap, DrawLevelling);
        Tab("Salvage", snap, DrawSalvage);
        Tab("Gil", snap, DrawGil);
        Tab("History", snap, DrawHistory);
        Tab("Projection", snap, DrawProjection);
        Tab("Tracking", snap, DrawTracking);
    }

    private void Tab(string name, StatusSnapshot snap, Action<StatusSnapshot> draw)
    {
        using var item = ImRaii.TabItem(name);
        if (!item) return;
        SafeDraw(name, snap, draw);
    }

    // A tab that throws shows that and nothing more; the others keep working. Each message is logged once.
    // Every ImGui scope inside a tab is an ImRaii one, so a throw closes it on the way out and the stack stays balanced.
    private void SafeDraw(string name, StatusSnapshot snap, Action<StatusSnapshot> draw)
    {
        try
        {
            draw(snap);
        }
        catch (Exception ex)
        {
            ImGui.TextColored(StatusColors.Red, $"Drydock could not draw this tab: {ex.GetType().Name}: {ex.Message}");
            if (loggedDrawErrors.First(name + ex.Message))
                Plugin.Log.Error(ex, "Drydock tab {0} failed", name);
        }
    }

    /// <summary>Draws the reason when the gate is closed; true when the tab may go on.</summary>
    private static bool DrawGate(TabGate gate)
    {
        if (gate.IsOpen) return true;
        foreach (var line in gate.Lines)
        {
            if (gate.Kind == GateKind.Error) ImGui.TextColored(StatusColors.Red, line);
            else ImGui.TextDisabled(line);
        }
        return false;
    }

    private static void DrawStale(StatusSnapshot snap, params PluginRequirement[] used)
    {
        foreach (var (requirement, note) in snap.StaleNotes)
        {
            if (used.Contains(requirement)) ImGui.TextColored(StatusColors.Amber, note);
        }
    }

    private void DrawOverview(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.SubmarineTracker, PluginRequirements.AllaganTools);
        if (!DrawGate(snap.Gates.Ticked)) return;
        OverviewDraw.Draw(snap.Overview!, GetClock(snap));
    }

    // The countdowns, the back count and the timeline move with the clock: rebuilt once a second and on a new snapshot, not per frame.
    private long tilesVersion = -1;
    private long tilesSecond;
    private OverviewClock? tilesCache;

    private OverviewClock GetClock(StatusSnapshot snap)
    {
        var now = DateTime.UtcNow;
        var second = now.Ticks / TimeSpan.TicksPerSecond;
        if (tilesCache is not null && tilesVersion == snap.Version && tilesSecond == second) return tilesCache;
        tilesCache = OverviewCards.Clock(snap.TickedSubs, snap.Overview!.Sent, now, TimeZoneInfo.Local);
        (tilesVersion, tilesSecond) = (snap.Version, second);
        return tilesCache;
    }

    private void DrawSalvage(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.SubmarineTracker, PluginRequirements.AllaganTools, PluginRequirements.CashFlow);
        if (!DrawGate(snap.Gates.Production)) return;
        if (snap.Economics is not { } e)
        {
            ImGui.TextDisabled("Reading...");
            return;
        }
        if (snap.Inputs.Loot is { HasSectors: false })
            ImGui.TextColored(StatusColors.Amber, "Submarine Tracker's loot has no sector column (an older version): hulls are told apart by rank 85 and the routes table is left out. Update Submarine Tracker for route figures.");

        var g = SalvageMath.FormatGil;
        ImGui.TextUnformatted("Production");
        ImGui.TextDisabled($"Today {g(e.Today)}, yesterday {g(e.Yesterday)}");
        ImGui.TextDisabled($"Per day: {g(e.PerDay30)} over 30 days, {g(e.PerDay7)} over 7 days ({(e.TrendPct >= 0 ? "+" : "")}{e.TrendPct.ToString("0.0", CultureInfo.InvariantCulture)}%)");
        var perHull = e.Model.PerHull > 0 ? g(e.PerHullDay) : "-";
        ImGui.TextDisabled($"Per hull per day {perHull}, {e.VoyagesDay.ToString("0.0", CultureInfo.InvariantCulture)} voyages and {e.SectorsDay.ToString("0.0", CultureInfo.InvariantCulture)} sectors per day");
        if (e.Model.PerHull <= 0)
            ImGui.TextDisabled("No hull is farming a gil route with salvage in the last 30 days, so there is no per hull figure.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Upkeep");
        ImGui.TextDisabled($"Per day {g(e.UpkeepPerDay)}, per sector {g(e.PerSector)}" + (e.PctOfProduction is { } pct ? $", {pct.ToString("0.0", CultureInfo.InvariantCulture)}% of production" : ""));
        ImGui.TextDisabled($"Ceruleum {e.FuelUnitsDay} a day ({g(e.FuelPerDay)}), kits {e.KitUnitsDay.ToString("0.0", CultureInfo.InvariantCulture)} a day ({g(e.KitsPerDay)})");
        ImGui.TextDisabled($"Net per day {g(e.NetPerDay)}");
        ImGui.TextDisabled($"Repair kit price: {snap.KitPriceText}");
        ImGui.TextDisabled($"Ceruleum and kit use per sector: {snap.RatesText}");

        if (e.Routes.Count > 0 && snap.Inputs.Loot is not { HasSectors: false })
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Routes");
            using (var table = ImRaii.Table("salvage-routes", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthStretch, 2);
                    ImGui.TableSetupColumn("Hulls", ImGuiTableColumnFlags.WidthFixed, S(50));
                    ImGui.TableSetupColumn("Voyages 30d", ImGuiTableColumnFlags.WidthFixed, S(95));
                    ImGui.TableSetupColumn("Cycle (h)", ImGuiTableColumnFlags.WidthFixed, S(75));
                    ImGui.TableSetupColumn("Gil/voyage", ImGuiTableColumnFlags.WidthStretch, 2);
                    ImGui.TableSetupColumn("Expected", ImGuiTableColumnFlags.WidthStretch, 2);
                    ImGui.TableSetupColumn("%", ImGuiTableColumnFlags.WidthFixed, S(60));
                    ImGui.TableHeadersRow();
                    foreach (var r in e.Routes)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.Route.Name);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.HullsNow.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.Voyages30.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.CycleHours is { } c ? c.ToString("0.0", CultureInfo.InvariantCulture) : "-");
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.MeasuredGil is { } m ? g(m) : "-");
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(g(r.Route.SheetGil));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.PctOfSheet is { } p ? p.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-");
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Supplies");
        if (snap.SuppliesNote is { } suppliesNote)
        {
            ImGui.TextDisabled(suppliesNote);
        }
        else
        {
            DrawRunway(snap, "Ceruleum", e.CeruleumStock, e.CeruleumRunwayDays);
            DrawRunway(snap, "Repair kits", e.RepairKitStock, e.RepairKitRunwayDays);
        }

        ImGui.Spacing();
        DrawBacklog(snap, g);

        ImGui.Spacing();
        ImGui.TextUnformatted("Last 90 days");
        DrawPlot("##salvage-daily", snap.DailyValues, i => $"{DateLabels.Short(e.Daily[i].Date, snap.Today)}: {g(e.Daily[i].Value)}", true,
            snap.DailyRange, 90);

        if (snap.Inputs.SalvageNames is { } items && ImGui.CollapsingHeader("Salvage prices"))
        {
            foreach (var i in items)
                ImGui.TextDisabled($"{i.Name}: {i.Price:N0}");
        }
    }

    private void DrawBacklog(StatusSnapshot snap, Func<double, string> g)
    {
        ImGui.TextUnformatted("Backlog");
        if (snap.Backlog is not { } b)
        {
            ImGui.TextDisabled(snap.BacklogReason);
            return;
        }

        ImGui.TextDisabled($"Unsold salvage {g(b.TotalValue)}, {b.TotalItems:N0} items in {b.TotalSlots:N0} slots");
        var byCharacterNote = snap.BacklogByCharacterNote;
        if (byCharacterNote is not null)
            ImGui.TextDisabled(byCharacterNote);
        using (var table = ImRaii.Table("salvage-backlog", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Held", ImGuiTableColumnFlags.WidthFixed, S(60));
                ImGui.TableSetupColumn("Unit", ImGuiTableColumnFlags.WidthFixed, S(60));
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Slots", ImGuiTableColumnFlags.WidthFixed, S(50));
                ImGui.TableHeadersRow();
                foreach (var i in b.Items)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(i.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(i.Held.ToString("N0", CultureInfo.InvariantCulture));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(i.Unit.ToString("N0", CultureInfo.InvariantCulture));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(g(i.Value));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(i.Slots.ToString(CultureInfo.InvariantCulture));
                }
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Total");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(b.TotalItems.ToString("N0", CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(g(b.TotalValue));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(b.TotalSlots.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (byCharacterNote is not null || b.Characters.Count == 0) return;
        using var chars = ImRaii.Table("salvage-backlog-chars", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp);
        if (chars)
        {
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("Salvage", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("Slots", ImGuiTableColumnFlags.WidthFixed, S(50));
            ImGui.TableHeadersRow();
            foreach (var c in b.Characters)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(c.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(g(c.Value));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(c.Slots.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private void DrawGil(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.AllaganTools);
        if (!DrawGate(snap.Gates.Gil)) return;

        var h = snap.Holdings!;
        var reason = snap.BacklogReason;
        var g = SalvageMath.FormatGil;

        using (var table = ImRaii.Table("gil-tiles", 3, ImGuiTableFlags.SizingStretchSame))
        {
            if (table)
            {
                Tile(h.NoLiquidData ? "-" : g(h.Liquid), "Liquid",
                    h.NoLiquidData ? "AutoRetainer carries no gil figure" : h.Unread > 0 ? $"{h.Unread} not read" : "characters and retainers");
                Tile(h.SalvageKnown ? g(h.Salvage) : "-", "Unsold salvage", h.SalvageKnown ? null : TabGates.Short(reason));
                Tile(h.LiquidKnown ? g(h.Position) : "-", "Position", !h.LiquidKnown ? "needs the gil figures" : h.SalvageKnown ? null : "gil only");
            }
        }
        if (h.Unread > 0)
            ImGui.TextColored(StatusColors.Amber, $"{h.Unread} of {h.Ticked} ticked characters have no gil or retainer gil in AutoRetainer's file; their rows show \"-\" and the totals leave them out.");
        if (!h.SalvageKnown)
            ImGui.TextDisabled((h.LiquidKnown ? "Position is gil only. " : "") + reason);

        ImGui.Spacing();
        if (h.Rows.Count == 0)
        {
            ImGui.TextDisabled("No ticked character holds any gil or salvage.");
            return;
        }
        using (var table = ImRaii.Table("gil-holdings", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Retainers", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Salvage", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableHeadersRow();
                foreach (var r in h.Rows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.World);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.Gil is { } gil ? g(gil) : "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.RetainerGil is { } rg ? g(rg) : "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(h.SalvageKnown ? g(r.Salvage) : "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.Unread ? "-" : g(r.Total));
                }
            }
        }
    }

    private void DrawProjection(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.SubmarineTracker, PluginRequirements.AllaganTools);
        if (!DrawGate(snap.Gates.Projection)) return;

        var view = snap.Projection!;
        var h = view.Holdings;
        var noGil = view.NoGil;
        var reason = snap.BacklogReason;
        if (h is not null)
        {
            if (h.NoLiquidData)
            {
                ImGui.TextDisabled("Needs gil figures: AutoRetainer's file carries no gil for the ticked characters, so there is no position to start from.");
                return;
            }
            if (h.Unread > 0)
                ImGui.TextColored(StatusColors.Amber, $"The start position leaves out {h.Unread} ticked characters without a gil figure.");
        }

        var plan = view.HasPlan;
        var today = snap.Today;
        var p = view.Result!;
        var g = SalvageMath.FormatGil;

        ImGui.TextUnformatted($"Gil target {g(p.Target)}");
        if (p.TargetReached)
            ImGui.TextColored(StatusColors.Green, "Target reached");
        else if (p.Date is { } hit && p.Days is { } days)
            ImGui.TextUnformatted($"Reached {DateLabels.Short(hit, today)}, in {days} days");
        else
            ImGui.TextColored(StatusColors.Amber, "Not within a year");
        if (noGil is not null)
            ImGui.TextDisabled(noGil.Note + (noGil.Position > 0 ? $" ({g(p.Start)})." : "."));
        else
            ImGui.TextDisabled(h!.SalvageKnown ? $"From a position of {g(p.Start)} (gil and unsold salvage)." : $"From liquid gil {g(p.Start)}. {reason}");
        ImGui.TextDisabled(!plan
            ? "Levelling hulls are assumed to stay levelling: the game's data is not read yet."
            : "Each levelling hull starts farming at its worst-case date from the Levelling tab.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Position over the next year");
        DrawPlot("##projection-curve", view.CurveValues, i => $"{DateLabels.Short(p.Curve[i].Date, today)}: {g(p.Curve[i].Value)}", false,
            view.CurveOverlay, 90, view.CurveDays);

        ImGui.Spacing();
        ImGui.TextUnformatted("Horizons");
        using (var table = ImRaii.Table("projection-horizons", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableHeadersRow();
                foreach (var hz in p.Horizons)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(hz.Label);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(DateLabels.Short(hz.Date, today));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(g(hz.Value));
                }
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Milestones");
        if (p.Milestones.Count == 0)
            ImGui.TextDisabled("None within a year.");
        foreach (var ms in p.Milestones)
            ImGui.TextDisabled($"{g(ms.Value)}: {DateLabels.Short(ms.Date, today)}, in {ms.Days} days" + (ms.Value == p.Target ? " (target)" : ""));

        ImGui.Spacing();
        ImGui.TextUnformatted("Run rate");
        using (var table = ImRaii.Table("projection-rates", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Per day", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Per week", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Per month", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Per year", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableHeadersRow();
                var r = p.Rates;
                foreach (var (label, d, w, mo, y) in new[]
                {
                    ("Now", r.NowDay, r.NowWeek, r.NowMonth, r.NowYear),
                    ("Every hull farming", r.SteadyDay, r.SteadyWeek, r.SteadyMonth, r.SteadyYear),
                })
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(label);
                    foreach (var v in new[] { d, w, mo, y })
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(g(v));
                    }
                }
            }
        }
        ImGui.TextDisabled("Production and upkeep are the 30-day figures from the Salvage tab, net of ceruleum and kits.");
    }

    private bool confirmDeleteHistory;
    private string? historyDeleteError;
    private string? historyAsideError;

    // The delete and the move-aside run off the draw thread; the tab shows "Working..." until the task is done.
    private Task<string?>? historyWork;
    private bool historyWorkIsAside;

    private void StartHistoryWork(bool aside)
    {
        historyWorkIsAside = aside;
        if (aside) historyAsideError = null;
        else historyDeleteError = null;
        historyWork = aside ? status.MoveHistoryAsideAsync() : status.DeleteHistoryAsync();
    }

    private void CollectHistoryWork()
    {
        if (historyWork is not { IsCompleted: true } done) return;
        historyWork = null;
        var error = done.IsFaulted ? done.Exception!.GetBaseException().Message : done.Result;
        if (historyWorkIsAside) historyAsideError = error;
        else historyDeleteError = error;
        status.Refresh();
    }

    private void DrawHistory(StatusSnapshot snap)
    {
        CollectHistoryWork();
        var busy = historyWork is not null;
        if (busy) ImGui.TextDisabled("Working...");
        if (snap.Inputs.HistoryError is { } error)
        {
            ImGui.TextColored(StatusColors.Red, "Could not use Drydock's history: " + error);
            if (!busy && status.HistoryFailure is HistoryFailure.Corrupt or HistoryFailure.NewerSchema && ImGui.Button("Move history.db aside and start a new history"))
                StartHistoryWork(aside: true);
        }
        if (historyAsideError is { } asideError)
            ImGui.TextColored(StatusColors.Red, "Could not move the file: " + asideError);
        if (snap.History is not { } history)
        {
            if (snap.Inputs.HistoryError is null) ImGui.TextDisabled("Reading...");
            return;
        }

        var rows = history.Rows;
        var g = SalvageMath.FormatGil;
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No days recorded yet.");
            if (history.WhyNotRecording is { } why)
                ImGui.TextDisabled(why);
        }
        else
        {
            ImGui.TextUnformatted($"{rows.Count} {(rows.Count == 1 ? "day" : "days")} recorded, since {history.Since}");
            ImGui.TextDisabled("Fleet-wide: the character ticks on the Tracking tab do not apply here.");
            if (rows.Count >= 1 && history.WhyNotRecording is { } paused)
                ImGui.TextColored(StatusColors.Amber, "Not recording right now: " + paused);
        }
        if (rows.Count < 2)
        {
            ImGui.TextDisabled("History starts today; charts fill in from tomorrow.");
        }
        else
        {
            var cal = snap.Inputs.Calibration;
            using (var table = ImRaii.Table("history-tiles", 2, ImGuiTableFlags.SizingStretchSame))
            {
                if (table)
                {
                    Tile(cal?.RealizedPerDay is { } real ? g(real) : "-", "Realized per day",
                        cal?.RealizedDays is { } span ? $"position over {span} days" : "needs two days with a position");
                    var net = snap.ModelledNet;
                    Tile(net is { } n ? g(n) : "-", "Modelled net per day", net is null ? "needs the Salvage tab's inputs" : "30-day production minus upkeep");
                }
            }
            if (cal is not null)
            {
                ImGui.TextDisabled(cal.FuelPerSector is not null || cal.KitsPerSector is not null
                    ? $"Measured per sector: ceruleum {(cal.FuelPerSector is { } f ? f.ToString("0.0000", CultureInfo.InvariantCulture) : "-")}, kits {(cal.KitsPerSector is { } k ? k.ToString("0.0000", CultureInfo.InvariantCulture) : "-")}, over {cal.SectorsSampled:N0} sectors"
                    : $"Use per sector is measured from {CalibrationMath.MinSectors} sectors up; {cal.SectorsSampled:N0} so far");
            }

            ImGui.Spacing();
            foreach (var chart in history.Charts) HistoryChart(chart, snap.Today);
        }

        if (rows.Count > 0)
        {
            ImGui.Spacing();
            if (!confirmDeleteHistory)
            {
                if (!busy && ImGui.Button("Delete history")) confirmDeleteHistory = true;
            }
            else
            {
                ImGui.TextColored(StatusColors.Amber, $"Delete all {rows.Count} recorded days? This cannot be undone.");
                if (!busy && ImGui.Button("Yes, delete"))
                {
                    StartHistoryWork(aside: false);
                    confirmDeleteHistory = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) confirmDeleteHistory = false;
            }
        }
        if (historyDeleteError is { } deleteError)
            ImGui.TextColored(StatusColors.Red, "Could not delete the history: " + deleteError);
    }

    private static void HistoryChart(HistoryChartData chart, DateOnly today)
    {
        ImGui.TextUnformatted(chart.Title);
        if (!chart.Enough)
        {
            ImGui.TextDisabled("Not enough data yet.");
            return;
        }
        var g = SalvageMath.FormatGil;
        DrawPlot($"##history-{chart.Title}", chart.Values, i => $"{DateLabels.Short(chart.Dates[i], today)}: {g(chart.Values[i])}", false,
            chart.Overlay, 70, chart.Days);
    }

    // ImGui's own plots show a raw "83: 7.878e+06" tooltip that cannot be turned off, so the plots are drawn here.
    // The width is the content width, never -1: -1 drew 4 px wide in game.
    // Lines are spaced by date when days is given (one per point, ascending), never by point index.
    private static void DrawPlot(string id, float[] values, Func<int, string> tip, bool bars, string overlay, float height, int[]? days = null)
    {
        var size = new Vector2(Math.Max(ImGui.GetContentRegionAvail().X, S(10f)), S(height));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(ImGuiCol.FrameBg));
        var n = values.Length;
        if (n > 0)
        {
            var lo = bars ? 0f : values.Min();
            var hi = values.Max();
            if (hi <= lo) hi = lo + 1;
            var color = ImGui.GetColorU32(bars ? ImGuiCol.PlotHistogram : ImGuiCol.PlotLines);
            var pad = S(2);
            float Y(float v) => end.Y - pad - (v - lo) / (hi - lo) * (size.Y - 2 * pad);
            if (bars)
            {
                for (var i = 0; i < n; i++)
                {
                    var x0 = origin.X + size.X * i / n;
                    var x1 = origin.X + size.X * (i + 1) / n;
                    if (x1 - x0 > S(3)) x1 -= S(1);
                    dl.AddRectFilled(new Vector2(x0, Y(values[i])), new Vector2(x1, end.Y), color);
                }
            }
            else if (n > 1)
            {
                float X(int i) => days is null
                    ? origin.X + size.X * i / (n - 1)
                    : ChartMath.XAtDay(days[i], days[0], days[^1], origin.X, end.X);
                for (var i = 1; i < n; i++)
                    dl.AddLine(new Vector2(X(i - 1), Y(values[i - 1])), new Vector2(X(i), Y(values[i])), color, S(1.5f));
            }
        }
        dl.AddText(origin + new Vector2(S(4), S(2)), ImGui.GetColorU32(ImGuiCol.Text), overlay);
        if (hovered)
        {
            var idx = bars
                ? ChartMath.IndexAt(ImGui.GetMousePos().X, origin.X, end.X, n)
                : days is not null
                    ? ChartMath.NearestPointByDay(ImGui.GetMousePos().X, origin.X, end.X, days)
                    : ChartMath.NearestPoint(ImGui.GetMousePos().X, origin.X, end.X, n);
            if (idx is { } i) ImGui.SetTooltip(tip(i));
        }
    }

    private static void DrawRunway(StatusSnapshot snap, string label, int? stock, double? days)
    {
        if (stock is not { } s)
        {
            ImGui.TextDisabled($"{label}: unknown, AutoRetainer has no figure for some ticked character");
            return;
        }
        var text = days is { } d ? $"{label}: {s:N0}, {d.ToString("0", CultureInfo.InvariantCulture)} days" : $"{label}: {s:N0}, no runway until use is measured";
        if (days is { } r && r < snap.Settings.RunwayDays) ImGui.TextColored(StatusColors.Red, text);
        else ImGui.TextUnformatted(text);
    }

    private static void Tile(string value, string label, string? extra = null)
    {
        ImGui.TableNextColumn();
        BigFont.Text(value);
        ImGui.TextDisabled(label);
        if (extra is not null) ImGui.TextDisabled(extra);
    }

    private void DrawFleet(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.SubmarineTracker, PluginRequirements.AutoRetainer);
        if (!DrawGate(snap.Gates.Fleet)) return;

        var fleets = snap.TickedFleets!;
        var now = DateTime.UtcNow;
        var all = snap.TickedSubs;
        if (all.Count == 0)
        {
            ImGui.TextDisabled(TabGates.AllUnticked);
            return;
        }
        var farmingCount = snap.FarmingCount;
        ImGui.TextDisabled($"{all.Count} submarines, {all.Count(s => s.ReturnUtc <= now)} back" + (farmingCount is { } fc0 ? $", {fc0} farming" : ", no routes recorded yet"));

        foreach (var fc in fleets)
        {
            var next = fc.NextReturnUtc;
            var sent = snap.SentToday.GetValueOrDefault(fc.CharacterName);
            var sentText = sent is null ? "" : $", {sent} sent today";
            var header = $"{fc.CharacterName} «{fc.Tag}» {fc.World} - {fc.Submarines.Count} subs{sentText}, next back {(next <= now ? "now" : SubmarineTrackerDb.FormatCountdown(next - now))}";
            // the id keeps the header open across frames while its text counts down
            if (!ImGui.CollapsingHeader($"{header}###fc-{fc.CharacterName}-{fc.Tag}-{fc.World}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            using (var table = ImRaii.Table($"fleet-{fc.CharacterName}-{fc.Tag}-{fc.World}", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, S(50));
                    ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthFixed, S(80));
                    ImGui.TableSetupColumn("Returns", ImGuiTableColumnFlags.WidthFixed, S(90));
                    ImGui.TableHeadersRow();

                    foreach (var s in fc.Submarines)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(s.Name);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(s.Rank.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(s.Route.Count == 0 ? "-" : Routes.Label(s.Route, snap.Inputs.Sectors));
                        ImGui.TableNextColumn();
                        if (s.ReturnUtc <= now) ImGui.TextColored(StatusColors.Green, "Back");
                        else ImGui.TextUnformatted(SubmarineTrackerDb.FormatCountdown(s.ReturnUtc - now));
                    }
                }
            }
        }
    }

    private static string Range(long lo, long hi, string format) =>
        lo == hi ? lo.ToString(format, CultureInfo.InvariantCulture) : lo.ToString(format, CultureInfo.InvariantCulture) + "-" + hi.ToString(format, CultureInfo.InvariantCulture);

    private static string Dates(DateOnly best, DateOnly worst, DateOnly today) =>
        best == worst ? DateLabels.Short(best, today) : DateLabels.Short(best, today) + " - " + DateLabels.Short(worst, today);

    private void DrawLevelling(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.SubmarineTracker);
        if (!DrawGate(snap.Gates.Levelling)) return;

        var view = snap.Levelling!;
        var all = snap.TickedSubs;
        var plan = view.Plan;
        var target = plan.TargetRank;
        var today = snap.Today;
        var entries = plan.Entries;
        if (view.FallbackNote is { } expNote)
            ImGui.TextDisabled(expNote);

        if (entries.Count == 0)
        {
            ImGui.TextColored(StatusColors.Green, $"All {all.Count} hulls are on a farming route.");
        }
        else
        {
            ImGui.TextUnformatted($"{entries.Count} hulls not farming" + (view.LastEta is { } last ? $", the last one reaches rank {target} by {DateLabels.Short(last, today)} at the worst" : ""));
            if (view.Unestimated > 0)
                ImGui.TextDisabled($"{view.Unestimated} of them cannot be estimated yet; the table says why.");
            using (var table = ImRaii.Table("levelling-hulls", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, S(45));
                    ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthFixed, S(70));
                    ImGui.TableSetupColumn("Voyage", ImGuiTableColumnFlags.WidthFixed, S(60));
                    ImGui.TableSetupColumn("Cycle", ImGuiTableColumnFlags.WidthFixed, S(115));
                    ImGui.TableSetupColumn("EXP per voyage", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableSetupColumn($"Days to {target}", ImGuiTableColumnFlags.WidthFixed, S(90));
                    ImGui.TableSetupColumn($"Reaches {target}", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableHeadersRow();
                    foreach (var e in entries)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(e.Character);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(e.Hull);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(e.Rank.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(e.RouteLabel.Length == 0 ? "-" : e.RouteLabel);
                        if (e.Estimate is { } est)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(est.VoyageHours.ToString("0.0", CultureInfo.InvariantCulture) + " h");
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(est.CycleHours.ToString("0.0", CultureInfo.InvariantCulture) + " h "
                                + est.CycleSource switch { CycleSource.Measured => "measured", CycleSource.Cadence => "cadence", _ => "voyage" });
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(Range(est.ExpWorst, est.ExpBest, "N0"));
                        }
                        else
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("-");
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("-");
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(e.Problem ?? "-");
                        }
                        ImGui.TableNextColumn();
                        if (e.Typical is { } t)
                        {
                            ImGui.TextUnformatted(Range(t.DaysBest, t.DaysWorst, "0") + " *");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Typical for a sub at this rank that moves to better routes as it levels.\nThe current route on its own would take longer.");
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(Dates(t.EtaBest, t.EtaWorst, today));
                        }
                        else if (e.Estimate is { DaysBest: { } db, DaysWorst: { } dw, EtaBest: { } eb, EtaWorst: { } ew })
                        {
                            ImGui.TextUnformatted(Range(db, dw, "0"));
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(Dates(eb, ew, today));
                        }
                        else
                        {
                            ImGui.TextDisabled("-");
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("-");
                        }
                    }
                }
            }
            ImGui.TextDisabled("Days without * assume the sub stays on its current route; a route change resets the estimate.");
            if (entries.Any(x => x.Typical is not null))
                ImGui.TextDisabled("Days with * are typical for a sub at that rank that moves to better routes as it levels, measured from 68 subs levelled from rank 1.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Rank spread");
        ImGui.TextDisabled($"1-49: {view.Under50}");
        ImGui.TextDisabled($"50-74: {view.From50To74}");
        ImGui.TextDisabled($"75-84: {view.From75To84}");
        ImGui.TextDisabled($"85+: {view.From85}");
    }

    private void DrawTracking(StatusSnapshot snap)
    {
        DrawStale(snap, PluginRequirements.AutoRetainer, PluginRequirements.SubmarineTracker);
        if (!DrawGate(snap.Gates.Tracking)) return;
        var tracking = snap.Tracking!;
        var excluded = configuration.ExcludedCharacters;
        foreach (var note in snap.TrackingNotes)
            ImGui.TextDisabled(note);

        if (tracking.SentToday is { } sentToday) ImGui.TextDisabled($"{sentToday} sent today");
        else ImGui.TextDisabled("AutoRetainer has no voyage record for these characters");
        if (tracking.Characters.All(c => excluded.Contains(c.Key)))
            ImGui.TextColored(StatusColors.Amber, TabGates.AllUnticked);
        using (var table = ImRaii.Table("tracking-characters", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Include", ImGuiTableColumnFlags.WidthFixed, S(65));
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Subs", ImGuiTableColumnFlags.WidthFixed, S(40));
                ImGui.TableSetupColumn("Sent today", ImGuiTableColumnFlags.WidthFixed, S(90));
                ImGui.TableSetupColumn("Days silent", ImGuiTableColumnFlags.WidthFixed, S(90));
                ImGui.TableSetupColumn("Ceruleum", ImGuiTableColumnFlags.WidthFixed, S(75));
                ImGui.TableSetupColumn("Repair kits", ImGuiTableColumnFlags.WidthFixed, S(90));
                ImGui.TableHeadersRow();

                foreach (var c in tracking.Characters)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var included = !excluded.Contains(c.Key);
                    if (ImGui.Checkbox($"##include-{c.Key}", ref included))
                    {
                        if (included) excluded.Remove(c.Key);
                        else excluded.Add(c.Key);
                        configuration.Save();
                        status.Recompute();
                        onExclusionsChanged();
                    }
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.World);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(tracking.HullsKnown ? c.Hulls.ToString() : "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.SentToday?.ToString() ?? "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.DaysSilent switch { null => "-", 0 => "today", var d => d.ToString() });
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.Ceruleum?.ToString() ?? "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(c.RepairKits?.ToString() ?? "-");
                }
            }
        }

        if (tracking.Phantoms.Count > 0 || snap.OldCompanies.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Ignored entries");
            foreach (var p in tracking.Phantoms)
                ImGui.TextDisabled($"{p.Character}: {p.Entry} (in AutoRetainer, not in Submarine Tracker)");
            foreach (var o in snap.OldCompanies)
                ImGui.TextDisabled($"{o.Character}: {(o.Tag.Length > 0 ? "FC <" + o.Tag + ">" : "an old FC")} with {o.Submarines} sub{(o.Submarines == 1 ? "" : "s")} (Submarine Tracker remembers it, the character is in another FC now; its loot still counts)");
        }
    }

    private void DrawStatusStrip(StatusSnapshot snap)
    {
        foreach (var r in snap.Results)
        {
            ImGui.TextColored(StatusColors.For(r), "●");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{r.Requirement.DisplayName}: {StatusColors.Label(r.Status)}");
            ImGui.SameLine();
            ImGui.TextUnformatted(r.Requirement.DisplayName);
            ImGui.SameLine();
        }

        ImGui.TextDisabled(snap.RefreshedText);
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) status.Refresh();

        if (snap.NeedsText is { } needs)
        {
            ImGui.TextColored(StatusColors.Red, needs);
            ImGui.SameLine();
            if (ImGui.Button("Open setup")) openSetup();
        }
    }
}
