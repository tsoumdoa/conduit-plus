namespace ConduitPlus.Core;

internal static class DistributionPainter
{
    public static void Paint(ICanvas2D canvas, CardSpec card, Theme theme)
    {
        var accent = theme.Accent;
        var layout = Distribution.Geometry(card, theme);
        var plot = layout.Plot;
        var bottom = plot.Y + plot.Height;
        var right = plot.X + plot.Width;
        double X(double value) => plot.X + plot.Width * (layout.Min == layout.Max ? .5 :
            Math.Clamp(ColourRange.Fraction(value, layout.Min, layout.Max), 0, 1));
        void Text(string text, double x, double y, string colour) => canvas.Text(text, new(x, y), 10, colour, theme.Font);
        foreach (var hit in Distribution.HeaderHits(card, theme))
        {
            var choice = Distribution.Choice(hit.Key);
            if (choice == layout.Mode) canvas.RoundedRect(new(hit.X, hit.Y, hit.W, hit.H), 4, theme.Track);
            Text(choice.ToString()!, hit.X + 5, hit.Y + 5, layout.Forced ? theme.Muted : choice == layout.Mode ? accent : theme.Ink);
        }
        canvas.Line(new(plot.X, bottom), new(right, bottom), theme.Muted);
        if (layout.Count == 0) { Text("No values", plot.X, plot.Y + 52, theme.Muted); return; }
        if (card.Distribution?.ThresholdLines != false && card.Thresholds is { } thresholds)
        {
            foreach (var threshold in thresholds.Entries)
            {
                var x = X(threshold.Value);
                var left = threshold.Side == ThresholdSide.Above ? x : plot.X;
                var end = threshold.Side == ThresholdSide.Above ? right : x;
                canvas.Rect(new(left, plot.Y, end - left, plot.Height), threshold.Colour + "18");
            }
        }
        var selected = card.SelectedKeys.ToHashSet(StringComparer.Ordinal);
        var hoveredMembers = card.HoveredKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var mark in layout.Marks)
        {
            var hit = mark.Hit;
            var rect = new Rect2(hit.X, hit.Y, hit.W, hit.H);
            var hovered = card.HoverKey == hit.Key || mark.Members.Any(i => hoveredMembers.Contains(card.Keys[i]));
            string Fill(int i) => card.Colors.Length == 0 || card.Colors[i].Length == 0 ? theme.Data : card.Colors[i];
            if (mark.Bin is null)
            {
                canvas.RoundedRect(rect, rect.Width / 2, hovered || selected.Contains(hit.Key) ? accent : Fill(mark.Members[0]));
                if (hovered || selected.Contains(hit.Key))
                    canvas.RoundedRect(new(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), rect.Width / 2 - 1, Fill(mark.Members[0]));
                continue;
            }
            var step = rect.Height / mark.Members.Length;
            if (card.Colors.Length == 0) canvas.Rect(rect, theme.Data);
            else
            {
                for (var start = 0; start < mark.Members.Length;)
                {
                    var fill = Fill(mark.Members[start]);
                    var end = start + 1;
                    while (end < mark.Members.Length && Fill(mark.Members[end]) == fill) end++;
                    canvas.Rect(new(rect.X, bottom - end * step, rect.Width, (end - start) * step), fill);
                    start = end;
                }
            }
            var count = 0;
            for (var j = 0; j < mark.Members.Length; j++)
            {
                var i = mark.Members[j];
                var slice = new Rect2(rect.X, bottom - (j + 1) * step, rect.Width, step);
                if (selected.Contains(card.Keys[i])) { count++; Outline(canvas, slice, accent); }
            }
            if (hovered) Outline(canvas, rect, accent);
            if (count > 0 && count < mark.Members.Length)
                canvas.Line(new(rect.X + rect.Width / 4, rect.Y + 3), new(rect.X + rect.Width * .75, rect.Y + 3), accent, 2);
        }
        if (card.Distribution?.ThresholdLines != false && card.Thresholds is { } lines)
        {
            foreach (var threshold in lines.Entries)
            {
                var x = X(threshold.Value);
                var label = threshold.Value < layout.Min ? "‹ " + threshold.Label :
                    threshold.Value > layout.Max ? threshold.Label + " ›" : threshold.Label;
                for (var y = plot.Y; y < bottom; y += 7)
                    canvas.Line(new(x, y), new(x, Math.Min(bottom, y + 4)), threshold.Colour);
                Text(label, Math.Clamp(x - 10, plot.X, right - canvas.MeasureText(label, 10, theme.Font).Width), plot.Y - 14, threshold.Colour);
            }
        }
        var labelRight = double.NegativeInfinity;
        foreach (var tick in ChartTicks.Nice(layout.Min, layout.Max).Where(v => v >= layout.Min && v <= layout.Max))
        {
            var x = X(tick);
            var text = Distribution.Number(tick);
            var width = canvas.MeasureText(text, 10, theme.Font).Width;
            var start = Math.Clamp(x - width / 2, plot.X, right - width);
            if (start < labelRight + 4) continue;
            canvas.Line(new(x, bottom), new(x, bottom + 4), theme.Muted);
            Text(text, start, bottom + 5, theme.Muted);
            labelRight = start + width;
        }
        if (card.Distribution?.Quartiles != false)
        {
            double Q(double p)
            {
                var rank = p * (layout.SortedValues.Length - 1);
                return ColourRange.Lerp(layout.SortedValues[(int)rank], layout.SortedValues[(int)Math.Ceiling(rank)], rank % 1);
            }
            var q1 = X(Q(.25)); var median = X(Q(.5)); var q3 = X(Q(.75));
            canvas.RoundedRect(new(q1, bottom + 23, Math.Max(1, q3 - q1), 6), 3, ColorRgba.Parse(accent).WithAlpha(0x55).ToHex());
            canvas.Line(new(median, bottom + 20), new(median, bottom + 32), accent, 2);
        }
        if (card.Unit.Length > 0) Text(card.Unit, plot.X, bottom + 33, theme.Muted);
    }

    private static void Outline(ICanvas2D canvas, Rect2 r, string accent)
    {
        canvas.Polyline([new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height),
            new(r.X, r.Y + r.Height), new(r.X, r.Y)], accent);
    }
}
