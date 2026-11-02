using System.Globalization;

namespace Drydock.Core.Fleet;

/// <summary>One date style for every label, tooltip and overlay: "Aug 4", with the year only when it is not the current one.</summary>
public static class DateLabels
{
    public static string Short(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "MMM d" : "MMM d yyyy", CultureInfo.InvariantCulture);

    public static string Range(DateOnly first, DateOnly last, DateOnly today) =>
        $"{Short(first, today)} to {Short(last, today)}";
}
