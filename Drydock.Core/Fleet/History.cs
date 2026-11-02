using System.Globalization;
using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <summary>
/// What the stored days say about real use.
/// </summary>
/// <param name="SectorsSampled">Sectors over the day pairs that count.</param>
/// <param name="Days">Stored days.</param>
/// <param name="KitsPerSector">Null until <see cref="CalibrationMath.MinSectors"/> sectors were sampled and a kit was used.</param>
/// <param name="FuelPerSector">Likewise for ceruleum.</param>
/// <param name="RealizedPerDay">Position moved per day, first to last row with a position; null under a day of span.</param>
/// <param name="RealizedDays">The span in days.</param>
public sealed record Calibration(
    long SectorsSampled,
    int Days,
    double? KitsPerSector,
    double? FuelPerSector,
    long? RealizedPerDay,
    int? RealizedDays);

/// <summary>A port of the collector's calibrate(), on Drydock's own history instead of the Python snapshots.</summary>
public static class CalibrationMath
{
    /// <summary>Do not trust a calibration thinner than this many sectors.</summary>
    // 400 is a judgement call, roughly 2-3 days of a full OJ fleet. any less and one restock day skews it
    public const int MinSectors = 400;

    /// <summary>
    /// Stock only falls through use and rises through purchase, so use is the sum of the falls between
    /// consecutive stored days; sectors come from the lifetime counters, which makes the delta exact.
    /// A pair with a day that has no stock, or no new sectors, is skipped.
    /// </summary>
    public static Calibration Compute(IReadOnlyList<HistoryRow> history)
    {
        var rows = history.OrderBy(r => r.Date, StringComparer.Ordinal).ToList();
        long kits = 0, cer = 0, secs = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var a = rows[i - 1];
            var b = rows[i];
            if (a.Kits is not { } ak || b.Kits is not { } bk || a.Ceruleum is not { } ac || b.Ceruleum is not { } bc) continue;
            var ds = b.LifetimeSectors - a.LifetimeSectors;
            if (ds <= 0) continue;
            secs += ds;
            var dk = ak - bk;
            var dc = ac - bc;
            if (dk > 0) kits += dk;
            if (dc > 0) cer += dc;
        }

        double? kitRate = null, fuelRate = null;
        if (secs >= MinSectors)
        {
            if (kits != 0) kitRate = Math.Round((double)kits / secs, 4, MidpointRounding.ToEven);
            if (cer != 0) fuelRate = Math.Round((double)cer / secs, 4, MidpointRounding.ToEven);
        }

        // realized gil per day from the position moving, the counterweight to the modelled rate
        var pts = rows.Where(r => r.Position.HasValue).Select(r => (Date: r.DateValue, Position: r.Position!.Value)).ToList();
        long? realized = null;
        int? realizedDays = null;
        if (pts.Count >= 2)
        {
            var span = pts[^1].Date.DayNumber - pts[0].Date.DayNumber;
            if (span >= 1)
            {
                realized = (long)Math.Round((double)(pts[^1].Position - pts[0].Position) / span, MidpointRounding.ToEven);
                realizedDays = span;
            }
        }
        return new(secs, rows.Count, kitRate, fuelRate, realized, realizedDays);
    }
}

/// <summary>The per-sector use the economics run on, and where it came from.</summary>
public sealed record RateChoice(double FuelPerSector, double KitsPerSector, bool FuelMeasured, bool KitsMeasured, long Sectors)
{
    public string Text =>
        FuelMeasured && KitsMeasured ? string.Create(CultureInfo.InvariantCulture, $"measured over {Sectors:N0} sectors")
        : FuelMeasured ? string.Create(CultureInfo.InvariantCulture, $"ceruleum measured over {Sectors:N0} sectors, kits from settings")
        : KitsMeasured ? string.Create(CultureInfo.InvariantCulture, $"kits measured over {Sectors:N0} sectors, ceruleum from settings")
        : "settings";

    /// <summary>Each rate on its own: the measured one when it exists and is wanted, else the setting.</summary>
    public static RateChoice Choose(Calibration? cal, bool useMeasured, double fuelSetting, double kitsSetting)
    {
        var fuel = useMeasured ? cal?.FuelPerSector : null;
        var kits = useMeasured ? cal?.KitsPerSector : null;
        return new(fuel ?? fuelSetting, kits ?? kitsSetting, fuel.HasValue, kits.HasValue, cal?.SectorsSampled ?? 0);
    }
}

