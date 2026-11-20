using System.Globalization;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <summary>One character's tanks and kits for the Restock tab.</summary>
/// <param name="World">The character's world; two characters sharing a name are one row, their worlds joined.</param>
/// <param name="TanksOnHand">Null when AutoRetainer did not carry the figure; the to-buy is then null too.</param>
/// <param name="KitCost">Kits to buy at the upkeep's kit price, in gil; tanks are bought with company credits.</param>
public sealed record RestockRow(
    string Name,
    string World,
    int? TanksOnHand,
    double TanksPerDay,
    long? TanksToBuy,
    int? KitsOnHand,
    double KitsPerDay,
    long? KitsToBuy,
    long? KitCost)
{
    /// <summary>Days until the first of the two runs out at today's use; null when a stock is unknown.</summary>
    public double? DaysLeft =>
        TanksOnHand is not { } tanks || KitsOnHand is not { } kits
            ? null
            : Math.Min(TanksPerDay > 0 ? tanks / TanksPerDay : double.PositiveInfinity, KitsPerDay > 0 ? kits / KitsPerDay : double.PositiveInfinity);

    public object?[] Cells() => [Name, World, TanksOnHand, TanksPerDay, TanksToBuy, KitsOnHand, KitsPerDay, KitsToBuy, KitCost];
}

/// <param name="Rows">The characters whose subs use tanks or kits, the one that runs out first at the top.</param>
/// <param name="TanksPerDay">Over the rows: the fleet's use as the Overview runway counts it.</param>
public sealed record RestockView(
    int Days,
    int KitPrice,
    IReadOnlyList<RestockRow> Rows,
    double TanksPerDay,
    long TanksToBuy,
    double KitsPerDay,
    long KitsToBuy,
    long KitCost)
{
    public static IReadOnlyList<string> Headers { get; } =
        ["Character", "World", "Tanks on hand", "Tanks per day", "Tanks to buy", "Kits on hand", "Kits per day", "Kits to buy", "Kit cost (gil)"];

    public string Header => string.Create(CultureInfo.InvariantCulture, $"What to buy so every character's subs last {Days} days. Change the days in settings.");

    /// <summary>Some row has no stock figure, so its to-buy is "-" and the totals leave it out.</summary>
    public bool StockUnknown => Rows.Any(r => r.TanksToBuy is null || r.KitsToBuy is null);

    public string CopyText(IEnumerable<RestockRow> rows) => TableText.Build(Headers, rows, r => r.Cells());
}

public static class RestockMath
{
    public const int DefaultDays = 30;
    public const int MinDays = 1;
    public const int MaxDays = 365;

    public const string NoUse = "None of the counted characters' subs uses any tanks or kits right now.";
    public const string StockNote = "AutoRetainer has no ceruleum or kit figure for some characters: their to-buy shows \"-\" and the totals leave them out.";

    /// <summary>max(0, ceil(perDay * days - onHand)).</summary>
    public static long ToBuy(double perDay, int days, int onHand)
    {
        // 0.1 * 30 is 3.0000000000000004 in floating point; a whole number of units must not round up to the next
        var need = Math.Round(perDay * days - onHand, 6, MidpointRounding.ToEven);
        return Math.Max(0, (long)Math.Ceiling(need));
    }

    /// <summary>
    /// Per character, the fleet's own use model: each character's endgame and levelling hulls (counted as the economics
    /// count them, under the same <paramref name="rule"/>) times the economics' sectors per hull per day, times the
    /// ceruleum and kits per sector the economics ran on. The characters are those whose subs run (ticked, owning a hull,
    /// not excluded from deployables in AutoRetainer); <paramref name="subsExcluded"/> is the unticked plus AutoRetainer's.
    /// </summary>
    public static RestockView Compute(
        TrackingRead tracking,
        IReadOnlyList<FreeCompanyFleet> fleets,
        IReadOnlySet<string> subsExcluded,
        ModelRates rates,
        double fuelPerSector,
        double kitsPerSector,
        int kitPrice,
        int days,
        EndgameRule rule)
    {
        days = Math.Clamp(days, MinDays, MaxDays);
        var fleetsByName = CharacterFilter.Apply(fleets, subsExcluded)
            .GroupBy(f => f.CharacterName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FreeCompanyFleet>)g.ToList(), StringComparer.Ordinal);

        var rows = new List<RestockRow>();
        // fleets are matched by name alone, so two characters sharing one share its hulls: one row, or the use counts twice
        foreach (var same in tracking.Counted(subsExcluded).GroupBy(c => c.Name, StringComparer.Ordinal))
        {
            if (!fleetsByName.TryGetValue(same.Key, out var own)) continue;
            var (farming, levelling) = FleetLevelling.EconomicsHulls(own, rule);
            var sectors = rates.SectorsPerDay(farming, levelling);
            var tanksPerDay = sectors * fuelPerSector;
            var kitsPerDay = sectors * kitsPerSector;
            if (tanksPerDay <= 0 && kitsPerDay <= 0) continue;

            var chars = same.ToList();
            int? tanks = chars.All(c => c.Ceruleum.HasValue) ? chars.Sum(c => c.Ceruleum!.Value) : null;
            int? kits = chars.All(c => c.RepairKits.HasValue) ? chars.Sum(c => c.RepairKits!.Value) : null;
            long? tanksToBuy = tanks is { } t ? ToBuy(tanksPerDay, days, t) : null;
            long? kitsToBuy = kits is { } k ? ToBuy(kitsPerDay, days, k) : null;
            rows.Add(new RestockRow(
                same.Key,
                string.Join(", ", chars.Select(c => c.World).Distinct(StringComparer.Ordinal)),
                tanks,
                tanksPerDay,
                tanksToBuy,
                kits,
                kitsPerDay,
                kitsToBuy,
                kitsToBuy * (long)kitPrice));
        }

        var sorted = rows
            .OrderBy(r => r.DaysLeft ?? double.PositiveInfinity)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return new RestockView(
            days,
            kitPrice,
            sorted,
            sorted.Sum(r => r.TanksPerDay),
            sorted.Sum(r => r.TanksToBuy ?? 0),
            sorted.Sum(r => r.KitsPerDay),
            sorted.Sum(r => r.KitsToBuy ?? 0),
            sorted.Sum(r => r.KitCost ?? 0));
    }
}
