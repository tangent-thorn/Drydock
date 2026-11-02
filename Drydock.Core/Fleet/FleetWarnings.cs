using System.Globalization;

namespace Drydock.Core.Fleet;

public enum WarningKind
{
    NoVoyages,
    Silent,
    LowSupplies,
    LowRunway,
}

public sealed record FleetWarning(WarningKind Kind, string Text);

public sealed record WarningThresholds(int SilentDays, int LowCeruleum, int LowRepairKits, int RunwayDays = 14)
{
    /// <summary>The collector's defaults.</summary>
    public static WarningThresholds Default { get; } = new(2, 1000, 100);
}

public static class FleetWarnings
{
    /// <summary>What the collector prints for the same condition; the plugin words it differently.</summary>
    public const string CollectorBlackout = "BLACKOUT: no voyages recorded by any character today";

    /// <summary>
    /// A warning needs a value that was read: a character AutoRetainer has no figure for raises nothing, and
    /// "no voyages today" is only said when every ticked character has a voyage record and all of them are zero.
    /// </summary>
    /// <param name="economics">When given, adds the fleet-wide runway warnings; null while the loot table is unread.</param>
    public static IReadOnlyList<FleetWarning> Compute(TrackingRead tracking, WarningThresholds t, IReadOnlySet<string> excluded, SalvageEconomics? economics = null)
    {
        var chars = tracking.Counted(excluded).ToList();
        var warnings = new List<FleetWarning>();
        if (chars.Count > 0 && chars.All(c => c.SentToday.HasValue) && chars.Sum(c => c.SentToday!.Value) == 0)
            warnings.Add(new(WarningKind.NoVoyages, "No voyages recorded today"));
        foreach (var c in chars)
        {
            if (c.DaysSilent is { } d && d >= t.SilentDays)
                warnings.Add(new(WarningKind.Silent, $"{c.Name} silent {d}d"));
        }
        foreach (var c in chars)
        {
            var lowCer = c.Ceruleum is { } cer && cer < t.LowCeruleum;
            var lowKits = c.RepairKits is { } kits && kits < t.LowRepairKits;
            if (lowCer || lowKits)
                warnings.Add(new(WarningKind.LowSupplies, $"{c.Name} low supplies (cer {Show(c.Ceruleum)}, kits {Show(c.RepairKits)})"));
        }
        if (economics is not null)
        {
            if (economics.CeruleumRunwayDays is { } cer && economics.CeruleumStock is { } cerStock && cer < t.RunwayDays)
                warnings.Add(new(WarningKind.LowRunway, string.Create(CultureInfo.InvariantCulture, $"Fleet ceruleum runway {Math.Round(cer, MidpointRounding.ToEven):0}d ({cerStock:N0} tanks)")));
            if (economics.RepairKitRunwayDays is { } kit && economics.RepairKitStock is { } kitStock && kit < t.RunwayDays)
                warnings.Add(new(WarningKind.LowRunway, string.Create(CultureInfo.InvariantCulture, $"Fleet repair kit runway {Math.Round(kit, MidpointRounding.ToEven):0}d ({kitStock:N0} kits)")));
        }
        return warnings;
    }

    private static string Show(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
