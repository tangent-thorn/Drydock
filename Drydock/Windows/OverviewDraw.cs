using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Drydock.Core.Fleet;

namespace Drydock.Windows;

// The Overview tab. Every string and number arrives built (the snapshot's OverviewView, the per-second OverviewClock),
// so this only lays out and draws and a frame allocates nothing. Base colours come from the user's ImGui style and the
// accents from Dalamud's palette, so a light theme gets light cards.
internal static class OverviewDraw
{
    private static readonly string IconSalvage = FontAwesomeIcon.Coins.ToIconString();
    private static readonly string IconFarming = FontAwesomeIcon.Anchor.ToIconString();
    private static readonly string IconBack = FontAwesomeIcon.Ship.ToIconString();
    private static readonly string IconPosition = FontAwesomeIcon.Star.ToIconString();
    private static readonly string IconNoVoyages = FontAwesomeIcon.Ban.ToIconString();
    private static readonly string IconSilent = FontAwesomeIcon.Clock.ToIconString();
    private static readonly string IconSupplies = FontAwesomeIcon.Toolbox.ToIconString();
    private static readonly string IconRunway = FontAwesomeIcon.GasPump.ToIconString();

    private static readonly string[] TickLabels = ["now", "+3h", "+6h", "+9h", "+12h", "+15h", "+18h", "+21h", "+24h"];

    private static float S(float px) => MainWindow.S(px);

