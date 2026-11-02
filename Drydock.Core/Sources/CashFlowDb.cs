using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Drydock.Core.Sources;

/// <param name="Price">The median price; null with fewer than <see cref="CashFlowDb.MinSamples"/> purchases.</param>
/// <param name="Samples">Purchases in the window.</param>
public sealed record KitPriceRead(long? Price, int Samples, string? Error);

/// <summary>The kit price in use and where it came from.</summary>
public sealed record KitPriceChoice(int Price, bool FromCashFlow, int Samples)
{
    public string Text => FromCashFlow
        ? string.Create(CultureInfo.InvariantCulture, $"using {Price:N0} from {Samples} CashFlow purchases")
        : "using the setting";

    public static KitPriceChoice Choose(KitPriceRead? read, int setting) =>
        read is { Error: null, Price: { } p } ? new((int)Math.Min(p, int.MaxValue), true, read.Samples) : new(setting, false, 0);

    /// <summary>Why the setting is used, for the line shown beside the figure: the figure is the setting's, not a measurement.</summary>
    public static string Explain(KitPriceRead? read, bool cashFlowUsable, int setting)
    {
        var why = !cashFlowUsable ? "CashFlow is not available"
            : read is null ? "CashFlow has not been read yet"
            : read.Error is not null ? "CashFlow's database could not be read"
            : read.Price is null ? string.Create(CultureInfo.InvariantCulture, $"CashFlow has {read.Samples} of {CashFlowDb.MinSamples} purchases it needs")
            : null;
        return why is null ? Choose(read, setting).Text : $"using the setting ({why})";
    }
}

// Reads another plugin's live WAL database, so failures come back as Error, never thrown.
public static class CashFlowDb
{
    /// <summary>Magitek Repair Materials.</summary>
    public const int RepairKitItem = 10373;
    public const int WindowDays = 120;
    public const int MinSamples = 5;

    /// <summary>
    /// Unix milliseconds of <paramref name="localNow"/> minus the window, taken on the wall clock of
    /// <paramref name="zone"/> like the collector's naive local datetime.
    /// </summary>
    public static long CutoffMs(DateTime localNow, TimeZoneInfo zone)
    {
        var t = DateTime.SpecifyKind(localNow.AddDays(-WindowDays), DateTimeKind.Unspecified);
        return new DateTimeOffset(t, zone.GetUtcOffset(t)).ToUnixTimeMilliseconds();
    }

    /// <summary>The element at index n/2 of the sorted prices (the upper one for an even count); null below the sample minimum.</summary>
    public static long? Median(IEnumerable<long> prices)
    {
        var sorted = prices.Order().ToList();
        return sorted.Count < MinSamples ? null : sorted[sorted.Count / 2];
    }

    public static KitPriceRead ReadKitPrice(string dbPath, DateTime localNow, TimeZoneInfo zone) =>
        SourceFailure.WithRetry(() => ReadKitPriceOnce(dbPath, localNow, zone));

    private static (KitPriceRead Value, Exception? Error) ReadKitPriceOnce(string dbPath, DateTime localNow, TimeZoneInfo zone)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = SourceFailure.BusyTimeoutSeconds,
            }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using (var probe = conn.CreateCommand())
            {
                probe.CommandText = "select name from pragma_table_info('ShopPurchases')";
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var pr = probe.ExecuteReader();
                while (pr.Read()) columns.Add(pr.GetString(0));
                if (columns.Count == 0)
                    return (new(null, 0, "the database has no ShopPurchases table (a CashFlow version Drydock does not read)"), null);
                if (new[] { "Item", "Price", "UnixTime" }.Where(c => !columns.Contains(c)).ToList() is { Count: > 0 } missing)
                    return (new(null, 0, "ShopPurchases lacks " + string.Join(", ", missing) + " (a CashFlow version Drydock does not read)"), null);
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "select Price from ShopPurchases where Item = $item and Price > 0 and UnixTime >= $cut";
            cmd.Parameters.AddWithValue("$item", RepairKitItem);
            cmd.Parameters.AddWithValue("$cut", CutoffMs(localNow, zone));
            var prices = new List<long>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!r.IsDBNull(0)) prices.Add(r.GetInt64(0));
            }
            return (new(Median(prices), prices.Count, null), null);
        }
        catch (Exception ex)
        {
            return (new(null, 0, SourceFailure.Describe(ex)), ex);
        }
    }
}
