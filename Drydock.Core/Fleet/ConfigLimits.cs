namespace Drydock.Core.Fleet;

/// <summary>Keeps a setting loaded from a hand-edited or older config file inside the range its input box allows.</summary>
public static class ConfigLimits
{
    public static int Int(int value, int min, int max) => Math.Clamp(value, min, max);

    public static long Long(long value, long min, long max) => Math.Clamp(value, min, max);

    /// <summary>NaN and infinity fall back to the default instead of poisoning every sum they touch.</summary>
    public static double Double(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
