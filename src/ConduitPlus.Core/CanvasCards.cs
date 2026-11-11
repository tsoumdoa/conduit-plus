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
    private IBrushBinding? _binding;
    private Guid _owner;
    private bool _enabled = true;
    private long _gesture;
    private IReadOnlySet<string> _selectedMembers = new HashSet<string>(StringComparer.Ordinal);
    private BrushOrigin Origin => new(_binding?.ParticipantId ?? "canvas", "canvas", _gesture);
    public BrushDomainSnapshot Current => _binding?.Current ?? _brush.Current;
    public event Action<InteractionChange>? Changed;
    public void Bind(IBrushBinding? binding, Guid owner, bool enabled = true)
    {
        if (!ReferenceEquals(_binding, binding)) { CancelHover(); _brush = new(); }
        _binding = binding; _owner = owner; _enabled = enabled; Reconcile();
    }
    public void Refresh() => Reconcile();
    public void CancelHover() { Hover(null); _gesture++; }
    public bool CanInteract => _enabled && (_binding?.IsActive ?? true);
    public CardSpec? Spec { get; private set; }
    public MembershipIndex Membership => Current.Membership;
    public string[]? TooltipFor(string key) => Spec is null ? null : Tooltip.For(Spec, key, Membership);
    public void Replace(CardSpec? source)
    {
        source?.Validate();
        var previous = Current.SelectionRevision;
        if (_source?.Id != source?.Id)
        { if (_binding is null) _brush.Prune([], []); _offset = 0; _column = null; _descending = false; }
        // Own the input, including its arrays; incoming revisions and host view state are not authoritative.
        _source = source is null ? null : CardJson.Deserialize<CardSpec>(CardJson.Serialize(source with { Rev = 0 }));
        Reconcile();
        if (_binding is null && previous != Current.SelectionRevision) Changed?.Invoke(new(Current, InteractionChangeKind.Reconciliation));
    }
    private void Reconcile()
    {
        if (_source is null) { if (_binding is null) _brush.Prune([], []); Spec = null; _store.Update([]); return; }
        if (_source.Kind != CardKind.Table || _column < 0 || _column >= _source.Headers.Length)
        { _column = null; _descending = false; }
        _offset = _source.Kind is CardKind.Table or CardKind.Bar ? Math.Clamp(_offset, 0, TableScroll.MaxOffset(_source)) : 0;
        if (_binding is null) _brush.Prune([_source], []);
        var spec = Current.Project([_source])[0] with
        {
            ScrollOffset = _offset, SortColumn = _column, SortDescending = _descending,
            WiredSlot = null
        };
        if (spec.Kind is (CardKind.Table or CardKind.Bar) && BrushPresentation.FirstNewRow(spec, _selectedMembers) is { } added)
        { _offset = TableScroll.Reveal(spec, added); spec = spec with { ScrollOffset = _offset }; }
        _selectedMembers = Current.SelectedMemberKeys;
        _store.Update([spec]); Spec = _store.Snapshot[0];
    }
    public void Hover(string? key)
    {
        var origin = Origin;
        var clear = string.IsNullOrEmpty(key) || CardMeasure.IsReserved(key);
        if (!clear && !CanInteract) return;
        var kind = _source is null || key is null ? (BrushTargetKind?)null : BrushPresentation.TargetKind(_source, key);
        var changed = _binding is { } binding
            ? clear ? binding.ClearHover(origin) : binding.Hover(origin, key, kind)
            : clear ? _brush.ClearHover(origin) : _brush.Hover(origin, key, kind);
        if (changed) Reconcile();
    }
    public void Activate(string key, BrushSelectMode mode = BrushSelectMode.Replace)
    {
        if (Spec is null || !CanInteract) return;
        var previous = Current.SelectionRevision;
        var source = new BrushActionSource(_owner, Spec.Id, "canvas");
        if (key == CardMeasure.ClearKey) { if (_binding is { } binding) binding.Clear(source); else _brush.Clear(source); }
        else if (TableSort.Column(key) is { } column && Spec.Kind == CardKind.Table && column >= 0 && column < Spec.Headers.Length)
        { var sorted = TableSort.Cycle(Spec, column); _column = sorted.SortColumn; _descending = sorted.SortDescending; }
        else if (_binding is { } binding) binding.Select(key, mode, BrushPresentation.TargetKind(Spec, key), source);
        else _brush.Select(key, mode, source, BrushPresentation.TargetKind(Spec, key));
        Reconcile();
        if (_binding is null && previous != Current.SelectionRevision) Changed?.Invoke(new(Current, InteractionChangeKind.Selection));
    }
    public void Scroll(int offset) { _offset = offset; Reconcile(); }
}
