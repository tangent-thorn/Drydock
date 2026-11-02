using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Drydock.Core.Sources;

/// <summary>
/// One day of Drydock's own history. A null stock is a value whose source was missing that day
/// (no Allagan Tools, no AutoRetainer); it is never read as 0.
/// The whole history is fleet-wide: the character filter does not apply to it, so what was
/// recorded does not change when a character is ticked or unticked.
/// </summary>
/// <param name="Date">Local date, yyyy-MM-dd.</param>
/// <param name="WrittenUtc">Unix seconds of the last write of the day.</param>
/// <param name="SalvageDay">Salvage produced on that local date, at vendor price.</param>
public sealed record HistoryRow(
    string Date,
    long WrittenUtc,
    int Hulls,
    int Farming,
    long? Liquid,
    long? SalvageHeld,
    long? Position,
    long? Ceruleum,
    long? Kits,
    long LifetimeVoyages,
    long LifetimeSectors,
    long SalvageDay)
{
    // parsed once when the row is built; a date that does not parse still throws on access, as before
    private readonly DateOnly? dateValue =
        DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    public DateOnly DateValue => dateValue ?? DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed record HistoryRead(IReadOnlyList<HistoryRow>? Rows, int SchemaVersion, string? Error);

/// <summary>
/// Drydock's own daily history in a SQLite file of its own. Opens, works and closes on every call,
/// holds only the schema flag and the last written row between calls, and returns failures as an error string instead of throwing into the game.
/// </summary>
public sealed class HistoryStore(string dbPath)
{
    public const int SchemaVersion = 1;

    private const string Columns =
        "date, written_utc, hulls, farming, liquid, salvage_held, position, ceruleum, kits, lifetime_voyages, lifetime_sectors, salvage_day";

    public string Path { get; } = dbPath;

    // the schema is created once per file; deleting or moving the file aside arms it again
    private bool schemaReady;
    private HistoryRow? lastWritten;

    /// <summary>How many rows this instance has written.</summary>
    public int Writes { get; private set; }

    /// <summary>Whether a row differs from the one last written in anything but its write time; the first row of a run always does.</summary>
    public static bool Differs(HistoryRow? last, HistoryRow row) => last is null || last with { WrittenUtc = row.WrittenUtc } != row;

    /// <summary>Writes the row unless it equals the last one written in everything but WrittenUtc; null on success or when skipped.</summary>
    public string? UpsertIfChanged(HistoryRow row)
    {
        if (!Differs(lastWritten, row) && File.Exists(Path)) return null;
        var error = Upsert(row);
        if (error is null) lastWritten = row;
        return error;
    }

    /// <summary>Writes the day's row, replacing an earlier one of the same date; null on success.</summary>
    public string? Upsert(HistoryRow row)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"insert or replace into daily ({Columns}) values ($d, $w, $h, $f, $l, $sh, $p, $c, $k, $lv, $ls, $sd)";
            cmd.Parameters.AddWithValue("$d", row.Date);
            cmd.Parameters.AddWithValue("$w", row.WrittenUtc);
            cmd.Parameters.AddWithValue("$h", row.Hulls);
            cmd.Parameters.AddWithValue("$f", row.Farming);
            cmd.Parameters.AddWithValue("$l", Db(row.Liquid));
            cmd.Parameters.AddWithValue("$sh", Db(row.SalvageHeld));
            cmd.Parameters.AddWithValue("$p", Db(row.Position));
            cmd.Parameters.AddWithValue("$c", Db(row.Ceruleum));
            cmd.Parameters.AddWithValue("$k", Db(row.Kits));
            cmd.Parameters.AddWithValue("$lv", row.LifetimeVoyages);
            cmd.Parameters.AddWithValue("$ls", row.LifetimeSectors);
            cmd.Parameters.AddWithValue("$sd", row.SalvageDay);
            cmd.ExecuteNonQuery();
            lastWritten = row;
            Writes++;
            LastFailure = HistoryFailure.None;
            return null;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    /// <summary>Every stored day, oldest first.</summary>
    public HistoryRead ReadAll()
    {
        try
        {
            using var conn = schemaReady && File.Exists(Path) ? OpenReadOnly() : Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select {Columns} from daily order by date";
            var rows = new List<HistoryRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // a row whose date does not parse would throw out of every chart and the calibration
                if (!DateOnly.TryParseExact(r.IsDBNull(0) ? "" : r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                rows.Add(new HistoryRow(
                    r.GetString(0), r.GetInt64(1), (int)r.GetInt64(2), (int)r.GetInt64(3),
                    Nullable(r, 4), Nullable(r, 5), Nullable(r, 6), Nullable(r, 7), Nullable(r, 8),
                    r.GetInt64(9), r.GetInt64(10), r.GetInt64(11)));
            }
            LastFailure = HistoryFailure.None;
            return new(rows, SchemaVersion, null);
        }
        catch (Exception ex)
        {
            return new(null, 0, Fail(ex));
        }
    }

    /// <summary>Removes every stored day and keeps the file and its schema; null on success.</summary>
    public string? DeleteAll()
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "delete from daily";
            cmd.ExecuteNonQuery();
            lastWritten = null;
            LastFailure = HistoryFailure.None;
            return null;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    /// <summary>The schema_version in the file's meta table, or an error.</summary>
    public (int? Version, string? Error) ReadSchemaVersion()
    {
        try
        {
            using var conn = Open();
            LastFailure = HistoryFailure.None;
            return (StoredVersion(conn), null);
        }
        catch (Exception ex)
        {
            return (null, Fail(ex));
        }
    }

    private SqliteConnection OpenReadOnly()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            conn.Open();
            var v = StoredVersion(conn);
            if (v != SchemaVersion)
                throw new HistorySchemaException(v);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    // Creates the folder, file and tables on the first call for a file, and refuses a file of a newer schema.
    private SqliteConnection Open()
    {
        if (!schemaReady || !File.Exists(Path))
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            schemaReady = false;
        }

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var conn = new SqliteConnection(cs);
        try
        {
            conn.Open();
            if (!schemaReady)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    create table if not exists daily (
                        date text primary key,
                        written_utc integer not null,
                        hulls integer not null,
                        farming integer not null,
                        liquid integer null,
                        salvage_held integer null,
                        position integer null,
                        ceruleum integer null,
                        kits integer null,
                        lifetime_voyages integer not null,
                        lifetime_sectors integer not null,
                        salvage_day integer not null
                    );
                    create table if not exists meta (key text primary key, value text);
                    insert or ignore into meta (key, value) values ('schema_version', '1');
                    """;
                cmd.ExecuteNonQuery();
            }
            var v = StoredVersion(conn);
            if (v != SchemaVersion)
                throw new HistorySchemaException(v);
            schemaReady = true;
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static int StoredVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select value from meta where key = 'schema_version'";
        return int.TryParse(cmd.ExecuteScalar() as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static object Db(long? v) => v.HasValue ? v.Value : DBNull.Value;

    private static long? Nullable(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    /// <summary>What kind of problem the last failed call had; None after a call that worked.</summary>
    public HistoryFailure LastFailure { get; private set; }

    private string Fail(Exception ex)
    {
        schemaReady = false;
        if (ex is HistorySchemaException schema)
        {
            LastFailure = schema.Version > SchemaVersion ? HistoryFailure.NewerSchema : HistoryFailure.Corrupt;
            return schema.Version > SchemaVersion
                ? $"history.db was written by a newer Drydock (schema {schema.Version}, this one reads {SchemaVersion}). Update Drydock, or move the file aside to start a new history."
                : $"history.db has an unreadable schema version ({schema.Version}). Move the file aside to start a new history.";
        }
        var text = SourceFailure.Describe(ex);
        LastFailure = ex is SqliteException { SqliteErrorCode: 11 or 26 } ? HistoryFailure.Corrupt : HistoryFailure.Other;
        return LastFailure == HistoryFailure.Corrupt ? "history.db is not a readable database. Move the file aside to start a new history. " + text : text;
    }

    /// <summary>
    /// Renames history.db (and its -wal and -shm) to history.db.bak, replacing an earlier .bak, so the next write starts a
    /// new file; for a file that is corrupt or from a newer Drydock. Null on success.
    /// </summary>
    public string? MoveAside()
    {
        try
        {
            schemaReady = false;
            lastWritten = null;
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var from = Path + suffix;
                if (File.Exists(from)) File.Move(from, Path + ".bak" + suffix, true);
            }
            LastFailure = HistoryFailure.None;
            return null;
        }
        catch (Exception ex)
        {
            return SourceFailure.Describe(ex);
        }
    }
}

public enum HistoryFailure
{
    None,
    /// <summary>Not a database, or a schema version that makes no sense: moving the file aside is the way out.</summary>
    Corrupt,
    /// <summary>Written by a newer Drydock.</summary>
    NewerSchema,
    /// <summary>Locked, unwritable folder, full disk: nothing to do with the file's content.</summary>
    Other,
}

internal sealed class HistorySchemaException(int version) : Exception($"history.db has schema version {version}")
{
    public int Version { get; } = version;
}
