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
    internal sealed record Mark(ThresholdEntry Entry, double Tick, string Text, Rect2 Label, int Row, int Index, Point2[] Leader);

    private sealed record GradientLayout(double Left, double Right, Mark[] Marks, int Rows, bool Constant)
    {
        public double Top => GradientTop + (Constant ? 0 : Rows * MarkRow);
        public double Height => Rows * MarkRow;
        public double LabelTop(int row) => Constant ? GradientTop - 2 + row * MarkRow
            : Top - MarkGap - (row + 1) * MarkRow;
    }

    private const double LeaderWidth = .75;

    private static Rect2 SegmentBounds(Point2 a, Point2 b, double padding = 0) =>
        new(Math.Min(a.X, b.X) - padding, Math.Min(a.Y, b.Y) - padding,
            Math.Abs(a.X - b.X) + 2 * padding, Math.Abs(a.Y - b.Y) + 2 * padding);

    private static bool Overlaps(Rect2 a, Rect2 b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X &&
        a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static IEnumerable<(Point2 Start, Point2 End)> Segments(Point2[] path) =>
        path.Zip(path.Skip(1));

    private static GradientLayout Layout(CardSpec c, Theme t)
    {
        var min = c.Min!.Value;
        var max = c.Max!.Value;
        var cap = (c.Form.Compact ? GradientCompactBar : GradientBar) * .6 + .5;
        double Reserve(double value) => Math.Max(cap,
            TextMetrics.Measurer(value.ToString("G4", CultureInfo.InvariantCulture), 10, t.Font).Width / 2);
        var left = t.Padding + Reserve(min);
        var right = min == max ? left + 24 : Math.Max(left + 1, c.Width - t.Padding - Reserve(max));
        double Tick(ThresholdEntry entry) => entry.Value < min ? left - cap : entry.Value > max ? right + cap
            : min == max ? left + 12 : left + ColourRange.Fraction(entry.Value, min, max) * (right - left);
        var entries = c.Thresholds?.Entries.OrderBy(e => e.Value).ThenBy(e => e.Side)
            .Select((entry, index) => new Mark(entry, Tick(entry), $"{entry.Label} · {entry.Count}", default, 0, index, [])).ToArray() ?? [];
        var constant = min == max;
        var (marks, rows) = PlaceLabels(entries, t, c.Width, constant ? right + cap + 8 : null);
        return new(left, right, marks, rows, constant);
    }

    internal static (Mark[] Marks, int Rows) PlaceLabels(Mark[] entries, Theme t, double cardWidth,
        double? constantAnchor = null, bool straightOnly = false)
    {
        var marks = new List<Mark>();
        var rows = 0;
        var constant = constantAnchor.HasValue;
        var limit = cardWidth - t.Padding;
        double Width(Mark mark) => Math.Min(TextMetrics.Measurer(mark.Text, 10, t.Font).Width + 2,
            Math.Max(0, limit - t.Padding));
        // Work right to left so two crowded Above labels can face away from each other.
        // At the right end, work outward so inward-facing labels keep the next tick clear.
        var sequence = constant ? entries : entries.OrderBy(m => m.Tick + Width(m) > limit)
            .ThenBy(m => m.Tick + Width(m) > limit ? m.Tick : -m.Tick).ToArray();
        var pending = entries.ToHashSet();
        foreach (var mark in sequence)
        {
            pending.Remove(mark);
            var width = Width(mark);
            Rect2 Label(double x, int row) => new(x, -MarkGap - (row + 1) * MarkRow, width, LabelHeight);
            var tick = mark.Tick;
            var above = mark.Entry.Side == ThresholdSide.Above;
            var placed = constant
                ? mark with { Label = Label(Math.Clamp(constantAnchor!.Value, t.Padding, limit - width), rows), Row = rows }
                : Place();
            marks.Add(placed);
            rows = Math.Max(rows, placed.Row + 1);

            Mark Place()
            {
                var leaders = marks.SelectMany(m => Segments(m.Leader)).ToArray();
                // Try both sides at the tick before shifting the attachment or adding a row.
                // Keep other ticks accessible for straight leaders in subsequent rows.
                for (var row = 0; row <= rows; row++)
                {
                    foreach (var forward in new[] { above, !above })
                    {
                        var label = Label(forward ? tick : tick - width, row);
                        Point2[] path = [new(tick, 0), new(tick, label.Y + label.Height)];
                        if (Fits(label, path)) return mark with { Label = label, Row = row, Leader = path };
                    }
                }

                for (var row = 1; !straightOnly && row <= rows; row++)
                {
                    var anchors = marks.SelectMany(m => new[] { m.Label.X - 1, m.Label.X + m.Label.Width + 1 })
                        .Concat([t.Padding, limit, t.Padding + width, limit - width])
                        .OrderBy(x => Math.Abs(x - tick));
                    for (var gap = row - 1; gap >= 0; gap--)
                    foreach (var anchor in anchors)
                    foreach (var forward in new[] { above, !above })
                    {
                        if (anchor == tick) continue;
                        var label = Label(forward ? anchor : anchor - width, row);
                        var y = -MarkGap - gap * MarkRow - 1;
                        Point2[] path = [new(tick, 0), new(tick, y), new(anchor, y), new(anchor, label.Y + label.Height)];
                        if (Fits(label, path)) return mark with { Label = label, Row = row, Leader = path };
                    }
                }

                // A very narrow authored card may leave no collision-free route.
                var fallback = Label(Math.Clamp(tick, t.Padding, limit - width), rows);
                return mark with { Label = fallback, Row = rows, Leader = straightOnly
                    ? [new(tick, 0), new(Math.Clamp(tick, fallback.X, fallback.X + width), fallback.Y + fallback.Height)]
                    : [] };

                bool Fits(Rect2 label, Point2[] path)
                {
                    if (label.X < t.Padding || label.X + label.Width > limit ||
                        marks.Any(m => Overlaps(label with { X = label.X - MarkGap, Width = width + 2 * MarkGap }, m.Label)) ||
                        pending.Any(m => m.Tick > label.X && m.Tick < label.X + width) ||
                        leaders.Any(s => Overlaps(label, SegmentBounds(s.Start, s.End))))
                        return false;

                    return Segments(path).All(s =>
                    {
                        var bounds = SegmentBounds(s.Start, s.End);
                        return !marks.Any(m => Overlaps(bounds, m.Label)) && !leaders.Any(other =>
                            // Parallel tick stems may be arbitrarily close or share the same x.
                            !(s.Start.X == tick && s.End.X == tick && other.Start.X == other.End.X) &&
                            Overlaps(SegmentBounds(s.Start, s.End, LeaderWidth / 2),
                                SegmentBounds(other.Start, other.End, LeaderWidth / 2)));
                    });
                }
            }
        }
        return (marks.OrderBy(m => m.Index).ToArray(), rows);
    }

    public static double ThresholdHeight(CardSpec c, Theme t) =>
        c.Thresholds is { Entries.Length: > 0 } ? Layout(c, t).Height : 0;

    public static IEnumerable<HitRegion> ThresholdHits(CardSpec c, Theme t)
    {
        if (c.Min is null || c.Max is null || c.Thresholds is not { Entries.Length: > 0 }) return [];
        var layout = Layout(c, t);
        return layout.Marks.SelectMany(m =>
        {
            var key = ThresholdKey + m.Index;
            var label = new HitRegion(key, m.Label.X, layout.LabelTop(m.Row), m.Label.Width, m.Label.Height);
            return Segments(m.Leader).Select(s =>
            {
                var bounds = SegmentBounds(s.Start, s.End, LeaderWidth / 2);
                return new HitRegion(key, bounds.X, bounds.Y + layout.Top, bounds.Width, bounds.Height);
            }).Prepend(label);
        });
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
                    if (mark.Leader.Length > 0)
                        canvas.Polyline(mark.Leader.Select(p => new Point2(p.X, p.Y + top)).ToArray(), t.Ink, LeaderWidth);
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
