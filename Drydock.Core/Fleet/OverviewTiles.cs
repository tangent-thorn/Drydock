using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public static class OverviewTiles
{
    /// <summary>Farming hulls over all hulls; null when no hull has a route recorded, since nothing can be told farming from unknown.</summary>
    public static int? FarmingCount(IReadOnlyList<SubmarineRow> subs) =>
        subs.Count > 0 && subs.All(s => s.RouteClass == RouteClass.Unknown) ? null : subs.Count(s => s.RouteClass == RouteClass.Farming);
}
