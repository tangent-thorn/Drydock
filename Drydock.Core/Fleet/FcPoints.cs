using System.Globalization;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <summary>The company credits one Ceruleum Tank costs, and where that figure came from.</summary>
public sealed record TankPrice(int Credits, bool FromGame)
{
    public string Source => FromGame
        ? "price from the game's Company Credit Exchange list"
        : string.Create(CultureInfo.InvariantCulture, $"the usual {FcPointsMath.DefaultTankPrice} credits (the game's Company Credit Exchange list was not read)");

    /// <summary>The game's price when it was read and is above 0; the usual price otherwise.</summary>
    public static TankPrice Choose(int? fromGame) =>
        fromGame is > 0 ? new(fromGame.Value, true) : new(FcPointsMath.DefaultTankPrice, false);
}

/// <param name="Unticked">Unticked on the Tracking tab: out of every figure on the tab except the FC's own credits.</param>
/// <param name="ExcludedFromSubs">Unticked, or excluded from deployables in AutoRetainer: its hulls are out of the tank use.</param>
/// <param name="FcFromFleet">AutoRetainer holds no FC for the character; the FC is the one its Submarine Tracker fleet carries.</param>
public sealed record FcMember(string Name, string World, bool Unticked, bool ExcludedFromSubs, bool FcFromFleet = false);

/// <summary>One free company. Every figure is the FC's own, counted once however many characters belong to it.</summary>
/// <param name="Name">AutoRetainer's FC name; else Submarine Tracker's tag; else "FC" and the end of the id.</param>
/// <param name="Members">Every AutoRetainer character in the FC, in AutoRetainer's order.</param>
/// <param name="Rank">Null when not read.</param>
/// <param name="Points">Company credits; null when AutoRetainer never read them or has no entry for the FC.</param>
/// <param name="PointsAgeDays">Days since AutoRetainer read the credits; null when it never did.</param>
/// <param name="Stale">The credits were read longer ago than the stale setting.</param>
/// <param name="TanksBuyable">floor(credits / price); 0 below the rank needed; null without credits or a price.</param>
/// <param name="BuyNote">Why <see cref="TanksBuyable"/> is null or forced to 0, or that the rank behind it was not read; null otherwise.</param>
/// <param name="FarmingHulls">Hulls of the FC on a gil route now, of characters not excluded from submarine figures.</param>
/// <param name="LevellingHulls">The FC's other hulls with a route, likewise.</param>
/// <param name="TanksPerDay">Ceruleum tanks the FC's hulls burn per day; null without the economics.</param>
/// <param name="DaysFromPoints">Days of tanks the credits buy at today's use; null when the use is 0 or unknown.</param>
/// <param name="TanksOnHand">The ceruleum of the ticked members that own a hull (all ticked ones while the hulls are
/// unknown), as the fleet's stock counts it; null when none counts or any of them lacks the field.</param>
/// <param name="DaysWithStock">(buyable + on hand) / use; null when any part is unknown or the use is 0.</param>
/// <param name="DaysNote">Why the days are null; null when <see cref="DaysFromPoints"/> has a value.</param>
public sealed record FcPointsRow(
    ulong Id,
    string Name,
    IReadOnlyList<FcMember> Members,
    int? Rank,
    long? Points,
    DateTime? PointsUpdatedUtc,
    double? PointsAgeDays,
    bool Stale,
    long? TanksBuyable,
    string? BuyNote,
    int FarmingHulls,
    int LevellingHulls,
    double? TanksPerDay,
    double? DaysFromPoints,
    int? TanksOnHand,
    double? DaysWithStock,
    string? DaysNote)
{
    public bool RunsSubs => FarmingHulls + LevellingHulls > 0;

    /// <summary>The FC's rank is known and below the one tanks need: <see cref="TanksBuyable"/> is a forced 0.</summary>
    public bool RankBlocked { get; init; }
}

