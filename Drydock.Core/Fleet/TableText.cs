using System.Globalization;

namespace Drydock.Core.Fleet;

/// <summary>
/// A table as tab separated text for the clipboard, so it pastes into a spreadsheet as cells: the header line, then one
/// line per row in the order given. Numbers go out whole and without separators, fractions to two places, dates as
/// yyyy-MM-dd, and a null as an empty cell. Tabs and line breaks inside a cell become spaces.
/// </summary>
public static class TableText
{
    public static string Build<T>(IReadOnlyList<string> headers, IEnumerable<T> rows, Func<T, object?[]> cells)
    {
        var lines = new List<string> { Line(headers) };
        foreach (var row in rows) lines.Add(Line(cells(row)));
        return string.Join("\n", lines);
    }

    public static string Cell(object? value) => value switch
    {
        null => "",
        string s => Clean(s),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => Clean(f.ToString(null, CultureInfo.InvariantCulture)),
        _ => Clean(value.ToString() ?? ""),
    };

    private static string Line(IEnumerable<object?> cells) => string.Join("\t", cells.Select(Cell));

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
