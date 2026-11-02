namespace Drydock.Core.Fleet;

// Plain copies of the game's submarine sheets (SubmarineRank, SubmarineExploration, SubmarinePart).
// Core stays free of Lumina: the plugin fills these from the sheets.

/// <param name="Rank">The row id of SubmarineRank.</param>
/// <param name="ExpToNext">EXP needed to go from this rank to the next.</param>
/// <param name="Capacity">Non-zero for every rank the game has; the last such row is the max rank.</param>
public sealed record RankInfo(int Rank, long ExpToNext, int Capacity, int Surveillance, int Retrieval, int Speed, int Range, int Favor);

/// <param name="Id">The row id of SubmarineExploration, global across maps.</param>
/// <param name="Map">SubmarineMap row id.</param>
/// <param name="Letter">"A" to "AD"; empty on a start point.</param>
/// <param name="SurveyDurationMin">The sheet's SurveyDurationmin.</param>
/// <param name="CeruleumTankReq">Ceruleum tanks the sector costs.</param>
public sealed record SectorData(
    uint Id, int Map, string Letter, long ExpReward, int SurveyDurationMin, int X, int Y, int Z,
    int CeruleumTankReq, int RankReq, bool StartingPoint = false);

/// <param name="Id">The row id of SubmarinePart; the submarine table's Hull, Stern, Bow and Bridge columns hold these.</param>
public sealed record PartData(uint Id, int Class, int Slot, int Surveillance, int Retrieval, int Speed, int Range, int Favor, int RepairMaterials);

/// <summary>A hull's five stats: rank bonus plus its four parts.</summary>
public sealed record BuildStats(int Surveillance, int Retrieval, int Speed, int Range, int Favor);

public sealed class SubmarineGameData
{
    private readonly Dictionary<int, RankInfo> ranks;
    private readonly Dictionary<uint, SectorData> sectors;
    private readonly Dictionary<uint, PartData> parts;
    private readonly Dictionary<int, SectorData> startByMap;

    public SubmarineGameData(IEnumerable<RankInfo> ranks, IEnumerable<SectorData> sectors, IEnumerable<PartData> parts)
    {
        this.ranks = ranks.ToDictionary(r => r.Rank);
        this.sectors = sectors.ToDictionary(s => s.Id);
        this.parts = parts.ToDictionary(p => p.Id);
        startByMap = new();
        foreach (var s in this.sectors.Values.Where(s => s.StartingPoint).OrderBy(s => s.Id))
            startByMap.TryAdd(s.Map, s);
        // the last rank row with a capacity, as Submarine Tracker does
        MaxRank = this.ranks.Values.Where(r => r.Capacity != 0).Select(r => r.Rank).DefaultIfEmpty(0).Max();
    }

    public int MaxRank { get; }

    public IReadOnlyDictionary<uint, SectorData> Sectors => sectors;

    public IReadOnlyDictionary<int, RankInfo> Ranks => ranks;

    /// <summary>Sector row id to map and letter, for route labels; start points carry no letter and are left out.</summary>
    public IReadOnlyDictionary<uint, SectorInfo> SectorLabels() =>
        sectors.Values.Where(s => !s.StartingPoint && s.Letter.Length > 0).ToDictionary(s => s.Id, s => new SectorInfo(s.Map, s.Letter));

    public SectorData? StartOf(int map) => startByMap.GetValueOrDefault(map);

    public PartData? Part(int id) => id > 0 ? parts.GetValueOrDefault((uint)id) : null;

    /// <summary>
    /// The rank's stat bonus. Above the max rank the last row's bonus grows by one per rank, which is what
    /// Submarine Tracker predicts (Data/Build.cs, SubRank.From). Null when there is no rank table.
    /// </summary>
    public RankInfo? RankBonus(int rank)
    {
        if (MaxRank == 0 || rank < 1) return null;
        if (rank <= MaxRank) return ranks.GetValueOrDefault(rank);
        var last = ranks[MaxRank];
        var over = rank - MaxRank;
        return last with
        {
            Rank = rank,
            Surveillance = last.Surveillance + over,
            Retrieval = last.Retrieval + over,
            Speed = last.Speed + over,
            Range = last.Range + over,
            Favor = last.Favor + over,
        };
    }

    /// <summary>Rank bonus plus the four parts; null when the rank table or a part is missing.</summary>
    public BuildStats? StatsFor(int rank, int hull, int stern, int bow, int bridge)
    {
        if (RankBonus(rank) is not { } r) return null;
        if (Part(hull) is not { } h || Part(stern) is not { } s || Part(bow) is not { } b || Part(bridge) is not { } c) return null;
        return new BuildStats(
            r.Surveillance + h.Surveillance + s.Surveillance + b.Surveillance + c.Surveillance,
            r.Retrieval + h.Retrieval + s.Retrieval + b.Retrieval + c.Retrieval,
            r.Speed + h.Speed + s.Speed + b.Speed + c.Speed,
            r.Range + h.Range + s.Range + b.Range + c.Range,
            r.Favor + h.Favor + s.Favor + b.Favor + c.Favor);
    }

    /// <summary>EXP to climb from <paramref name="rank"/> to <paramref name="target"/>, before the hull's current EXP; null when a rank row is missing.</summary>
    public long? ExpBetween(int rank, int target)
    {
        long sum = 0;
        for (var r = rank; r < target; r++)
        {
            if (!ranks.TryGetValue(r, out var info)) return null;
            sum += info.ExpToNext;
        }
        return sum;
    }
}