public static class HistoryRecorder
{
    /// <summary>
    /// Whether today's row may be written. A row is a fact about the day, so it needs a fresh, read, non-empty
    /// fleet and loot table: stale files would write old numbers as today's, and an empty fleet a row of zeros.
    /// </summary>
    public static bool ShouldRecord(IReadOnlyList<PluginCheckResult> results, FleetRead? fleet, LootRead? loot)
    {
        var st = TabGates.Result(results, PluginRequirements.SubmarineTracker);
        return st?.Status == PluginStatus.Ready
            && fleet?.Fleets is { } fleets && fleets.Sum(f => f.Submarines.Count) > 0
            && loot?.Rows is not null;
    }

    /// <summary>Why no row is being written, for the History tab; null when one is.</summary>
    public static string? WhyNotRecording(IReadOnlyList<PluginCheckResult> results, FleetRead? fleet, LootRead? loot)
    {
        if (results.Count == 0) return "Drydock has not read its sources yet.";
        if (ShouldRecord(results, fleet, loot)) return null;
        var st = TabGates.Result(results, PluginRequirements.SubmarineTracker);
        if (TabGates.NeedsLine(st) is { } needs) return needs;
        if (st?.Status == PluginStatus.Stale) return "Submarine Tracker's data is stale, so nothing is recorded until it is refreshed. " + st.Fix;
        if (fleet?.Error is not null || loot?.Error is not null) return "Submarine Tracker's database could not be read.";
        if (fleet?.Fleets is { } f && f.Sum(x => x.Submarines.Count) == 0) return TabGates.NoSubmarines;
        return "Waiting for Submarine Tracker's data.";
    }

    /// <summary>The source when its plugin is fresh (Ready), else null, so a stale file feeds no number into a row.</summary>
    public static T? FreshOnly<T>(IReadOnlyList<PluginCheckResult> results, PluginRequirement req, T? read) where T : class =>
        TabGates.Result(results, req)?.Status == PluginStatus.Ready ? read : null;

    /// <summary>All-time distinct (hull, return) voyages and loot rows, from the loot read; unfiltered by the character filter.</summary>
    public static (long Voyages, long Sectors) Lifetime(IEnumerable<LootRow> loot)
    {
        var voyages = new HashSet<(long, long)>();
        long sectors = 0;
        foreach (var r in loot)
        {
            voyages.Add((r.SubmarineId, r.Return));
            sectors++;
        }
        return (voyages.Count, sectors);
    }

    /// <summary>
    /// Today's row from the cached reads, fleet-wide. A missing source gives null for what it feeds:
    /// no AutoRetainer, no liquid gil and stock; no Allagan Tools, no held salvage and no position.
    /// </summary>
    public static HistoryRow Build(
        DateOnly today,
        long nowUtcSeconds,
        IReadOnlyList<FreeCompanyFleet> fleets,
        IReadOnlyList<LootRow> loot,
        IReadOnlyDictionary<uint, int> prices,
        IReadOnlyList<AutoRetainerCharacter>? characters,
        TrackingRead? tracking,
        SalvageBacklog? backlog,
        TimeZoneInfo zone)
    {
        var subs = fleets.SelectMany(f => f.Submarines).ToList();
        var (voyages, sectors) = Lifetime(loot);

        long salvageDay = 0;
        foreach (var r in loot)
        {
            var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(r.Return).UtcDateTime, zone));
            if (d != today) continue;
            if (prices.TryGetValue(r.PrimaryItem, out var p1)) salvageDay += (long)p1 * r.PrimaryCount;
            if (prices.TryGetValue(r.AdditionalItem, out var p2)) salvageDay += (long)p2 * r.AdditionalCount;
        }

        HoldingsSummary? h = characters is null ? null : HoldingsMath.Compute(characters, backlog, new HashSet<string>());
        var (cer, kits) = tracking is null ? (null, null) : FleetTracking.SupplyTotals(tracking, new HashSet<string>());
        return new(
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            nowUtcSeconds,
            subs.Count,
            subs.Count(s => s.RouteClass == RouteClass.Farming),
            h is { LiquidKnown: true } ? h.Liquid : null,
            h is { SalvageKnown: true } ? h.Salvage : null,
            h is { SalvageKnown: true, LiquidKnown: true } ? h.Position : null,
            cer,
            kits,
            voyages,
            sectors,
            salvageDay);
    }
}
