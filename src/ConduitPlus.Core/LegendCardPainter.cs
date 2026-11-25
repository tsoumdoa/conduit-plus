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
    public const string ThresholdKey = "\u0001threshold:";
    private const double MarkRow = 16, LabelHeight = 14, MarkGap = 4;
    private sealed record Mark(ThresholdEntry Entry, double Tick, Rect2 Label, int Row, int Index, Point2[] Leader)
    {
        public string Text => $"{Entry.Label} · {Entry.Count}";
    }

    private sealed record GradientLayout(double Left, double Right, Mark[] Marks, int Rows, bool Constant)
    {
        public double Top => GradientTop + (Constant ? 0 : Rows * MarkRow);
        public double Height => Rows * MarkRow;
        public double LabelTop(int row) => Constant ? GradientTop - 2 + row * MarkRow
            : Top - MarkGap - (row + 1) * MarkRow;
    }

    // Leaders use the space between rows to step around labels closer to the bar.
    // Coordinates are relative to the bar, so adding an outer row moves everything together.
    private static Point2[] Leader(double tick, Rect2 label, int row, List<List<Mark>> rows)
    {
        var points = new List<Point2> { new(tick, 0) };
        var x = tick;
        for (var i = 0; i < row; i++)
        {
            var y = -MarkGap - i * MarkRow - 1;
            points.Add(new(x, y));
            foreach (var mark in rows[i].OrderBy(m => m.Label.X))
                if (x > mark.Label.X - 1 && x < mark.Label.X + mark.Label.Width + 1)
                    x = mark.Label.X + mark.Label.Width + 1;
            points.Add(new(x, y));
        }
        var approach = label.Y + MarkRow - 1;
        var edge = tick <= label.X ? label.X : label.X + label.Width;
        var elbow = edge + (edge == label.X ? -1 : 1);
        points.Add(new(x, approach));
        points.Add(new(elbow, approach));
        points.Add(new(elbow, label.Y + LabelHeight / 2));
        points.Add(new(edge, label.Y + LabelHeight / 2));
        return points.ToArray();
    }

    private static GradientLayout Layout(CardSpec c, Theme t)
    {
        var min = c.Min!.Value;
        var max = c.Max!.Value;
        var cap = (c.Form.Compact ? GradientCompactBar : GradientBar) * .6 + .5;
        double Reserve(double value) => Math.Max(cap,
            TextMetrics.Measurer(value.ToString("G4", CultureInfo.InvariantCulture), 10, t.Font).Width / 2);
        var left = t.Padding + Reserve(min);
        var right = min == max ? left + 24 : Math.Max(left + 1, c.Width - t.Padding - Reserve(max));
        var rows = new List<List<Mark>>();
        var marks = new List<Mark>();
        foreach (var entry in c.Thresholds?.Entries.OrderBy(e => e.Value).ThenBy(e => e.Side) ?? Enumerable.Empty<ThresholdEntry>())
        {
            var tick = entry.Value < min ? left - cap : entry.Value > max ? right + cap
                : min == max ? left + 12 : left + ColourRange.Fraction(entry.Value, min, max) * (right - left);
            var mark = new Mark(entry, tick, default, 0, marks.Count, []);
            var width = TextMetrics.Measurer(mark.Text, 10, t.Font).Width + 2;
            var forward = min == max || entry.Side == ThresholdSide.Above;
            var x = forward ? tick + 4 : tick - width - 4;
            if (x < t.Padding || x + width > c.Width - t.Padding)
                x = forward ? tick - width - 4 : tick + 4;
            if (min == max) x = right + cap + 8;
            x = Math.Clamp(x, t.Padding, Math.Max(t.Padding, c.Width - t.Padding - width));
            var row = 0;
            for (; row < rows.Count; row++)
            {
                if (min == max) continue;
                if (rows[row].Any(m => x < m.Label.X + m.Label.Width + 4 && x + width + 4 > m.Label.X))
                    continue;
                var top = -MarkGap - (row + 1) * MarkRow;
                // A previously routed leader may pass through this row on its way outward.
                if (marks.Any(m => m.Leader.Zip(m.Leader.Skip(1)).Any(segment =>
                    Math.Min(segment.First.X, segment.Second.X) < x + width + 1 &&
                    Math.Max(segment.First.X, segment.Second.X) > x - 1 &&
                    Math.Min(segment.First.Y, segment.Second.Y) < top + LabelHeight &&
                    Math.Max(segment.First.Y, segment.Second.Y) > top)))
                    continue;
                break;
            }
            if (row == rows.Count) rows.Add([]);
            var label = new Rect2(x, -MarkGap - (row + 1) * MarkRow, width, LabelHeight);
            mark = mark with { Label = label, Row = row,
                Leader = min == max ? [] : Leader(tick, label, row, rows) };
            rows[row].Add(mark);
            marks.Add(mark);
        }
        return new(left, right, marks.ToArray(), rows.Count, min == max);
    }

    public static double ThresholdHeight(CardSpec c, Theme t) =>
        c.Thresholds is { Entries.Length: > 0 } ? Layout(c, t).Height : 0;

    public static IEnumerable<HitRegion> ThresholdHits(CardSpec c, Theme t)
    {
        if (c.Min is null || c.Max is null || c.Thresholds is not { Entries.Length: > 0 }) return [];
        var layout = Layout(c, t);
        return layout.Marks.Select(m => new HitRegion(ThresholdKey + m.Index,
            m.Label.X, layout.LabelTop(m.Row), m.Label.Width, m.Label.Height));
    }

    public static ThresholdEntry? ThresholdAt(CardSpec c, string key)
    {
        if (!key.StartsWith(ThresholdKey, StringComparison.Ordinal) ||
            !int.TryParse(key.AsSpan(ThresholdKey.Length), out var index) || index < 0) return null;
        return c.Thresholds?.Entries.OrderBy(e => e.Value).ThenBy(e => e.Side).ElementAtOrDefault(index);
    }
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        if (c.Min is { } min && c.Max is { } max)
        {
            string Label(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
            double LabelWidth(double value) => canvas.MeasureText(Label(value), 10, t.Font).Width;
            // The unit sits on the title row, right-aligned (CardPainter keeps the title clear of it).
            if (!string.IsNullOrWhiteSpace(c.Unit))
                canvas.Text(c.Unit, new(c.Width - t.Padding - canvas.MeasureText(c.Unit, t.FontSize, t.Font).Width, 10), t.FontSize, t.Muted, t.Font);
            var barH = c.Form.Compact ? GradientCompactBar : GradientBar;
            var capW = barH * .6;
            // Reserve each cap (including its stroke) or half the endpoint label, whichever is wider.
            // Caps stay inside the existing measured card bounds in both forms.
            var left = t.Padding + Math.Max(c.BelowColor is null ? 2 : capW + .5, LabelWidth(min) / 2);
            var right = c.Width - t.Padding - Math.Max(c.AboveColor is null ? 2 : capW + .5, LabelWidth(max) / 2);
            var layout = c.Thresholds is { Entries.Length: > 0 } ? Layout(c, t) : null;
            var top = min == max ? GradientTop : layout?.Top ?? GradientTop;
            if (layout is not null) { left = layout.Left; right = layout.Right; }
            var width = Math.Max(1, right - left);
            if (layout is null)
                canvas.Gradient(new(left, top, width, barH), c.Colors, c.ColorPositions);
            else if (min == max)
                canvas.Rect(new(left, top, 24, barH), c.Thresholds!.WinnerFor(min)?.Colour ?? c.Mapping?.ColourAt(min) ?? c.Colors[0]);
            else
            {
                canvas.Gradient(new(left, top, width, barH), c.Colors, c.ColorPositions);
                var edges = layout.Marks.Select(m => m.Entry.Value).Where(v => v > min && v < max)
                    .Distinct().Prepend(min).Append(max).ToArray();
                foreach (var (lo, hi) in edges.Zip(edges.Skip(1)))
                {
                    // Compare interval boundaries directly, even when no double lies between them.
                    var winner = c.Thresholds!.WinnerBetween(lo, hi);
                    if (winner is null || winner.Value < min || winner.Value > max) continue;
                    var start = ColourRange.Fraction(lo, min, max);
                    var end = ColourRange.Fraction(hi, min, max);
                    if (end > start) canvas.Rect(new(left + start * width, top, (end - start) * width, barH), winner.Colour);
                }
            }
            if (layout is not null)
                foreach (var mark in layout.Marks)
                {
                    var direction = mark.Entry.Value < min ? -1 : mark.Entry.Value > max ? 1 : 0;
                    if (min != max)
                        canvas.Polyline(mark.Leader.Select(p => new Point2(p.X, p.Y + top)).ToArray(), t.Ink, .75);
                    if (min != max && direction != 0)
                        canvas.Polyline([new(mark.Tick - direction * 3, top - 1), new(mark.Tick, top + 3),
                            new(mark.Tick - direction * 3, top + 7)], t.Ink);
                    else if (min != max)
                        canvas.Line(new(mark.Tick, top), new(mark.Tick, top + 1), t.Ink);
                    canvas.Text(mark.Text, new(mark.Label.X, layout.LabelTop(mark.Row)), 10, t.Ink, t.Font);
                }
            void Cap(double x, double direction, string colour)
            {
                Point2[] points = [new(x, top), new(x + direction * capW, top + barH / 2), new(x, top + barH)];
                canvas.FillPolygon(points, colour);
                canvas.Polyline([.. points, points[0]], t.Muted, .75);
            }
            if ((layout is null || min != max) && c.BelowColor is { } below) Cap(left, -1, below);
            if ((layout is null || min != max) && c.AboveColor is { } above) Cap(right, 1, above);
            var tickTop = top + barH; var labelTop = tickTop + 6;
            var placed = new List<(double Left, double Right)>();
            // End labels first so a middle label never crowds them out; then middle ticks that fit.
            foreach (var tick in Ticks(c).OrderBy(v => v == min || v == max ? 0 : 1))
            {
                var x = min == max && layout is not null ? left + 12 : left + (min == max ? .5 : ColourRange.Fraction(tick, min, max)) * width;
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
