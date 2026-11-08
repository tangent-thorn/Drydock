using System.Globalization;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public sealed record SalvageItem(uint Id, string Name, int Price);

public static class SalvageTable
{
    /// <summary>The eight vendor-priced salvage items, the collector's SALVAGE table.</summary>
    public static IReadOnlyList<SalvageItem> Defaults { get; } =
    [
        new(22500, "Salvaged Ring", 8000),
        new(22501, "Salvaged Bracelet", 9000),
        new(22502, "Salvaged Earring", 10000),
        new(22503, "Salvaged Necklace", 13000),
        new(22504, "Extravagant Salvaged Ring", 27000),
        new(22505, "Extravagant Salvaged Bracelet", 28500),
        new(22506, "Extravagant Salvaged Earring", 30000),
        new(22507, "Extravagant Salvaged Necklace", 34500),
    ];

    public static IReadOnlyDictionary<uint, int> DefaultPrices { get; } = Defaults.ToDictionary(i => i.Id, i => i.Price);
}

public sealed record EconomicsSettings(
    double FuelPerSector,
    double KitsPerSector,
    int FuelPrice,
    int KitPrice,
    int RunwayDays)
{
    public static EconomicsSettings Default { get; } = new(4.5, 0.872, 300, 1800, 14);
}

/// <summary>How a voyage and a hull are told apart as endgame or levelling.</summary>
public enum EndgameRule
{
    /// <summary>The collector's rule: a voyage is endgame at loot rank 85 or more.</summary>
    ByRank85,
    /// <summary>A voyage is endgame when its sector set is a gil route.</summary>
    ByRoute,
}

/// <param name="EndgameHulls">Hulls counted as endgame: at rank 85 or more (ByRank85) or on a gil route now (ByRoute).</param>
/// <param name="LevellingHulls">Hulls counted as levelling: below 85 (ByRank85) or on a route that is not a gil route (ByRoute).</param>
/// <param name="HullsByRoute">Hulls on each gil route now, by route name; feeds the per-route table.</param>
public sealed record EconomicsFleet(int EndgameHulls, int LevellingHulls, int? CeruleumStock, int? RepairKitStock, IReadOnlyDictionary<string, int>? HullsByRoute = null);

/// <summary>One gil route in use. Measured numbers are over the last 30 days.</summary>
/// <param name="CycleHours">30 * 24 / (voyages per hull); null with no voyages.</param>
/// <param name="MeasuredGil">Salvage value of the route's voyages over their count; null with no voyages.</param>
/// <param name="PctOfSheet">Measured gil as a percentage of the sheet's.</param>
public sealed record RouteStat(GilRoute Route, int HullsNow, int Voyages30, double? CycleHours, long? MeasuredGil, double? PctOfSheet);

/// <param name="PerDay">Production per day over 30 days.</param>
/// <param name="PerHull">Production per day per endgame hull.</param>
/// <param name="SecPerEnd">Sectors per day for one endgame hull.</param>
/// <param name="SecPerLvl">Sectors per day for one levelling hull.</param>
/// <param name="CostPerSector">Ceruleum and kits per sector, in gil.</param>
/// <param name="UpkeepDay">Upkeep per day now.</param>
public sealed record ModelRates(double PerDay, double PerHull, double SecPerEnd, double SecPerLvl, double CostPerSector, double UpkeepDay);

