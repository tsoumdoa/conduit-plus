using System.Globalization;

namespace ConduitPlus.Core;

public static class LegendCardPainter
{
    public const double GradientTop = 40, GradientBar = 10, GradientCompactBar = 8;
    /// <summary>Gradient legend heights: title row, bar, ticks and labels, then padding.</summary>
    public const double GradientHeight = 84, GradientCompactHeight = 76;
    // Include the exact scale limits so the final color's value is always explicit.
    public static double[] Ticks(CardSpec c) => ChartTicks.Nice(c.Min!.Value, c.Max!.Value).Where(v => v > c.Min.Value && v < c.Max.Value).Prepend(c.Min.Value).Append(c.Max.Value).ToArray();
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
            canvas.Gradient(new(left, GradientTop, width, barH), c.Colors);
            var tickTop = GradientTop + barH; var labelTop = tickTop + 6;
            var placed = new List<(double Left, double Right)>();
            // End labels first so a middle label never crowds them out; then middle ticks that fit.
            foreach (var tick in Ticks(c).OrderBy(v => v == min || v == max ? 0 : 1))
            {
                var x = left + (tick - min) / (max - min) * width;
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
        for (var i = 0; i < c.Colors.Length; i++)
        {
            var y = 36 + i * t.RowHeight;
            if (hits.Length > 0) CardPainter.Highlight(canvas, c, t, hits[i]);
            canvas.Rect(new(t.Padding + 3, y + 4, 14, 14), c.Colors[i]);
            using var clip = canvas.Clip(new(t.Padding + 26, y, c.Width - 2 * t.Padding - 26, t.RowHeight));
            canvas.Text(c.Labels[i], new(t.Padding + 26, y + 2), t.FontSize, t.Ink, t.Font);
        }
    }
}
