namespace Drydock.Core.Plugins;

public enum PluginStatus
{
    Ready,
    NotLoaded,
    Missing,
    NoDataYet,
    Stale,
}

public sealed record PluginCheckResult(
    PluginRequirement Requirement,
    PluginStatus Status,
    string Detail,
    string Fix,
    DateTime? DataLastWriteUtc,
    TimeSpan? DataAge)
{
    public bool IsReady => Status == PluginStatus.Ready;

    /// <summary>Opens the setup window on its own.</summary>
    public bool BlocksStartup => Requirement.Required && Status != PluginStatus.Ready;
}
