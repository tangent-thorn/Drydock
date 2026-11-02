namespace Drydock.Core.Plugins;

/// <param name="RepoUrl">null when it's in the official repo.</param>
/// <param name="ConfigPath">Relative to pluginConfigs, forward slashes on every OS.</param>
public sealed record PluginRequirement(
    string InternalName,
    string DisplayName,
    bool Required,
    string Why,
    string Source,
    string? RepoUrl,
    string ConfigPath)
{
    public string ConfigFolder => ConfigPath.Split('/')[0];

    /// <summary>What to do when the plugin is loaded but has written no data file yet.</summary>
    public string NoDataFix { get; init; } = "Log in on a character so the plugin writes its data.";
}

public static class PluginRequirements
{
    public const string AutoRetainerRepoUrl = "https://love.puni.sh/ment.json";

    public static readonly PluginRequirement AutoRetainer = new(
        "AutoRetainer",
        "AutoRetainer",
        true,
        "Voyages per day, ceruleum and repair kits per character, submarine EXP, return times, character and retainer gil.",
        "Puni.sh custom repository",
        AutoRetainerRepoUrl,
        "AutoRetainer/DefaultConfig.json")
    {
        NoDataFix = "Log in on a character with AutoRetainer enabled so it writes its configuration.",
    };

    public static readonly PluginRequirement SubmarineTracker = new(
        "SubmarineTracker",
        "Submarine Tracker",
        true,
        "The real list of submarines, the loot table behind production, sectors and upkeep.",
        "Official Dalamud repository",
        null,
        "SubmarineTracker/submarine-sqlite.db")
    {
        NoDataFix = "Log in on a character with a free company workshop and submarines, and open Submarine Tracker once so it records them.",
    };

    public static readonly PluginRequirement AllaganTools = new(
        "InventoryTools",
        "Allagan Tools",
        false,
        "Unsold salvage backlog and salvage per character.",
        "Official Dalamud repository",
        null,
        "InventoryTools/inventories.csv")
    {
        NoDataFix = "Log in on a character so Allagan Tools writes inventories.csv. Salvage totals are left out until it does.",
    };

    public static readonly PluginRequirement CashFlow = new(
        "CashFlow",
        "CashFlow",
        false,
        "Gil history and the repair kit price actually paid.",
        "Official Dalamud repository",
        null,
        "CashFlow/DefaultDbV2.sqlite")
    {
        NoDataFix = "Log in on a character so CashFlow creates its database. The repair kit price setting is used until then.",
    };

    public static readonly IReadOnlyList<PluginRequirement> All =
        [AutoRetainer, SubmarineTracker, AllaganTools, CashFlow];
}
