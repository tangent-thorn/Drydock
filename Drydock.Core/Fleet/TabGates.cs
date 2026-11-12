using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public enum GateKind
{
    /// <summary>The tab has what it needs.</summary>
    Open,
    /// <summary>A refresh has not landed yet.</summary>
    Reading,
    /// <summary>A plugin is missing, off, or has written nothing usable.</summary>
    Needs,
    /// <summary>A source exists but could not be read.</summary>
    Error,
    /// <summary>The sources were read and hold nothing to show (no characters, no submarines, no loot, everyone unticked).</summary>
    Empty,
}

/// <summary>Why a tab shows lines of explanation instead of numbers; Open means it can show them.</summary>
public sealed record TabGate(GateKind Kind, IReadOnlyList<string> Lines)
{
    public bool IsOpen => Kind == GateKind.Open;

    public static TabGate Open { get; } = new(GateKind.Open, []);

    public static TabGate Reading { get; } = new(GateKind.Reading, ["Reading..."]);
}

/// <summary>What the plugin has read so far; any part is null until its refresh landed or when its plugin is unusable.</summary>
public sealed record SourceReads(
    IReadOnlyList<PluginCheckResult> Results,
    FleetRead? Fleet,
    AutoRetainerRead? AutoRetainer,
    LootRead? Loot,
    TrackingRead? Tracking);

/// <summary>
/// The decision of what each tab may show, as plain functions of what was read. The ImGui code only draws the lines
/// of a closed gate; a number is drawn only through an open one, so a source that was missing, broken, empty or
/// unticked never shows up as a 0.
/// </summary>
public static class TabGates
{
    public static bool Usable(PluginCheckResult? r) => r?.Status is PluginStatus.Ready or PluginStatus.Stale;

    public static PluginCheckResult? Result(IReadOnlyList<PluginCheckResult> results, PluginRequirement req) =>
        results.FirstOrDefault(r => r.Requirement == req);

    /// <summary>"Needs X: detail. fix" for a plugin that cannot be read from; null when it can.</summary>
    public static string? NeedsLine(PluginCheckResult? r) =>
        r is null || Usable(r) ? null : $"Needs {r.Requirement.DisplayName}: {r.Detail} {r.Fix}".TrimEnd();

    /// <summary>One amber line per stale source among those a tab shows numbers from.</summary>
    public static IReadOnlyList<string> StaleNotes(IReadOnlyList<PluginCheckResult> results, params PluginRequirement[] used) =>
        results
            .Where(r => r.Status == PluginStatus.Stale && used.Contains(r.Requirement))
            .Select(r => $"{r.Requirement.DisplayName} data is stale. {r.Detail} {r.Fix}".TrimEnd())
            .ToList();

    /// <summary>The submarine list alone (the Fleet tab).</summary>
    public static TabGate Fleet(SourceReads s)
    {
        if (s.Results.Count == 0) return TabGate.Reading;
        var st = Result(s.Results, PluginRequirements.SubmarineTracker);
        if (NeedsLine(st) is { } needs) return new(GateKind.Needs, [needs]);
        if (s.Fleet is null) return TabGate.Reading;
        if (s.Fleet.Fleets is null) return new(GateKind.Error, [FleetError(s.Fleet.Error)]);
        if (s.Fleet.Total == 0) return new(GateKind.Empty, [NoSubmarines]);
        return TabGate.Open;
    }

    /// <summary>
    /// The Tracking tab: AutoRetainer's characters, each tickable. Submarine Tracker only fills the Subs column, so
    /// without it the gate is open and <see cref="TrackingNotes"/> says what is missing.
    /// </summary>
    public static TabGate Tracking(SourceReads s)
    {
        if (s.Results.Count == 0) return TabGate.Reading;
        var ar = Result(s.Results, PluginRequirements.AutoRetainer);
        var st = Result(s.Results, PluginRequirements.SubmarineTracker);
        var lines = new List<string>();
        if (NeedsLine(ar) is { } a)
        {
            lines.Add(a);
            if (NeedsLine(st) is { } t) lines.Add(t);
            return new(GateKind.Needs, lines);
        }

        if (s.AutoRetainer?.Error is { } arError) lines.Add(AutoRetainerError(arError));
        if (s.Fleet?.Error is { } stError) lines.Add(FleetError(stError));
        if (lines.Count > 0) return new(GateKind.Error, lines);

        if (s.AutoRetainer?.Characters is not { } characters) return TabGate.Reading;
        if (characters.Count == 0) return new(GateKind.Empty, [NoCharacters]);
        if (Usable(st) && s.Fleet?.Fleets is null) return TabGate.Reading;
        if (s.Tracking is not { } tracking) return TabGate.Reading;
        if (tracking.Characters.Count == 0) return new(GateKind.Empty, [s.Fleet?.Total == 0 ? NoSubmarines : NoMatch]);
        return TabGate.Open;
    }

