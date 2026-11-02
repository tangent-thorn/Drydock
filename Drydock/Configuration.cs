using Dalamud.Configuration;
using Drydock.Core.Fleet;

namespace Drydock;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public int StaleDays { get; set; } = 3;

    public bool ShowInfoBar { get; set; } = true;

    public int SilentDays { get; set; } = 2;

    public int LowCeruleum { get; set; } = 1000;

    public int LowRepairKits { get; set; } = 100;

    /// <summary>How often hulls are resent, in hours; 0 means as soon as they are back.</summary>
    public int ResendCadenceHours { get; set; } = 0;

    /// <summary>The rank the Levelling tab counts days to.</summary>
    public int TargetRank { get; set; } = 85;

    public int FuelPrice { get; set; } = 300;

    public int KitPrice { get; set; } = 1800;

    public double FuelPerSector { get; set; } = 4.5;

    public double KitsPerSector { get; set; } = 0.872;

    public int RunwayDays { get; set; } = 14;

    /// <summary>Use the ceruleum and kit use measured from Drydock's own history instead of the two settings above.</summary>
    public bool UseMeasuredRates { get; set; } = true;

    /// <summary>The position the Projection tab counts to.</summary>
    public long GilTarget { get; set; } = 1_000_000_000; // 1B, the number everyone ends up asking about

    /// <summary>The gap between milestones on the Overview card and the Projection tab.</summary>
    public long MilestoneStep { get; set; } = ProjectionMath.DefaultMilestoneStep;

    /// <summary>"Name@World" of characters unticked on the Tracking tab.</summary>
    public HashSet<string> ExcludedCharacters { get; set; } = [];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>Brings every value into the range of its input box; a config edited by hand must not divide by zero or throw.</summary>
    public void Sanitize()
    {
        StaleDays = ConfigLimits.Int(StaleDays, 1, 365);
        SilentDays = ConfigLimits.Int(SilentDays, 1, 30);
        LowCeruleum = ConfigLimits.Int(LowCeruleum, 0, 100000);
        LowRepairKits = ConfigLimits.Int(LowRepairKits, 0, 100000);
        ResendCadenceHours = ConfigLimits.Int(ResendCadenceHours, 0, 168);
        TargetRank = ConfigLimits.Int(TargetRank, 2, 145);
        FuelPrice = ConfigLimits.Int(FuelPrice, 0, 100000);
        KitPrice = ConfigLimits.Int(KitPrice, 0, 1000000);
        FuelPerSector = ConfigLimits.Double(FuelPerSector, 0, 1000, 4.5);
        KitsPerSector = ConfigLimits.Double(KitsPerSector, 0, 1000, 0.872);
        RunwayDays = ConfigLimits.Int(RunwayDays, 1, 365);
        GilTarget = ConfigLimits.Long(GilTarget, ProjectionMath.MinTarget, ProjectionMath.MaxTarget);
        MilestoneStep = ConfigLimits.Long(MilestoneStep, ProjectionMath.MinMilestoneStep, ProjectionMath.MaxTarget);
        ExcludedCharacters ??= [];
    }
}