/// <summary>Sums over distinct FCs. There is no pooled days figure: credits cannot move between FCs.</summary>
/// <param name="Points">The credits of the FCs whose credits were read.</param>
/// <param name="PointsUnknown">FCs whose credits were never read, left out of <see cref="Points"/>.</param>
/// <param name="TanksBuyable">Over the FCs that have a figure.</param>
/// <param name="TanksPerDay">Null without the economics.</param>
/// <param name="TanksOnHand">Over the FCs with a member that counts for stock; null when one of them is unknown.</param>
/// <param name="ShortestDays">The fewest days the credits last across the FCs that run subs; null when none has a figure.</param>
/// <param name="ShortestName">That FC's name.</param>
public sealed record FcPointsTotals(
    long Points,
    int PointsUnknown,
    long TanksBuyable,
    double? TanksPerDay,
    int? TanksOnHand,
    double? ShortestDays,
    ulong? ShortestId,
    string? ShortestName);

/// <param name="Rows">One per FC the ticked characters belong to, ordered by name.</param>
/// <param name="OldCompanies">AutoRetainer FC entries no character belongs to any more; not rows.</param>
/// <param name="CompaniesRead">False when AutoRetainer's file has no FC data at all: every row's credits are then unknown.</param>
/// <param name="DaysUnavailable">Why no row has a tank use figure; null when the economics were there.</param>
/// <param name="UnmatchedHulls">Counted hulls that fell in no row (their FC is not one an AutoRetainer character belongs to).</param>
public sealed record FcPointsView(
    IReadOnlyList<FcPointsRow> Rows,
    FcPointsTotals Totals,
    int OldCompanies,
    bool CompaniesRead,
    TankPrice Price,
    int RankNeeded,
    int StaleDays,
    string? DaysUnavailable,
    int UnmatchedHulls);

public static class FcPointsMath
{
    /// <summary>The Ceruleum Tank's item id.</summary>
    public const uint TankItemId = 10155;

    /// <summary>Company credits per tank at the Company Credit Exchange.</summary>
    public const int DefaultTankPrice = 100;

    /// <summary>The FC rank the Company Credit Exchange sells tanks at.</summary>
    public const int RankNeeded = 6;

    public const int DefaultStaleDays = 7;

    public const string NotRunningSubs = "not running subs";
    public const string PointsNotRead = "credits not read yet: open the FC window on a member with AutoRetainer loaded";
    public const string NoPrice = "no tank price";

    /// <summary>The highest FC rank, the last row of the game's FCRank sheet.</summary>
    public const int MaxFcRank = 30;

    public static string RankTooLow(int rank, int needed) =>
        string.Create(CultureInfo.InvariantCulture, $"needs FC rank {needed} to buy tanks (rank {rank})");

    public static string RankNotRead(int needed) =>
        string.Create(CultureInfo.InvariantCulture, $"FC rank not read yet; tanks need rank {needed}");

    /// <summary>
    /// The FC view from AutoRetainer's characters and FC data and Submarine Tracker's current fleets (old FCs already
    /// split off). <paramref name="unticked"/> is the Tracking tab's ticks alone; <paramref name="subsExcluded"/> adds the
    /// characters AutoRetainer excludes from deployables, whose hulls leave the use but whose FC stays.
    /// <paramref name="rates"/> null means no tank use is known and <paramref name="daysUnavailable"/> says why.
    /// </summary>
    public static FcPointsView Compute(
        IReadOnlyList<AutoRetainerCharacter> characters,
        IReadOnlyDictionary<ulong, AutoRetainerCompany>? companies,
        IReadOnlyList<FreeCompanyFleet>? fleets,
        IReadOnlySet<string> unticked,
        IReadOnlySet<string> subsExcluded,
        ModelRates? rates,
        double fuelPerSector,
        TankPrice price,
        DateTime nowUtc,
        int staleDays = DefaultStaleDays,
        int rankNeeded = RankNeeded,
        EndgameRule rule = EndgameRule.ByRoute,
        string? daysUnavailable = null)
    {
        var fromFleet = new HashSet<AutoRetainerCharacter>(ReferenceEqualityComparer.Instance);
        var membersById = new Dictionary<ulong, List<AutoRetainerCharacter>>();
        var fcByName = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var (c, fcid, byFleet) in CompaniesOf(characters, fleets))
        {
            if (byFleet) fromFleet.Add(c);
            fcByName.TryAdd(c.Name, fcid);
            if (fcid == 0) continue;
            if (!membersById.TryGetValue(fcid, out var list)) membersById[fcid] = list = [];
            list.Add(c);
        }
        var old = companies?.Keys.Count(id => !membersById.ContainsKey(id)) ?? 0;

