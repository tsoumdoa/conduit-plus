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
    public static CanvasRasterPlan Create(Size2 size, double requestedScale, long byteBudget = BitmapBudget, double margin = 0)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0 || !double.IsFinite(margin) || margin < 0 ||
            !double.IsFinite(requestedScale) || requestedScale <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (byteBudget < 4 || byteBudget > BitmapBudget) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        var scale = Math.Min(requestedScale, Math.Min((MaxDimension - 1e-9) / (size.Width + 2 * margin), (MaxDimension - 1e-9) / (size.Height + 2 * margin)));
        // Ceil adds up to one pixel per edge. Binary search keeps the actual allocation within budget.
        int Width(double s) => Math.Max(1, (int)Math.Ceiling(size.Width * s) + 2 * (int)Math.Ceiling(margin * s));
        int Height(double s) => Math.Max(1, (int)Math.Ceiling(size.Height * s) + 2 * (int)Math.Ceiling(margin * s));
        if (Width(scale) > MaxDimension || Height(scale) > MaxDimension || (long)Width(scale) * Height(scale) * 4 > byteBudget)
        {
            double low = 0, high = scale;
            for (var i = 0; i < 64; i++)
            {
                var mid = (low + high) / 2;
                if (Width(mid) <= MaxDimension && Height(mid) <= MaxDimension && (long)Width(mid) * Height(mid) * 4 <= byteBudget) low = mid; else high = mid;
            }
            scale = low;
        }
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return new(scale, Width(scale), Height(scale));
    }
}

public sealed record CanvasStackFrameKey(long Revision, Theme Theme, Size2 Size, double Scale, int RetainedCards);

/// <summary>Each card has its own bounds. Document pressure changes retention, never raster density.</summary>
public static class CanvasStackRaster
{
    public static CanvasRasterPlan[] Plan(CanvasStackLayout layout, Theme theme, double scale) => layout.Cards
        .Select(card => CanvasRasterPlan.Create(new(card.W, card.H), scale,
            margin: theme.Shadow ? theme.ShadowExtent + 1 : 0)).ToArray();

    public static int[] RetainedCards(IReadOnlyList<CanvasRasterPlan> plans, int visibleCards, long budget)
    {
        if (budget < 0 || budget > CanvasRasterPlan.DocumentBudget / 2) throw new ArgumentOutOfRangeException(nameof(budget));
        var retained = new List<int>();
        // Visible cards first; spare capacity may retain offscreen neighbours for smooth navigation.
        foreach (var i in Enumerable.Range(0, plans.Count).OrderByDescending(i => (visibleCards & (1 << i)) != 0))
        {
            if (plans[i].Bytes > budget) continue;
            budget -= plans[i].Bytes; retained.Add(i);
        }
        return retained.ToArray();
    }

    public static int RetainedMask(IReadOnlyList<CanvasRasterPlan> plans, int visibleCards) =>
        RetainedCards(plans, visibleCards, CanvasRasterPlan.DocumentBudget / 2).Aggregate(0, (mask, i) => mask | (1 << i));
}

