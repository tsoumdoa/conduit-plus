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
        void Text(string text, double x, double y, string colour) => canvas.Text(text, new(x, y), 10, colour, theme.Font);
        foreach (var hit in Distribution.HeaderHits(card, theme))
        {
            var choice = Distribution.Choice(hit.Key);
            if (choice == layout.Mode) canvas.RoundedRect(new(hit.X, hit.Y, hit.W, hit.H), 4, theme.Track);
            Text(choice.ToString()!, hit.X + 5, hit.Y + 5, layout.Forced ? theme.Muted : choice == layout.Mode ? accent : theme.Ink);
        }
        canvas.Line(new(plot.X, bottom), new(right, bottom), theme.Muted);
        if (layout.Count == 0) { Text("No values", plot.X, plot.Y + 52, theme.Muted); return; }
        foreach (var mark in layout.Annotations)
        {
            var threshold = mark.Entry;
            var left = threshold.Side == ThresholdSide.Above ? mark.Tick : plot.X;
            var end = threshold.Side == ThresholdSide.Above ? right : mark.Tick;
            canvas.Rect(new(left, plot.Y, end - left, plot.Height), threshold.Colour + "18");
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
        foreach (var mark in layout.Annotations)
        {
            var x = mark.Tick;
            var threshold = mark.Entry;
            using (canvas.Clip(new(theme.Padding, 32, card.Width - 2 * theme.Padding, plot.Y - 32)))
            {
                if (mark.Leader.Length == 2)
                    canvas.Line(new(x, plot.Y), new(mark.Leader[1].X, plot.Y + mark.Leader[1].Y), threshold.Colour, .75);
                Text(mark.Text, mark.Label.X, plot.Y + mark.Label.Y, threshold.Colour);
            }
            for (var y = plot.Y; y < bottom; y += 7)
                canvas.Line(new(x, y), new(x, Math.Min(bottom, y + 4)), threshold.Colour);
        }
        var tickRight = right;
        if (!string.IsNullOrWhiteSpace(card.Unit))
        {
            var unitWidth = canvas.MeasureText(card.Unit, 10, theme.Font).Width;
            var unitX = Math.Max(plot.X, right - unitWidth);
            using var clip = canvas.Clip(new(unitX, bottom + 5, right - unitX, 15));
            Text(card.Unit, unitX, bottom + 5, theme.Muted);
            tickRight = unitX - 6;
        }
        var labelRight = double.NegativeInfinity;
        foreach (var tick in ChartTicks.Nice(layout.Min, layout.Max).Where(v => v >= layout.Min && v <= layout.Max))
        {
            var x = layout.X(tick);
            var text = Distribution.Number(tick);
            var width = canvas.MeasureText(text, 10, theme.Font).Width;
            if (width > tickRight - plot.X) continue;
            var start = Math.Clamp(x - width / 2, plot.X, tickRight - width);
            if (start < labelRight + 4) continue;
            canvas.Line(new(x, bottom), new(x, bottom + 4), theme.Muted);
            Text(text, start, bottom + 5, theme.Muted);
            labelRight = start + width;
        }
        if (card.Distribution?.Quartiles != false)
        {
            var median = layout.X(layout.Quantile(.5));
            canvas.RoundedRect(layout.QuartileStrip, 3, ColorRgba.Parse(accent).WithAlpha(0x55).ToHex());
            canvas.Line(new(median, bottom + 20), new(median, bottom + 32), accent, 2);
            Text("Middle 50%", plot.X, bottom + 33, theme.Muted);
        }
    }

    private static void Outline(ICanvas2D canvas, Rect2 r, string accent)
    {
        canvas.Polyline([new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height),
            new(r.X, r.Y + r.Height), new(r.X, r.Y)], accent);
    }
}
