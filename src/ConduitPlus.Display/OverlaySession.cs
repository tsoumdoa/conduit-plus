using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input.Custom;
using System.Drawing;

namespace ConduitPlus.Rhino;


internal sealed partial class OverlaySession : IDisposable
{
    internal Size2 ViewSize => new(View.MainViewport.Size.Width, View.MainViewport.Size.Height);
    public Guid Owner { get; }
    public RhinoDoc Document { get; }
    public RhinoView View { get; }
    public CardSpec[] Cards { get; private set; } = [];
    internal string Label { get; set; } = "Overlay";
    internal double Size { get; set; } = 1;
    public Theme Theme { get; private set; } = Theme.Paper;
    private Theme _themeSelection = Theme.Paper;
    internal double Unit { get; private set; }
    internal void PrepareUnit(double unit) { Unit = unit; if (_raster.Scale != unit) _raster.Prepare(Cards, Theme, unit); }
    /// <summary>Compact forms chosen by the view's layout. View state like scroll, re-applied to every incoming spec.</summary>
    private readonly Dictionary<string, CardForm> _forms = new(StringComparer.Ordinal);
    /// <summary>Cards as authored: the layout always measures these, never the compact forms it chose.</summary>
    internal CardSpec[] AuthoredCards => Cards.Select(c => c.Form == CardForm.None ? c : c with { Form = CardForm.None }).ToArray();
    /// <summary>Apply a settled layout: scale and compact forms. Re-presents cards only when a form changed.</summary>
    internal void ApplyLayout(double unit, IReadOnlyDictionary<string, CardForm> forms)
    {
        Unit = unit;
        var next = forms.Where(p => p.Value != CardForm.None).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (next.Count != _forms.Count || next.Any(p => !_forms.TryGetValue(p.Key, out var f) || f != p.Value))
        {
            _forms.Clear();
            foreach (var p in next) _forms[p.Key] = p.Value;
            Update(Cards, preserveGesture: true);
        }
        else if (_raster.Scale != unit) _raster.Prepare(Cards, Theme, unit);
    }
    public double CaptureMultiplier { get; set; } = 1;
    public long FrameCount { get; private set; }
    public bool Visible { get => _conduit.Enabled; set => _conduit.Enabled = value; }
    public object RasterCaptureState => new { scale = _raster.Scale, renders = _raster.RenderCount, cards = _raster.Prepared.Values.Select(p => new { id = p.Capture.Spec.Id, rev = p.Capture.Spec.Rev, width = p.Capture.Bitmap.Width, height = p.Capture.Bitmap.Height }).ToArray() };
    private readonly CardStore _store = new();
    private readonly SessionConduit _conduit;
    private readonly RasterCardRenderer _raster = new();
    private bool _disposed;
    private Link[] _links = [];
    internal bool HasLinks => _links.Length > 0;
    private BoundingBox _linkBounds = BoundingBox.Empty;
    private BrushSnapshot _brush = new([], null);
    private IReadOnlySet<string> _selectedMembers = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlySet<string> _hoveredMembers = new HashSet<string>(StringComparer.Ordinal);
    private bool _rasterHasCaptureState;
    /// <summary>Per-card scroll position. View state, so it is re-applied to every incoming spec (GH solves send 0).</summary>
    private readonly Dictionary<string, int> _scroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int? Column, bool Descending)> _sort = new(StringComparer.Ordinal);
    private (string Card, string Key)? _headerHover;
    internal const string ScrollbarKey = "\u0001scrollbar";
    public OverlaySession(RhinoDoc doc, RhinoView view, Guid owner)
    { OverlayManager.Start(); Document = doc; View = view; Owner = owner; Unit = UiContext.Dpi(view); _conduit = new(this) { Enabled = true }; _tooltipTimer.Tick += (_, _) => TickTooltip(); }

    public void Update(CardSpec[] specs, bool preserveGesture = false, Theme? theme = null)
    {
        UiContext.Ensure();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (theme is not null) _themeSelection = theme;
        var resolved = RhinoTheme.Resolve(_themeSelection);
        if (Theme != resolved)
        {
            OverlayManager.CancelGesture(this);
            HideTooltip();
            Theme = resolved;
        }
        specs = ApplyViewState(OverlayManager.Project(this, specs));
        _brush = OverlayManager.Snapshot(this);
        if (OverlayManager.DomainSnapshot(this) is { } domain)
        { _selectedMembers = domain.SelectedMemberKeys; _hoveredMembers = domain.HoveredKeys; }
        var dirty = _store.Update(specs, Theme);
        var next = _store.Snapshot;
        if (!preserveGesture && (dirty.Count > 0 || !Cards.Select(c => c.Id).SequenceEqual(next.Select(c => c.Id)))) OverlayManager.CancelGesture(this);
        _raster.Prepare(next, Theme, Unit, force: _rasterHasCaptureState);
        _rasterHasCaptureState = false;
        Cards = next;
        var live = next.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _scroll.Keys.Where(id => !live.Contains(id)).ToArray()) _scroll.Remove(id);
        foreach (var id in _sort.Keys.Where(id => !live.Contains(id)).ToArray()) _sort.Remove(id);
        foreach (var id in _forms.Keys.Where(id => !live.Contains(id)).ToArray()) _forms.Remove(id);
        SyncTooltip();
    }

    internal void RefreshTheme()
    {
        if (_disposed || Theme == RhinoTheme.Resolve(_themeSelection)) return;
        Update(Cards);
        OverlayManager.Settle(Document, View);
    }

    private CardSpec[] ApplyViewState(CardSpec[] specs) => specs.Select(c =>
    {
        // Form first: it changes how many rows a Table or Bar shows, which bounds the scroll offset below.
        var form = _forms.GetValueOrDefault(c.Id, CardForm.None);
        if (c.Form != form) c = c with { Form = form };
        if (c.Kind is (CardKind.Table or CardKind.Bar) && _scroll.TryGetValue(c.Id, out var offset)) c = c with { ScrollOffset = Math.Clamp(offset, 0, TableScroll.MaxOffset(c)) };
        if (_sort.TryGetValue(c.Id, out var sort))
        {
            if (c.Kind != CardKind.Table || sort.Column is { } col && (col < 0 || col >= c.Headers.Length))
            { _sort.Remove(c.Id); c = c with { SortColumn = null, SortDescending = false }; }
            else c = c with { SortColumn = sort.Column, SortDescending = sort.Descending };
        }
        if (_headerHover is { } header && header.Card == c.Id)
        {
            if (c.Kind == CardKind.Table && TableSort.Column(header.Key) is { } col && col < c.Headers.Length) c = c with { HoverKey = header.Key };
            else _headerHover = null;
        }
        // Reveal only newly selected keys: hover, solves and manual scrolling must not snap back.
        if (c.Kind is (CardKind.Table or CardKind.Bar) && BrushPresentation.FirstNewRow(c, _selectedMembers) is { } added)
        {
            var revealed = TableScroll.Reveal(c, added);
            _scroll[c.Id] = revealed;
            c = c with { ScrollOffset = revealed };
        }
        return c;
    }).ToArray();

    public CardSpec? Card(string id) => Cards.FirstOrDefault(c => c.Id == id);
    internal void Sort(string id, int column)
    {
        if (Card(id) is not { Kind: CardKind.Table } c || column < 0 || column >= c.Headers.Length) return;
        var next = TableSort.Cycle(c, column);
        _sort[id] = (next.SortColumn, next.SortDescending);
        HideTooltip();
        UpdateInteraction(Cards);
    }

    /// <summary>Viewport client pixels → card-local CSS pixels for the card's current layout.</summary>
    public Point2? Local(string cardId, double x, double y)
    {
        var size = View.MainViewport.Size; var dpi = Unit;
        var layout = OverlayManager.Placements(this).FirstOrDefault(l => l.Id == cardId);
        return layout is null ? null : new Point2((x - layout.X) / dpi, (y - layout.Y) / dpi);
    }

    public void ScrollBy(string cardId, int rows) { if (Card(cardId) is { } c) ScrollTo(cardId, TableScroll.Offset(c) + rows); }
    public void ScrollTo(string cardId, int offset)
    {
        if (Card(cardId) is not { } c || !TableScroll.CanScroll(c)) return;
        offset = Math.Clamp(offset, 0, TableScroll.MaxOffset(c));
        if (_scroll.TryGetValue(cardId, out var old) && old == offset) return;
        _scroll[cardId] = offset;
        UpdateInteraction(Cards); // re-render just this card: its ScrollOffset changes the revision
    }

    public (string CardId, string Key)? Hit(double x, double y)
    {
        if (OverlayManager.CoveredByShown(this, x, y)) return null;
        var size = View.MainViewport.Size;
        foreach (var layout in OverlayManager.Placements(this).AsEnumerable().Reverse())
        {
            if (!new Rect2(layout.X, layout.Y, layout.W, layout.H).Contains(x, y)) continue;
            var dpi = Unit;
            if (Cards.FirstOrDefault(c => c.Id == layout.Id) is { } spec && TableScroll.Bar(spec, Theme) is { } bar)
            {
                // Generous horizontal target: the bar is 6 px wide but easy to grab within the card's right padding.
                var lx = (x - layout.X) / dpi; var ly = (y - layout.Y) / dpi;
                if (lx >= bar.Track.X - 4 && lx <= spec.Width && ly >= bar.Track.Y && ly <= bar.Track.Y + bar.Track.Height) return (layout.Id, ScrollbarKey);
            }
            var prepared = _raster.Prepared;
            var hits = prepared.TryGetValue(layout.Id, out var p) ? p.Capture.Layout.HitRegions : layout.HitRegions;
            var hit = hits.FirstOrDefault(h => h.Contains((x - layout.X) / Unit, (y - layout.Y) / Unit));
            return (layout.Id, hit?.Key ?? "");
        }
        return null;
    }
    public void SetHover(string? card, string? key)
    {
        _cardHover = card is not null && !string.IsNullOrEmpty(key) && !CardMeasure.IsReserved(key) ? (card, key) : null;
        var header = card is not null && key is not null && TableSort.Column(key).HasValue ? (card, key) : ((string, string)?)null;
        var changed = _headerHover != header; _headerHover = header;
        OverlayManager.Hover(this, string.IsNullOrEmpty(key) || CardMeasure.IsReserved(key) ? null : key, card: card);
        if (changed) UpdateInteraction(Cards);
        SyncTooltip();
    }
    internal void ClearCardHover()
    {
        _cardHover = null;
        if (_headerHover is null) return;
        _headerHover = null; UpdateInteraction(Cards);
    }
    public void Select(string card, string key, BrushSelectMode mode = BrushSelectMode.Replace) => OverlayManager.Select(this, card, key, mode);
    internal void RefreshInteraction() => UpdateInteraction(Cards);
    internal void SetLinks(Link[] links)
    {
        _links = links; _linkBounds = BoundingBox.Empty;
        foreach (var link in links) _linkBounds.Union(link.Bounds);
    }
    internal void Pick(PickContext context, Transform screen, Point2 cursor, ref PickCandidate? best)
    {
        foreach (var link in _links) link.Pick(context, screen, cursor, Owner, ref best);
    }
    private void UpdateInteraction(CardSpec[] cards)
    {
        if (_disposed) return;
        try { Update(cards, true); View.Redraw(); }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+ interaction: {e.Message}"); }
    }
    internal void Draw(DrawEventArgs e) => DrawCards(e, false);
    internal void DrawFloating(DrawEventArgs e) => DrawCards(e, true);
    private void DrawCards(DrawEventArgs e, bool floatingOnly)
    {
        if (_disposed || e.RhinoDoc.RuntimeSerialNumber != Document.RuntimeSerialNumber || e.Viewport.Id != View.MainViewport.Id) return;
        if (!floatingOnly) DrawHighlights(e.Display, !e.Display.IsInViewCapture);
        var size = e.Viewport.Size;
        var scale = Unit * (e.Display.IsInViewCapture ? CaptureMultiplier : 1);
        // Rhino's viewport Size remains the source view size during ViewCapture.
        var viewport = e.Display.IsInViewCapture ? new Size2(View.MainViewport.Size.Width * CaptureMultiplier, View.MainViewport.Size.Height * CaptureMultiplier) : new Size2(size.Width, size.Height);
        var multiplier = e.Display.IsInViewCapture ? CaptureMultiplier : 1;
        var layouts = OverlayManager.Placements(this, !e.Display.IsInViewCapture).Where(l => floatingOnly ? l.Id == OverlayManager.ShownId(this) : l.Id != OverlayManager.ShownId(this)).Select(l => l with { X = l.X * multiplier, Y = l.Y * multiplier, W = l.W * multiplier, H = l.H * multiplier });
        // Synchronous GDI+ work only. A target-scale ViewCapture paints fresh pixels here,
        // including direct captures; the next screen draw lazily restores screen resolution.
        if (_raster.Scale != scale || e.Display.IsInViewCapture || _rasterHasCaptureState)
        {
            // Never let an exception escape a display conduit (e.g. GDI+ out of memory for a very large capture):
            // keep drawing the existing bitmaps, scaled as they are, and report the failure.
            try
            {
                _raster.Prepare(e.Display.IsInViewCapture ? Cards.Select(BrushPresentation.WithoutHover).ToArray() : Cards, Theme, scale, force: e.Display.IsInViewCapture || _rasterHasCaptureState);
                _rasterHasCaptureState = e.Display.IsInViewCapture;
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or System.ComponentModel.Win32Exception or OverflowException)
            {
                RhinoApp.WriteLine($"Conduit+ raster at {scale:0.##}× failed. {ex.Message}");
                // A capture must never fall back to pixels containing transient hover.
                if (e.Display.IsInViewCapture) return;
            }
        }
        foreach (var layout in layouts)
        {
            if (_raster.Prepared.TryGetValue(layout.Id, out var p))
                e.Display.DrawBitmap(p.Display, (int)Math.Round(layout.X) - p.Capture.Margin, (int)Math.Round(layout.Y) - p.Capture.Margin);
        }
        if (!e.Display.IsInViewCapture && OverlayManager.ShownId(this) is { } shownId && layouts.FirstOrDefault(l => l.Id == shownId) is { } shownLayout)
            e.Display.Draw2dRectangle(new System.Drawing.Rectangle((int)shownLayout.X, (int)shownLayout.Y, (int)shownLayout.W, (int)shownLayout.H), ConduitPlus.Rendering.GdiCanvas.ParseColor(Theme.Accent), 2, System.Drawing.Color.Transparent);
        FrameCount++;
    }
    private void DrawHighlights(DisplayPipeline display, bool includeHover)
    {
        if (_links.Length == 0) return;
        var muted = ConduitPlus.Rendering.GdiCanvas.ParseColor(Theme.Muted);
        foreach (var link in _links.Where(l => l.ShowBase))
            foreach (var key in link.LinkedKeys) link.Draw(display, key, muted, 2, false, false);
        if ((!includeHover || _hoveredMembers.Count == 0) && _selectedMembers.Count == 0) return;
        var color = ConduitPlus.Rendering.GdiCanvas.ParseColor(Theme.Accent);
        display.PushDepthTesting(false);
        try
        {
            display.PushDepthWriting(false);
            try
            {
                // Composite every face before any accent wire, independent of selection order.
                if (includeHover)
                    foreach (var hover in _hoveredMembers)
                        if (!_selectedMembers.Contains(hover))
                            foreach (var link in _links) link.DrawFaces(display, hover, color, false);
                foreach (var key in _selectedMembers)
                    foreach (var link in _links) link.DrawFaces(display, key, color, true);
                if (includeHover)
                    foreach (var hover in _hoveredMembers)
                        if (!_selectedMembers.Contains(hover))
                            foreach (var link in _links) link.Draw(display, hover, color, 2, false, false);
                foreach (var key in _selectedMembers)
                    foreach (var link in _links) link.Draw(display, key, color, 4, true, false);
            }
            finally { display.PopDepthWriting(); }
        }
        finally { display.PopDepthTesting(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _conduit.Enabled = false;
        HideTooltip(); _tooltipTimer.Dispose();
        _raster.Dispose();
    }
    private sealed class SessionConduit(OverlaySession session) : DisplayConduit
    {
        protected override void DrawForeground(DrawEventArgs e) => OverlayManager.DrawForeground(session, e);
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (!session._disposed && e.RhinoDoc.RuntimeSerialNumber == session.Document.RuntimeSerialNumber && e.Viewport.Id == session.View.MainViewport.Id && session._linkBounds.IsValid)
                e.IncludeBoundingBox(session._linkBounds);
        }
    }
}
