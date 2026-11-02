using System.Globalization;
using Drydock.Core.Fleet;
using Microsoft.Data.Sqlite;

namespace Drydock.Core.Sources;

public sealed record SubmarineRow(string Name, int Rank, DateTime ReturnUtc, uint CurrentExp, uint NextExp)
{
    /// <summary>Sector row ids of the current or last route, in sent order; empty when none is recorded.</summary>
    public IReadOnlyList<uint> Route { get; init; } = [];

    /// <summary>Submarine Tracker's hull id (a timestamp), the key its loot rows carry; 0 when the column is missing.</summary>
    public long SubmarineId { get; init; }

    /// <summary>SubmarinePart row ids of the fitted parts; 0 when the column is missing.</summary>
    public int Hull { get; init; }
    public int Stern { get; init; }
    public int Bow { get; init; }
    public int Bridge { get; init; }

    public RouteClass RouteClass => Routes.Classify(Route);
}

public sealed record FreeCompanyFleet(string CharacterName, string Tag, string World, IReadOnlyList<SubmarineRow> Submarines)
{
    public DateTime NextReturnUtc => Submarines.Count == 0 ? DateTime.MaxValue : Submarines[0].ReturnUtc;
}

public sealed record FleetRead(IReadOnlyList<FreeCompanyFleet>? Fleets, string? Error)
{
    public int Total => Fleets?.Sum(f => f.Submarines.Count) ?? 0;
}

/// <param name="Return">Unix seconds the voyage came back; every sector row of a voyage shares it.</param>
/// <param name="SubmarineId">Submarine Tracker's hull id (a timestamp).</param>
/// <param name="CharacterName">Owner of the company, empty when the company row is gone.</param>
/// <param name="Sector">The sector's row id in the game's SubmarineExploration sheet.</param>
public sealed record LootRow(uint PrimaryItem, uint PrimaryCount, uint AdditionalItem, uint AdditionalCount, long Return, long SubmarineId, int Rank, string CharacterName, uint Sector = 0);

/// <param name="HasSectors">False when the loot table has no Sector column (an older Submarine Tracker): the rows carry no route.</param>
public sealed record LootRead(IReadOnlyList<LootRow>? Rows, string? Error)
{
    public bool HasSectors { get; init; } = true;
}

// Reads another plugin's live WAL database, so failures come back as Error, never thrown.
public static class SubmarineTrackerDb
{
    private static readonly string[] SubmarineRequired = ["FreeCompanyId", "Name", "Rank", "Return", "CExp", "NExp"];
    private static readonly string[] LootRequired = ["PrimaryItem", "PrimaryCount", "AdditionalItem", "AdditionalCount", "Return", "SubmarineId", "Rank", "FreeCompanyId"];

    public static FleetRead Read(string dbPath) => SourceFailure.WithRetry(() => ReadOnce(dbPath));