    /// <summary>The inline lines beside the Tracking table: what Submarine Tracker would have filled in.</summary>
    public static IReadOnlyList<string> TrackingNotes(IReadOnlyList<PluginCheckResult> results) =>
        NeedsLine(Result(results, PluginRequirements.SubmarineTracker)) is { } n ? [n] : [];

    /// <summary>AutoRetainer and Submarine Tracker both read, joined, with characters and submarines in them.</summary>
    public static TabGate Joined(SourceReads s)
    {
        if (s.Results.Count == 0) return TabGate.Reading;
        var ar = Result(s.Results, PluginRequirements.AutoRetainer);
        var st = Result(s.Results, PluginRequirements.SubmarineTracker);
        var lines = new List<string>();
        var needed = false;
        if (NeedsLine(ar) is { } a) { lines.Add(a); needed = true; }
        if (NeedsLine(st) is { } t) { lines.Add(t); needed = true; }
        if (needed) return new(GateKind.Needs, lines);

        if (s.AutoRetainer?.Error is { } arError) lines.Add(AutoRetainerError(arError));
        if (s.Fleet?.Error is { } stError) lines.Add(FleetError(stError));
        if (lines.Count > 0) return new(GateKind.Error, lines);

        if (s.AutoRetainer?.Characters is not { } characters || s.Fleet?.Fleets is null) return TabGate.Reading;
        if (characters.Count == 0) return new(GateKind.Empty, [NoCharacters]);
        if (s.Fleet.Total == 0) return new(GateKind.Empty, [NoSubmarines]);
        if (s.Tracking is not { } tracking) return TabGate.Reading;
        if (tracking.Characters.Count == 0) return new(GateKind.Empty, [NoMatch]);
        return TabGate.Open;
    }

    /// <summary>
    /// Why nothing is ticked, or null when something is: every listed character is unticked, or the ticked ones own no
    /// submarine. Works on whatever was read, so it holds without AutoRetainer too.
    /// </summary>
    private static TabGate? NothingTicked(SourceReads s, IReadOnlySet<string> excluded)
    {
        if (s.Tracking is { Characters.Count: > 0 } t && t.Characters.All(c => excluded.Contains(c.Key)))
            return new(GateKind.Empty, [AllUnticked]);
        if (s.Fleet?.Fleets is { } fleets && CharacterFilter.Apply(fleets, excluded).Count == 0)
            return new(GateKind.Empty, [s.Tracking is null ? AllUnticked : NoTickedSubs]);
        return null;
    }

    /// <summary>The joined gate plus: at least one character that owns a submarine is ticked on the Tracking tab.</summary>
    public static TabGate Ticked(SourceReads s, IReadOnlySet<string> excluded)
    {
        var g = Joined(s);
        if (!g.IsOpen) return g;
        return NothingTicked(s, excluded) ?? g;
    }

    /// <summary>Submarine Tracker alone (the submarine list), with someone who owns a submarine ticked; AutoRetainer is not needed.</summary>
    public static TabGate FleetTicked(SourceReads s, IReadOnlySet<string> excluded)
    {
        var g = Fleet(s);
        if (!g.IsOpen) return g;
        return NothingTicked(s, excluded) ?? g;
    }

    /// <summary>The ticked gate plus: the loot table was read and holds a voyage for the ticked characters.</summary>
    public static TabGate Loot(SourceReads s, IReadOnlySet<string> excluded) => LootOf(Ticked(s, excluded), s, excluded);

    /// <summary>
    /// Production, upkeep, routes and the 90-day chart: Submarine Tracker's fleet and loot, ticked by name. AutoRetainer
    /// only adds the supplies, so without it this gate is open and <see cref="SuppliesNote"/> says what is missing.
    /// </summary>
    public static TabGate Production(SourceReads s, IReadOnlySet<string> excluded) => LootOf(FleetTicked(s, excluded), s, excluded);

