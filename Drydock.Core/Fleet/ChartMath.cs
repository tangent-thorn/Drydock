namespace Drydock.Core.Fleet;

/// <summary>The arithmetic behind the plugin's own charts, kept out of the ImGui code.</summary>
public static class ChartMath
{
    /// <summary>
    /// The index of the point under <paramref name="mouseX"/> in a plot spanning <paramref name="minX"/> to
    /// <paramref name="maxX"/> with <paramref name="count"/> evenly spaced points; null outside the plot or with no points.
    /// </summary>
    public static int? IndexAt(float mouseX, float minX, float maxX, int count)
    {
        if (count <= 0 || maxX <= minX || mouseX < minX || mouseX > maxX) return null;
        var i = (int)((mouseX - minX) / (maxX - minX) * count);
        return Math.Clamp(i, 0, count - 1);
    }

    /// <summary>For a line, the point nearest the mouse: points sit at the ends and evenly between.</summary>
    public static int? NearestPoint(float mouseX, float minX, float maxX, int count)
    {
        if (count <= 0 || maxX <= minX || mouseX < minX || mouseX > maxX) return null;
        if (count == 1) return 0;
        var i = (int)Math.Round((mouseX - minX) / (maxX - minX) * (count - 1));
        return Math.Clamp(i, 0, count - 1);
    }

    /// <summary>
    /// Where a day sits on the x axis of a plot spanning <paramref name="minX"/> to <paramref name="maxX"/>, from the
    /// first to the last day. A single day (or none to span) sits at the left edge.
    /// </summary>
    public static float XAtDay(int day, int firstDay, int lastDay, float minX, float maxX) =>
        lastDay <= firstDay ? minX : minX + (maxX - minX) * (day - firstDay) / (lastDay - firstDay);

    /// <summary>
    /// For a line whose points sit at <paramref name="days"/> (ascending, day numbers), the point nearest the mouse by
    /// date, not by index; null outside the plot or with no points. A tie goes to the earlier point.
    /// </summary>
    public static int? NearestPointByDay(float mouseX, float minX, float maxX, IReadOnlyList<int> days)
    {
        if (days.Count == 0 || maxX <= minX || mouseX < minX || mouseX > maxX) return null;
        if (days.Count == 1) return 0;
        var first = days[0];
        var last = days[^1];
        if (last <= first) return 0;
        var day = first + (double)(mouseX - minX) / (maxX - minX) * (last - first);
        var best = 0;
        var bestGap = double.MaxValue;
        for (var i = 0; i < days.Count; i++)
        {
            var gap = Math.Abs(days[i] - day);
            if (gap < bestGap)
            {
                best = i;
                bestGap = gap;
            }
        }
        return best;
    }
}
