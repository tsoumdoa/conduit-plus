using System.Globalization;

namespace ConduitPlus.Core;

public static class LegendCardPainter
{
    public const double GradientTop = 40, GradientBar = 10, GradientCompactBar = 8;
    /// <summary>Gradient legend heights: title row, bar, ticks and labels, then padding.</summary>
    public const double GradientHeight = 84, GradientCompactHeight = 76;
    /// <summary>Content width in CSS pixels; independent of authored width, layout forms and selection.</summary>
    public static double CategoricalWidth(CardSpec c, Theme t, Func<string, double, string, Size2> measure)
    {
        double Widest(IEnumerable<string> text) => text.Select(s => measure(s, t.FontSize, t.Font).Width).DefaultIfEmpty().Max();
        var row = 2 * t.Padding + 12 + 8 + Widest(c.Labels)
            + (c.ValueLabels.Length > 0 ? 8 + Widest(c.ValueLabels) : 0);
        // Clear ends at Width - Padding; the title clips 4px before its 40px button.
        var title = 2 * t.Padding + measure(c.Title, t.FontSize, t.Font).Width + (c.Keys.Length > 0 ? 40 + 4 : 0);
        return Math.Ceiling(Math.Clamp(Math.Max(row, title), 120, 280));
    }
    // Include the exact scale limits so the final color's value is always explicit.
    public static double[] Ticks(CardSpec c) => c.Min == c.Max ? [c.Min!.Value] : ChartTicks.Nice(c.Min!.Value, c.Max!.Value).Where(v => v > c.Min.Value && v < c.Max.Value).Prepend(c.Min.Value).Append(c.Max.Value).ToArray();
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        if (c.Min is { } min && c.Max is { } max)
        {
            string Label(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
            double LabelWidth(double value) => canvas.MeasureText(Label(value), 10, t.Font).Width;
            // The unit sits on the title row, right-aligned (CardPainter keeps the title clear of it).
            if (!string.IsNullOrWhiteSpace(c.Unit))
                canvas.Text(c.Unit, new(c.Width - t.Padding - canvas.MeasureText(c.Unit, t.FontSize, t.Font).Width, 10), t.FontSize, t.Muted, t.Font);
            // Full-width bar, inset only enough to centre the end labels under its ends.
            var left = t.Padding + Math.Max(2, LabelWidth(min) / 2); var right = c.Width - t.Padding - Math.Max(2, LabelWidth(max) / 2);
            var width = Math.Max(1, right - left);
            var barH = c.Form.Compact ? GradientCompactBar : GradientBar;
            canvas.Gradient(new(left, GradientTop, width, barH), c.Colors, c.ColorPositions);
            var tickTop = GradientTop + barH; var labelTop = tickTop + 6;
            var placed = new List<(double Left, double Right)>();
            // End labels first so a middle label never crowds them out; then middle ticks that fit.
            foreach (var tick in Ticks(c).OrderBy(v => v == min || v == max ? 0 : 1))
            {
                var x = left + (min == max ? .5 : ColourRange.Fraction(tick, min, max)) * width;
                var labelWidth = LabelWidth(tick);
                var labelX = x - labelWidth / 2;
                if (placed.Any(p => labelX < p.Right + 6 && labelX + labelWidth > p.Left - 6)) continue;
                canvas.Line(new(x, tickTop), new(x, tickTop + 4), t.Muted);
                canvas.Text(Label(tick), new(labelX, labelTop), 10, t.Muted, t.Font);
                placed.Add((labelX, labelX + labelWidth));
            }
            return;
        }
        var hits = CardMeasure.DataHits(c, t);
        var categoricalRight = CardMeasure.Measure(c, t).Width - t.Padding;
        for (var i = 0; i < c.Colors.Length; i++)
        {
            var y = CardMeasure.BodyTop + i * t.RowHeight;
            if (hits.Length > 0) CardPainter.Highlight(canvas, c, t, hits[i]);
            var swatch = new Rect2(t.Padding, y + (t.RowHeight - 12) / 2, 12, 12);
            var radius = t.Radius == 0 ? 0 : 2;
            canvas.RoundedRect(swatch, radius, c.Colors[i]);
            // A 1px stroke centred half a pixel inside the fill leaves the entire edge inset.
            canvas.Polyline(ThemePainter.Outline(new(swatch.X + .5, swatch.Y + .5, 11, 11), Math.Max(0, radius - .5)),
                ColorRgba.Parse(t.Ink).WithAlpha(0x29).ToHex());

            var labelX = t.Padding + 12 + 8;
            var right = categoricalRight;
            var labelRight = right;
            if (c.ValueLabels.Length > 0)
            {
                var value = c.ValueLabels[i];
                var size = canvas.MeasureText(value, t.FontSize, t.Font);
                labelRight -= size.Width + 8;
                using var valueClip = canvas.Clip(new(labelX, y, Math.Max(0, right - labelX), t.RowHeight));
                canvas.Text(value, new(right - size.Width, y + (t.RowHeight - size.Height) / 2), t.FontSize, t.Muted, t.Font);
            }
            var available = Math.Max(0, labelRight - labelX);
            var label = c.Labels[i];
            if (canvas.MeasureText(label, t.FontSize, t.Font).Width > available)
            {
                while (label.Length > 0 && canvas.MeasureText(label + "…", t.FontSize, t.Font).Width > available) label = label[..^1];
                label = canvas.MeasureText("…", t.FontSize, t.Font).Width <= available ? label + "…" : "";
            }
            using var clip = canvas.Clip(new(labelX, y, available, t.RowHeight));
            canvas.Text(label, new(labelX, y + (t.RowHeight - canvas.MeasureText(label, t.FontSize, t.Font).Height) / 2), t.FontSize, t.Ink, t.Font);
        }
    }
}
