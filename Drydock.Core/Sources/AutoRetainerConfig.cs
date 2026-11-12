using System.Text.Json;

namespace Drydock.Core.Sources;

/// <summary>
/// One AutoRetainer character. A number the file did not carry is null, never 0: only a value that was read is shown.
/// </summary>
/// <param name="Ceruleum">Null when the field is missing.</param>
/// <param name="RepairKits">Null when the field is missing.</param>
/// <param name="SentVoyagesByDay">Keyed by UTC epoch day; null when the field is missing (an empty object means nothing was ever sent).</param>
/// <param name="AdditionalSubmarines">AutoRetainer's own submarine records, in file order; they can outlive a real hull.</param>
public sealed record AutoRetainerCharacter(
    string Name,
    string World,
    ulong Cid,
    ulong Fcid,
    int? Ceruleum,
    int? RepairKits,
    IReadOnlyDictionary<long, int>? SentVoyagesByDay,
    IReadOnlyList<AutoRetainerSubmarine> AdditionalSubmarines)
{
    /// <summary>Gil on the character; null when the field is missing.</summary>
    public long? Gil { get; init; }

    /// <summary>The sum of the gil of the character's retainers; 0 when there are none, null when the list or any retainer's gil is missing.</summary>
    public long? RetainerGil { get; init; }

    /// <summary>AutoRetainer's "exclude from deployables" for the character; false when the field is missing.</summary>
    public bool ExcludeWorkshop { get; init; }

    public IEnumerable<string> AdditionalSubmarineNames => AdditionalSubmarines.Select(s => s.Name);
}

public sealed record AutoRetainerSubmarine(string Name, int Level, long CurrentExp, long NextLevelExp);

public sealed record AutoRetainerRead(IReadOnlyList<AutoRetainerCharacter>? Characters, string? Error);

// AutoRetainer rewrites this file while we read it, so failures come back as Error, never thrown.
public static class AutoRetainerConfig
{
    /// <summary>Pause before the one retry of a read that hit a half-written file.</summary>
    public static int RetryDelayMs { get; set; } = 250;

    public static AutoRetainerRead Read(string path)
    {
        var first = TryRead(path);
        if (first.Error is null || !Retryable(first.Error)) return first;
        Thread.Sleep(RetryDelayMs);
        return TryRead(path);
    }

    // a half-written file is bad JSON or a sharing violation; a missing file or a wrong shape is not worth a second try
    private static bool Retryable(string error) =>
        error.StartsWith(nameof(JsonException), StringComparison.Ordinal) || error.StartsWith(nameof(IOException), StringComparison.Ordinal);

    private static AutoRetainerRead TryRead(string path)
    {
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            if (bytes.Length == 0 || bytes.All(b => b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0))
                return new(null, "the file is empty (AutoRetainer may be rewriting it)");

            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            using var doc = JsonDocument.Parse(bytes.AsMemory(start));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new(null, "the file is not an AutoRetainer config (no top-level object)");
            if (!doc.RootElement.TryGetProperty("OfflineData", out var offline) || offline.ValueKind != JsonValueKind.Array)
                return new(null, "the file has no OfflineData list (not an AutoRetainer config, or a format Drydock does not know)");

            var list = new List<AutoRetainerCharacter>();
            foreach (var c in offline.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                var name = Str(c, "Name");
                if (name.Length == 0) continue;
                list.Add(new AutoRetainerCharacter(
                    name,
                    Str(c, "World"),
                    UInt(c, "CID"),
                    UInt(c, "FCID"),
                    Count(c, "Ceruleum"),
                    Count(c, "RepairKits"),
                    Sent(c),
                    Subs(c))
                {
                    Gil = Money(c, "Gil"),
                    RetainerGil = RetainerGilOf(c),
                    ExcludeWorkshop = c.TryGetProperty("ExcludeWorkshop", out var ew) && ew.ValueKind == JsonValueKind.True,
                });
            }
            return new(list, null);
        }
        catch (Exception ex)
        {
            return new(null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static ulong UInt(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var n) ? n : 0;

    private static int? Count(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? (int)Math.Clamp(n, 0, int.MaxValue)
            : null;

    private static long? Money(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static long? RetainerGilOf(JsonElement o)
    {
        if (!o.TryGetProperty("RetainerData", out var v) || v.ValueKind != JsonValueKind.Array) return null;
        long sum = 0;
        foreach (var r in v.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object || Money(r, "Gil") is not { } gil) return null;
            sum += gil;
        }
        return sum;
    }

    private static Dictionary<long, int>? Sent(JsonElement o)
    {
        if (!o.TryGetProperty("SentVoyagesByDay", out var v) || v.ValueKind != JsonValueKind.Object) return null;
        var d = new Dictionary<long, int>();
        foreach (var p in v.EnumerateObject())
        {
            if (long.TryParse(p.Name, out var day) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n))
                d[day] = n;
        }
        return d;
    }

    private static List<AutoRetainerSubmarine> Subs(JsonElement o)
    {
        var l = new List<AutoRetainerSubmarine>();
        if (o.TryGetProperty("AdditionalSubmarineData", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in v.EnumerateObject())
            {
                var e = p.Value;
                var isObj = e.ValueKind == JsonValueKind.Object;
                l.Add(new AutoRetainerSubmarine(
                    p.Name,
                    isObj ? (int)Math.Min(UInt(e, "Level"), int.MaxValue) : 0,
                    isObj ? (long)Math.Min(UInt(e, "CurrentExp"), long.MaxValue) : 0,
                    isObj ? (long)Math.Min(UInt(e, "NextLevelExp"), long.MaxValue) : 0));
            }
        }
        return l;
    }
}
