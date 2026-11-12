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
    // All color arguments accept #RRGGBB and #RRGGBBAA, using source-over alpha.
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
        ? new(ClearKey, Measure(c, t).Width - t.Padding - 40, 7, 40, 22) : null;
    // Untitled Text starts at (12,12): keep its explicit grip in the unused left edge.
    public static Rect2 Grip(CardSpec c) => new(1, 5, 10, 24);
    public static bool IsTitle(CardSpec c, Theme t, double x, double y) =>
        x >= 0 && x < Measure(c, t).Width && y >= 0 && y < 32 &&
        !Hits(c, t).Any(h => h.Contains(x, y)) &&
        (!string.IsNullOrEmpty(c.Title) || Grip(c).Contains(x, y));
    public static Size2 Measure(CardSpec c, Theme t) => FormMeasure.Size(c, t, c.Form, TextMetrics.Measurer);
    // DataHits remain content/paint bounds. Interaction also includes the visible padding tick.
    public static double TickLeft(HitRegion row) => Math.Max(0, row.X - 6);
    private static HitRegion Actionable(CardSpec c, HitRegion row) => c.Kind == CardKind.Line ? row :
        row with { X = TickLeft(row), W = row.W + row.X - TickLeft(row) };
    public static HitRegion[] Hits(CardSpec c, Theme t) => c.Kind == CardKind.Title ? [] : [.. DataHits(c, t).Select(h => Actionable(c, h)), .. TableSort.Headers(c, t), .. (Clear(c, t) is { } clear ? new[] { clear } : [])];
    public static HitRegion[] DataHits(CardSpec c, Theme t) => c.Kind switch
    {
        CardKind.Metric when c.Keys.Length == 1 && c.Form.Compact => [new(c.Keys[0], 0, 0, Clear(c, t)?.X ?? c.Width, 48)],
        CardKind.Metric when c.Keys.Length == 1 => [new(c.Keys[0], t.Padding, BodyTop, c.Width - 2 * t.Padding, Measure(c, t).Height - BodyTop - t.Padding)],
        CardKind.Bar => BarPresentation.Order(c).Select(i => c.Keys[i]).Skip(TableScroll.Offset(c)).Take(TableScroll.Visible(c)).Select((key, i) => new HitRegion(key, t.Padding, 36 + i * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        // Only visible rows are hit regions; region i is the i-th row on screen, not in the data.
        CardKind.Table => TableSort.Rows(c).Skip(TableScroll.Offset(c)).Take(TableScroll.Visible(c)).Select((row, i) => new HitRegion(row.Key, t.Padding, BodyTop + (i + 1) * t.RowHeight, c.Width - 2 * t.Padding, t.RowHeight)).ToArray(),
        CardKind.Line => LineCardPainter.Hits(c),
        CardKind.Legend when !c.Min.HasValue => LegendHits(c, t),
        _ => []
    };
    private static HitRegion[] LegendHits(CardSpec c, Theme t)
    {
        var width = Measure(c, t).Width;
        return c.Keys.Select((key, i) => new HitRegion(key, t.Padding, BodyTop + i * t.RowHeight, width - 2 * t.Padding, t.RowHeight)).ToArray();
    }
}

public static class SlotLayout
{
    public static Slot[] ResolveAll(IReadOnlyList<CardSpec> cards)
    {
        var n = 0;
        var sources = new Dictionary<string, Slot>(StringComparer.Ordinal);
        return cards.Select(c =>
        {
            if (c.Kind == CardKind.Title) return c.WiredSlot ?? Slot.Tl;
            var source = CardIdentity.Source(c.Id);
            if (!sources.ContainsKey(source)) sources[source] = (Slot)(n++ % 6);
            var slot = sources[source];
            return c.WiredSlot ?? slot;
        }).ToArray();
    }
    public static Slot Resolve(CardSpec c, int index) => c.WiredSlot ?? (c.Kind == CardKind.Title ? Slot.Tl : (Slot)(index % 6));
}
