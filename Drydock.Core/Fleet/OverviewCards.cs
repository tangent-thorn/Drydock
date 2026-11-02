using System.Globalization;
using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public enum GaugeLevel
{
    Ok,
    Near,
    Low,
}

/// <param name="Reason">Set when <paramref name="Value"/> is "-": why the figure is missing.</param>
/// <param name="Spark">The last 14 days, today last.</param>
/// <param name="DeltaSign">-1, 0 or 1; picks the delta's colour.</param>
public sealed record SalvageCard(string Value, string? Reason, string? Compare, string? Delta, int DeltaSign, float[] Spark);

public sealed record FarmingCard(string Value, string? Reason, float Fraction, string Pct, string? Routes, string? Levelling);

/// <param name="Fraction">How far the position is from the last 250M milestone to the next.</param>
public sealed record PositionCard(string Value, string? Reason, string? Parts, float Fraction, string? Next);

/// <param name="Fraction">The runway against the panel's shared scale; 0 when unknown.</param>
public sealed record RunwayGauge(string Label, string Days, float Fraction, GaugeLevel Level);

public sealed record KitRow(string Name, string Kits, float Fraction, GaugeLevel Level);

/// <param name="Note">Set instead of the gauges when the runway cannot be shown.</param>
/// <param name="Marker">Where the runway warning setting sits on the gauges' scale.</param>
public sealed record SuppliesPanel(
    string? Note,
    string Header,
    float Marker,
    RunwayGauge? Ceruleum,
    RunwayGauge? Kits,
    string KitsHeader,
    string? KitsNote,
    IReadOnlyList<KitRow> Lowest);

public sealed record WarningRow(WarningKind Kind, string Text, bool Severe);

/// <summary>The Overview's figures that only change with a new snapshot, with every string built.</summary>
public sealed record OverviewView(
    SalvageCard Salvage,
    FarmingCard Farming,
    string? Sent,
    PositionCard Position,
    SuppliesPanel Supplies,
    IReadOnlyList<WarningRow> Warnings,
    string WarningCount);

/// <param name="Back">Returned at or before now: in port, waiting to be sent.</param>
/// <param name="FirstStartUtc">Start of bucket 0, the quarter hour now falls in.</param>
/// <param name="Later">Returning after the timeline's end.</param>
public sealed record ReturnBuckets(int Back, DateTime FirstStartUtc, TimeSpan Width, int[] Counts, int Later);

/// <summary>The Overview's figures that move with the clock: rebuilt once a second, never per frame.</summary>
/// <param name="Centers">Per bucket, its middle as a fraction of the timeline from now to +24h.</param>
/// <param name="Tips">Per bucket, the hover text; null for an empty one.</param>
public sealed record OverviewClock(
    string Back,
    string Status,
    float InPort,
    string PortText,
    string TimelineHeader,
    int[] Counts,
    float[] Centers,
    string?[] Tips);

public static class OverviewMath
{
    public const int SparkDays = 14;
    public const int TimelineHours = 24;
    public const int BucketMinutes = 15;
    public const int LowestKitRows = 4;
    public const double MaxRunwayScaleDays = 365;

    /// <summary>The value per day for the <paramref name="days"/> days up to today, today last; a day the series lacks is 0.</summary>
    public static float[] Sparkline(IReadOnlyList<(DateOnly Date, long Value)> daily, DateOnly today, int days = SparkDays)
    {
        var values = new float[days];
        var first = today.DayNumber - days + 1;
        foreach (var (date, value) in daily)
        {
            var i = date.DayNumber - first;
            if (i >= 0 && i < days) values[i] = value;
        }
        return values;
    }

    /// <summary>The change from <paramref name="baseline"/> to <paramref name="current"/> in percent, one decimal; null without a baseline.</summary>
    public static double? DeltaPct(double current, double baseline) =>
        baseline > 0 ? Math.Round((current / baseline - 1) * 100, 1, MidpointRounding.ToEven) : null;

