using System.Globalization;

namespace ConduitPlus.Core;

public static class ChartTicks
{
    // Use 1/2/5 decade steps for readable, evenly spaced axis ticks.
    public static double[] Nice(double minimum, double maximum, int intervals = 4)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum || intervals is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (minimum == maximum)
        {
            var delta = Math.Max(.5, Math.Abs(minimum) * .05);
            minimum = Math.Max(-double.MaxValue, minimum - delta);
            maximum = Math.Min(double.MaxValue, maximum + delta);
        }
        var raw = (maximum - minimum) / intervals;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        if (!double.IsFinite(step) || step <= 0) return [minimum, maximum];
        var first = Math.Floor(minimum / step);
        var last = Math.Ceiling(maximum / step);
        if (!double.IsFinite(last - first) || last - first > 100) return [minimum, maximum];
        var ticks = Enumerable.Range(0, (int)(last - first) + 1).Select(i => (first + i) * step).ToArray();
        return ticks.Length < 2 || ticks.Any(v => !double.IsFinite(v)) ? [minimum, maximum] : ticks;
    }
}

public static class LineCardPainter
{
    public static HitRegion[] Hits(CardSpec c)
    {
        var step = (c.Width - 64) / (c.Values.Length - 1);
        return c.Keys.Select((key, i) =>
        {
            var left = i == 0 ? 40 : 44 + (i - .5) * step;
            var right = i == c.Keys.Length - 1 ? c.Width - 16 : 44 + (i + .5) * step;
            return new HitRegion(key, left, 40, right - left, c.Form.Compact ? 68 : 148);
        }).ToArray();
    }

    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        var bottom = c.Form.Compact ? 104 : 184;
        var hits = Hits(c);
        foreach (var hit in hits)
            if (hit.Key == c.HoverKey) canvas.Rect(new(hit.X, hit.Y, hit.W, hit.H), t.Track);
        var ticks = ChartTicks.Nice(Math.Min(0, c.Values.Min() ?? 0), Math.Max(c.Threshold, c.Values.Max() ?? 0));
        var lo = ticks[0]; var hi = ticks[^1]; var right = c.Width - 20;
        double Y(double v) => bottom - (v - lo) / (hi - lo) * (bottom - 44);
        double X(int i) => 44 + i * (right - 44) / (c.Values.Length - 1);
        if (c.Threshold > 0) canvas.Rect(new(44, 44, right - 44, Y(c.Threshold) - 44), t.ThresholdBand);
        foreach (var tick in ticks)
        {
            canvas.Line(new(44, Y(tick)), new(right, Y(tick)), t.Line);
            canvas.Text(tick.ToString("G4", CultureInfo.InvariantCulture), new(5, Y(tick) - 7), 10, t.Muted, t.Font);
        }
        canvas.Line(new(44, 44), new(44, bottom), t.Muted);
        canvas.Line(new(44, bottom), new(right, bottom), t.Muted);
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
        if (c.Threshold > 0) canvas.Line(new(44, Y(c.Threshold)), new(right, Y(c.Threshold)), t.Bad);
        for (var i = 0; i < c.Values.Length; i++)
        {
            if (i == 0 || i == c.Values.Length - 1 || i % Math.Max(1, (int)Math.Ceiling(c.Values.Length / 4d)) == 0)
            {
                using var clip = canvas.Clip(new(X(i) - 12, bottom + 6, Math.Min(50, c.Width - X(i) + 12), 20));
                canvas.Text(c.Labels[i], new(X(i) - 12, bottom + 6), 10, t.Muted, t.Font);
            }
            var selected = c.SelectedKeys.Contains(c.Keys[i]);
            if (selected) canvas.Line(new(X(i), 44), new(X(i), bottom), t.Ink, 2);
            if (c.HoveredKeys.Contains(c.Keys[i]) || c.HoverKey == c.Keys[i])
                CardPainter.Outline(canvas, hits[i], t.Accent);
        }
    }
}