        bool IsUnticked(AutoRetainerCharacter c) => unticked.Contains(CharacterFilter.Key(c.Name, c.World));
        var shown = membersById.Where(kv => !kv.Value.All(IsUnticked)).Select(kv => kv.Key).ToHashSet();

        // a fleet whose FC id could not be read goes to the FC its character is in
        ulong FcOf(FreeCompanyFleet f) => f.FcId != 0 ? f.FcId : fcByName.GetValueOrDefault(f.CharacterName);
        IReadOnlyList<FreeCompanyFleet> counted = fleets is null ? [] : CharacterFilter.Apply(fleets, subsExcluded);
        var fleetsById = new Dictionary<ulong, List<FreeCompanyFleet>>();
        var unmatched = 0;
        foreach (var f in counted)
        {
            var id = FcOf(f);
            if (!shown.Contains(id)) { unmatched += f.Submarines.Count; continue; }
            if (!fleetsById.TryGetValue(id, out var list)) fleetsById[id] = list = [];
            list.Add(f);
        }

        // owners by name over every fleet given, as FleetTracking.JoinAll counts them; null when the fleet was not read
        var hullOwners = fleets?.Where(f => f.Submarines.Count > 0).Select(f => f.CharacterName).ToHashSet(StringComparer.Ordinal);
        var stockedRows = new HashSet<ulong>();
        var rows = new List<FcPointsRow>();
        foreach (var id in shown)
        {
            var members = membersById[id];
            AutoRetainerCompany? fc = null;
            companies?.TryGetValue(id, out fc);

            var (farming, levelling) = fleetsById.TryGetValue(id, out var own) ? FleetLevelling.EconomicsHulls(own, rule) : (0, 0);
            double? perDay = rates is null ? null : rates.SectorsPerDay(farming, levelling) * fuelPerSector;

            long? buyable;
            string? buyNote = null;
            var rankBlocked = false;
            if (price.Credits <= 0) { buyable = null; buyNote = NoPrice; }
            else if (fc?.Rank is { } rank && rank < rankNeeded) { buyable = 0; buyNote = RankTooLow(rank, rankNeeded); rankBlocked = true; }
            else if (fc?.Points is { } pts)
            {
                buyable = Math.Max(pts, 0) / price.Credits;
                // the figure stands, but the game may still refuse: say so
                if (fc.Rank is null) buyNote = RankNotRead(rankNeeded);
            }
            else { buyable = null; buyNote = PointsNotRead; }

            // the fleet's stock rule: ticked members that own a hull, or every ticked one while the hulls are unknown
            var stocked = members.Where(c => !IsUnticked(c) && (hullOwners is null || hullOwners.Contains(c.Name))).ToList();
            int? onHand = stocked.Count > 0 && stocked.All(c => c.Ceruleum.HasValue) ? stocked.Sum(c => c.Ceruleum!.Value) : null;
            if (stocked.Count > 0) stockedRows.Add(id);

            double? days = null, withStock = null;
            string? daysNote;
            if (perDay is null) daysNote = daysUnavailable ?? "no tank use figure yet";
            else if (perDay <= 0) daysNote = NotRunningSubs;
            else if (buyable is null) daysNote = buyNote;
            else
            {
                daysNote = null;
                days = buyable.Value / perDay.Value;
                if (onHand is { } h) withStock = (buyable.Value + h) / perDay.Value;
            }

            double? age = fc?.PointsUpdatedUtc is { } at ? Math.Max((nowUtc - at).TotalDays, 0) : null;
            rows.Add(new FcPointsRow(
                id,
                NameOf(id, fc, fleets, members),
                members.Select(c => new FcMember(c.Name, c.World, IsUnticked(c), subsExcluded.Contains(CharacterFilter.Key(c.Name, c.World)), fromFleet.Contains(c))).ToList(),
                fc?.Rank,
                fc?.Points,
                fc?.PointsUpdatedUtc,
                age,
                age is { } a && staleDays > 0 && a > staleDays,
                buyable,
                buyNote,
                farming,
                levelling,
                perDay,
                days,
                onHand,
                withStock,
                daysNote) { RankBlocked = rankBlocked });
        }
        rows.Sort((x, y) =>
        {
            var c = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            if (c == 0) c = StringComparer.Ordinal.Compare(x.Name, y.Name);
            return c != 0 ? c : x.Id.CompareTo(y.Id);
        });

