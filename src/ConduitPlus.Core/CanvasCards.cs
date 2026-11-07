namespace ConduitPlus.Core;

/// <summary>Canvas units are logical card pixels; the host supplies the actual device basis.</summary>
public static class CanvasCardGeometry
{
    public static Point2 ToLocal(Point2 canvas, Point2 bodyOrigin) => new(canvas.X - bodyOrigin.X, canvas.Y - bodyOrigin.Y);
    public static Point2 ToCanvas(Point2 local, Point2 bodyOrigin) => new(local.X + bodyOrigin.X, local.Y + bodyOrigin.Y);
    public static double DeviceScale(Point2 origin, Point2 unitX, Point2 unitY) => Math.Max(
        Math.Sqrt(Math.Pow(unitX.X - origin.X, 2) + Math.Pow(unitX.Y - origin.Y, 2)),
        Math.Sqrt(Math.Pow(unitY.X - origin.X, 2) + Math.Pow(unitY.Y - origin.Y, 2)));
    public static bool WithinClick(Point2 down, Point2 up) => Math.Pow(up.X - down.X, 2) + Math.Pow(up.Y - down.Y, 2) <= 16;
}

public readonly record struct CanvasRasterPlan(double Scale, int Width, int Height)
{
    public const long BitmapBudget = 16L * 1024 * 1024;
    public const long DocumentBudget = 64L * 1024 * 1024;
    public const int MaxDimension = 4096;
    public long Bytes => (long)Width * Height * 4;
    public static CanvasRasterPlan Create(Size2 size, double requestedScale, long byteBudget = BitmapBudget)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0 ||
            !double.IsFinite(requestedScale) || requestedScale <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (byteBudget < 4 || byteBudget > BitmapBudget) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        var scale = Math.Min(requestedScale, Math.Min((MaxDimension - 1e-9) / size.Width, (MaxDimension - 1e-9) / size.Height));
        // Ceil adds up to one pixel per edge. Binary search keeps the actual allocation within budget.
        int Width(double s) => Math.Max(1, (int)Math.Ceiling(size.Width * s));
        int Height(double s) => Math.Max(1, (int)Math.Ceiling(size.Height * s));
        if ((long)Width(scale) * Height(scale) * 4 > byteBudget)
        {
            double low = 0, high = scale;
            for (var i = 0; i < 64; i++)
            {
                var mid = (low + high) / 2;
                if ((long)Width(mid) * Height(mid) * 4 <= byteBudget) low = mid; else high = mid;
            }
            scale = low;
        }
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return new(scale, Width(scale), Height(scale));
    }
}

public sealed record CanvasFrameKey(string Id, long Revision, Theme Theme, Size2 Size, CanvasRasterPlan Raster);

public sealed record CanvasCacheEntry(string Id, long Bytes, long LastVisible);
public static class CanvasCacheBudget
{
    /// <summary>Share capacity among visible cards, leaving space for one atomic replacement.</summary>
    public static long PerVisibleCard(int count) => Math.Min(CanvasRasterPlan.BitmapBudget,
        CanvasRasterPlan.DocumentBudget / (Math.Max(1L, count) + 1));
    /// <summary>Account for both the old frame and its replacement until atomic publication succeeds.</summary>
    public static string[] Evictions(IReadOnlyList<CanvasCacheEntry> entries, string owner, long allocation)
    {
        if (allocation < 0 || allocation > CanvasRasterPlan.BitmapBudget) throw new ArgumentOutOfRangeException(nameof(allocation));
        var total = entries.Sum(e => e.Bytes);
        var evictions = new List<string>();
        foreach (var entry in entries.Where(e => e.Id != owner).OrderBy(e => e.LastVisible))
        {
            if (total + allocation <= CanvasRasterPlan.DocumentBudget) break;
            total -= entry.Bytes; evictions.Add(entry.Id);
        }
        if (total + allocation > CanvasRasterPlan.DocumentBudget) throw new InvalidOperationException("Canvas Card cache budget exceeded.");
        return evictions.ToArray();
    }
}

/// <summary>Transient state for exactly one document/component owner. Never schedules a host solution.</summary>
public sealed class CanvasCardState
{
    private BrushState _brush = new();
    private CardSpec? _source;
    private readonly CardStore _store = new();
    private int _offset;
    private int? _column;
    private bool _descending;
    public CardSpec? Spec { get; private set; }
    public void Replace(CardSpec? source)
    {
        source?.Validate();
        if (_source?.Id != source?.Id)
        { _brush = new(); _offset = 0; _column = null; _descending = false; }
        // Own the input, including its arrays; incoming revisions and host view state are not authoritative.
        _source = source is null ? null : CardJson.Deserialize<CardSpec>(CardJson.Serialize(source with { Rev = 0 }));
        Reconcile();
    }
    private void Reconcile()
    {
        if (_source is null) { Spec = null; _store.Update([]); return; }
        if (_source.Kind != CardKind.Table || _column < 0 || _column >= _source.Headers.Length)
        { _column = null; _descending = false; }
        _offset = _source.Kind == CardKind.Table ? Math.Clamp(_offset, 0, TableScroll.MaxOffset(_source)) : 0;
        _brush.Prune([_source], []);
        var spec = _brush.Project([_source])[0] with
        {
            ScrollOffset = _offset, SortColumn = _column, SortDescending = _descending,
            WiredSlot = null, ManualSlot = null, OrderSlot = null, ManualOrder = null
        };
        _store.Update([spec]); Spec = _store.Snapshot[0];
    }
    public void Hover(string? key) { if (_brush.Hover(key)) Reconcile(); }
    public void Activate(string key)
    {
        if (Spec is null) return;
        if (key == CardMeasure.ClearKey) _brush.Clear();
        else if (TableSort.Column(key) is { } column && Spec.Kind == CardKind.Table && column >= 0 && column < Spec.Headers.Length)
        { var sorted = TableSort.Cycle(Spec, column); _column = sorted.SortColumn; _descending = sorted.SortDescending; }
        else _brush.Toggle(key);
        Reconcile();
    }
    public void Scroll(int offset) { _offset = offset; Reconcile(); }
}
