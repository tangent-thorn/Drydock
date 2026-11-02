using Drydock.Core.Sources;

namespace Drydock.Core.Fleet;

/// <param name="Gil">Null when AutoRetainer did not carry the figure.</param>
/// <param name="RetainerGil">Null when AutoRetainer did not carry the retainers' gil.</param>
/// <param name="Salvage">Unsold salvage at vendor price; 0 when the backlog is unavailable.</param>
public sealed record Holding(string Name, string World, long? Gil, long? RetainerGil, long Salvage)
{
    /// <summary>The sum of what was read.</summary>
    public long Total => (Gil ?? 0) + (RetainerGil ?? 0) + Salvage;

    /// <summary>True when the gil or the retainers' gil was not read, so <see cref="Total"/> is only part of the picture.</summary>
    public bool Unread => Gil is null || RetainerGil is null;
}

/// <param name="Liquid">Gil on the characters plus gil on their retainers, over what was read.</param>
/// <param name="Salvage">Unsold salvage over every owner, as on the Overview tile; 0 when the backlog is unavailable.</param>
/// <param name="SalvageKnown">False when Allagan Tools is unavailable: the position is gil only.</param>
/// <param name="Unread">Ticked characters whose gil or retainer gil AutoRetainer did not carry.</param>
/// <param name="Ticked">Characters counted (not unticked).</param>
public sealed record HoldingsSummary(
    IReadOnlyList<Holding> Rows,
    long Liquid,
    long Salvage,
    bool SalvageKnown,
    int Unread = 0,
    int Ticked = 0)
{
    /// <summary>True when at least one character is ticked and every ticked one has its gil read; otherwise <see cref="Liquid"/> is not the whole.</summary>
    public bool LiquidKnown => Ticked > 0 && Unread == 0;

    /// <summary>True when there is nothing to show: no character is ticked, or none has its gil read.</summary>
    public bool NoLiquidData => Ticked == 0 || Unread == Ticked;

    public long Position => Liquid + Salvage;
}

/// <summary>Where the gil is: AutoRetainer's gil per character and retainers, plus the unsold salvage.</summary>
public static class HoldingsMath
{
    public static HoldingsSummary Compute(
        IReadOnlyList<AutoRetainerCharacter> characters,
        SalvageBacklog? backlog,
        IReadOnlySet<string> excluded)
    {
        var salvageByKey = new Dictionary<string, long>(StringComparer.Ordinal);
        if (backlog is not null)
        {
            foreach (var c in backlog.Characters)
            {
                if (c.Key is not null) salvageByKey.TryAdd(c.Key, c.Value);
            }
        }

        var rows = new List<Holding>();
        long liquid = 0;
        int unread = 0, ticked = 0;
        foreach (var c in characters)
        {
            var key = CharacterFilter.Key(c.Name, c.World);
            if (excluded.Contains(key)) continue;
            ticked++;
            liquid += (c.Gil ?? 0) + (c.RetainerGil ?? 0);
            var holding = new Holding(c.Name, c.World, c.Gil, c.RetainerGil, salvageByKey.GetValueOrDefault(key));
            if (holding.Unread) unread++;
            else if (c.Gil == 0 && c.RetainerGil == 0 && holding.Salvage == 0) continue;
            rows.Add(holding);
        }

        // OrderBy is stable: equal totals keep AutoRetainer's order
        var sorted = rows.OrderByDescending(r => r.Total).ToList();
        return new(sorted, liquid, backlog?.TotalValue ?? 0, backlog is not null, unread, ticked);
    }
}