        // an FC below the rank cannot buy at all: its 0 days is not a shortfall the credits could fix
        var shortest = rows.Where(r => r.RunsSubs && r.DaysFromPoints.HasValue && !r.RankBlocked).OrderBy(r => r.DaysFromPoints).FirstOrDefault();
        var stockRows = rows.Where(r => stockedRows.Contains(r.Id)).ToList();
        var totals = new FcPointsTotals(
            rows.Sum(r => r.Points ?? 0),
            rows.Count(r => r.Points is null),
            rows.Sum(r => r.TanksBuyable ?? 0),
            rates is null ? null : rows.Sum(r => r.TanksPerDay ?? 0),
            stockRows.Count > 0 && stockRows.All(r => r.TanksOnHand.HasValue) ? stockRows.Sum(r => r.TanksOnHand!.Value) : null,
            shortest?.DaysFromPoints,
            shortest?.Id,
            shortest?.Name);
        return new FcPointsView(rows, totals, old, companies is not null, price, rankNeeded, staleDays, rates is null ? daysUnavailable ?? "no tank use figure yet" : null, unmatched);
    }

    /// <summary>
    /// The FC each character is in: AutoRetainer's FCID, or for FCID 0 the one non-zero FC id its Submarine Tracker
    /// fleets carry (FromFleet); 0 when neither names one.
    /// </summary>
    public static IEnumerable<(AutoRetainerCharacter Character, ulong FcId, bool FromFleet)> CompaniesOf(
        IReadOnlyList<AutoRetainerCharacter> characters, IReadOnlyList<FreeCompanyFleet>? fleets)
    {
        // AutoRetainer can hold FCID 0 for a character whose FC it has not read; Submarine Tracker's fleet then names it
        var fleetFc = FleetFcByName(fleets);
        foreach (var c in characters)
        {
            if (c.Fcid == 0 && fleetFc.TryGetValue(c.Name, out var f) && f != 0) yield return (c, f, true);
            else yield return (c, c.Fcid, false);
        }
    }

    /// <summary>The game's rank for tanks when it was read and is a real FC rank; the usual 6 otherwise.</summary>
    public static int RankNeededFrom(int? fromGame) => fromGame is >= 1 and <= MaxFcRank ? fromGame.Value : RankNeeded;

    // the one non-zero FC id each character's fleets carry; a name with two different ones is left out, as unknown
    private static Dictionary<string, ulong> FleetFcByName(IReadOnlyList<FreeCompanyFleet>? fleets)
    {
        var d = new Dictionary<string, ulong>(StringComparer.Ordinal);
        if (fleets is null) return d;
        foreach (var f in fleets)
        {
            if (f.FcId == 0) continue;
            if (d.TryGetValue(f.CharacterName, out var seen)) { if (seen != f.FcId) d[f.CharacterName] = 0; }
            else d[f.CharacterName] = f.FcId;
        }
        return d;
    }

    private static string NameOf(ulong id, AutoRetainerCompany? fc, IReadOnlyList<FreeCompanyFleet>? fleets, List<AutoRetainerCharacter> members)
    {
        if (!string.IsNullOrWhiteSpace(fc?.Name)) return fc.Name;
        if (fleets is not null)
        {
            var names = members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            var tag = fleets.FirstOrDefault(f => f.FcId == id && f.Tag.Length > 0)?.Tag
                ?? fleets.FirstOrDefault(f => f.FcId == 0 && names.Contains(f.CharacterName) && f.Tag.Length > 0)?.Tag;
            if (tag is not null) return tag;
        }
        return string.Create(CultureInfo.InvariantCulture, $"FC {id % 100000:D5}");
    }
}
