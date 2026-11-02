using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <param name="SentToday">Null when AutoRetainer has no voyage record for the character at all.</param>
/// <param name="Ceruleum">Null when AutoRetainer did not carry the field.</param>
/// <param name="RepairKits">Null when AutoRetainer did not carry the field.</param>
public sealed record TrackedCharacter(string Name, string World, int Hulls, int? SentToday, long? DaysSilent, int? Ceruleum, int? RepairKits)
{
    public string Key => CharacterFilter.Key(Name, World);
}

public sealed record PhantomEntry(string Character, string Entry);

/// <param name="DayKey">The UTC epoch day "today" was counted for; 0 when not set.</param>
public sealed record TrackingRead(IReadOnlyList<TrackedCharacter> Characters, IReadOnlyList<PhantomEntry> Phantoms)
{
    public long DayKey { get; init; }

    /// <summary>False when Submarine Tracker was not read: every Hulls figure is then 0 because it is unknown, not because the character owns none.</summary>
    public bool HullsKnown { get; init; } = true;

    /// <summary>
    /// The ticked characters whose figures count for the fleet: those that own a hull. A character without submarines is
    /// listed so it can be unticked, but its missing voyage record or supplies say nothing about the fleet.
    /// </summary>
    public IEnumerable<TrackedCharacter> Counted(IReadOnlySet<string> excluded) =>
        Characters.Where(c => !excluded.Contains(c.Key) && (!HullsKnown || c.Hulls > 0));

    /// <summary>Voyages sent today over the characters that have a record; null when none has one.</summary>
    public int? SentToday => Characters.Any(c => c.SentToday.HasValue) ? Characters.Sum(c => c.SentToday ?? 0) : null;
}

public static class FleetTracking
{
    /// <summary>
    /// The UTC epoch day of local noon on that date. AutoRetainer files voyages under the UTC day of the
    /// daily run, and now() lands on the next UTC day after 17:00 Pacific, so noon is the stable anchor.
    /// </summary>
    public static long DayKey(DateOnly localDate, TimeZoneInfo zone)
    {
        var noon = localDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(noon, zone.GetUtcOffset(noon)).ToUnixTimeSeconds() / 86400;
    }

    /// <summary>
    /// The ceruleum and repair kits over the ticked characters. A total is null when any of them lacks the figure,
    /// or when none is ticked: a partial sum would pass for the whole stock.
    /// </summary>
    public static (int? Ceruleum, int? RepairKits) SupplyTotals(TrackingRead tracking, IReadOnlySet<string> excluded)
    {
        var chars = tracking.Counted(excluded).ToList();
        if (chars.Count == 0) return (null, null);
        return (
            chars.All(c => c.Ceruleum.HasValue) ? chars.Sum(c => c.Ceruleum!.Value) : null,
            chars.All(c => c.RepairKits.HasValue) ? chars.Sum(c => c.RepairKits!.Value) : null);
    }

    /// <summary>Only characters that own hulls in Submarine Tracker count; hulls are matched by character name.</summary>
    public static TrackingRead Join(IReadOnlyList<FreeCompanyFleet> fleets, IReadOnlyList<AutoRetainerCharacter> ar, long dayKey)
    {
        var hullsByCharacter = HullsByCharacter(fleets);
        var characters = new List<TrackedCharacter>();
        var phantoms = new List<PhantomEntry>();
        foreach (var c in ar)
        {
            if (!hullsByCharacter.TryGetValue(c.Name, out var hulls) || hulls.Count == 0) continue;
            AddPhantoms(phantoms, c, hulls);
            characters.Add(Row(c, hulls.Count, dayKey));
        }
        return new(characters, phantoms) { DayKey = dayKey };
    }

    /// <summary>
    /// Every AutoRetainer character, in AutoRetainer's order, so each can be ticked or unticked. One that owns no
    /// submarine has 0 hulls; without Submarine Tracker's fleets (<paramref name="fleets"/> null) the hulls are unknown
    /// and <see cref="TrackingRead.HullsKnown"/> is false.
    /// </summary>
    public static TrackingRead JoinAll(IReadOnlyList<FreeCompanyFleet>? fleets, IReadOnlyList<AutoRetainerCharacter> ar, long dayKey)
    {
        var hullsByCharacter = fleets is null ? null : HullsByCharacter(fleets);
        var characters = new List<TrackedCharacter>();
        var phantoms = new List<PhantomEntry>();
        foreach (var c in ar)
        {
            HashSet<string>? hulls = null;
            if (hullsByCharacter is not null && hullsByCharacter.TryGetValue(c.Name, out var set) && set.Count > 0)
            {
                hulls = set;
                AddPhantoms(phantoms, c, hulls);
            }
            characters.Add(Row(c, hulls?.Count ?? 0, dayKey));
        }
        return new(characters, phantoms) { DayKey = dayKey, HullsKnown = fleets is not null };
    }

    private static Dictionary<string, HashSet<string>> HullsByCharacter(IReadOnlyList<FreeCompanyFleet> fleets)
    {
        var hullsByCharacter = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in fleets)
        {
            if (!hullsByCharacter.TryGetValue(f.CharacterName, out var set))
                hullsByCharacter[f.CharacterName] = set = new(StringComparer.Ordinal);
            foreach (var s in f.Submarines) set.Add(s.Name);
        }
        return hullsByCharacter;
    }

    private static void AddPhantoms(List<PhantomEntry> phantoms, AutoRetainerCharacter c, HashSet<string> hulls)
    {
        // AR keeps a sub's name after it's gone (renamed, or the slot redone), and nothing clears it
        // on its own. Listing them is the only way to explain why AR says 4 subs and ST says 3.
        foreach (var n in c.AdditionalSubmarineNames)
        {
            if (!hulls.Contains(n)) phantoms.Add(new PhantomEntry(c.Name, n));
        }
    }

    private static TrackedCharacter Row(AutoRetainerCharacter c, int hulls, long dayKey)
    {
        var sent = c.SentVoyagesByDay;
        return new TrackedCharacter(
            c.Name,
            c.World,
            hulls,
            sent?.GetValueOrDefault(dayKey),
            sent is null || sent.Count == 0 ? null : dayKey - sent.Keys.Max(),
            c.Ceruleum,
            c.RepairKits);
    }
}

public static class CharacterFilter
{
    public static string Key(string name, string world) => name + "@" + world;

    /// <summary>
    /// Drops the companies of unticked characters. Matches on the name alone: Submarine Tracker's world
    /// for a company can be the company's world, not the character's.
    /// </summary>
    public static IReadOnlyList<FreeCompanyFleet> Apply(IReadOnlyList<FreeCompanyFleet> fleets, IReadOnlySet<string> excluded)
    {
        if (excluded.Count == 0) return fleets;
        var names = Names(excluded);
        return fleets.Where(f => !names.Contains(f.CharacterName)).ToList();
    }

    /// <summary>Loot rows of unticked characters' hulls dropped, matched by name like the fleets.</summary>
    public static IReadOnlyList<LootRow> Apply(IReadOnlyList<LootRow> loot, IReadOnlySet<string> excluded)
    {
        if (excluded.Count == 0) return loot;
        var names = Names(excluded);
        return loot.Where(r => !names.Contains(r.CharacterName)).ToList();
    }

    private static HashSet<string> Names(IReadOnlySet<string> excluded) =>
        excluded.Select(k => k[..Math.Max(k.LastIndexOf('@'), 0)]).ToHashSet(StringComparer.Ordinal);
}
