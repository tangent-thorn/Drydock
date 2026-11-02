namespace Drydock.Core.Fleet;

/// <param name="Farming">Hulls farming on that day.</param>
public sealed record ProjectionPoint(DateOnly Date, long Value, int Farming);

public sealed record ProjectionMilestone(long Value, DateOnly Date, int Days);

public sealed record ProjectionHorizon(string Label, int Days, DateOnly Date, long Value);

public sealed record ProjectionRates(
    long NowDay, long NowWeek, long NowMonth, long NowYear,
    long SteadyDay, long SteadyWeek, long SteadyMonth, long SteadyYear);

/// <param name="Days">Days until the position first reaches the target; null when it does not within the horizon or already has.</param>
/// <param name="Date">The date of <paramref name="Days"/>.</param>
/// <param name="Start">The position the projection starts from.</param>
public sealed record ProjectionResult(
    long Target,
    long Start,
    int? Days,
    DateOnly? Date,
    IReadOnlyList<ProjectionPoint> Curve,
    IReadOnlyList<ProjectionMilestone> Milestones,
    IReadOnlyList<ProjectionHorizon> Horizons,
    ProjectionRates Rates)
{
    public bool TargetReached => Start >= Target;
}

/// <summary>
/// A port of the collector's projection block: a year forward, each levelling hull starting to farm on the day it
/// reaches rank 85, which also makes it cheaper to run.
/// </summary>
public static class ProjectionMath
{
    public const int HorizonDays = 365;
    public const long MilestoneStep = 250_000_000;
    public const long DefaultTarget = 1_000_000_000;
    public const long MinTarget = 1_000_000;
    public const long MaxTarget = 1_000_000_000_000;

    private static readonly (string Label, int Days)[] HorizonTable = [("1 month", 30), ("3 months", 91), ("6 months", 182), ("12 months", 365)];

    private static long Round(double x) => (long)Math.Round(x, MidpointRounding.ToEven);

    /// <param name="position">Gil plus unsold salvage now.</param>
    /// <param name="farming">Hulls farming now.</param>
    /// <param name="levellingEtaDays">Per levelling hull, the days until it farms; infinity for one that never will.</param>
    /// <param name="perHull">Salvage value per farming hull per day, unrounded.</param>
    /// <param name="secPerEnd">Sectors per farming hull per day.</param>
    /// <param name="secPerLvl">Sectors per levelling hull per day.</param>
    /// <param name="totalHulls">All hulls; the steady rate assumes every one farms.</param>
    /// <param name="perDay">Production per day over 30 days, unrounded.</param>
    /// <param name="upkeepDay">Upkeep per day now, unrounded.</param>
    public static ProjectionResult Compute(
        double position,
        int farming,
        IReadOnlyList<double> levellingEtaDays,
        double perHull,
        double secPerEnd,
        double secPerLvl,
        double costPerSector,
        int totalHulls,
        double perDay,
        double upkeepDay,
        long target,
        DateOnly today)
    {
        var nLvl = levellingEtaDays.Count;
        var curve = new List<ProjectionPoint> { new(today, Round(position), farming) };
        var milestones = new List<ProjectionMilestone>();
        var nextMs = ((long)Math.Floor(position / MilestoneStep) + 1) * MilestoneStep;
        var g = position;
        int? hitDay = null;
        DateOnly? hit = null;
        var d = today;
        var ne = farming;

        for (var day = 1; day <= HorizonDays; day++)
        {
            var arrived = 0;
            foreach (var e in levellingEtaDays)
            {
                if (e <= day) arrived++;
            }
            ne = farming + arrived;
            var nl = nLvl - arrived;
            g += ne * perHull - (ne * secPerEnd + nl * secPerLvl) * costPerSector;
            d = today.AddDays(day);
            while (g >= nextMs)
            {
                milestones.Add(new(nextMs, d, day));
                if (nextMs == target)
                {
                    hit = d;
                    hitDay = day;
                }
                nextMs += MilestoneStep;
            }
            if ((day <= 60 && day % 2 == 0) || (day > 60 && day % 7 == 0))
                curve.Add(new(d, Round(g), ne));
        }
        if (curve[^1].Date != d) curve.Add(new(d, Round(g), ne));

        var horizons = new List<ProjectionHorizon>();
        foreach (var (label, days) in HorizonTable)
        {
            var best = curve[0];
            var bestGap = int.MaxValue;
            foreach (var p in curve)
            {
                var gap = Math.Abs(p.Date.DayNumber - today.DayNumber - days);
                if (gap < bestGap)
                {
                    best = p;
                    bestGap = gap;
                }
            }
            horizons.Add(new(label, days, best.Date, best.Value));
        }

        var nowRate = perDay - upkeepDay;
        var steady = totalHulls * perHull - totalHulls * secPerEnd * costPerSector;
        var rates = new ProjectionRates(
            Round(nowRate), Round(nowRate * 7), Round(nowRate * 30.44), Round(nowRate * 365),
            Round(steady), Round(steady * 7), Round(steady * 30.44), Round(steady * 365));

        return new(target, Round(position), hitDay, hit, curve, milestones, horizons, rates);
    }
}
