using System.Globalization;

namespace Drydock.Core.Fleet;

public enum RouteClass
{
    /// <summary>The hull's route is one of the gil routes.</summary>
    Farming,
    /// <summary>A route that is not a gil route: the hull is levelling.</summary>
    Levelling,
    /// <summary>No route recorded.</summary>
    Unknown,
}

/// <summary>A sector's map (SubmarineMap row id) and letter ("A" to "AD") from the SubmarineExploration sheet.</summary>
public sealed record SectorInfo(int Map, string Letter);

/// <param name="Name">Letters in the sheet's order, e.g. "OJ".</param>
/// <param name="Sectors">Map 1 sector ids; matching ignores order.</param>
/// <param name="SheetGil">Gil per submarine per voyage.</param>
/// <param name="SheetCycleHours">The cycle lengths the sheet lists for it.</param>
public sealed record GilRoute(string Name, IReadOnlySet<uint> Sectors, long SheetGil, IReadOnlyList<int> SheetCycleHours);

public static class Routes
{
    /// <summary>Map 1 (Deep-sea Site): sector row 1 is "A", 26 is "Z", 27 to 30 are "AA" to "AD"; row 0 is the start point.</summary>
    public static IReadOnlyDictionary<uint, SectorInfo> DefaultSectors { get; } = BuildMap1();

    private static Dictionary<uint, SectorInfo> BuildMap1()
    {
        var d = new Dictionary<uint, SectorInfo>();
        for (uint i = 1; i <= 30; i++)
            d[i] = new SectorInfo(1, i <= 26 ? ((char)('A' + i - 1)).ToString() : "A" + (char)('A' + i - 27));
        return d;
    }

    private static uint Id(char letter) => (uint)(letter - 'A' + 1);

    private static GilRoute Gil(string letters, long gil, params int[] cycles) =>
        new(letters, letters.Select(Id).ToHashSet(), gil, cycles);

    /// <summary>
    /// Expected gil per submarine per voyage, Patch 7.5.
    /// Routes are map 1 sector sets, so JO and OJ are the same route.
    /// </summary>
    public static IReadOnlyList<GilRoute> Gils { get; } =
    [
        Gil("OJ", 118_854, 24),
        Gil("MOJ", 140_721, 24, 36),
        Gil("ROJ", 159_540, 36),
        Gil("JOZ", 169_865, 36),
        Gil("MROJ", 181_406, 36, 48),
        Gil("MOJZ", 191_731, 36),
        Gil("JORZ", 210_550, 36, 48),
        Gil("MROJZ", 232_416, 36, 48),
        Gil("RZVOJ", 210_550, 36, 48),
        Gil("MRVOJ", 181_406, 36),
    ];

    /// <summary>The gil route whose sector set equals this one, order ignored; null for anything else (including empty).</summary>
    public static GilRoute? FindGil(IEnumerable<uint> sectors)
    {
        var set = sectors.ToHashSet();
        if (set.Count == 0) return null;
        return Gils.FirstOrDefault(g => g.Sectors.SetEquals(set));
    }

    public static RouteClass Classify(IEnumerable<uint> sectors)
    {
        var list = sectors as IReadOnlyCollection<uint> ?? sectors.ToList();
        if (list.Count == 0) return RouteClass.Unknown;
        return FindGil(list) is null ? RouteClass.Levelling : RouteClass.Farming;
    }

    /// <summary>Letters in sent order ("OJ"); an id the lookup lacks shows as "#id"; empty for no route.</summary>
    public static string Label(IReadOnlyList<uint> route, IReadOnlyDictionary<uint, SectorInfo>? lookup = null)
    {
        lookup ??= DefaultSectors;
        // ST can store a sector id our lookup doesn't have (sheet not read yet, or a patch added sectors).
        // "#id" is ugly but better than dropping it, since the route would read like a different one
        return string.Concat(route.Select(id =>
            lookup.TryGetValue(id, out var s) ? s.Letter : "#" + id.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Decodes the Route column, a MessagePack array of sector row ids; anything empty or garbled gives an empty route.</summary>
    public static IReadOnlyList<uint> Decode(byte[]? blob)
    {
        if (blob is null || blob.Length == 0) return [];
        try
        {
            int pos, count;
            var head = blob[0];
            if (head is >= 0x90 and <= 0x9f) { count = head & 0x0f; pos = 1; }
            else if (head == 0xdc)
            {
                if (blob.Length < 3) return [];
                count = (blob[1] << 8) | blob[2];
                pos = 3;
            }
            else return [];

            var ids = new List<uint>(count);
            for (var i = 0; i < count; i++)
            {
                if (pos >= blob.Length) return [];
                var b = blob[pos++];
                switch (b)
                {
                    case <= 0x7f:
                        ids.Add(b);
                        break;
                    case 0xcc:
                        if (pos + 1 > blob.Length) return [];
                        ids.Add(blob[pos]);
                        pos += 1;
                        break;
                    case 0xcd:
                        if (pos + 2 > blob.Length) return [];
                        ids.Add((uint)((blob[pos] << 8) | blob[pos + 1]));
                        pos += 2;
                        break;
                    case 0xce:
                        if (pos + 4 > blob.Length) return [];
                        ids.Add(((uint)blob[pos] << 24) | ((uint)blob[pos + 1] << 16) | ((uint)blob[pos + 2] << 8) | blob[pos + 3]);
                        pos += 4;
                        break;
                    default:
                        return [];
                }
            }
            return ids;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
