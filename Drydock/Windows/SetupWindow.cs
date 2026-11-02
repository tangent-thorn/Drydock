using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Drydock.Core.Fleet;
using Drydock.Core.Plugins;
using Drydock.Core.Sources;

namespace Drydock.Windows;

public sealed class SetupWindow : Window
{
    private readonly PluginStatusService status;
    private DateTime lastAutoCheck = DateTime.MinValue;

    internal SetupWindow(PluginStatusService status) : base("Drydock setup###DrydockSetup")
    {
        this.status = status;
        Size = new Vector2(760, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private static float S(float px) => MainWindow.S(px);

    public override void OnOpen() => status.Refresh();

    // What was found in each plugin's file once the check passed: a count, or why the file could not be used.
    private void DrawReadResult(PluginCheckResult r)
    {
        if (!TabGates.Usable(r)) return;
        var req = r.Requirement;
        if (req == PluginRequirements.SubmarineTracker)
        {
            if (status.Fleet is { } c)
            {
                if (c.Fleets is not null)
                {
                    ImGui.TextDisabled($"Submarines in its database: {c.Total}");
                    if (c.Total == 0) ImGui.TextColored(StatusColors.Amber, TabGates.NoSubmarines);
                }
                else ImGui.TextColored(StatusColors.Red, "Could not read its database: " + c.Error);
            }
            if (status.Loot is { Error: { } lootError })
                ImGui.TextColored(StatusColors.Red, "Could not read its loot: " + lootError);
            else if (status.Loot is { Rows: { } loot })
                ImGui.TextDisabled($"Loot rows in its database: {loot.Count:N0}");
        }
        else if (req == PluginRequirements.AutoRetainer && status.AutoRetainer is { } ar)
        {
            if (ar.Characters is not null)
            {
                ImGui.TextDisabled($"Characters in its configuration: {ar.Characters.Count}");
                if (ar.Characters.Count == 0) ImGui.TextColored(StatusColors.Amber, TabGates.NoCharacters);
            }
            else ImGui.TextColored(StatusColors.Red, "Could not read its configuration: " + ar.Error);
        }
        else if (req == PluginRequirements.AllaganTools && status.Inventory is { } inv)
        {
            if (inv.Rows is not null) ImGui.TextDisabled($"Inventory rows read: {inv.Rows.Count:N0}");
            else ImGui.TextColored(StatusColors.Red, "Could not read its inventory file: " + inv.Error);
        }
        else if (req == PluginRequirements.CashFlow && status.KitPrice is { } kp)
        {
            if (kp.Error is null) ImGui.TextDisabled($"Repair kit purchases in the last {CashFlowDb.WindowDays} days: {kp.Samples}");
            else ImGui.TextColored(StatusColors.Red, "Could not read its database: " + kp.Error);
        }
    }

    public override void Draw()
    {
        // The Dalamud event covers plugins loading and unloading; this also catches data files
        // aging into Stale and plugins that write their file while this window is open.
        if (DateTime.UtcNow - lastAutoCheck > TimeSpan.FromSeconds(30))
        {
            lastAutoCheck = DateTime.UtcNow;
            status.Refresh();
        }

        ImGui.TextWrapped("Drydock only reads data that other plugins keep on this computer. It needs AutoRetainer and Submarine Tracker; Allagan Tools and CashFlow are optional.");
        ImGui.Spacing();

        var results = status.Results;
        if (results.Count == 0)
        {
            ImGui.TextDisabled("Checking...");
            return;
        }

        using (var table = ImRaii.Table("drydock-setup", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthFixed, S(150));
                ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, S(170));
                ImGui.TableHeadersRow();

                foreach (var r in results)
                {
                    ImGui.TableNextRow();
                    // every text column wraps at its own edge instead of clipping
                    ImGui.TableNextColumn();
                    using (ImRaii.TextWrapPos(0f))
                    {
                        ImGui.TextUnformatted(r.Requirement.DisplayName);
                        ImGui.TextDisabled(r.Requirement.Required ? "required" : "optional");
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(r.Requirement.Why);

                    ImGui.TableNextColumn();
                    using (ImRaii.TextWrapPos(0f))
                    {
                        ImGui.TextColored(StatusColors.For(r), StatusColors.Label(r.Status));
                        ImGui.TextWrapped(r.Detail);
                        if (r.Fix.Length > 0) ImGui.TextWrapped(r.Fix);
                        DrawReadResult(r);
                    }

                    ImGui.TableNextColumn();
                    var id = r.Requirement.InternalName;
                    if (r.Requirement.RepoUrl is { } url && ImGui.Button($"Copy repo URL##{id}"))
                        ImGui.SetClipboardText(url);
                    if (r.Status != PluginStatus.Ready && ImGui.Button($"Open plugin installer##{id}"))
                        Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, r.Requirement.DisplayName);
                }
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Refresh")) status.Refresh();
        ImGui.SameLine();
        ImGui.TextDisabled("Custom repositories are added in Settings > Experimental > Custom Plugin Repositories.");
    }
}
