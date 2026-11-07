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
    /// <summary>Horizontal linear gradient with evenly spaced color stops, left to right.</summary>
    void Gradient(Rect2 rect, IReadOnlyList<string> colors);
    void RoundedRect(Rect2 rect, double radius, string fill);
    void Line(Point2 from, Point2 to, string color, double width = 1);
    void Text(string text, Point2 topLeft, double size, string color, string font);
    void Polyline(IReadOnlyList<Point2> points, string color, double width = 1);
    void FillPolygon(IReadOnlyList<Point2> points, string fill);
    IDisposable Clip(Rect2 rect);
}
public static class CardMeasure
{
    public const double BodyTop = 36;
    public const string ClearKey = "\u0001clear";
    public static bool IsReserved(string key) => key.StartsWith('\u0001');
    public static HitRegion? Clear(CardSpec c, Theme t) => BrushState.Keys(c).Any() && (c.OwnerHasSelection || c.SelectedKeys.Length > 0)
        ? new(ClearKey, c.Width - t.Padding - 40, 7, 40, 22) : null;
    // Untitled Text starts at (12,12): keep its explicit grip in the unused left edge.
    public static Rect2 Grip(CardSpec c) => new(1, 5, 10, 24);
    public static bool IsTitle(CardSpec c, Theme t, double x, double y) =>
        x >= 0 && x < c.Width && y >= 0 && y < 32 &&
        !Hits(c, t).Any(h => h.Contains(x, y)) &&
        (!string.IsNullOrEmpty(c.Title) || Grip(c).Contains(x, y));
    public static Size2 Measure(CardSpec c, Theme t) => new(c.Width, c.Kind switch
    {
        CardKind.Metric => 92,
        CardKind.Line => 220,
        CardKind.Text => TextMetrics.Layout(c, t).Height,
        CardKind.Legend => c.Min.HasValue ? 112 : 48 + c.Colors.Length * t.RowHeight,
        CardKind.Bar => 48 + c.Values.Length * t.RowHeight,
        _ => 48 + (TableScroll.Visible(c) + 1) * t.RowHeight
    });
    public static HitRegion[] Hits(CardSpec c, Theme t) => [.. DataHits(c, t), .. TableSort.Headers(c, t), .. (Clear(c, t) is { } clear ? new[] { clear } : [])];
    public static HitRegion[] DataHits(CardSpec c, Theme t) => c.Kind switch
    {
        CardKind.Bar => c.Keys.Select((key, i) => new HitRegion(key, t.Padding, 36 + i * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        // Only visible rows are hit regions; region i is the i-th row on screen, not in the data.
        CardKind.Table => TableSort.Rows(c).Skip(TableScroll.Offset(c)).Take(TableScroll.Visible(c)).Select((row, i) => new HitRegion(row.Key, t.Padding, BodyTop + (i + 1) * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        CardKind.Line => LineCardPainter.Hits(c),
        CardKind.Legend when !c.Min.HasValue => c.Keys.Select((key, i) => new HitRegion(key, t.Padding, 36 + i * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
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
        var groups = cards.Select((c, i) => (c, i, slot: Resolve(c, i))).GroupBy(x => x.slot);
        foreach (var group in groups)
        {
            var items = group.OrderBy(x => x.c.OrderSlot != x.slot || !x.c.ManualOrder.HasValue).ThenBy(x => x.c.OrderSlot == x.slot ? x.c.ManualOrder : null).ThenBy(x => x.i).Select(x => x.c).ToArray();
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