public sealed record SalvageEconomics(
    long Today,
    long Yesterday,
    long PerDay30,
    long PerDay7,
    double TrendPct,
    long PerHullDay,
    double VoyagesDay,
    double SectorsDay,
    long NetPerDay,
    IReadOnlyList<(DateOnly Date, long Value)> Daily,
    long UpkeepPerDay,
    long PerSector,
    double? PctOfProduction,
    long FuelPerDay,
    long KitsPerDay,
    long FuelUnitsDay,
    double KitUnitsDay,
    long EndgamePerDay,
    long LevellingPerDay,
    long? EndgameCycleHours,
    long? LevellingCycleHours,
    int? CeruleumStock,
    int? RepairKitStock,
    double? CeruleumRunwayDays,
    double? RepairKitRunwayDays)
{
    /// <summary>Per gil route in use (hulls on it now or voyages in the last 30 days), in the sheet's order.</summary>
    public IReadOnlyList<RouteStat> Routes { get; init; } = [];

    /// <summary>The unrounded numbers the projection runs on.</summary>
    public ModelRates Model { get; init; } = new(0, 0, 0, 0, 0, 0);

    public static long Round(double x) => (long)Math.Round(x, MidpointRounding.ToEven);
}

public static class SalvageMath
{
    private sealed class Voyage(DateOnly date, int rank, long hull)
    {
        public DateOnly Date { get; } = date;
        public int Rank { get; } = rank;
        public long Hull { get; } = hull;
        public int Sectors;
        public long Value;
        public HashSet<uint> SectorSet { get; } = [];
    }

    /// <summary>
    /// A port of the collector's production and upkeep block, quirks included: the windows are
    /// "days ago &lt;= 30" and "&lt;= 7" (future days count), a voyage is a distinct (hull, return) pair
    /// and its row count is its sectors.
    /// </summary>
    public static SalvageEconomics Compute(
        IEnumerable<LootRow> loot,
        IReadOnlyDictionary<uint, int> prices,
        EconomicsFleet fleet,
        EconomicsSettings s,
        DateOnly today,
        TimeZoneInfo zone,
        EndgameRule rule = EndgameRule.ByRank85)
    {
        var prod = new Dictionary<DateOnly, long>();
        var voyages = new Dictionary<(long Hull, long Return), Voyage>();
        foreach (var r in loot)
        {
            var d = LocalDate(r.Return, zone);
            var v = RowValue(r, prices);
            if (v != 0) prod[d] = prod.GetValueOrDefault(d) + v;

            var key = (r.SubmarineId, r.Return);
            if (!voyages.TryGetValue(key, out var voy))
                voyages[key] = voy = new Voyage(d, r.Rank, r.SubmarineId);
            voy.Sectors++;
            voy.Value += v;
            voy.SectorSet.Add(r.Sector);
        }

        int Ago(DateOnly d) => today.DayNumber - d.DayNumber;
        var perDay = prod.Where(kv => Ago(kv.Key) <= 30).Sum(kv => (double)kv.Value) / 30;
        var perDay7 = prod.Where(kv => Ago(kv.Key) <= 7).Sum(kv => (double)kv.Value) / 7;
        var trend = perDay != 0 ? perDay7 / perDay - 1 : 0;
        var perHull = fleet.EndgameHulls != 0 ? perDay / fleet.EndgameHulls : 0;

        var daily = new List<(DateOnly, long)>(90);
        for (var i = 89; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            daily.Add((d, prod.GetValueOrDefault(d)));
        }

        var rec = voyages.Values.Where(v => Ago(v.Date) <= 30).ToList();
        bool IsEndgame(Voyage v) => rule == EndgameRule.ByRoute ? Routes.FindGil(v.SectorSet) is not null : v.Rank >= 85;
        var endV = rec.Where(IsEndgame).ToList();
        var lvlV = rec.Where(v => !IsEndgame(v)).ToList();
        var endHulls = Math.Max(endV.Select(v => v.Hull).Distinct().Count(), 1);
        var lvlHulls = Math.Max(lvlV.Select(v => v.Hull).Distinct().Count(), 1);

        var secPerEnd = endV.Sum(v => (double)v.Sectors) / 30 / endHulls;
        var secPerLvl = lvlV.Sum(v => (double)v.Sectors) / 30 / lvlHulls;
        var voyPerEnd = (double)endV.Count / 30 / endHulls;
        var voyPerLvl = (double)lvlV.Count / 30 / lvlHulls;

        var costPerSector = s.FuelPerSector * s.FuelPrice + s.KitsPerSector * s.KitPrice;
        var endSecDay = fleet.EndgameHulls * secPerEnd;
        var lvlSecDay = fleet.LevellingHulls * secPerLvl;
        var secDay = endSecDay + lvlSecDay;
        var voyDay = fleet.EndgameHulls * voyPerEnd + fleet.LevellingHulls * voyPerLvl;
        var upkeepDay = secDay * costPerSector;

        // no runway without a stock that was read, or without any use to divide it by
        double? cerRunway = secDay * s.FuelPerSector > 0 ? fleet.CeruleumStock / (secDay * s.FuelPerSector) : null;
        double? kitRunway = secDay * s.KitsPerSector > 0 ? fleet.RepairKitStock / (secDay * s.KitsPerSector) : null;

        return new SalvageEconomics(
            prod.GetValueOrDefault(today),
            prod.GetValueOrDefault(today.AddDays(-1)),
            SalvageEconomics.Round(perDay),
            SalvageEconomics.Round(perDay7),
            Math.Round(trend * 100, 1, MidpointRounding.ToEven),
            SalvageEconomics.Round(perHull),
            Math.Round(voyDay, 1, MidpointRounding.ToEven),
            Math.Round(secDay, 1, MidpointRounding.ToEven),
            SalvageEconomics.Round(perDay - upkeepDay),
            daily,
            SalvageEconomics.Round(upkeepDay),
            SalvageEconomics.Round(costPerSector),
            perDay != 0 ? Math.Round(100 * upkeepDay / perDay, 1, MidpointRounding.ToEven) : null,
            SalvageEconomics.Round(secDay * s.FuelPerSector * s.FuelPrice),
            SalvageEconomics.Round(secDay * s.KitsPerSector * s.KitPrice),
            SalvageEconomics.Round(secDay * s.FuelPerSector),
            Math.Round(secDay * s.KitsPerSector, 1, MidpointRounding.ToEven),
            SalvageEconomics.Round(endSecDay * costPerSector),
            SalvageEconomics.Round(lvlSecDay * costPerSector),
            voyPerEnd != 0 ? SalvageEconomics.Round(24 / voyPerEnd) : null,
            voyPerLvl != 0 ? SalvageEconomics.Round(24 / voyPerLvl) : null,
            fleet.CeruleumStock,
            fleet.RepairKitStock,
            cerRunway,
            kitRunway)
        {
            Routes = RouteStats(rec, fleet.HullsByRoute),
            Model = new(perDay, perHull, secPerEnd, secPerLvl, costPerSector, upkeepDay),
        };
    }

