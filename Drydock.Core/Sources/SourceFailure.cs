using Microsoft.Data.Sqlite;

namespace Drydock.Core.Sources;

/// <summary>Plain wording for why a source could not be read, shared by every reader.</summary>
public static class SourceFailure
{
    /// <summary>Pause before the one retry of a read that hit a database another plugin was writing.</summary>
    public static int RetryDelayMs { get; set; } = 400;

    /// <summary>How long a read waits for a plugin that is writing its database before it gives up.</summary>
    public static int BusyTimeoutSeconds { get; set; } = 5;

    public static string Describe(Exception ex)
    {
        if (ex is SqliteException s)
        {
            return s.SqliteErrorCode switch
            {
                5 or 6 => "the database is locked by the plugin that owns it (SqliteException: " + s.Message + ")",
                11 or 26 => "the file is not a readable database, it may be corrupt or half written (SqliteException: " + s.Message + ")",
                14 => "the database file cannot be opened (SqliteException: " + s.Message + ")",
                _ => ex.GetType().Name + ": " + ex.Message,
            };
        }
        return ex.GetType().Name + ": " + ex.Message;
    }

    /// <summary>True for the failures that a second look a moment later can clear: a writer holding the file.</summary>
    public static bool IsTransient(Exception ex) =>
        ex is SqliteException { SqliteErrorCode: 5 or 6 or 11 or 26 } or IOException;

    /// <summary>Runs the read; one retry after a short pause when it failed in a way another plugin's write explains.</summary>
    public static T WithRetry<T>(Func<(T Value, Exception? Error)> read)
    {
        var first = read();
        if (first.Error is { } ex && IsTransient(ex))
        {
            // one blind retry isn't pretty, but ST and AR write in short bursts and a second look is usually enough
            Thread.Sleep(RetryDelayMs);
            return read().Value;
        }
        return first.Value;
    }
}
