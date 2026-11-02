using System.Numerics;
using Dalamud.Interface.Colors;
using Drydock.Core.Plugins;

namespace Drydock.Windows;

internal static class StatusColors
{
    public static readonly Vector4 Green = ImGuiColors.HealerGreen;
    public static readonly Vector4 Amber = ImGuiColors.DalamudOrange;
    public static readonly Vector4 Red = ImGuiColors.DalamudRed;
    public static readonly Vector4 Grey = ImGuiColors.DalamudGrey;

    public static Vector4 For(PluginCheckResult r) => r.Status switch
    {
        PluginStatus.Ready => Green,
        PluginStatus.NotLoaded => Amber,
        PluginStatus.NoDataYet => Amber,
        PluginStatus.Stale => Amber,
        PluginStatus.Missing => r.Requirement.Required ? Red : Grey,
        _ => Grey,
    };

    public static string Label(PluginStatus s) => s switch
    {
        PluginStatus.Ready => "Ready",
        PluginStatus.NotLoaded => "Not loaded",
        PluginStatus.Missing => "Missing",
        PluginStatus.NoDataYet => "No data yet",
        PluginStatus.Stale => "Stale",
        _ => s.ToString(),
    };
}
