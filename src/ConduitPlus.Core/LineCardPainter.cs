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
    public sealed record AxisLabel(int Index, string Text, double X, double Width);
    /// <summary>Bound measurement input, reserve endpoints, then use a regular sample stride.</summary>
    public static AxisLabel[] Labels(CardSpec c, Theme t, Func<string, double, string, Size2> measure)
    {
        const double left = 44, gap = 4;
        var right = c.Width - 20;
        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        double Width(string text)
        {
            if (!widths.TryGetValue(text, out var w)) widths[text] = w = measure(text, 10, t.Font).Width;
            return w;
        }
        // Never send an imported multi-kilobyte description to GDI. Drop the final partial
        // text element at the cap, preserving surrogate pairs and combining sequences.
        static string Bounded(string text)
        {
            if (text.Length <= 256) return text;
            var starts = StringInfo.ParseCombiningCharacters(text[..257]);
            return text[..starts[^1]] + "…";
        }
        string Fit(string text, double width)
        {
            if (Width(text) <= width) return text;
            if (Width("…") > width) return "";
            var starts = StringInfo.ParseCombiningCharacters(text);
            var lo = 0; var hi = starts.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var end = mid < starts.Length ? starts[mid] : text.Length;
                if (Width(text[..end] + "…") <= width) lo = mid; else hi = mid - 1;
            }
            var length = lo < starts.Length ? starts[lo] : text.Length;
            return text[..length] + "…";
        }
        var labels = c.Labels.Select(Bounded).ToArray();
        var first = labels[0]; var last = labels[^1];
        if (Width(first) + Width(last) + gap > right - left)
        {
            var half = Math.Max(0, (right - left - gap) / 2);
            first = Fit(first, half); last = Fit(last, half);
        }
        var placed = new List<AxisLabel> { new(0, first, left, Width(first)) };
        var lastX = right - Width(last);
        var step = (right - left) / (labels.Length - 1);
        var widest = labels.Skip(1).SkipLast(1).Select(Width).DefaultIfEmpty(0).Max();
        var stride = Math.Max(1, (int)Math.Ceiling(Math.Max(widest + gap, Width(first) + widest / 2 + gap) / step));
        for (var i = stride; i < labels.Length - 1; i += stride)
        {
            var width = Width(labels[i]);
            var x = left + i * step - width / 2;
            // The last endpoint is inward anchored, so omit any final stride label it overlaps.
            if (x + width + gap > lastX) break;
            placed.Add(new(i, labels[i], x, width));
        }
        placed.Add(new(labels.Length - 1, last, lastX, Width(last)));
        return placed.ToArray();
    }

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
        var ticks = ChartTicks.Nice(Math.Min(0, c.Values.Min() ?? 0), Math.Max(c.Threshold, c.Values.Max() ?? 0));
        var lo = ticks[0]; var hi = ticks[^1]; var right = c.Width - 20;
        double Y(double v) => bottom - (v - lo) / (hi - lo) * (bottom - 44);
        double X(int i) => 44 + i * (right - 44) / (c.Values.Length - 1);
        if (c.Threshold > 0) canvas.Rect(new(44, 44, right - 44, Y(c.Threshold) - 44), t.ThresholdBand);
        foreach (var hit in hits)
            if (!c.SelectedKeys.Contains(hit.Key) && hit.Key == c.HoverKey) canvas.Rect(new(hit.X, hit.Y, hit.W, hit.H), t.Track);
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
        foreach (var label in Labels(c, t, canvas.MeasureText))
            canvas.Text(label.Text, new(label.X, bottom + 6), 10, t.Muted, t.Font);
        for (var i = 0; i < c.Values.Length; i++)
        {
            var selected = c.SelectedKeys.Contains(c.Keys[i]);
            if (selected)
            {
                canvas.Line(new(X(i), 44), new(X(i), bottom), t.Accent);
                if (c.Values[i] is { } value)
                {
                    canvas.Rect(new(X(i) - 6, Y(value) - 6, 12, 12), t.Surface);
                    canvas.Rect(new(X(i) - 4, Y(value) - 4, 8, 8), t.Accent);
                }
            }
            else if ((c.HoveredKeys.Contains(c.Keys[i]) || c.HoverKey == c.Keys[i]) && c.Values[i] is { } hoveredValue)
            {
                canvas.Rect(new(X(i) - 6, Y(hoveredValue) - 6, 12, 12), t.Surface);
                canvas.Rect(new(X(i) - 4, Y(hoveredValue) - 4, 8, 8), t.Accent);
                canvas.Rect(new(X(i) - 3, Y(hoveredValue) - 3, 6, 6), t.Surface);
            }
        }
    }
}
