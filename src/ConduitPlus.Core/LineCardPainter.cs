using System.Globalization;

namespace ConduitPlus.Core;

public static class ChartTicks
{
    // Use 1/2/5 decade steps for readable, evenly spaced axis ticks.
    public static double[] Nice(double minimum, double maximum, int intervals = 4)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum || intervals is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (minimum == maximum) { minimum -= .5; maximum += .5; }
        var raw = (maximum - minimum) / intervals;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        var first = Math.Floor(minimum / step);
        var last = Math.Ceiling(maximum / step);
        return Enumerable.Range(0, checked((int)(last - first)) + 1).Select(i => (first + i) * step).ToArray();
    }
}

public static class LineCardPainter
{
    public static HitRegion[] Hits(CardSpec c)
    {
        var step = (c.Width - 64) / (c.Values.Length - 1);
        return c.Keys.Select((key, i) => new HitRegion(key, Math.Max(44, 44 + (i - .5) * step), 44,
            step * (i == 0 || i == c.Keys.Length - 1 ? .5 : 1), 140)).ToArray();
    }

    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        var ticks = ChartTicks.Nice(Math.Min(0, c.Values.Min() ?? 0), Math.Max(c.Threshold, c.Values.Max() ?? 0));
        var lo = ticks[0]; var hi = ticks[^1]; var right = c.Width - 20;
        double Y(double v) => 184 - (v - lo) / (hi - lo) * 140;
        double X(int i) => 44 + i * (right - 44) / (c.Values.Length - 1);
        canvas.Rect(new(44, 44, right - 44, Y(c.Threshold) - 44), t.ThresholdBand);
        foreach (var tick in ticks)
        {
            canvas.Line(new(44, Y(tick)), new(right, Y(tick)), t.Line);
            canvas.Text(tick.ToString("G4", CultureInfo.InvariantCulture), new(5, Y(tick) - 7), 10, t.Muted, t.Font);
        }
        canvas.Line(new(44, 44), new(44, 184), t.Muted);
        canvas.Line(new(44, 184), new(right, 184), t.Muted);
        var segment = new List<Point2>();
        for (var i = 0; i <= c.Values.Length; i++)
        {
            if (i < c.Values.Length && c.Values[i] is { } v) segment.Add(new(X(i), Y(v)));
            else { Flush(); segment.Clear(); }
        }
        Flush();
        void Flush()
        {
            if (segment.Count == 0) return;
            canvas.FillPolygon([new(segment[0].X, Y(0)), .. segment, new(segment[^1].X, Y(0))], t.Area);
            canvas.Polyline(segment, t.Accent, 2);
            foreach (var p in segment) canvas.Rect(new(p.X - 2, p.Y - 2, 4, 4), t.Accent);
        }
        canvas.Line(new(44, Y(c.Threshold)), new(right, Y(c.Threshold)), t.Bad);
        for (var i = 0; i < c.Values.Length; i++)
        {
            if (i == 0 || i == c.Values.Length - 1 || i % Math.Max(1, (int)Math.Ceiling(c.Values.Length / 4d)) == 0)
            {
                using var clip = canvas.Clip(new(X(i) - 12, 190, Math.Min(50, c.Width - X(i) + 12), 20));
                canvas.Text(c.Labels[i], new(X(i) - 12, 190), 10, t.Muted, t.Font);
            }
            if (c.HoverKey == c.Keys[i] || c.SelectedKeys.Contains(c.Keys[i]))
                canvas.Line(new(X(i), 44), new(X(i), 184), t.Ink, 2);
        }
    }
}