    private static TabGate LootOf(TabGate g, SourceReads s, IReadOnlySet<string> excluded)
    {
        if (!g.IsOpen) return g;
        if (s.Loot is null) return TabGate.Reading;
        if (s.Loot.Rows is null) return new(GateKind.Error, ["Could not read Submarine Tracker's loot: " + s.Loot.Error]);
        if (s.Loot.Rows.Count == 0) return new(GateKind.Empty, [s.Loot.LifetimeSectors > 0 ? NoRecentLoot : NoLoot]);
        if (CharacterFilter.Apply(s.Loot.Rows, excluded).Count == 0) return new(GateKind.Empty, [NoLootTicked]);
        return TabGate.Open;
    }

    /// <summary>The Supplies block's line when the ceruleum and kit stock cannot be had (AutoRetainer carries it); null when it can.</summary>
    public static string? SuppliesNote(SourceReads s)
    {
        if (s.Tracking is not null) return null;
        if (NeedsLine(Result(s.Results, PluginRequirements.AutoRetainer)) is { } needs) return needs;
        if (s.AutoRetainer?.Error is { } error) return AutoRetainerError(error);
        return "Reading...";
    }

    /// <summary>The Levelling tab's line when hulls lack the EXP Submarine Tracker records and AutoRetainer (the fallback) is unusable; null otherwise.</summary>
    public static string? LevellingFallbackNote(IReadOnlyList<PluginCheckResult> results, int hullsWithoutExp)
    {
        if (hullsWithoutExp <= 0) return null;
        if (NeedsLine(Result(results, PluginRequirements.AutoRetainer)) is not { } needs) return null;
        return $"{needs} {hullsWithoutExp} {(hullsWithoutExp == 1 ? "hull has" : "hulls have")} no EXP recorded by Submarine Tracker, so {(hullsWithoutExp == 1 ? "its" : "their")} days count from zero.";
    }

    /// <summary>The Salvage backlog's line when salvage cannot be told apart by character; null when it can.</summary>
    public static string? BacklogByCharacterNote(IReadOnlyList<PluginCheckResult> results) =>
        Usable(Result(results, PluginRequirements.AutoRetainer))
            ? null
            : (NeedsLine(Result(results, PluginRequirements.AutoRetainer)) ?? "Needs AutoRetainer.") + " Unsold salvage is the total over every character, not split by character or by the ticks.";

    /// <summary>The loot gate plus: the economics have a measured production rate to project from. AutoRetainer is not needed.</summary>
    public static TabGate Projection(SourceReads s, IReadOnlySet<string> excluded, SalvageEconomics? economics)
    {
        var g = Production(s, excluded);
        if (!g.IsOpen) return g;
        if (economics is null) return TabGate.Reading;
        if (economics.Model.PerHull <= 0 || economics.Model.PerDay <= 0)
            return new(GateKind.Empty, ["Needs a measured production rate: no salvage from a farming hull in the last 30 days, so there is nothing to project from yet. Send hulls on a gil route and let a few voyages come back."]);
        return TabGate.Open;
    }

    /// <summary>The Gil tab: AutoRetainer alone, with someone ticked.</summary>
    public static TabGate Gil(SourceReads s, IReadOnlySet<string> excluded)
    {
        if (s.Results.Count == 0) return TabGate.Reading;
        var ar = Result(s.Results, PluginRequirements.AutoRetainer);
        if (NeedsLine(ar) is { } needs) return new(GateKind.Needs, [needs]);
        if (s.AutoRetainer is null) return TabGate.Reading;
        if (s.AutoRetainer.Characters is not { } characters) return new(GateKind.Error, [AutoRetainerError(s.AutoRetainer.Error)]);
        if (characters.Count == 0) return new(GateKind.Empty, [NoCharacters]);
        if (characters.All(c => excluded.Contains(CharacterFilter.Key(c.Name, c.World)))) return new(GateKind.Empty, [AllUnticked]);
        return TabGate.Open;
    }

    /// <summary>The Levelling tab: Submarine Tracker's fleet plus the game's sheets. AutoRetainer only backs up the EXP.</summary>
    public static TabGate Levelling(SourceReads s, IReadOnlySet<string> excluded, bool gameDataRead, string? gameDataError)
    {
        var g = FleetTicked(s, excluded);
        if (!g.IsOpen) return g;
        if (gameDataRead) return TabGate.Open;
        return gameDataError is null
            ? TabGate.Reading
            : new(GateKind.Error, ["Could not read the game's submarine data: " + gameDataError + ". Drydock tries again on the next refresh."]);
    }