    private static Vector4 Col(ImGuiCol c) => ImGui.GetStyle().Colors[(int)c];

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W);

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);

    // The window background lifted toward the frame colour by its own alpha, so it reads as a raised card on any theme.
    private static Vector4 CardBg()
    {
        var w = Col(ImGuiCol.WindowBg);
        var f = Col(ImGuiCol.FrameBg);
        return Mix(w, f with { W = w.W }, Math.Clamp(f.W * 0.6f, 0.15f, 1f));
    }

    private static Vector4 Track() => Mix(CardBg(), Col(ImGuiCol.Text), 0.12f);

    private static Vector4 LevelColor(GaugeLevel l) => l switch
    {
        GaugeLevel.Low => ImGuiColors.DalamudRed,
        GaugeLevel.Near => ImGuiColors.DalamudOrange,
        _ => ImGuiColors.HealerGreen,
    };

    public static void Draw(OverviewView v, OverviewClock c)
    {
        var style = ImGui.GetStyle();
        var gap = style.ItemSpacing.X;
        var width = ImGui.GetContentRegionAvail().X;
        var lineH = ImGui.GetTextLineHeight();
        var spY = style.ItemSpacing.Y;
        var padY = style.WindowPadding.Y;

        // four in a row needs room for "yesterday 8.63M · 7d avg 8.73M"; narrower goes 2x2
        var cols = width >= S(880) ? 4 : 2;
        var card = new Vector2((width - gap * (cols - 1)) / cols, padY * 2 + lineH * 5 + spY * 4 + S(30));

        SalvageCard(v.Salvage, card);
        ImGui.SameLine();
        FarmingCard(v.Farming, card);
        if (cols == 4) ImGui.SameLine();
        BackCard(c, card);
        ImGui.SameLine();
        PositionCard(v.Position, card);

        Timeline(c, width, lineH, spY, padY);

        var both = width >= S(600);
        var panelW = both ? (width - gap) / 2 : width;
        var supH = SuppliesHeight(v.Supplies, panelW, lineH, spY, padY);
        var warnH = WarningsHeight(v.Warnings, lineH, spY, padY);
        if (both) supH = warnH = Math.Max(supH, warnH);
        Supplies(v.Supplies, new Vector2(panelW, supH), lineH);
        if (both) ImGui.SameLine();
        Warnings(v.Warnings, v.WarningCount, new Vector2(panelW, warnH), lineH);
    }

    // ChildBg is read when the child begins, so the colour scope can close before the content.
    private static ImRaii.ChildDisposable Card(string id, Vector2 size, Vector4? accent)
    {
        ImRaii.ChildDisposable child;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg()))
            child = ImRaii.Child(id, size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (child.Success && accent is { } a)
        {
            // the child's own clip rect stops short of its border, where the accent bar sits
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetWindowPos();
            var end = p + new Vector2(S(3), ImGui.GetWindowSize().Y);
            dl.PushClipRect(p, end, false);
            dl.AddRectFilled(p, end, ImGui.GetColorU32(a), ImGui.GetStyle().ChildRounding, ImDrawFlags.RoundCornersLeft);
            dl.PopClipRect();
        }
        return child;
    }

    private static void Title(string icon, string title, Vector4 accent)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(accent, icon);
        ImGui.SameLine();
        ImGui.TextDisabled(title);
    }

    private static void Big(string value)
    {
        ImGui.SetWindowFontScale(2f);
        try { ImGui.TextUnformatted(value); }
        finally { ImGui.SetWindowFontScale(1f); }
    }

    private static void RightText(string text, Vector4 color)
    {
        ImGui.SameLine();
        var tw = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > tw) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - tw);
        ImGui.TextColored(color, text);
    }

    private static void Bar(float fraction, Vector4 color, float height, float marker = -1)
    {
        var o = ImGui.GetCursorScreenPos();
        var w = Math.Max(ImGui.GetContentRegionAvail().X, S(10));
        var dl = ImGui.GetWindowDrawList();
        var r = height / 2;
        dl.AddRectFilled(o, o + new Vector2(w, height), ImGui.GetColorU32(Track()), r);
        if (fraction > 0)
            dl.AddRectFilled(o, o + new Vector2(Math.Max(w * fraction, height), height), ImGui.GetColorU32(color), r);
        if (marker >= 0)
        {
            var x = o.X + w * marker;
            dl.AddLine(new Vector2(x, o.Y - S(2)), new Vector2(x, o.Y + height + S(2)), ImGui.GetColorU32(ImGuiColors.DalamudRed), S(2));
        }
        ImGui.Dummy(new Vector2(w, height));
    }

    private static void SalvageCard(SalvageCard v, Vector2 size)
    {
        var accent = ImGuiColors.DalamudOrange;
        using var child = Card("##ov-salvage", size, accent);
        if (!child) return;
        Title(IconSalvage, "Salvage today", accent);
        Big(v.Value);
        if (v.Compare is null)
        {
            ImGui.TextDisabled(v.Reason ?? "");
            return;
        }
        ImGui.TextDisabled(v.Compare);
        if (v.Delta is { } delta)
            ImGui.TextColored(v.DeltaSign > 0 ? ImGuiColors.HealerGreen : v.DeltaSign < 0 ? ImGuiColors.DalamudRed : Col(ImGuiCol.TextDisabled), delta);
        else
            ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight()));
        Spark(v.Spark, accent);
    }

    private static void Spark(float[] values, Vector4 accent)
    {
        var o = ImGui.GetCursorScreenPos();
        var w = Math.Max(ImGui.GetContentRegionAvail().X, S(10));
        var h = Math.Clamp(ImGui.GetContentRegionAvail().Y - S(2), S(10), S(30));
        var dl = ImGui.GetWindowDrawList();
        var n = values.Length;
        var max = 0f;
        for (var i = 0; i < n; i++) max = Math.Max(max, values[i]);
        var bottom = o.Y + h;
        if (n > 0 && max > 0)
        {
            var full = ImGui.GetColorU32(accent);
            // today is still filling, so its bar is dimmed
            var today = ImGui.GetColorU32(WithAlpha(accent, 0.45f));
            var slot = w / n;
            var gap = slot > S(4) ? S(2) : 0;
            for (var i = 0; i < n; i++)
            {
                if (values[i] <= 0) continue;
                var x0 = o.X + slot * i;
                var top = bottom - Math.Max(values[i] / max * h, S(1));
                dl.AddRectFilled(new Vector2(x0, top), new Vector2(x0 + slot - gap, bottom), i == n - 1 ? today : full);
            }
        }
        dl.AddLine(new Vector2(o.X, bottom), new Vector2(o.X + w, bottom), ImGui.GetColorU32(Track()), S(1));
        ImGui.Dummy(new Vector2(w, h));
    }

    private static void FarmingCard(FarmingCard v, Vector2 size)
    {
        var accent = ImGuiColors.HealerGreen;
        using var child = Card("##ov-farming", size, accent);
        if (!child) return;
        Title(IconFarming, "Farming", accent);
        if (v.Reason is not null)
        {
            Big(v.Value);
            ImGui.TextDisabled(v.Reason);
            return;
        }

        var o = ImGui.GetCursorScreenPos();
        var dia = Math.Clamp(ImGui.GetContentRegionAvail().Y - S(4), S(30), S(72));
        var thick = Math.Max(S(5), dia * 0.1f);
        var radius = (dia - thick) / 2;
        var center = o + new Vector2(dia / 2, dia / 2);
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(center, radius, ImGui.GetColorU32(Track()), 0, thick);
        if (v.Fraction >= 1f)
        {
            dl.AddCircle(center, radius, ImGui.GetColorU32(accent), 0, thick);
        }
        else if (v.Fraction > 0f)
        {
            const float start = -MathF.PI / 2;
            dl.PathArcTo(center, radius, start, start + MathF.Tau * v.Fraction);
            dl.PathStroke(ImGui.GetColorU32(accent), ImDrawFlags.None, thick);
        }
        var ts = ImGui.CalcTextSize(v.Pct);
        dl.AddText(center - ts / 2, ImGui.GetColorU32(ImGuiCol.Text), v.Pct);

        ImGui.SetCursorScreenPos(new Vector2(o.X + dia + S(12), o.Y));
        using (ImRaii.Group())
        {
            Big(v.Value);
            ImGui.TextDisabled(v.Routes ?? "");
            ImGui.TextDisabled(v.Levelling ?? "");
        }
    }

    private static void BackCard(OverviewClock c, Vector2 size)
    {
        var accent = ImGuiColors.TankBlue;
        using var child = Card("##ov-back", size, accent);
        if (!child) return;
        Title(IconBack, "Back now", accent);
        Big(c.Back);
        ImGui.TextDisabled(c.Status);
        Bar(c.InPort, accent, S(6));
        ImGui.TextDisabled(c.PortText);
    }

    private static void PositionCard(PositionCard v, Vector2 size)
    {
        var accent = ImGuiColors.DalamudViolet;
        using var child = Card("##ov-position", size, accent);
        if (!child) return;
        Title(IconPosition, "Position", accent);
        Big(v.Value);
        if (v.Reason is not null)
        {
            ImGui.TextDisabled(v.Reason);
            return;
        }
        ImGui.TextDisabled(v.Parts ?? "");
        Bar(v.Fraction, accent, S(6));
        ImGui.TextDisabled(v.Next ?? "");
    }

    private static void Timeline(OverviewClock c, float width, float lineH, float spY, float padY)
    {
        var plotH = S(64);
        using var child = Card("##ov-returns", new Vector2(width, padY * 2 + lineH * 2 + spY * 2 + plotH + S(4)), null);
        if (!child) return;
        ImGui.TextDisabled("RETURNS, NEXT 24 HOURS");
        RightText(c.TimelineHeader, Col(ImGuiCol.TextDisabled));

        var o = ImGui.GetCursorScreenPos();
        var w = Math.Max(ImGui.GetContentRegionAvail().X, S(10));
        ImGui.InvisibleButton("##ov-returns-plot", new Vector2(w, plotH + S(4) + lineH));
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var bottom = o.Y + plotH;
        var grid = ImGui.GetColorU32(Mix(CardBg(), Col(ImGuiCol.Text), 0.08f));
        var dim = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        var ticks = TickLabels.Length - 1;
        for (var k = 0; k <= ticks; k++)
        {
            var x = o.X + w * k / ticks;
            if (k > 0 && k < ticks) dl.AddLine(new Vector2(x, o.Y), new Vector2(x, bottom), grid, S(1));
            var label = TickLabels[k];
            var tw = ImGui.CalcTextSize(label).X;
            var lx = k == 0 ? x : k == ticks ? x - tw : x - tw / 2;
            dl.AddText(new Vector2(lx, bottom + S(4)), dim, label);
        }
        dl.AddLine(new Vector2(o.X, bottom), new Vector2(o.X + w, bottom), grid, S(1));
        dl.AddLine(new Vector2(o.X + S(1), o.Y), new Vector2(o.X + S(1), bottom), ImGui.GetColorU32(ImGuiColors.DalamudOrange), S(2));

        var counts = c.Counts;
        var slot = w / Math.Max(counts.Length, 1);
        var r = Math.Clamp(slot / 2 - S(0.5f), S(1.5f), S(4));
        var step = 2 * r + S(1);
        var maxStack = Math.Max(1, (int)((plotH - S(4)) / step));
        var dot = ImGui.GetColorU32(ImGuiColors.TankBlue);
        var mouseX = ImGui.GetMousePos().X;
        var hit = -1;
        var hitGap = Math.Max(slot / 2, r * 1.5f);
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0) continue;
            var x = Math.Clamp(o.X + w * c.Centers[i], o.X + r + S(2), o.X + w - r);
            var shown = Math.Min(counts[i], maxStack);
            for (var k = 0; k < shown; k++)
                dl.AddCircleFilled(new Vector2(x, bottom - S(2) - r - k * step), r, dot);
            var gapX = Math.Abs(mouseX - x);
            if (hovered && gapX <= hitGap)
            {
                hit = i;
                hitGap = gapX;
            }
        }
        if (hit >= 0 && c.Tips[hit] is { } tip) ImGui.SetTooltip(tip);
    }

    private static float SuppliesHeight(SuppliesPanel s, float width, float lineH, float spY, float padY)
    {
        var items = 3 + s.Lowest.Count + (s.KitsNote is null ? 0 : 1);
        var h = lineH * (2 + s.Lowest.Count + (s.KitsNote is null ? 0 : 1));
        if (s.Note is { } note)
        {
            h += ImGui.CalcTextSize(note, false, width - ImGui.GetStyle().WindowPadding.X * 2).Y;
            items += 1;
        }
        else
        {
            h += 2 * (lineH + S(8));
            items += 4;
        }
        return h + items * spY + padY * 2 + S(4);
    }

    private static void Supplies(SuppliesPanel s, Vector2 size, float lineH)
    {
        using var child = Card("##ov-supplies", size, null);
        if (!child) return;
        var dimCol = Col(ImGuiCol.TextDisabled);
        ImGui.TextDisabled("FLEET SUPPLIES");
        RightText(s.Header, dimCol);
        if (s.Note is { } note)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, dimCol))
                ImGui.TextWrapped(note);
        }
        else
        {
            Gauge(s.Ceruleum!, s.Marker);
            Gauge(s.Kits!, s.Marker);
        }
        ImGui.Spacing();
        ImGui.TextDisabled("LOWEST KITS");
        RightText(s.KitsHeader, dimCol);
        if (s.KitsNote is { } kitsNote) ImGui.TextDisabled(kitsNote);

        var w = ImGui.GetContentRegionAvail().X;
        var nameW = Math.Min(S(140), w * 0.4f);
        var valueW = S(44);
        var barW = Math.Max(w - nameW - valueW - S(8), S(10));
        var dl = ImGui.GetWindowDrawList();
        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var track = ImGui.GetColorU32(Track());
        var barH = S(6);
        var lowest = s.Lowest;
        for (var i = 0; i < lowest.Count; i++)
        {
            var row = lowest[i];
            var o = ImGui.GetCursorScreenPos();
            dl.PushClipRect(o, o + new Vector2(nameW - S(6), lineH), true);
            dl.AddText(o, text, row.Name);
            dl.PopClipRect();
            var b = new Vector2(o.X + nameW, o.Y + (lineH - barH) / 2);
            dl.AddRectFilled(b, b + new Vector2(barW, barH), track, barH / 2);
            if (row.Fraction > 0)
                dl.AddRectFilled(b, b + new Vector2(Math.Max(barW * row.Fraction, barH), barH), ImGui.GetColorU32(LevelColor(row.Level)), barH / 2);
            dl.AddText(new Vector2(b.X + barW + S(8), o.Y), text, row.Kits);
            ImGui.Dummy(new Vector2(w, lineH));
        }
    }

    private static void Gauge(RunwayGauge g, float marker)
    {
        ImGui.TextUnformatted(g.Label);
        RightText(g.Days, g.Fraction > 0 ? LevelColor(g.Level) : Col(ImGuiCol.TextDisabled));
        Bar(g.Fraction, LevelColor(g.Level), S(8), marker);
    }

    private static float RowHeight(float lineH) => lineH + S(8);

    private static float WarningsHeight(IReadOnlyList<WarningRow> rows, float lineH, float spY, float padY) =>
        padY * 2 + lineH + spY + (rows.Count == 0 ? lineH + spY : rows.Count * (RowHeight(lineH) + spY)) + S(4);

    private static void Warnings(IReadOnlyList<WarningRow> rows, string count, Vector2 size, float lineH)
    {
        using var child = Card("##ov-warnings", size, null);
        if (!child) return;
        ImGui.TextDisabled("WARNINGS");
        RightText(count, Col(ImGuiCol.TextDisabled));
        if (rows.Count == 0)
        {
            ImGui.TextColored(StatusColors.Green, "None");
            return;
        }

        var w = ImGui.GetContentRegionAvail().X;
        var rowH = RowHeight(lineH);
        var bg = ImGui.GetColorU32(Mix(CardBg(), Col(ImGuiCol.Text), 0.05f));
        var rounding = ImGui.GetStyle().FrameRounding;
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var color = row.Severe ? StatusColors.Red : StatusColors.Amber;
            var o = ImGui.GetCursorScreenPos();
            dl.AddRectFilled(o, o + new Vector2(w, rowH), bg, rounding);
            dl.AddRectFilled(o, o + new Vector2(S(3), rowH), ImGui.GetColorU32(color), rounding, ImDrawFlags.RoundCornersLeft);

            var pad = (rowH - lineH) / 2;
            ImGui.SetCursorScreenPos(o + new Vector2(S(10), pad));
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextColored(color, Icon(row.Kind));
            ImGui.SetCursorScreenPos(o + new Vector2(S(34), pad));
            ImGui.TextUnformatted(row.Text);
            ImGui.SetCursorScreenPos(o);
            ImGui.Dummy(new Vector2(w, rowH));
        }
    }

    private static string Icon(WarningKind kind) => kind switch
    {
        WarningKind.NoVoyages => IconNoVoyages,
        WarningKind.Silent => IconSilent,
        WarningKind.LowSupplies => IconSupplies,
        _ => IconRunway,
    };
}
