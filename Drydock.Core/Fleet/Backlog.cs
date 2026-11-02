using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

public sealed record BacklogItem(uint Id, string Name, long Held, int Unit, long Value, long Slots);

/// <param name="Key">Name@World of an AutoRetainer character; null for the group of owners that match none.</param>
public sealed record BacklogCharacter(string Name, string? Key, long Value, long Slots);

public sealed record SalvageBacklog(
    IReadOnlyList<BacklogItem> Items,
    long TotalItems,
    long TotalValue,
    long TotalSlots,
    IReadOnlyList<BacklogCharacter> Characters);

/// <summary>Unsold salvage from plain inventory rows; knows nothing about where the rows came from.</summary>
public static class BacklogMath
{
    public const int StackSize = 99;
    public const string OtherName = "Other (retainers or unknown)";

    public static SalvageBacklog Compute(
        IEnumerable<InventoryRow> rows,
        IReadOnlyList<SalvageItem> salvage,
        IReadOnlyList<AutoRetainerCharacter> characters,
        IReadOnlySet<string> excluded)
    {
        var byId = salvage.ToDictionary(s => s.Id);
        var cidToChar = new Dictionary<ulong, AutoRetainerCharacter>();
        foreach (var c in characters) cidToChar.TryAdd(c.Cid, c);
        var excludedCids = cidToChar
            .Where(kv => excluded.Contains(CharacterFilter.Key(kv.Value.Name, kv.Value.World)))
            .Select(kv => kv.Key)
            .ToHashSet();

        // owner (Has false: the row has no id) -> item -> quantity
        var perOwner = new Dictionary<(bool Has, ulong Id), Dictionary<uint, long>>();
        var held = new Dictionary<uint, long>();
        foreach (var r in rows)
        {
            if (!byId.ContainsKey(r.ItemId)) continue;
            if (r.OwnerId is { } o && excludedCids.Contains(o)) continue;
            var ownerKey = (r.OwnerId.HasValue, r.OwnerId.GetValueOrDefault());
            held[r.ItemId] = held.GetValueOrDefault(r.ItemId) + r.Quantity;
            if (!perOwner.TryGetValue(ownerKey, out var items)) perOwner[ownerKey] = items = [];
            items[r.ItemId] = items.GetValueOrDefault(r.ItemId) + r.Quantity;
        }

        var list = new List<BacklogItem>();
        foreach (var s in salvage)
        {
            var q = held.GetValueOrDefault(s.Id);
            list.Add(new BacklogItem(s.Id, s.Name, q, s.Price, q * s.Price, Slots(q)));
        }

        var chars = new List<BacklogCharacter>();
        long otherValue = 0, otherSlots = 0;
        foreach (var ((has, id), items) in perOwner)
        {
            var value = items.Sum(kv => byId[kv.Key].Price * kv.Value);
            var slots = items.Values.Sum(Slots);
            if (has && cidToChar.TryGetValue(id, out var c))
                chars.Add(new BacklogCharacter(c.Name, CharacterFilter.Key(c.Name, c.World), value, slots));
            else
            {
                otherValue += value;
                otherSlots += slots;
            }
        }
        chars = chars.Where(c => c.Value != 0 || c.Slots != 0).OrderByDescending(c => c.Value).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        if (otherValue != 0 || otherSlots != 0) chars.Add(new BacklogCharacter(OtherName, null, otherValue, otherSlots));

        return new(list, list.Sum(i => i.Held), list.Sum(i => i.Value), list.Sum(i => i.Slots), chars);
    }

    private static long Slots(long quantity) => quantity <= 0 ? 0 : (quantity + StackSize - 1) / StackSize;
}
