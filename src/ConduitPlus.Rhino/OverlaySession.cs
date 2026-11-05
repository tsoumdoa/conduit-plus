using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;


internal sealed class OverlaySession : IDisposable
{
    public Guid Owner { get; }
    public RhinoDoc Document { get; }
    public RhinoView View { get; }
    public CardSpec[] Cards { get; private set; } = [];
    public Theme Theme { get; } = Theme.Paper;
    public double CaptureMultiplier { get; set; } = 1;
    public long FrameCount { get; private set; }
    public bool Visible { get => _conduit.Enabled; set => _conduit.Enabled = value; }
    public object RasterCaptureState => new { scale = _raster.Scale, renders = _raster.RenderCount, cards = _raster.Prepared.Values.Select(p => new { id = p.Capture.Spec.Id, rev = p.Capture.Spec.Rev, width = p.Capture.Bitmap.Width, height = p.Capture.Bitmap.Height }).ToArray() };
    private readonly CardStore _store = new();
    private readonly SessionConduit _conduit;
    private readonly RasterCardRenderer _raster = new();
    private bool _disposed;
    private (string? Card, string? Key) _hover;
    /// <summary>Per-card scroll position. View state, so it is re-applied to every incoming spec (GH solves send 0).</summary>
    private readonly Dictionary<string, int> _scroll = new(StringComparer.Ordinal);
    internal const string ScrollbarKey = "\u0001scrollbar";
    public OverlaySession(RhinoDoc doc, RhinoView view, Guid owner)
    { Document = doc; View = view; Owner = owner; _conduit = new(this) { Enabled = true }; }

    public void Update(CardSpec[] specs)
    {
        UiContext.Ensure();
        ObjectDisposedException.ThrowIf(_disposed, this);
        specs = ApplyViewState(specs);
        _store.Update(specs);
        var next = _store.Snapshot;
        _raster.Prepare(next, Theme, UiContext.Dpi(View));
        Cards = next;
        var live = next.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _scroll.Keys.Where(id => !live.Contains(id)).ToArray()) _scroll.Remove(id);
    }

    private CardSpec[] ApplyViewState(CardSpec[] specs) => specs.Select(c =>
    {
        if (c.Kind == CardKind.Table && _scroll.TryGetValue(c.Id, out var offset)) c = c with { ScrollOffset = Math.Clamp(offset, 0, TableScroll.MaxOffset(c)) };
        // Only override hover while the pointer is over this session; explicit HoverKey specs (benchmarks) pass through otherwise.
        if (_hover.Card is not null) c = c with { HoverKey = c.Id == _hover.Card ? _hover.Key : null };
        return c;
    }).ToArray();

    public CardSpec? Card(string id) => Cards.FirstOrDefault(c => c.Id == id);

    /// <summary>Viewport client pixels → card-local CSS pixels for the card's current layout.</summary>
    public Point2? Local(string cardId, double x, double y)
    {
        var size = View.ActiveViewport.Size; var dpi = UiContext.Dpi(View);
        var layout = SlotLayout.Place(Cards, Theme, new(size.Width, size.Height), dpi).FirstOrDefault(l => l.Id == cardId);
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
        var size = View.ActiveViewport.Size;
        foreach (var layout in SlotLayout.Place(Cards, Theme, new(size.Width, size.Height), UiContext.Dpi(View)).AsEnumerable().Reverse())
        {
            if (!new Rect2(layout.X, layout.Y, layout.W, layout.H).Contains(x, y)) continue;
            var dpi = UiContext.Dpi(View);
            if (Cards.FirstOrDefault(c => c.Id == layout.Id) is { } spec && TableScroll.Bar(spec, Theme) is { } bar)
            {
                // Generous horizontal target: the bar is 6 px wide but easy to grab within the card's right padding.
                var lx = (x - layout.X) / dpi; var ly = (y - layout.Y) / dpi;
                if (lx >= bar.Track.X - 4 && lx <= spec.Width && ly >= bar.Track.Y && ly <= bar.Track.Y + bar.Track.Height) return (layout.Id, ScrollbarKey);
            }
            var prepared = _raster.Prepared;
            var hits = prepared.TryGetValue(layout.Id, out var p) ? p.Capture.Layout.HitRegions : layout.HitRegions;
            var hit = hits.FirstOrDefault(h => h.Contains((x - layout.X) / UiContext.Dpi(View), (y - layout.Y) / UiContext.Dpi(View)));
            return (layout.Id, hit?.Key ?? "");
        }
        return null;
    }
    public void SetHover(string? card, string? key)
    {
        if (_hover == (card, key)) return;
        _hover = (card, key);
        UpdateInteraction(Cards.Select(c => c with { HoverKey = c.Id == card ? key : null }).ToArray());
    }
    public void Select(string card, string key) => UpdateInteraction(Cards.Select(c => c.Id == card ? c with { SelectedKeys = c.SelectedKeys.Contains(key) ? c.SelectedKeys.Where(k => k != key).ToArray() : [.. c.SelectedKeys, key] } : c).ToArray());
    private void UpdateInteraction(CardSpec[] cards)
    {
        if (_disposed) return;
        try { Update(cards); View.Redraw(); }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+ interaction: {e.Message}"); }
    }
    private void Draw(DrawEventArgs e)
    {
        if (_disposed || e.RhinoDoc.RuntimeSerialNumber != Document.RuntimeSerialNumber || e.Viewport.Id != View.MainViewport.Id) return;
        var size = e.Viewport.Size;
        var scale = UiContext.Dpi(View) * (e.Display.IsInViewCapture ? CaptureMultiplier : 1);
        // Rhino's viewport Size remains the source view size during ViewCapture.
        var viewport = e.Display.IsInViewCapture ? new Size2(View.ActiveViewport.Size.Width * CaptureMultiplier, View.ActiveViewport.Size.Height * CaptureMultiplier) : new Size2(size.Width, size.Height);
        var layouts = SlotLayout.Place(Cards, Theme, viewport, scale);
        // Synchronous GDI+ work only. A target-scale ViewCapture paints fresh pixels here,
        // including direct captures; the next screen draw lazily restores screen resolution.
        if (_raster.Scale != scale)
        {
            // Never let an exception escape a display conduit (e.g. GDI+ out of memory for a very large capture):
            // keep drawing the existing bitmaps, scaled as they are, and report the failure.
            try { _raster.Prepare(Cards, Theme, scale); }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or System.ComponentModel.Win32Exception or OverflowException)
            { RhinoApp.WriteLine($"Conduit+ raster at {scale:0.##}× failed; using screen-resolution cards. {ex.Message}"); }
        }
        foreach (var layout in layouts)
        {
            if (_raster.Prepared.TryGetValue(layout.Id, out var p))
                e.Display.DrawBitmap(p.Display, (int)Math.Round(layout.X), (int)Math.Round(layout.Y));
        }
        FrameCount++;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _conduit.Enabled = false;
        _raster.Dispose();
    }
    private sealed class SessionConduit(OverlaySession session) : DisplayConduit
    { protected override void DrawForeground(DrawEventArgs e) => session.Draw(e); }
}
