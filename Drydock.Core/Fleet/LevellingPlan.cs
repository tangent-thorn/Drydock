using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <summary>Which cycle length an estimate used.</summary>
public enum CycleSource
{
    /// <summary>The hull is resent as soon as it is back (cadence 0): the voyage time itself.</summary>
    Voyage,
    /// <summary>The voyage time rounded up to a multiple of the resend cadence setting.</summary>
    Cadence,
    /// <summary>The hull's own gap between returns on this route over the last 30 days.</summary>
    Measured,
}

/// <param name="VoyageHours">One voyage at the hull's current rank and parts.</param>
/// <param name="CycleHours">Hours from one send to the next; the days to target count on this.</param>
/// <param name="ExpWorst">EXP per voyage with every sector at its guaranteed tier.</param>
/// <param name="ExpBest">EXP per voyage with every sector at its maximum tier.</param>
/// <param name="DaysBest">Whole days to the target at the best EXP; null when the hull earns nothing.</param>
/// <param name="DaysWorst">Whole days to the target at the guaranteed EXP.</param>
public sealed record LevellingEstimate(
    double VoyageHours, double CycleHours, CycleSource CycleSource, int Fuel, long ExpWorst, long ExpBest,
    int? DaysBest, int? DaysWorst, DateOnly? EtaBest, DateOnly? EtaWorst);

/// <param name="Estimate">Null when the hull can't be estimated; <paramref name="Problem"/> says why.</param>
public sealed record LevellingEntry(string Character, string World, string Hull, int Rank, string RouteLabel, LevellingEstimate? Estimate, string? Problem)
{
    public string Key => CharacterFilter.Key(Character, World);

    /// <summary>True when neither Submarine Tracker nor AutoRetainer gave the hull's EXP, so the estimate counts from zero.</summary>
    public bool ExpUnknown { get; init; }

    /// <summary>
    /// Days from <see cref="LevellingBaseline"/>, set when the current route alone would take longer than a typical sub
    /// that changes routes as it ranks, or when there is no route estimate; the days and dates shown are these then.
    /// </summary>
    public TypicalEstimate? Typical { get; init; }

    public int? DaysWorst => Typical?.DaysWorst ?? Estimate?.DaysWorst;

    public int? DaysBest => Typical?.DaysBest ?? Estimate?.DaysBest;

    public DateOnly? EtaWorst => Typical?.EtaWorst ?? Estimate?.EtaWorst;
}

public sealed record TypicalEstimate(int DaysBest, int DaysWorst, DateOnly EtaBest, DateOnly EtaWorst);

/// <param name="TargetRank">The rank the days count to: the setting, capped at the game's max rank.</param>
public sealed record LevellingPlan(int TargetRank, IReadOnlyList<LevellingEntry> Entries);

public static class LevellingPlanner
{
    /// <summary>A hull's measured cycle needs this many voyages on its current route in the window.</summary>
    public const int MinMeasuredVoyages = 3;

    public const int MeasureWindowDays = 30;

    /// <summary>
    /// Every hull whose route is not a gil route (or has none), with days to the target rank from the game's own
    /// numbers: the rank table's EXP, the hull's route and parts. Best uses the maximum bonus tiers, worst the guaranteed ones.
    /// </summary>
    /// <param name="cadenceHours">How often the player resends: 0 resends as soon as the hull is back.</param>
    /// <param name="loot">Submarine Tracker's loot, for each hull's measured cycle; null skips it.</param>
    public static LevellingPlan Compute(
        IReadOnlyList<FreeCompanyFleet> fleets,
        IReadOnlyList<AutoRetainerCharacter> ar,
        SubmarineGameData data,
        DateOnly today,
        int targetRank,
        int cadenceHours,
        IReadOnlyList<LootRow>? loot = null,
        DateTime? nowUtc = null,
        IReadOnlyDictionary<uint, SectorInfo>? labels = null)
    {
        var target = Math.Min(targetRank, data.MaxRank);
        var now = nowUtc ?? DateTime.UtcNow;
        var measured = loot is null ? null : LootIndex.Build(loot, now);

        // hull name to row per character name, the last duplicate wins, as everywhere else
        var byCharacter = new Dictionary<string, Dictionary<string, SubmarineRow>>(StringComparer.Ordinal);
        var worlds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in fleets)
        {
            if (!byCharacter.TryGetValue(f.CharacterName, out var hulls))
                byCharacter[f.CharacterName] = hulls = new(StringComparer.Ordinal);
            foreach (var s in f.Submarines) hulls[s.Name] = s;
            worlds.TryAdd(f.CharacterName, f.World);
        }