    private static List<RouteStat> RouteStats(List<Voyage> recent, IReadOnlyDictionary<string, int>? hullsByRoute)
    {
        var stats = new List<RouteStat>();
        foreach (var g in Routes.Gils)
        {
            var hullsNow = hullsByRoute?.GetValueOrDefault(g.Name) ?? 0;
            var vs = recent.Where(v => g.Sectors.SetEquals(v.SectorSet)).ToList();
            if (hullsNow == 0 && vs.Count == 0) continue;

            // hulls on the route now; when none are, the hulls that ran it
            var hulls = hullsNow > 0 ? hullsNow : vs.Select(v => v.Hull).Distinct().Count();
            double? cycle = vs.Count > 0 ? 30.0 * 24 * hulls / vs.Count : null;
            long? measured = vs.Count > 0 ? SalvageEconomics.Round((double)vs.Sum(v => v.Value) / vs.Count) : null;
            double? pct = measured is { } m ? Math.Round(100.0 * m / g.SheetGil, 1, MidpointRounding.ToEven) : null;
            stats.Add(new RouteStat(g, hullsNow, vs.Count, cycle is { } c ? Math.Round(c, 1, MidpointRounding.ToEven) : null, measured, pct));
        }
        return stats;
    }

    /// <summary>
    /// The economics for the ticked characters from a loot read. Voyages and hulls are told apart as endgame or levelling by
    /// route; a loot table without a Sector column (an older Submarine Tracker) falls back to rank 85, the collector's rule.
    /// </summary>
    public static SalvageEconomics ComputeFor(
        IReadOnlyList<LootRow> rows,
        bool hasSectors,
        IReadOnlyList<FreeCompanyFleet> fleets,
        TrackingRead? tracking,
        IReadOnlySet<string> excluded,
        EconomicsSettings settings,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var loot = CharacterFilter.Apply(rows, excluded);
        if (hasSectors)
            return Compute(loot, SalvageTable.DefaultPrices, FleetInputsByRoute(fleets, tracking, excluded), settings, today, zone, EndgameRule.ByRoute);

        var ticked = CharacterFilter.Apply(fleets, excluded);
        var levelling = FleetLevelling.CountPerCharacter(ticked) - FleetLevelling.CountAt85(ticked);
        return Compute(loot, SalvageTable.DefaultPrices, FleetInputs(fleets, tracking, levelling, excluded), settings, today, zone, EndgameRule.ByRank85);
    }

