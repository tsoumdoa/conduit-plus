using System.Globalization;

namespace ConduitPlus.Core;

public static class CardPainter
{
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        var size = CardMeasure.Measure(c, t);
        using var clip = canvas.Clip(new(0, 0, size.Width, size.Height));
        canvas.RoundedRect(new(0, 0, size.Width, size.Height), t.Radius, t.Surface);
        var clear = CardMeasure.Clear(c, t);
        using (canvas.Clip(new(t.Padding, 7, (clear?.X ?? (c.Width - t.Padding)) - t.Padding - 4, 24)))
            canvas.Text(c.Title, new(t.Padding, 10), t.FontSize, t.Muted, t.Font);
        if (clear is not null)
        {
            canvas.RoundedRect(new(clear.X, clear.Y, clear.W, clear.H), 3, t.Track);
            canvas.Text("Clear", new(clear.X + 5, clear.Y + 3), t.FontSize, t.Accent, t.Font);
        }
        if (c.Kind == CardKind.Line) { LineCardPainter.Paint(canvas, c, t); return; }
        if (c.Kind == CardKind.Metric)
        {
            var value = c.Value ?? "—";
            canvas.Text(value, new(t.Padding, 36), 28, t.Ink, t.Font);
            var measured = canvas.MeasureText(value, 28, t.Font);
            canvas.Text(c.Unit, new(Math.Min(c.Width - 45, t.Padding + measured.Width + 8), 51), t.FontSize, t.Muted, t.Font);
            return;
        }
        var hits = CardMeasure.DataHits(c, t);
        var max = Math.Max(c.Threshold, c.Values.Select(v => Math.Abs(v ?? 0)).DefaultIfEmpty(0).Max());
        if (c.Kind == CardKind.Bar)
        {
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i]; Highlight(canvas, c, t, h);
                canvas.Text(c.Labels[i], new(h.X + 2, h.Y + 2), t.FontSize, t.Ink, t.Font);
                var x = h.X + 55; var w = h.W - 110; var y = h.Y + 7;
                canvas.Rect(new(x, y, w, 8), t.Track);
                var value = c.Values[i];
                // Signed axis: negatives extend left of zero, nulls remain gaps.
                var signed = c.Values.Any(v => v < 0); var origin = signed ? x + w / 2 : x; var span = signed ? w / 2 : w;
                if (value.HasValue) canvas.Rect(new(value < 0 ? origin - Math.Abs(value.Value) / max * span : origin, y, Math.Abs(value.Value) / max * span, 8), value > c.Threshold ? t.Bad : t.Good);
                canvas.Line(new(origin + c.Threshold / max * span, y - 2), new(origin + c.Threshold / max * span, y + 10), t.Muted);
                canvas.Text(value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—", new(h.X + h.W - 43, h.Y + 2), t.FontSize, t.Ink, t.Font);
            }
        }
        else
        {
            var width = (c.Width - 2 * t.Padding) / c.Headers.Length;
            for (var col = 0; col < c.Headers.Length; col++) Cell(c.Headers[col], col, 36, t.Muted);
            var offset = TableScroll.Offset(c);
            for (var row = 0; row < hits.Length; row++)
            {
                var h = hits[row]; Highlight(canvas, c, t, h);
                canvas.Line(new(h.X, h.Y), new(h.X + h.W, h.Y), t.Line);
                for (var col = 0; col < c.Headers.Length; col++) Cell(c.Rows[offset + row].Cells[col] ?? "—", col, h.Y, t.Ink);
            }
            if (TableScroll.Bar(c, t) is { } bar)
            {
                canvas.RoundedRect(bar.Track, TableScroll.BarWidth / 2, t.Track);
                canvas.RoundedRect(bar.Thumb, TableScroll.BarWidth / 2, t.Muted);
            }
            void Cell(string text, int col, double y, string color)
            {
                using var cellClip = canvas.Clip(new(t.Padding + col * width, y, width - 3, t.RowHeight));
                canvas.Text(text, new(t.Padding + col * width + 2, y + 2), t.FontSize, color, t.Font);
            }
        }
    }
    private static void Highlight(ICanvas2D canvas, CardSpec c, Theme t, HitRegion h)
    {
        if (c.SelectedKeys.Contains(h.Key) || c.HoverKey == h.Key) canvas.Rect(new(h.X, h.Y, h.W, h.H), t.Track);
        if (c.SelectedKeys.Contains(h.Key)) canvas.Rect(new(h.X, h.Y, 2, h.H), t.Accent);
    }
}
