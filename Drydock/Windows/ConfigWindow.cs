using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Drydock.Core.Fleet;

namespace Drydock.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration configuration;
    private readonly PluginStatusService status;

    private readonly Action onInfoBarChanged;

    internal ConfigWindow(Configuration configuration, PluginStatusService status, Action onInfoBarChanged) : base("Drydock settings###DrydockConfig")
    {
        this.configuration = configuration;
        this.status = status;
        this.onInfoBarChanged = onInfoBarChanged;
        Size = new Vector2(460, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // A field edits into these until it is left; then the value is clamped, applied and saved once.
    private readonly Dictionary<string, double> edits = [];
    private readonly Dictionary<string, Action<double>> applies = [];
    private bool staleEdited;

    public override void Draw()
    {
        var bar = configuration.ShowInfoBar;
        if (ImGui.Checkbox("Show in the server info bar", ref bar))
        {
            configuration.ShowInfoBar = bar;
            configuration.Save();
            onInfoBarChanged();
        }

        if (ImGui.CollapsingHeader("Warnings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Int("Stale after (days)", configuration.StaleDays, 1, 365, v => configuration.StaleDays = v, refresh: true);
            Int("Silent after (days)", configuration.SilentDays, 1, 30, v => configuration.SilentDays = v);
            Int("Low ceruleum below", configuration.LowCeruleum, 0, 100000, v => configuration.LowCeruleum = v);
            Int("Low repair kits below", configuration.LowRepairKits, 0, 100000, v => configuration.LowRepairKits = v);
            Int("Supply runway warning below (days)", configuration.RunwayDays, 1, 365, v => configuration.RunwayDays = v);
        }

        if (ImGui.CollapsingHeader("Levelling", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Int("Resend cadence (hours, 0 = as soon as back)", configuration.ResendCadenceHours, 0, 168, v => configuration.ResendCadenceHours = v);
            Int("Target rank", configuration.TargetRank, 2, 145, v => configuration.TargetRank = v);
        }

        if (ImGui.CollapsingHeader("Upkeep", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Int("Ceruleum price (gil)", configuration.FuelPrice, 0, 100000, v => configuration.FuelPrice = v);
            Int("Repair kit price (gil)", configuration.KitPrice, 0, 1000000, v => configuration.KitPrice = v);
            ImGui.TextDisabled("Repair kit price: " + status.KitPriceText);
            Double("Ceruleum per sector", configuration.FuelPerSector, 0, 1000, "%.3f", v => configuration.FuelPerSector = v);
            Double("Repair kits per sector", configuration.KitsPerSector, 0, 1000, "%.3f", v => configuration.KitsPerSector = v);
            ImGui.TextDisabled("The two rates above are used until the history has measured them.");
            var measured = configuration.UseMeasuredRates;
            if (ImGui.Checkbox("Use measured fuel and kit use", ref measured))
            {
                configuration.UseMeasuredRates = measured;
                configuration.Save();
                status.Recompute();
            }
            ImGui.TextDisabled("Salvage prices are the vendor prices.");
        }

        if (ImGui.CollapsingHeader("Projection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Double("Gil target", configuration.GilTarget, ProjectionMath.MinTarget, ProjectionMath.MaxTarget, "%.0f",
                v => configuration.GilTarget = (long)v);
            Double("Milestone step", configuration.MilestoneStep, ProjectionMath.MinMilestoneStep, ProjectionMath.MaxTarget, "%.0f",
                v => configuration.MilestoneStep = (long)v);
            ImGui.TextDisabled("The gap between milestones, 250,000,000 by default: the Overview's next milestone and the Projection's list.");
        }
    }

    private void Int(string label, int current, int min, int max, Action<int> set, bool refresh = false)
    {
        var v = edits.TryGetValue(label, out var e) ? (int)e : current;
        if (ImGui.InputInt(label, ref v))
        {
            edits[label] = v;
            applies[label] = d => set((int)Math.Clamp(d, min, max));
            if (refresh) staleEdited = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Commit(label, refresh);
    }

    private void Double(string label, double current, double min, double max, string format, Action<double> set)
    {
        var v = edits.TryGetValue(label, out var e) ? e : current;
        if (ImGui.InputDouble(label, ref v, 0, 0, format))
        {
            edits[label] = v;
            applies[label] = d => set(Math.Clamp(d, min, max));
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Commit(label, false);
    }

    private void Commit(string label, bool refresh)
    {
        if (!edits.Remove(label, out var value) || !applies.Remove(label, out var apply)) return;
        apply(value);
        configuration.Save();
        status.Recompute();
        if (refresh)
        {
            staleEdited = false;
            status.Refresh();
        }
    }

    /// <summary>Applies and saves any field still being edited; for closing the window and unloading the plugin.</summary>
    internal void Flush(bool refresh)
    {
        if (edits.Count == 0) return;
        foreach (var (label, value) in edits) applies[label](value);
        edits.Clear();
        applies.Clear();
        configuration.Save();
        status.Recompute();
        if (refresh && staleEdited) status.Refresh();
        staleEdited = false;
    }

    public override void OnClose() => Flush(true);
}
