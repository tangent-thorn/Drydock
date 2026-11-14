using System.Globalization;

namespace Drydock.Core.Fleet;

/// <summary>A cell's text, and the full reason behind it when the text is a short form.</summary>
public sealed record FcCell(string Text, string? Tooltip = null);

/// <summary>The words the FC Points tab draws, so the window only lays them out.</summary>
public static class FcPointsText
{
    public const string UntickedMark = " (unticked)";
    public const string SubsExcludedMark = " (no subs)";
    public const string FromFleetMark = " (from fleet)";

    public const string UntickedNote = "(unticked): unticked on the Tracking tab; the FC's credits still show, its hulls and ceruleum are left out.";
    public const string SubsExcludedNote = "(no subs): excluded from deployables in AutoRetainer; its hulls are left out of the tank use.";
    public const string FromFleetNote = "(from fleet): AutoRetainer has no FC for this character, so the FC is the one its Submarine Tracker fleet carries.";

    public const string OncePerFc = "Credits belong to the FC, so each FC is counted once however many characters are in it.";
    public const string WhenUpdated = "Credits update only when AutoRetainer sees the FC on one of its members.";
    public const string NoCompanyData = "AutoRetainer's file has no FC data yet, so no credits are known.";

    private static string N0(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    /// <summary>The members joined by ", ", each with a mark when it is unticked, out of the subs, or placed by its fleet.</summary>
    public static string Members(FcPointsRow row) => string.Join(", ", row.Members.Select(m =>
        m.Name + (m.Unticked ? UntickedMark : m.ExcludedFromSubs ? SubsExcludedMark : "") + (m.FcFromFleet ? FromFleetMark : "")));

    /// <summary>"today" under a day, else whole days, "3d ago"; "-" when the credits were never read.</summary>
    public static string Age(double? days) => days switch
    {
        null => "-",
        < 1 => "today",
        { } d => string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(d):0}d ago"),
    };

    /// <summary>Whole days, rounded down: the credits last at least that long. The cell and the copy both use this.</summary>
    public static long? WholeDays(double? days) => days is { } d ? (long)Math.Floor(d) : null;

    public static string Days(double days) => WholeDays(days)!.Value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The rank, or "?" with the reason when AutoRetainer has not read it.</summary>
    public static FcCell Rank(FcPointsRow row, int rankNeeded) =>
        row.Rank is { } r ? new(r.ToString(CultureInfo.InvariantCulture), row.RankBlocked ? row.BuyNote : null) : new("?", FcPointsMath.RankNotRead(rankNeeded));

    /// <summary>A reason short enough for a cell; the full one goes in the tooltip.</summary>
    public static string Short(string note) => note switch
    {
        FcPointsMath.NotRunningSubs => note,
        FcPointsMath.PointsNotRead => "not read",
        FcPointsMath.NoPrice => "no price",
        "Reading..." => "reading",
        _ when note.StartsWith("needs FC rank", StringComparison.Ordinal) => "rank too low",
        _ => "-",
    };

    private static FcCell Note(string? note) =>
        note is null ? new("-") : Short(note) is var s && s == note ? new(s) : new(s, note);

    public static FcCell Buyable(FcPointsRow row) =>
        row.TanksBuyable is { } b ? new(N0(b), row.BuyNote) : Note(row.BuyNote);

    public static FcCell DaysFromPoints(FcPointsRow row) =>
        row.DaysFromPoints is { } d ? new(Days(d), row.RankBlocked ? row.BuyNote : null) : Note(row.DaysNote);

    public static FcCell DaysWithStock(FcPointsRow row) =>
        row.DaysWithStock is { } d ? new(Days(d))
        : row.DaysFromPoints is not null ? new("-", "the ceruleum on hand is not known for every ticked member")
        : Note(row.DaysNote);

    /// <summary>The totals line above the table.</summary>
    public static string Totals(FcPointsView view)
    {
        var t = view.Totals;
        var parts = new List<string>
        {
            $"{N0(t.Points)} credits" + (t.PointsUnknown > 0 ? $" ({t.PointsUnknown} {Plural(t.PointsUnknown, "FC", "FCs")} not read yet)" : ""),
            $"{N0(t.TanksBuyable)} tanks buyable",
        };
        if (t.TanksPerDay is { } perDay) parts.Add(perDay.ToString("0.0", CultureInfo.InvariantCulture) + " tanks a day");
        if (t.ShortestDays is { } days && t.ShortestName is { } name) parts.Add($"shortest: {name} at {Days(days)}d");
        return string.Join(", ", parts);
    }

    public static string PriceLine(FcPointsView view) =>
        string.Create(CultureInfo.InvariantCulture, $"Tanks cost {view.Price.Credits:N0} credits each ({view.Price.Source}) and need FC rank {view.RankNeeded}.");

    /// <summary>The lines under the table, only those that apply, in a fixed order.</summary>
    public static IReadOnlyList<string> Footnotes(FcPointsView view)
    {
        var notes = new List<string>();
        var members = view.Rows.SelectMany(r => r.Members).ToList();
        if (members.Any(m => m.Unticked)) notes.Add(UntickedNote);
        if (members.Any(m => !m.Unticked && m.ExcludedFromSubs)) notes.Add(SubsExcludedNote);
        if (members.Any(m => m.FcFromFleet)) notes.Add(FromFleetNote);
        if (view.Rows.Any(r => r.Stale))
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Credits read more than {view.StaleDays} {Plural(view.StaleDays, "day", "days")} ago are amber."));
        if (!view.CompaniesRead) notes.Add(NoCompanyData);
        if (view.DaysUnavailable is { } why) notes.Add(why);
        if (view.OldCompanies > 0)
            notes.Add($"{view.OldCompanies} old {Plural(view.OldCompanies, "FC", "FCs")} left out: AutoRetainer still lists {Plural(view.OldCompanies, "it", "them")}, but none of its characters is in {Plural(view.OldCompanies, "it", "them")} now.");
        if (view.UnmatchedHulls > 0)
            notes.Add($"{view.UnmatchedHulls} {Plural(view.UnmatchedHulls, "hull is", "hulls are")} in no FC listed here, so {Plural(view.UnmatchedHulls, "its", "their")} tank use is on no row.");
        return notes;
    }
}
