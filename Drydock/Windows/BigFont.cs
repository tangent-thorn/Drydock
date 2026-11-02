using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace Drydock.Windows;

// SetWindowFontScale only stretches the atlas bitmap, so 2x numbers came out blurry. This is the default font
// built at twice the size; the atlas follows the UI scale by itself.
internal static class BigFont
{
    private static IFontHandle? handle;

    public static void Init(IUiBuilder ui) =>
        handle = ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * 2f)));

    public static void Dispose()
    {
        handle?.Dispose();
        handle = null;
    }

    public static void Text(string value)
    {
        if (handle is { Available: true } h)
        {
            using (h.Push()) ImGui.TextUnformatted(value);
            return;
        }
        // first frames, before the atlas has built it
        ImGui.SetWindowFontScale(2f);
        try { ImGui.TextUnformatted(value); }
        finally { ImGui.SetWindowFontScale(1f); }
    }
}
