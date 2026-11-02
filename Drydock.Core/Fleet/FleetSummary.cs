using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <param name="Text">Null hides the bar entry.</param>
public sealed record InfoBarView(string? Text, string Tooltip);

public static class FleetSummary
{
    public const string DefaultTooltip = "Drydock: open the fleet";

    /// <summary>"Subs 3 back, next in 1h 12m", "Subs next in 1h 12m", "Subs 76 back"; null when there are no submarines.</summary>
    public static string? InfoBarText(IReadOnlyList<FreeCompanyFleet> fleets, DateTime nowUtc)
    {
        var back = 0;
        DateTime? next = null;
        foreach (var s in fleets.SelectMany(f => f.Submarines))
        {
            if (s.ReturnUtc <= nowUtc) back++;
            else if (next is null || s.ReturnUtc < next) next = s.ReturnUtc;
        }

        if (next is null) return back == 0 ? null : $"Subs {back} back";
        var countdown = "next in " + SubmarineTrackerDb.FormatCountdown(next.Value - nowUtc);
        return back == 0 ? "Subs " + countdown : $"Subs {back} back, {countdown}";
    }

    /// <summary>
    /// What the info bar shows. A count is only shown when the fleet was read and is fresh: otherwise the entry says
    /// there is no data (or that it is stale) and the tooltip says why, instead of a "0 back" that was never counted.
    /// </summary>
    public static InfoBarView InfoBar(
        bool enabled,
        IReadOnlyList<PluginCheckResult> results,
        FleetRead? fleet,
        IReadOnlySet<string> excluded,
        DateTime nowUtc)
    {
        var hidden = new InfoBarView(null, DefaultTooltip);
        if (!enabled || results.Count == 0) return hidden;

        var st = results.FirstOrDefault(r => r.Requirement == PluginRequirements.SubmarineTracker);
        if (st == null)
            return hidden;
        if (st.Status is not (PluginStatus.Ready or PluginStatus.Stale))
            return new("Subs: no data", $"{st.Requirement.DisplayName}: {st.Detail} {st.Fix}".TrimEnd());
        if (st.Status == PluginStatus.Stale)
            return new($"Subs: stale {(st.DataAge is { } age ? PluginChecker.FormatAge(age) : "")}".TrimEnd(), $"{st.Detail} {st.Fix}".TrimEnd());

        if (fleet is null) return hidden;
        if (fleet.Fleets is not { } fleets)
            return new("Subs: no data", "Submarine Tracker's database could not be read: " + fleet.Error);
        if (fleets.Sum(f => f.Submarines.Count) == 0)
            return new("Subs: none", "Submarine Tracker has no submarines recorded yet.");

        var ticked = CharacterFilter.Apply(fleets, excluded);
        if (ticked.Sum(f => f.Submarines.Count) == 0)
            return new("Subs: none ticked", "Every character is unticked on the Tracking tab.");
        return new(InfoBarText(ticked, nowUtc), DefaultTooltip);
    }
}