        var entries = new List<LevellingEntry>();
        foreach (var (character, hulls) in byCharacter)
        {
            var arChar = ar.FirstOrDefault(c => c.Name == character);
            var world = arChar?.World ?? worlds[character];
            var arSubs = new Dictionary<string, AutoRetainerSubmarine>(StringComparer.Ordinal);
            if (arChar is not null)
                foreach (var s in arChar.AdditionalSubmarines) arSubs[s.Name] = s;

            foreach (var (name, hull) in hulls)
            {
                if (hull.RouteClass == RouteClass.Farming) continue;
                var label = Routes.Label(hull.Route, labels);
                // Submarine Tracker's EXP first, AutoRetainer's where it has none
                var cexp = hull.CurrentExp > 0 ? hull.CurrentExp : arSubs.TryGetValue(name, out var a) ? Math.Max(a.CurrentExp, 0) : 0;
                var (estimate, problem) = Estimate(hull, data, target, cexp, cadenceHours, measured, today);
                entries.Add(new LevellingEntry(character, world, name, hull.Rank, label, estimate, problem)
                {
                    ExpUnknown = cexp == 0,
                    // a character's only sub is the first of its FC: it opens every sector itself
                    Typical = Typical(estimate, hull.Rank, target, hulls.Count == 1, today),
                });
            }
        }