    private static (FleetRead Value, Exception? Error) ReadOnce(string dbPath)
    {
        try
        {
            using var conn = Open(dbPath);
            var tables = Tables(conn);
            if (!tables.Contains("submarine"))
                return (new(null, "the database has no submarine table (a Submarine Tracker version Drydock does not read)"), null);
            var have = Columns(conn, "submarine");
            if (SubmarineRequired.Where(c => !have.Contains(c)).ToList() is { Count: > 0 } missing)
                return (new(null, "the submarine table lacks " + string.Join(", ", missing) + " (a Submarine Tracker version Drydock does not read)"), null);

            var hasCompanies = tables.Contains("freecompany");
            string Col(string name) => have.Contains(name) ? "s." + name : "null";
            using var cmd = conn.CreateCommand();
            // the FC id is a MessagePack blob; comparing the raw bytes is enough
            cmd.CommandText = $"""
                select s.FreeCompanyId, s.Name, s.Rank, s.Return, s.CExp, s.NExp,
                       {(hasCompanies ? "f.CharacterName, f.FreeCompanyTag, f.World" : "null, null, null")}, {Col("Route")},
                       {Col("SubmarineId")}, {Col("Hull")}, {Col("Stern")}, {Col("Bow")}, {Col("Bridge")}
                from submarine s
                {(hasCompanies ? "left join freecompany f on f.FreeCompanyId = s.FreeCompanyId" : "")}
                """;

            var groups = new Dictionary<string, (string Character, string Tag, string World, List<SubmarineRow> Subs)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r[0] is byte[] idBytes ? Convert.ToHexString(idBytes) : "";
                if (!groups.TryGetValue(id, out var g))
                {
                    g = (Str(r, 6, "?"), Str(r, 7, ""), Str(r, 8, ""), []);
                    groups[id] = g;
                }
                g.Subs.Add(new SubmarineRow(
                    Str(r, 1, ""),
                    (int)Math.Clamp(Long(r, 2), 0, int.MaxValue),
                    UnixToUtc(Long(r, 3)),
                    (uint)Math.Clamp(Long(r, 4), 0, uint.MaxValue),
                    (uint)Math.Clamp(Long(r, 5), 0, uint.MaxValue))
                {
                    Route = r[9] is byte[] blob ? Routes.Decode(blob) : [],
                    SubmarineId = Long(r, 10),
                    Hull = (int)Math.Clamp(Long(r, 11), 0, int.MaxValue),
                    Stern = (int)Math.Clamp(Long(r, 12), 0, int.MaxValue),
                    Bow = (int)Math.Clamp(Long(r, 13), 0, int.MaxValue),
                    Bridge = (int)Math.Clamp(Long(r, 14), 0, int.MaxValue),
                });
            }

            return (new(OrderFleets(groups.Values.Select(g => new FreeCompanyFleet(g.Character, g.Tag, g.World, g.Subs))), null), null);
        }
        catch (Exception ex)
        {
            return (new(null, SourceFailure.Describe(ex)), ex);
        }
    }

    /// <summary>Every valid sector of loot, one row each; the whole table is read, so call it once per refresh.</summary>
    public static LootRead ReadLoot(string dbPath) => SourceFailure.WithRetry(() => ReadLootOnce(dbPath));

    private static (LootRead Value, Exception? Error) ReadLootOnce(string dbPath)
    {
        try
        {
            using var conn = Open(dbPath);
            var tables = Tables(conn);
            if (!tables.Contains("loot"))
                return (new(null, "the database has no loot table (a Submarine Tracker version Drydock does not read)"), null);
            var have = Columns(conn, "loot");
            if (LootRequired.Where(c => !have.Contains(c)).ToList() is { Count: > 0 } missing)
                return (new(null, "the loot table lacks " + string.Join(", ", missing) + " (a Submarine Tracker version Drydock does not read)"), null);

            var hasCompanies = tables.Contains("freecompany");
            var hasSector = have.Contains("Sector");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                select l.PrimaryItem, l.PrimaryCount, l.AdditionalItem, l.AdditionalCount, l.Return, l.SubmarineId, l.Rank,
                       {(hasCompanies ? "f.CharacterName" : "null")}, {(hasSector ? "l.Sector" : "null")}
                from loot l
                {(hasCompanies ? "left join freecompany f on f.FreeCompanyId = l.FreeCompanyId" : "")}
                {(have.Contains("Valid") ? "where l.Valid = 1" : "")}
                """;
            var rows = new List<LootRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new LootRow(
                    U32(r, 0), U32(r, 1), U32(r, 2), U32(r, 3),
                    Long(r, 4), Long(r, 5), (int)Math.Clamp(Long(r, 6), 0, int.MaxValue), Str(r, 7, ""),
                    U32(r, 8)));
            }
            return (new(rows, null) { HasSectors = hasSector }, null);
        }
        catch (Exception ex)
        {
            return (new(null, SourceFailure.Describe(ex)), ex);
        }
    }

    private static SqliteConnection Open(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = SourceFailure.BusyTimeoutSeconds,
        }.ToString();
        var conn = new SqliteConnection(cs);
        try
        {
            conn.Open();
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static HashSet<string> Tables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select name from sqlite_master where type = 'table'";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    // older Submarine Tracker databases may lack a column
    private static HashSet<string> Columns(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select name from pragma_table_info('{table}')";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static long Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

    private static uint U32(SqliteDataReader r, int i) => (uint)Math.Clamp(Long(r, i), 0, uint.MaxValue);

    private static string Str(SqliteDataReader r, int i, string fallback) => r.IsDBNull(i) ? fallback : r.GetString(i);

    // a garbage timestamp (milliseconds, say) must not throw out of the whole read
    private const long MaxUnixSeconds = 253402300799;

    private static DateTime UnixToUtc(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, MaxUnixSeconds)).UtcDateTime;

    /// <summary>Submarines soonest first inside each company, companies by their soonest return.</summary>
    public static List<FreeCompanyFleet> OrderFleets(IEnumerable<FreeCompanyFleet> fleets) =>
        fleets
            .Select(f => f with { Submarines = f.Submarines.OrderBy(s => s.ReturnUtc).ThenBy(s => s.Name, StringComparer.Ordinal).ToList() })
            .OrderBy(f => f.NextReturnUtc)
            .ThenBy(f => f.CharacterName, StringComparer.Ordinal)
            .ToList();

    /// <summary>"Back" at or past zero, else "1d 3h", "2h 14m", "12m" or "59s"; ">99d" for a return no voyage can have.</summary>
    public static string FormatCountdown(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "Back";
        if (left.TotalDays >= 100) return ">99d";
        if (left.TotalDays >= 1) return string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalDays}d {left.Hours}h");
        if (left.TotalHours >= 1) return string.Create(CultureInfo.InvariantCulture, $"{left.Hours}h {left.Minutes}m");
        if (left.TotalMinutes >= 1) return string.Create(CultureInfo.InvariantCulture, $"{left.Minutes}m");
        return string.Create(CultureInfo.InvariantCulture, $"{left.Seconds}s");
    }
}
