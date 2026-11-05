using System.Globalization;

namespace ConduitPlus.Core;

public readonly record struct Point2(double X, double Y);
public readonly record struct Size2(double Width, double Height);
public readonly record struct Rect2(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public sealed record HitRegion(string Key, double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => new Rect2(X, Y, W, H).Contains(x, y);
}
public sealed record CardLayout(string Id, long Rev, double X, double Y, double W, double H, HitRegion[] HitRegions);

public interface ICanvas2D
{
    Size2 MeasureText(string text, double size, string font);
    void Rect(Rect2 rect, string fill);
    void RoundedRect(Rect2 rect, double radius, string fill);
    void Line(Point2 from, Point2 to, string color, double width = 1);
    void Text(string text, Point2 topLeft, double size, string color, string font);
    void Polyline(IReadOnlyList<Point2> points, string color, double width = 1);
    IDisposable Clip(Rect2 rect);
}

public static class CardMeasure
{
    public static Size2 Measure(CardSpec c, Theme t) => new(c.Width, c.Kind switch
    {
        CardKind.Metric => 92,
        CardKind.Bar => 48 + c.Values.Length * t.RowHeight,
        _ => 48 + (c.Rows.Length + 1) * t.RowHeight
    });
    public static HitRegion[] Hits(CardSpec c, Theme t) => c.Kind switch
    {
        CardKind.Bar => c.Keys.Select((key, i) => new HitRegion(key, t.Padding, 36 + i * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        CardKind.Table => c.Rows.Select((row, i) => new HitRegion(row.Key, t.Padding, 36 + (i + 1) * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        _ => []
    };
}

public static class SlotLayout
{
    public static Slot Resolve(CardSpec c, int index) => c.WiredSlot ?? c.ManualSlot ?? (Slot)(index % 6);
    public static CardLayout[] Place(IReadOnlyList<CardSpec> cards, Theme theme, Size2 viewport, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var result = new List<CardLayout>();
        var groups = cards.Select((c, i) => (c, slot: Resolve(c, i))).GroupBy(x => x.slot);
        foreach (var group in groups)
        {
            var items = group.Select(x => x.c).ToArray();
            var total = items.Sum(c => CardMeasure.Measure(c, theme).Height * scale) + (items.Length - 1) * 8 * scale;
            var y = group.Key is Slot.Bl or Slot.Br ? viewport.Height - 12 * scale - total : group.Key is Slot.Lc or Slot.Rc ? (viewport.Height - total) / 2 : 36 * scale;
            foreach (var c in items)
            {
                var size = CardMeasure.Measure(c, theme);
                var x = group.Key is Slot.Tr or Slot.Br or Slot.Rc ? viewport.Width - (size.Width + 12) * scale : 12 * scale;
                result.Add(new(c.Id, c.Rev, x, y, size.Width * scale, size.Height * scale, CardMeasure.Hits(c, theme)));
                y += (size.Height + 8) * scale;
            }
        }
        // Preserve input order, which is also the paint order used for reverse hit testing.
        return cards.Select(c => result.Single(r => r.Id == c.Id)).ToArray();
    }
}

public static class NativeCardPainter
{
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        var size = CardMeasure.Measure(c, t);
        using var clip = canvas.Clip(new(0, 0, size.Width, size.Height));
        canvas.RoundedRect(new(0, 0, size.Width, size.Height), t.Radius, t.Surface);
        canvas.Text(c.Title, new(t.Padding, 10), t.FontSize, t.Muted, t.Font);
        if (c.Kind == CardKind.Metric)
        {
            var value = c.Value ?? "—";
            canvas.Text(value, new(t.Padding, 36), 28, t.Ink, t.Font);
            var measured = canvas.MeasureText(value, 28, t.Font);
            canvas.Text(c.Unit, new(Math.Min(c.Width - 45, t.Padding + measured.Width + 8), 51), t.FontSize, t.Muted, t.Font);
            return;
        }
        var hits = CardMeasure.Hits(c, t);
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
            for (var row = 0; row < c.Rows.Length; row++)
            {
                var h = hits[row]; Highlight(canvas, c, t, h);
                canvas.Line(new(h.X, h.Y), new(h.X + h.W, h.Y), t.Line);
                for (var col = 0; col < c.Headers.Length; col++) Cell(c.Rows[row].Cells[col] ?? "—", col, h.Y, t.Ink);
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
