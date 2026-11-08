namespace Drydock.Core.Fleet;

/// <summary>
/// The order of a table after a click on one of its headers. Rows whose key is null (shown as "-") go last in both
/// directions, rows with equal keys keep the order Drydock gave them, and text compares without regard to case.
/// </summary>
public static class TableSort
{
    public static IReadOnlyList<T> By<T>(IReadOnlyList<T> rows, Func<T, IComparable?> key, bool descending)
    {
        var known = new List<(T Row, IComparable Key)>(rows.Count);
        var unknown = new List<T>();
        foreach (var row in rows)
        {
            if (key(row) is { } k) known.Add((row, k));
            else unknown.Add(row);
        }
        var sorted = descending
            ? known.OrderByDescending(x => x.Key, KeyComparer.Instance)
            : known.OrderBy(x => x.Key, KeyComparer.Instance);
        return [.. sorted.Select(x => x.Row), .. unknown];
    }

    private sealed class KeyComparer : IComparer<IComparable>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(IComparable? a, IComparable? b) =>
            a is string sa && b is string sb ? StringComparer.OrdinalIgnoreCase.Compare(sa, sb) : a!.CompareTo(b);
    }
}