public sealed record CanvasCacheEntry(string Id, long Bytes, long LastVisible);
public static class CanvasCacheBudget
{
    /// <summary>Account for both the old frame and its replacement until atomic publication succeeds.</summary>
    public static string[] Evictions(IReadOnlyList<CanvasCacheEntry> entries, string owner, long allocation)
    {
        if (allocation < 0 || allocation > CanvasRasterPlan.DocumentBudget / 2) throw new ArgumentOutOfRangeException(nameof(allocation));
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

/// <summary>One left-aligned column in tree order. Gaps and unused width never target a card.</summary>
public sealed class CanvasStackLayout
{
    public const int Limit = 12;
    public const double Gap = 8;
    public CardLayout[] Cards { get; }
    public Size2 Size { get; }
    public CanvasStackLayout(IReadOnlyList<CardSpec> cards, Theme theme)
    {
        double y = 0, width = 0;
        Cards = cards.Select(c =>
        {
            var size = CardMeasure.Measure(c, theme);
            var layout = new CardLayout(c.Id, c.Rev, 0, y, size.Width, size.Height, CardMeasure.Hits(c, theme));
            y += size.Height + Gap; width = Math.Max(width, size.Width);
            return layout;
        }).ToArray();
        Size = Cards.Length == 0 ? new(280, 92) : new(width, y - Gap);
    }
    public CardLayout? CardAt(Point2 point) => Cards.LastOrDefault(c => point.X >= c.X && point.X < c.X + c.W && point.Y >= c.Y && point.Y < c.Y + c.H);
    public CanvasStackHit? Hit(Point2 point)
    {
        var card = CardAt(point);
        if (card is null) return null;
        var hit = card.HitRegions.LastOrDefault(h => h.Contains(point.X - card.X, point.Y - card.Y));
        return hit is null ? null : new(card.Id, hit with { X = hit.X + card.X, Y = hit.Y + card.Y });
    }
}
public sealed record CanvasStackHit(string CardId, HitRegion Region)
{
    public string Key => Region.Key;
    public double X => Region.X;
    public double Y => Region.Y;
    public double W => Region.W;
    public double H => Region.H;
}

/// <summary>Transient stack state for one component: one domain, view state keyed by live card ID.</summary>
public sealed class CanvasCardState
{
    private BrushState _brush = new();
    private CardSpec[] _sources = [];
    private readonly CardStore _store = new();
    private sealed class ViewState { public int Offset; public int? Column; public bool Descending; }
    private readonly Dictionary<string, ViewState> _views = new(StringComparer.Ordinal);
    private IBrushBinding? _binding;
    private Guid _owner;
    private bool _enabled = true;
    private long _gesture;
    private IReadOnlySet<string> _selectedMembers = new HashSet<string>(StringComparer.Ordinal);
    private BrushOrigin Origin => new(_binding?.ParticipantId ?? "canvas", "canvas", _gesture);
    public BrushDomainSnapshot Current => _binding?.Current ?? _brush.Current;
    public event Action<InteractionChange>? Changed;
    public long Revision { get; private set; }
    public void Bind(IBrushBinding? binding, Guid owner, bool enabled = true)
    {
        if (!ReferenceEquals(_binding, binding)) { CancelHover(); _brush = new(); }
        _binding = binding; _owner = owner; _enabled = enabled; Reconcile();
    }
    public void Refresh() => Reconcile();
    public void CancelHover() { Hover(null); _gesture++; }
    public bool CanInteract => _enabled && (_binding?.IsActive ?? true);
    public CardSpec[] Specs { get; private set; } = [];
    // Single-card API retained for existing adapters and fixtures.
    public CardSpec? Spec => Specs.FirstOrDefault();
    public MembershipIndex Membership => Current.Membership;
    public void Replace(CardSpec? source) => ReplaceCards(source is null ? [] : [source]);
    public void ReplaceCards(IReadOnlyList<CardSpec> sources)
    {
        foreach (var source in sources) source.Validate();
        var previous = Current.SelectionRevision;
        var ids = sources.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != sources.Count) throw new ArgumentException("Duplicate card IDs.");
        // Retain shared-key selection while some source survives, but preserve single-card replacement semantics.
        if (_binding is null && _sources.Length > 0 && !_sources.Any(c => ids.Contains(c.Id))) _brush.Prune([], []);
        foreach (var id in _views.Keys.Where(id => !ids.Contains(id)).ToArray()) _views.Remove(id);
        // Own arrays and ignore incoming revisions and view state.
        _sources = sources.Select(c => CardJson.Deserialize<CardSpec>(CardJson.Serialize(c with { Rev = 0 }))).ToArray();
        Reconcile();
        if (_binding is null && previous != Current.SelectionRevision) Changed?.Invoke(new(Current, InteractionChangeKind.Reconciliation));
    }
    private void Reconcile()
    {
        if (_binding is null) _brush.Prune(_sources, []);
        var projected = Current.Project(_sources);
        for (var i = 0; i < projected.Length; i++)
        {
            var source = _sources[i];
            if (!_views.TryGetValue(source.Id, out var view)) _views.Add(source.Id, view = new());
            if (source.Kind != CardKind.Table || view.Column < 0 || view.Column >= source.Headers.Length)
            { view.Column = null; view.Descending = false; }
            view.Offset = source.Kind is CardKind.Table or CardKind.Bar ? Math.Clamp(view.Offset, 0, TableScroll.MaxOffset(source)) : 0;
            var spec = projected[i] with { ScrollOffset = view.Offset, SortColumn = view.Column, SortDescending = view.Descending, WiredSlot = null };
            if (spec.Kind is (CardKind.Table or CardKind.Bar) && BrushPresentation.FirstNewRow(spec, _selectedMembers, Current.SelectedMemberKeys) is { } added)
            { view.Offset = TableScroll.Reveal(spec, added); spec = spec with { ScrollOffset = view.Offset }; }
            projected[i] = spec;
        }
        _selectedMembers = Current.SelectedMemberKeys;
        var before = Specs.Select(c => (c.Id, c.Rev)).ToArray();
        _store.Update(projected); Specs = _store.Snapshot.ToArray();
        if (!before.SequenceEqual(Specs.Select(c => (c.Id, c.Rev)))) Revision++;
    }
    public void Hover(string? key) => Hover(Spec?.Id, key);
    public void Hover(string? cardId, string? key)
    {
        var origin = Origin;
        var card = Specs.FirstOrDefault(c => c.Id == cardId);
        var clear = card is null || string.IsNullOrEmpty(key) || CardMeasure.IsReserved(key);
        if (!clear && !CanInteract) return;
        var kind = card is null || key is null ? (BrushTargetKind?)null : BrushPresentation.TargetKind(card, key);
        var changed = _binding is { } binding
            ? clear ? binding.ClearHover(origin) : binding.Hover(origin, key, kind)
            : clear ? _brush.ClearHover(origin) : _brush.Hover(origin, key, kind);
        if (changed) Reconcile();
    }
    public void Activate(string key, BrushSelectMode mode = BrushSelectMode.Replace) { if (Spec is { } spec) Activate(spec.Id, key, mode); }
    public void Activate(string cardId, string key, BrushSelectMode mode = BrushSelectMode.Replace)
    {
        var spec = Specs.FirstOrDefault(c => c.Id == cardId);
        if (spec is null || !CanInteract) return;
        var previous = Current.SelectionRevision;
        var source = new BrushActionSource(_owner, spec.Id, "canvas");
        if (key == CardMeasure.ClearKey) { if (_binding is { } binding) binding.Clear(source); else _brush.Clear(source); }
        else if (TableSort.Column(key) is { } column && spec.Kind == CardKind.Table && column >= 0 && column < spec.Headers.Length)
        { var sorted = TableSort.Cycle(spec, column); var view = _views[cardId]; view.Column = sorted.SortColumn; view.Descending = sorted.SortDescending; }
        else if (_binding is { } binding) binding.Select(key, mode, BrushPresentation.TargetKind(spec, key), source);
        else _brush.Select(key, mode, source, BrushPresentation.TargetKind(spec, key));
        Reconcile();
        if (_binding is null && previous != Current.SelectionRevision) Changed?.Invoke(new(Current, InteractionChangeKind.Selection));
    }
    public void Scroll(int offset) { if (Spec is { } spec) Scroll(spec.Id, offset); }
    public void Scroll(string cardId, int offset) { if (_views.TryGetValue(cardId, out var view)) { view.Offset = offset; Reconcile(); } }
}
