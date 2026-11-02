namespace Drydock.Core.Plugins;

public sealed record InstalledPluginInfo(string InternalName, bool IsLoaded, string? Version);

// Dalamud and the disk stay behind these two so Core tests run without the game.
public interface IPluginHost
{
    /// <summary>null when not installed.</summary>
    InstalledPluginInfo? Find(string internalName);
}

public interface IDataFileProbe
{
    /// <summary>null when the file doesn't exist.</summary>
    DateTime? GetLastWriteUtc(string pluginConfigsRelativePath);
}

public sealed class PluginChecker(IPluginHost host, IDataFileProbe files, Func<DateTime> utcNow)
{
    public PluginCheckResult Check(PluginRequirement req, TimeSpan staleAfter)
    {
        var installed = host.Find(req.InternalName);
        if (installed is null)
        {
            var fix = req.RepoUrl is null
                ? $"Install {req.DisplayName} from the plugin installer ({req.Source})."
                : $"Add the custom repository (Copy repo URL), then install {req.DisplayName} from the plugin installer ({req.Source}).";
            return new(req, PluginStatus.Missing, "Not installed.", fix, null, null);
        }

        if (!installed.IsLoaded)
            return new(req, PluginStatus.NotLoaded, "Installed but not loaded.", $"Enable {req.DisplayName} in /xlplugins.", null, null);

        var written = files.GetLastWriteUtc(req.ConfigPath);
        if (written is null)
        {
            return new(req, PluginStatus.NoDataYet, "No data file yet.",
                req.NoDataFix, null, null);
        }

        var age = utcNow() - written.Value;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age > staleAfter)
        {
            return new(req, PluginStatus.Stale, $"Data last written {FormatAge(age)} ago.",
                $"Log in on a character so {req.DisplayName} refreshes its data.", written, age);
        }

        return new(req, PluginStatus.Ready, $"Data written {FormatAge(age)} ago.", "", written, age);
    }

    /// <summary>One result per plugin; a check that itself fails marks only that plugin, as not loaded with the reason.</summary>
    public IReadOnlyList<PluginCheckResult> CheckAll(TimeSpan staleAfter) =>
        PluginRequirements.All.Select(r => CheckSafely(r, staleAfter)).ToList();

    private PluginCheckResult CheckSafely(PluginRequirement req, TimeSpan staleAfter)
    {
        try
        {
            return Check(req, staleAfter);
        }
        catch (Exception ex)
        {
            return new(req, PluginStatus.NotLoaded, "Could not be checked: " + ex.GetType().Name + ": " + ex.Message,
                "Open the plugin installer and make sure it is installed and enabled.", null, null);
        }
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return "under a minute";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} h";
        return $"{(int)age.TotalDays} d";
    }
}
