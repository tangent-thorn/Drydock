using System.Globalization;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public sealed record TileView(string Value, string Label, string? Extra = null);

/// <summary>What each Overview tile shows, so a figure that was not read is "-" with the reason and never a 0.</summary>
public static class OverviewTiles
{
    /// <summary>Farming hulls over all hulls; null when no hull has a route recorded, since nothing can be told farming from unknown.</summary>
    public static int? FarmingCount(IReadOnlyList<SubmarineRow> subs) =>
        subs.Count > 0 && subs.All(s => s.RouteClass == RouteClass.Unknown) ? null : subs.Count(s => s.RouteClass == RouteClass.Farming);

    public static IReadOnlyList<TileView> Compute(
        TrackingRead tracking,
        IReadOnlyList<FreeCompanyFleet> ticked,
        IReadOnlySet<string> excluded,
        DateTime nowUtc,
        SalvageEconomics? economics,
        string? economicsReason,
        SalvageBacklog? backlog,
        string? backlogReason)
    {
        var subs = ticked.SelectMany(f => f.Submarines).ToList();
        var chars = tracking.Counted(excluded).ToList();
        var known = chars.Where(c => c.SentToday.HasValue).ToList();
        var unknown = chars.Count - known.Count;

        TileView sent = known.Count == 0
            ? new("-", "Voyages today", "no voyage record in AutoRetainer")
            : new(known.Sum(c => c.SentToday!.Value).ToString(CultureInfo.InvariantCulture), "Voyages today",
                unknown == 0 ? null : $"{unknown} {(unknown == 1 ? "character" : "characters")} without a record");

        var salvage = economics is null
            ? new TileView("-", "Salvage today", economicsReason)
            : new TileView(SalvageMath.FormatGil(economics.Today), "Salvage today");

        var unsold = backlog is null
            ? new TileView("-", "Unsold salvage", TabGates.Short(backlogReason))
            : new TileView(SalvageMath.FormatGil(backlog.TotalValue), "Unsold salvage", $"{backlog.TotalItems:N0} items");

        var farming = FarmingCount(subs) is { } f
            ? new TileView($"{f} of {subs.Count}", "Farming")
            : new TileView("-", "Farming", "no routes recorded yet");

        var back = subs.Count(s => s.ReturnUtc <= nowUtc);
        var pending = subs.Where(s => s.ReturnUtc > nowUtc).Select(s => s.ReturnUtc).ToList();
        var next = pending.Count == 0 ? "all back" : "next in " + SubmarineTrackerDb.FormatCountdown(pending.Min() - nowUtc);

        return [sent, salvage, unsold, farming, new TileView(back.ToString(CultureInfo.InvariantCulture), "Back now", next)];
    }
}