    /// <summary>Hull counts and stock of the characters that are ticked; <paramref name="levellingHulls"/> is the ticked levelling list's length.</summary>
    public static EconomicsFleet FleetInputs(IReadOnlyList<FreeCompanyFleet> fleets, TrackingRead? tracking, int levellingHulls, IReadOnlySet<string> excluded)
    {
        var (cer, kits) = tracking is null ? (null, null) : FleetTracking.SupplyTotals(tracking, excluded);
        var ticked = CharacterFilter.Apply(fleets, excluded);
        return new(FleetLevelling.CountAt85(ticked), levellingHulls, cer, kits, FleetLevelling.HullsPerGilRoute(ticked));
    }

    /// <summary>The same by route: farming hulls are the endgame count, hulls on any other route the levelling count.</summary>
    public static EconomicsFleet FleetInputsByRoute(IReadOnlyList<FreeCompanyFleet> fleets, TrackingRead? tracking, IReadOnlySet<string> excluded)
    {
        var (cer, kits) = tracking is null ? (null, null) : FleetTracking.SupplyTotals(tracking, excluded);
        var ticked = CharacterFilter.Apply(fleets, excluded);
        return new(
            FleetLevelling.CountByRoute(ticked, RouteClass.Farming),
            FleetLevelling.CountByRoute(ticked, RouteClass.Levelling),
            cer,
            kits,
            FleetLevelling.HullsPerGilRoute(ticked));
    }

    /// <summary>The vendor value of one loot row: both item slots priced; an item the price table lacks counts 0.</summary>
    public static long RowValue(LootRow r, IReadOnlyDictionary<uint, int> prices)
    {
        long v = 0;
        if (prices.TryGetValue(r.PrimaryItem, out var p1)) v += (long)p1 * r.PrimaryCount;
        if (prices.TryGetValue(r.AdditionalItem, out var p2)) v += (long)p2 * r.AdditionalCount;
        return v;
    }

    private static DateOnly LocalDate(long unixSeconds, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime, zone));

    /// <summary>"1.02B", "7.74M", "101.8k", "950"; the sign is kept. A value that rounds up to the next unit shows in it ("1.00M", not "1000.0k").</summary>
    public static string FormatGil(double gil)
    {
        var a = Math.Abs(gil);
        if (a >= 999_995_000) return string.Create(CultureInfo.InvariantCulture, $"{gil / 1_000_000_000:0.00}B");
        if (a >= 999_950) return string.Create(CultureInfo.InvariantCulture, $"{gil / 1_000_000:0.00}M");
        if (a >= 1_000) return string.Create(CultureInfo.InvariantCulture, $"{gil / 1_000:0.0}k");
        return string.Create(CultureInfo.InvariantCulture, $"{gil:0}");
    }
}
