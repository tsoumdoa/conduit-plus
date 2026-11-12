using System.Globalization;

namespace ConduitPlus.Core;

public static class Tooltip
{
    public static string[]? For(CardSpec c, string? key, MembershipIndex? membership = null)
    {
        var lines = Content(c, key);
        if (lines is null && key is not null && c.Groups.ContainsKey(key)) lines = [key];
        if (lines is null || key is null) return lines;
        if (c.Groups.ContainsKey(key))
        {
            if (c.GroupStates.GetValueOrDefault(key) is { } group)
                return [.. lines, .. (group.IsEnabled ? GroupTooltip.Lines(group) : ["Group disabled: conflicting membership."])];
            return lines;
        }
        return WithMemberGroups(lines, membership, key);
    }
    public static string[] WithMemberGroups(string[] lines, MembershipIndex? membership, string key)
    {
        if (membership is null) return lines;
        var groups = GroupTooltip.ForMember(membership, key);
        return [.. lines, .. groups.Labels.Select(label => "Group: " + label),
            .. (groups.RemainingCount > 0 ? new[] { $"+{groups.RemainingCount} more groups" } : [])];
    }
    private static string[]? Content(CardSpec c, string? key)
    {
        if (string.IsNullOrEmpty(key) || CardMeasure.IsReserved(key)) return null;
        if (c.Kind == CardKind.Table)
        {
            var row = c.Rows.FirstOrDefault(r => r.Key == key);
            return row is null || c.Headers.Length == 0 ? null : c.Headers.Select((h, i) => $"{h}: {Shorten(row.Cells[i] ?? "—")}").ToArray();
        }
        var index = Array.IndexOf(c.Keys, key);
        if (index < 0) return null;
        if (c.Kind == CardKind.Metric) return [string.IsNullOrWhiteSpace(c.Title) ? key : c.Title, (c.Value ?? "—") + (string.IsNullOrEmpty(c.Unit) ? "" : " " + c.Unit)];
        if (c.Kind == CardKind.Legend && !c.Min.HasValue) return string.IsNullOrWhiteSpace(c.Labels[index]) ? null : [c.Labels[index]];
        if (c.Kind is not (CardKind.Bar or CardKind.Line)) return null;
        var label = c.Labels[index]; var value = c.Values[index];
        var number = value is null ? "missing" : c.Kind == CardKind.Bar
            ? BarPresentation.Format(value, BarPresentation.Precision(c.Values))
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        var lines = new List<string> { string.IsNullOrEmpty(label) ? key : label, number };
        if (c.Kind == CardKind.Bar && value.HasValue && c.HasThreshold && c.Threshold > 0)
            lines.Add(((c.GoodWhen == BarGoodWhen.Within ? Math.Abs(value.Value) : value.Value) / c.Threshold * 100).ToString("0.###", CultureInfo.InvariantCulture) + "% of threshold");
        if (c.Kind == CardKind.Bar && c.GoodWhen == BarGoodWhen.Within)
            lines.Add(BarPresentation.ThresholdWarning(c) ?? "Within: bars are neutral; only the part beyond the tolerance is red (−Threshold ≤ value ≤ Threshold).");
        if (c.Kind == CardKind.Bar && !string.IsNullOrEmpty(label) && key != label) lines.Add(key);
        return lines.ToArray();
    }
    private static string Shorten(string cell) => cell.Length > 40 ? cell[..39] + "…" : cell;
}

public static class TooltipPainter
{
    public const double Padding = 8;
    private static double FontSize(Theme t, int i) => t.FontSize + (i == 0 ? 1 : 0);
    private static double Height(ICanvas2D canvas, Theme t, int i) =>
        Math.Ceiling(Math.Max(FontSize(t, i) * 1.5, canvas.MeasureText("Mg", FontSize(t, i), t.Font).Height));
    public static Size2 Measure(ICanvas2D canvas, IReadOnlyList<string> lines, Theme t) => new(
        Math.Ceiling(lines.Select((line, i) => canvas.MeasureText(line, FontSize(t, i), t.Font).Width).DefaultIfEmpty(0).Max()) + Padding * 2,
        Enumerable.Range(0, lines.Count).Sum(i => Height(canvas, t, i)) + Padding * 2);
    public static void Paint(ICanvas2D canvas, IReadOnlyList<string> lines, Theme t)
    {
        var size = Measure(canvas, lines, t);
        var frame = t with { Radius = t.TooltipRadius };
        ThemePainter.Shadow(canvas, size, frame);
        using var clip = canvas.Clip(new(0, 0, size.Width, size.Height));
        canvas.RoundedRect(new(0, 0, size.Width, size.Height), t.TooltipRadius, t.TooltipSurface);
        ThemePainter.Border(canvas, size, frame);
        var y = Padding;
        for (var i = 0; i < lines.Count; i++)
        {
            canvas.Text(lines[i], new(Padding, y), FontSize(t, i), t.TooltipInk, t.Font);
            y += Height(canvas, t, i);
        }
    }
    /// <summary>All arguments in viewport client pixels. Flip before clamping, including oversized tooltips.</summary>
    public static Point2 Position(Point2 cursor, Size2 tooltip, Size2 viewport, double dpi = 1)
    {
        var gap = 14 * dpi;
        var x = cursor.X + gap; var y = cursor.Y + gap;
        if (x + tooltip.Width > viewport.Width) x = cursor.X - gap - tooltip.Width;
        if (y + tooltip.Height > viewport.Height) y = cursor.Y - gap - tooltip.Height;
        return new(Math.Clamp(x, 0, Math.Max(0, viewport.Width - tooltip.Width)),
            Math.Clamp(y, 0, Math.Max(0, viewport.Height - tooltip.Height)));
    }
}