    /// <summary>Part over whole, held to 0..1; 0 for an empty whole.</summary>
    public static float Fraction(double part, double whole) =>
        whole > 0 ? (float)Math.Clamp(part / whole, 0, 1) : 0f;

    /// <summary>The milestone below the position, the next one up and how far along the step it is; the same steps the projection uses.</summary>
    public static (long Floor, long Next, float Fraction) Milestone(long position, long step = ProjectionMath.MilestoneStep)
    {
        var floor = (long)Math.Floor((double)position / step) * step;
        return (floor, floor + step, Fraction(position - floor, step));
    }

    /// <summary>
    /// Hulls by when they come back: those already back are only counted; the rest fall into quarter-hour buckets from the
    /// quarter now is in, out to <paramref name="hours"/> ahead, and anything past that is counted as later.
    /// </summary>
    public static ReturnBuckets BucketReturns(IEnumerable<DateTime> returnsUtc, DateTime nowUtc, int hours = TimelineHours, int minutes = BucketMinutes)
    {
        var width = TimeSpan.FromMinutes(minutes);
        var first = new DateTime(nowUtc.Ticks - nowUtc.Ticks % width.Ticks, DateTimeKind.Utc);
        var end = nowUtc.AddHours(hours);
        var counts = new int[(int)Math.Ceiling((end - first) / width)];
        int back = 0, later = 0;
        foreach (var r in returnsUtc)
        {
            if (r <= nowUtc) back++;
            else if (r > end) later++;
            else counts[Math.Min((int)((r - first).Ticks / width.Ticks), counts.Length - 1)]++;
        }
        return new(back, first, width, counts, later);
    }

    /// <summary>The <paramref name="n"/> counted characters with the fewest kits, fewest first; one without a kit figure is left out.</summary>
    public static IReadOnlyList<TrackedCharacter> LowestKits(IEnumerable<TrackedCharacter> counted, int n = LowestKitRows) =>
        counted.Where(c => c.RepairKits.HasValue).OrderBy(c => c.RepairKits!.Value).Take(n).ToList();

    /// <summary>Low under the limit, near under twice it, ok above.</summary>
    public static GaugeLevel Level(double value, double limit) =>
        value < limit ? GaugeLevel.Low : value < 2 * limit ? GaugeLevel.Near : GaugeLevel.Ok;

    /// <summary>
    /// The days both runway gauges are drawn against: the longer runway, at least twice the warning so the marker never
    /// sits at the end, at most a year so one huge stock does not flatten the other gauge.
    /// </summary>
    public static double RunwayScale(double? ceruleumDays, double? kitDays, int warnDays)
    {
        var longest = Math.Max(ceruleumDays ?? 0, kitDays ?? 0);
        var floor = Math.Max(2.0 * warnDays, 1);
        return Math.Min(Math.Max(longest, floor), Math.Max(MaxRunwayScaleDays, floor));
    }
}

