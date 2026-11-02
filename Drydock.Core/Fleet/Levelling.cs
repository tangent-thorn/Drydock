using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public static class FleetLevelling
{
    /// <summary>Hull name to rank per character name, in the order Submarine Tracker lists them; the last duplicate wins.</summary>
    private static Dictionary<string, Dictionary<string, SubmarineRow>> HullsByCharacter(IReadOnlyList<FreeCompanyFleet> fleets)
    {
        var real = new Dictionary<string, Dictionary<string, SubmarineRow>>(StringComparer.Ordinal);
        foreach (var f in fleets)
        {
            if (!real.TryGetValue(f.CharacterName, out var hulls))
                real[f.CharacterName] = hulls = new(StringComparer.Ordinal);
            foreach (var s in f.Submarines) hulls[s.Name] = s;
        }
        return real;
    }

    /// <summary>All hulls, counted per character name like the others.</summary>
    public static int CountPerCharacter(IReadOnlyList<FreeCompanyFleet> fleets) =>
        HullsByCharacter(fleets).Values.Sum(h => h.Count);

    /// <summary>Hulls at rank 85 or more, counted per character name like the collector's at85.</summary>
    public static int CountAt85(IReadOnlyList<FreeCompanyFleet> fleets) =>
        HullsByCharacter(fleets).Values.Sum(h => h.Values.Count(s => s.Rank >= 85));

    /// <summary>Hulls whose current route is of this class, counted per character name.</summary>
    public static int CountByRoute(IReadOnlyList<FreeCompanyFleet> fleets, RouteClass cls) =>
        HullsByCharacter(fleets).Values.Sum(h => h.Values.Count(s => s.RouteClass == cls));

    /// <summary>Hulls on each gil route right now, keyed by route name, counted per character name.</summary>
    public static IReadOnlyDictionary<string, int> HullsPerGilRoute(IReadOnlyList<FreeCompanyFleet> fleets)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in HullsByCharacter(fleets).Values.SelectMany(h => h.Values))
        {
            if (Routes.FindGil(s.Route) is { } g) counts[g.Name] = counts.GetValueOrDefault(g.Name) + 1;
        }
        return counts;
    }
}