    /// <summary>
    /// Why the unsold-salvage backlog is unavailable, or null when it can be computed. Allagan Tools is optional,
    /// so this is a note beside the numbers it feeds and never closes a tab.
    /// </summary>
    public static string? BacklogReason(IReadOnlyList<PluginCheckResult> results, InventoryRead? inventory, AutoRetainerRead? autoRetainer)
    {
        var ag = Result(results, PluginRequirements.AllaganTools);
        if (ag is null) return "Reading...";
        if (NeedsLine(ag) is { } needs) return needs;
        if (inventory?.Error is { } error) return "Needs Allagan Tools: could not read its inventory file: " + error;
        // without AutoRetainer the total still counts; only the split by character is lost
        if (!Usable(Result(results, PluginRequirements.AutoRetainer)))
            return inventory?.Rows is null ? "Reading..." : null;
        if (autoRetainer?.Error is not null) return "Needs AutoRetainer: its config could not be read, so salvage cannot be told apart by character.";
        if (inventory?.Rows is null || autoRetainer?.Characters is null) return "Reading...";
        if (!InventoryToolsCsv.OwnersLookRight(inventory.Rows, autoRetainer.Characters.Select(c => c.Cid)))
            return "Needs Allagan Tools: inventories.csv no longer matches the format Drydock reads (none of AutoRetainer's characters appears in its owner column), so salvage is left out rather than counted wrong. An Allagan Tools update likely changed the file; update Drydock once a fix is out.";
        return null;
    }

    /// <summary>A tile's second line for a figure that sits behind a closed gate; null for an open one.</summary>
    public static string? TileReason(TabGate g) => g.Kind switch
    {
        GateKind.Open => null,
        GateKind.Reading => "reading",
        GateKind.Needs => "needs a plugin, see setup",
        GateKind.Error => "could not be read",
        _ => g.Lines.Count > 0 && (g.Lines[0] is NoLoot or NoLootTicked || g.Lines[0] == NoRecentLoot) ? "no voyage recorded yet" : "nothing to count",
    };

    /// <summary>The short form of a reason for a tile's second line.</summary>
    public static string Short(string? reason) => reason switch
    {
        null => "",
        _ when reason.StartsWith("Needs ", StringComparison.Ordinal) => "needs " + reason["Needs ".Length..].Split(':')[0],
        _ => "reading",
    };

    public const string NoSubmarines = "Submarine Tracker has no submarines recorded. Open Submarine Tracker on a character that owns a free company workshop with submarines, so it records them.";
    public const string NoCharacters = "AutoRetainer lists no characters yet. Log in on a character with AutoRetainer enabled so it writes its configuration.";
    public const string NoMatch = "None of the characters in AutoRetainer owns a submarine in Submarine Tracker's list, so there is nothing to join. Log in on the character that owns the submarines with both plugins enabled.";
    public const string NoTickedSubs = "None of the ticked characters owns a submarine. Tick one that does on the Tracking tab.";
    public const string AllUnticked = "Every character is unticked. Tick at least one on the Tracking tab.";
    public const string NoLoot = "Submarine Tracker has recorded no voyage yet, so there is no salvage to count. Let a submarine come back with Submarine Tracker loaded.";
    public static readonly string NoRecentLoot = $"Submarine Tracker has no voyage in the last {SubmarineTrackerDb.ReadWindowDays} days, so there is nothing current to count. Let a submarine come back with Submarine Tracker loaded.";
    public const string NoLootTicked = "Submarine Tracker has recorded no voyage for the ticked characters yet.";
    public const string ExcludedByAutoRetainer = "Excluded from deployables in AutoRetainer";

    /// <summary>The Tracking tab's line over the characters AutoRetainer excludes from deployables; null when there are none.</summary>
    public static string? ExcludedByAutoRetainerNote(TrackingRead tracking) =>
        tracking.Characters.Where(c => c.ExcludedByAutoRetainer).Select(c => c.Name).ToList() is { Count: > 0 } names
            ? $"{ExcludedByAutoRetainer}: {string.Join(", ", names)}. Left out of every submarine figure; gil still counts. Clear the setting in AutoRetainer to count them again."
            : null;

    private static string AutoRetainerError(string? error) =>
        $"Could not read AutoRetainer's config: {error}. Drydock tries again on the next refresh.";

    private static string FleetError(string? error) =>
        $"Could not read Submarine Tracker's database: {error}. Drydock tries again on the next refresh.";
}