        return new LevellingPlan(
            target,
            entries
                .OrderBy(e => e.DaysWorst ?? int.MaxValue)
                .ThenBy(e => e.Character, StringComparer.Ordinal)
                .ThenBy(e => e.Hull, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// The baseline days when the route estimate is missing or its best case is slower than the baseline's slow quarter:
    /// staying on one early route that long is not how anyone levels. Null when the baseline does not cover the target.
    /// </summary>
    public static TypicalEstimate? Typical(LevellingEstimate? estimate, int rank, int target, bool firstInFc, DateOnly today)
    {
        if (LevellingBaseline.Days(rank, target, firstInFc) is not { } days) return null;
        if (estimate is { DaysBest: { } best } && best <= days.Worst) return null;
        return new TypicalEstimate(days.Best, days.Worst, today.AddDays(days.Best), today.AddDays(days.Worst));
    }

    /// <summary>One hull's estimate, or the reason there is none.</summary>
    public static (LevellingEstimate? Estimate, string? Problem) Estimate(
        SubmarineRow hull, SubmarineGameData data, int target, long currentExp, int cadenceHours, LootIndex? measured, DateOnly today)
    {
        if (hull.Route.Count == 0) return (null, "no route recorded");
        if (hull.Hull == 0 || hull.Stern == 0 || hull.Bow == 0 || hull.Bridge == 0) return (null, "no build recorded");
        if (data.StatsFor(hull.Rank, hull.Hull, hull.Stern, hull.Bow, hull.Bridge) is not { } stats) return (null, "rank or part not in the game data");
        if (VoyageMath.Estimate(data, hull.Route, stats) is not { } voyage) return (null, "route not in the game data");

        var cycle = voyage.Hours;
        var source = CycleSource.Voyage;
        if (measured?.CycleHours(hull.SubmarineId, hull.Route) is { } m)
        {
            cycle = Math.Max(m, voyage.Hours);
            source = CycleSource.Measured;
        }
        else if (cadenceHours > 0)
        {
            cycle = RoundUpTo(voyage.DurationSeconds, cadenceHours);
            source = CycleSource.Cadence;
        }

        var remaining = 0L;
        if (hull.Rank < target)
        {
            if (data.ExpBetween(hull.Rank, target) is not { } span) return (null, "rank table incomplete");
            remaining = Math.Max(span - currentExp, 0);
        }

        var daysBest = Days(remaining, voyage.Exp.Best, cycle);
        var daysWorst = Days(remaining, voyage.Exp.Worst, cycle);
        return (new LevellingEstimate(
            voyage.Hours, cycle, source, voyage.Fuel, voyage.Exp.Worst, voyage.Exp.Best,
            daysBest, daysWorst,
            daysBest is { } b ? today.AddDays(b) : null,
            daysWorst is { } w ? today.AddDays(w) : null), null);
    }

    /// <summary>Hours of the voyage rounded up to the next multiple of the cadence.</summary>
    public static double RoundUpTo(uint voyageSeconds, int cadenceHours)
    {
        var step = cadenceHours * 3600.0;
        return Math.Ceiling(voyageSeconds / step) * cadenceHours;
    }

    /// <summary>Whole days of voyages at <paramref name="expPerVoyage"/> every <paramref name="cycleHours"/> to earn <paramref name="remaining"/>; null with no EXP.</summary>
    public static int? Days(long remaining, long expPerVoyage, double cycleHours)
    {
        if (remaining <= 0) return 0;
        if (expPerVoyage <= 0 || cycleHours <= 0) return null;
        var perDay = expPerVoyage * 24.0 / cycleHours;
        var days = Math.Ceiling(remaining / perDay);
        // a date a century out is not an estimate, and AddDays would throw past year 9999
        return double.IsNaN(days) || days > MaxDays ? null : (int)days;
    }

    /// <summary>Longest estimate shown; beyond it a hull counts as never reaching the target.</summary>
    public const int MaxDays = 36500;
}

/// <summary>Submarine Tracker's loot grouped by hull, to measure how often each hull is really resent.</summary>
public sealed class LootIndex
{
    private readonly Dictionary<long, List<(long Return, HashSet<uint> Sectors)>> byHull;

    private LootIndex(Dictionary<long, List<(long, HashSet<uint>)>> byHull) => this.byHull = byHull;

    public static LootIndex Build(IEnumerable<LootRow> loot, DateTime nowUtc)
    {
        var from = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds() - LevellingPlanner.MeasureWindowDays * 86400L;
        var voyages = new Dictionary<(long Hull, long Return), HashSet<uint>>();
        foreach (var r in loot)
        {
            if (r.Return < from) continue;
            if (!voyages.TryGetValue((r.SubmarineId, r.Return), out var set))
                voyages[(r.SubmarineId, r.Return)] = set = [];
            set.Add(r.Sector);
        }
        var byHull = new Dictionary<long, List<(long, HashSet<uint>)>>();
        foreach (var ((hull, ret), set) in voyages)
        {
            if (!byHull.TryGetValue(hull, out var list)) byHull[hull] = list = [];
            list.Add((ret, set));
        }
        return new LootIndex(byHull);
    }

    /// <summary>
    /// Mean hours between the hull's returns on this route (the same sector set) over the window; null with fewer than
    /// <see cref="LevellingPlanner.MinMeasuredVoyages"/> voyages.
    /// </summary>
    public double? CycleHours(long hullId, IReadOnlyList<uint> route)
    {
        if (!byHull.TryGetValue(hullId, out var list)) return null;
        var set = route.ToHashSet();
        var returns = list.Where(v => v.Sectors.SetEquals(set)).Select(v => v.Return).Order().ToList();
        if (returns.Count < LevellingPlanner.MinMeasuredVoyages) return null;
        return (returns[^1] - returns[0]) / 3600.0 / (returns.Count - 1);
    }
}
