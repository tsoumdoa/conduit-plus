using System.Globalization;

namespace ConduitPlus.Core;

public static class LegendCardPainter
{
    // Include the exact scale limits so the final color's value is always explicit.
    public static double[] Ticks(CardSpec c) => ChartTicks.Nice(c.Min!.Value, c.Max!.Value).Where(v => v > c.Min.Value && v < c.Max.Value).Prepend(c.Min.Value).Append(c.Max.Value).ToArray();
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        if (c.Min is { } min && c.Max is { } max)
        {
            var left = t.Padding + 16; var width = c.Width - 2 * left;
            canvas.Gradient(new(left, 40, width, 14), c.Colors);
            string Label(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
            double LabelWidth(double value) => canvas.MeasureText(Label(value), 10, t.Font).Width;
            var previousRight = double.NegativeInfinity;
            foreach (var tick in Ticks(c))
            {
                var x = left + (tick - min) / (max - min) * width;
                var labelWidth = LabelWidth(tick);
                var labelX = Math.Clamp(x - labelWidth / 2, t.Padding, Math.Max(t.Padding, c.Width - t.Padding - labelWidth));
                if (tick != min && tick != max && (labelX < previousRight + 4 || labelX + labelWidth + 4 > left + width - LabelWidth(max) / 2)) continue;
                canvas.Line(new(x, 54), new(x, 59), t.Muted);
                canvas.Text(Label(tick), new(labelX, c.Form.Compact ? 57 : 63), 10, t.Muted, t.Font);
                previousRight = labelX + labelWidth;
            }
            if (!c.Form.Compact) canvas.Text(c.Unit, new(left, 85), t.FontSize, t.Muted, t.Font);
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