public static class OverviewCards
{
    private static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);

    public static OverviewView Build(
        IReadOnlyList<SubmarineRow> subs,
        int? farmingCount,
        TrackingRead? tracking,
        ViewSettings s,
        SalvageEconomics? tileEconomics,
        string? tileEconomicsReason,
        TabGate gilGate,
        HoldingsSummary? holdings,
        string? backlogReason,
        ProjectionResult? projection,
        string? suppliesNote,
        IReadOnlyList<FleetWarning>? warnings,
        DateOnly today)
    {
        var counted = tracking?.Counted(s.Excluded).ToList() ?? [];
        var known = counted.Where(c => c.SentToday.HasValue).ToList();
        var rows = (warnings ?? []).Select(w => new WarningRow(w.Kind, w.Text, IsSevere(w.Kind))).ToList();
        return new(
            Salvage(tileEconomics, tileEconomicsReason, today),
            Farming(subs, farmingCount),
            known.Count == 0 ? null : N(known.Sum(c => c.SentToday!.Value)) + " sent today",
            Position(gilGate, holdings, backlogReason, projection, today),
            Supplies(counted, s, tileEconomics, tileEconomicsReason, suppliesNote),
            rows,
            N(rows.Count));
    }

    /// <summary>Supplies and a fleet that sent nothing today are red; the rest orange.</summary>
    public static bool IsSevere(WarningKind kind) => kind is WarningKind.LowSupplies or WarningKind.LowRunway or WarningKind.NoVoyages;

    public static SalvageCard Salvage(SalvageEconomics? e, string? reason, DateOnly today)
    {
        if (e is null) return new("-", reason, null, null, 0, []);
        var g = SalvageMath.FormatGil;
        var delta = OverviewMath.DeltaPct(e.PerDay7, e.PerDay30);
        return new(
            g(e.Today),
            null,
            $"yesterday {g(e.Yesterday)} · 7d avg {g(e.PerDay7)}",
            delta is { } d ? string.Create(CultureInfo.InvariantCulture, $"{(d > 0 ? "+" : "")}{d:0.0}% vs 30d avg") : null,
            delta is { } x ? Math.Sign(x) : 0,
            OverviewMath.Sparkline(e.Daily, today));
    }

    public static FarmingCard Farming(IReadOnlyList<SubmarineRow> subs, int? farmingCount)
    {
        if (farmingCount is not { } f) return new("-", "no routes recorded yet", 0, "-", null, null);
        var fraction = OverviewMath.Fraction(f, subs.Count);
        var levelling = subs.Count(x => x.RouteClass == RouteClass.Levelling);
        var unknown = subs.Count(x => x.RouteClass == RouteClass.Unknown);
        return new(
            $"{f} / {subs.Count}",
            null,
            fraction,
            string.Create(CultureInfo.InvariantCulture, $"{Math.Round(fraction * 100, MidpointRounding.ToEven):0}%"),
            RouteMix(subs, f),
            $"{levelling} levelling" + (unknown > 0 ? $" · {unknown} no route" : ""));
    }

    /// <summary>"all on OJ", "on OJ", "OJ 40 · MOJ 36", the three busiest routes and how many more.</summary>
    public static string RouteMix(IReadOnlyList<SubmarineRow> subs, int farming)
    {
        if (farming == 0) return "none farming";
        var byRoute = subs
            .Select(x => Routes.FindGil(x.Route)?.Name)
            .OfType<string>()
            .GroupBy(n => n)
            .Select(grp => (Name: grp.Key, Count: grp.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        if (byRoute.Count == 1) return (farming == subs.Count ? "all on " : "on ") + byRoute[0].Name;
        var shown = string.Join(" · ", byRoute.Take(3).Select(r => $"{r.Name} {r.Count}"));
        return byRoute.Count > 3 ? $"{shown} · {byRoute.Count - 3} more" : shown;
    }

    public static PositionCard Position(TabGate gilGate, HoldingsSummary? h, string? backlogReason, ProjectionResult? projection, DateOnly today)
    {
        if (!gilGate.IsOpen || h is null) return new("-", TabGates.TileReason(gilGate) ?? "reading", null, 0, null);
        if (h.NoLiquidData) return new("-", "AutoRetainer carries no gil figure", null, 0, null);
        if (!h.LiquidKnown) return new("-", $"needs the gil figures, {h.Unread} not read", null, 0, null);

        var g = SalvageMath.FormatGil;
        var (_, next, fraction) = OverviewMath.Milestone(h.Position);
        string nextText;
        if (projection is null)
            nextText = $"next {g(next)}";
        else if (projection.Milestones.FirstOrDefault(m => m.Value == next) is { } hit)
            nextText = $"next {g(next)} on {DateLabels.Short(hit.Date, today)} ({hit.Days} d)";
        else
            nextText = $"next {g(next)} not within a year";
        return new(
            g(h.Position),
            null,
            h.SalvageKnown ? $"gil {g(h.Liquid)} + salvage {g(h.Salvage)}" : "gil only, salvage " + TabGates.Short(backlogReason),
            fraction,
            nextText);
    }

    public static SuppliesPanel Supplies(IReadOnlyList<TrackedCharacter> counted, ViewSettings s, SalvageEconomics? e, string? economicsReason, string? suppliesNote)
    {
        var header = $"warning under {s.RunwayDays} days";
        var limit = s.LowRepairKits;
        var lowest = OverviewMath.LowestKits(counted)
            .Select(c => new KitRow(c.Name, N(c.RepairKits!.Value), OverviewMath.Fraction(c.RepairKits.Value, limit), OverviewMath.Level(c.RepairKits.Value, limit)))
            .ToList();
        var kitsHeader = $"limit {N(limit)}";
        var kitsNote = lowest.Count == 0 ? "AutoRetainer has no kit figure for the ticked characters" : null;

        var note = suppliesNote ?? (e is null ? "Runway " + (economicsReason ?? "reading") : null);
        if (note is not null) return new(note, header, 0, null, null, kitsHeader, kitsNote, lowest);

        var scale = OverviewMath.RunwayScale(e!.CeruleumRunwayDays, e.RepairKitRunwayDays, s.RunwayDays);
        return new(
            null,
            header,
            OverviewMath.Fraction(s.RunwayDays, scale),
            Gauge("Ceruleum", e.CeruleumStock, e.CeruleumRunwayDays, scale, s.RunwayDays),
            Gauge("Repair kits", e.RepairKitStock, e.RepairKitRunwayDays, scale, s.RunwayDays),
            kitsHeader,
            kitsNote,
            lowest);
    }

    private static RunwayGauge Gauge(string label, int? stock, double? days, double scale, int warnDays)
    {
        if (stock is not { } st) return new(label + " unknown", "no figure", 0, GaugeLevel.Ok);
        if (days is not { } d) return new($"{label} {N(st)}", "use not measured", 0, GaugeLevel.Ok);
        return new($"{label} {N(st)}", string.Create(CultureInfo.InvariantCulture, $"{d:0} days"), OverviewMath.Fraction(d, scale), OverviewMath.Level(d, warnDays));
    }

    public static OverviewClock Clock(IReadOnlyList<SubmarineRow> subs, string? sent, DateTime nowUtc, TimeZoneInfo zone)
    {
        var b = OverviewMath.BucketReturns(subs.Select(x => x.ReturnUtc), nowUtc);
        var atSea = subs.Count - b.Back;
        DateTime? next = null;
        foreach (var x in subs)
        {
            if (x.ReturnUtc > nowUtc && (next is null || x.ReturnUtc < next)) next = x.ReturnUtc;
        }
        var countdown = next is { } n ? SubmarineTrackerDb.FormatCountdown(n - nowUtc) : null;

        var status = countdown is null ? "all back" : "next in " + countdown;
        if (sent is not null) status += " · " + sent;

        string header;
        if (subs.Count == 0) header = "no submarines";
        else if (b.Back > 0) header = $"next back now · {b.Back} waiting to be sent";
        else header = $"next back {Local(next!.Value, zone)}, in {countdown}";
        if (b.Later > 0) header += $" · {b.Later} after {OverviewMath.TimelineHours}h";

        var span = TimeSpan.FromHours(OverviewMath.TimelineHours);
        var centers = new float[b.Counts.Length];
        var tips = new string?[b.Counts.Length];
        for (var i = 0; i < b.Counts.Length; i++)
        {
            var start = b.FirstStartUtc + b.Width * i;
            centers[i] = OverviewMath.Fraction((start + b.Width / 2 - nowUtc).Ticks, span.Ticks);
            if (b.Counts[i] == 0) continue;
            var from = start < nowUtc ? nowUtc : start;
            tips[i] = $"{b.Counts[i]} {(b.Counts[i] == 1 ? "hull" : "hulls")} back {Local(from, zone)} to {Local(start + b.Width, zone)}";
        }

        return new(
            N(b.Back),
            status,
            OverviewMath.Fraction(b.Back, subs.Count),
            $"{N(b.Back)} in port, {N(atSea)} at sea",
            header,
            b.Counts,
            centers,
            tips);
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
}
