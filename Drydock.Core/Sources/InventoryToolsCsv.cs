using System.Globalization;
using System.Text;

namespace Drydock.Core.Sources;

/// <summary>One stack from an item inventory. <paramref name="OwnerId"/> is a character or retainer id; null when the row has none.</summary>
public sealed record InventoryRow(uint ItemId, long Quantity, ulong? OwnerId);

public sealed record InventoryRead(IReadOnlyList<InventoryRow>? Rows, string? Error);

/// <summary>
/// Allagan Tools' inventories.csv: no header, one stack per row, written by the plugin whenever its
/// inventories change. Column 2 is the item id, 3 the quantity, 23 the owner (character or retainer id).
/// Everything else Drydock needs from it goes through <see cref="InventoryRow"/>, so the source can change.
/// </summary>
public static class InventoryToolsCsv
{
    private const int ItemColumn = 2;
    private const int QuantityColumn = 3;
    // The file has no header to validate these against; OwnersLookRight is the guard when the columns move.
    private const int OwnerColumn = 23;

    /// <summary>Owner rows below this count say too little to call the format wrong.</summary>
    public const int OwnerCheckMinRows = 20;

    /// <summary>
    /// Whether the owner column still holds character ids. The columns are blind indices into a header-less file, so
    /// if Allagan Tools adds or moves one, the owner cell silently becomes some other number and every stack lands on
    /// the wrong owner. With enough owner-carrying rows, at least one owner must be a character id AutoRetainer knows
    /// (the plugins see the same logins); when none is, the format has changed and the read must not be trusted.
    /// </summary>
    public static bool OwnersLookRight(IReadOnlyList<InventoryRow> rows, IEnumerable<ulong> knownCids)
    {
        var owners = new HashSet<ulong>();
        var owned = 0;
        foreach (var r in rows)
        {
            if (r.OwnerId is { } o)
            {
                owned++;
                owners.Add(o);
            }
        }
        if (owned < OwnerCheckMinRows) return true;
        var any = false;
        foreach (var cid in knownCids)
        {
            if (cid == 0) continue;
            any = true;
            if (owners.Contains(cid)) return true;
        }
        return !any;
    }

    // The plugin rewrites this file while we read it, so failures come back as Error, never thrown.
    public static InventoryRead Read(string path)
    {
        var (read, retry) = TryRead(path);
        if (!retry) return read;
        Thread.Sleep(SourceFailure.RetryDelayMs);
        return TryRead(path).Read;
    }

    // Retry is true when the file looked half written: it changed while we read it, or it was empty.
    private static (InventoryRead Read, bool Retry) TryRead(string path)
    {
        try
        {
            var before = Stamp(path);
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                text = sr.ReadToEnd();
            var changed = Stamp(path) != before;

            if (string.IsNullOrWhiteSpace(text))
                return (new(null, "inventories.csv is empty (Allagan Tools may be rewriting it)"), true);
            // the plugin ends every row with a line break, so a last row without one was cut off mid-write
            var lastBreak = text.LastIndexOfAny(['\n', '\r']);
            if (lastBreak < text.Length - 1) text = lastBreak < 0 ? "" : text[..(lastBreak + 1)];

            var rows = Parse(text);
            if (rows.Count == 0)
                return (new(null, "no inventory rows could be read from inventories.csv (empty, cut off, or a format Drydock does not know)"), true);
            return (new(rows, null), changed);
        }
        catch (Exception ex)
        {
            return (new(null, SourceFailure.Describe(ex)), ex is IOException && ex is not FileNotFoundException && ex is not DirectoryNotFoundException);
        }
    }

    private static (long Length, DateTime Written) Stamp(string path)
    {
        var f = new FileInfo(path);
        return (f.Length, f.LastWriteTimeUtc);
    }

    /// <summary>Rows that do not parse (a header, a short or empty line) are skipped.</summary>
    public static List<InventoryRow> Parse(string text)
    {
        var rows = new List<InventoryRow>();
        foreach (var f in Fields(text))
        {
            if (f.Count <= QuantityColumn) continue;
            if (!uint.TryParse(f[ItemColumn], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
            if (!long.TryParse(f[QuantityColumn], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var qty)) continue;
            ulong? owner = f.Count > OwnerColumn && ulong.TryParse(f[OwnerColumn], NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : null;
            rows.Add(new InventoryRow(id, qty, owner));
        }
        return rows;
    }

    /// <summary>RFC 4180: quoted fields may hold commas, doubled quotes and line breaks.</summary>
    public static IEnumerable<List<string>> Fields(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var row = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    row.Add(sb.ToString());
                    sb.Clear();
                    any = true;
                    break;
                case '\r':
                case '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    if (any || sb.Length > 0)
                    {
                        row.Add(sb.ToString());
                        yield return row;
                    }
                    row = new List<string>();
                    sb.Clear();
                    any = false;
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        if (any || sb.Length > 0)
        {
            row.Add(sb.ToString());
            yield return row;
        }
    }
}
