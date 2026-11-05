using System.Diagnostics;
using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.UI;
using System.Runtime.InteropServices;

namespace ConduitPlus.Rhino;

public sealed record OverlayInteraction(Guid Owner, string CardId, string Key, string Kind);

public static class OverlayManager
{
    // One session per (document, view, owner): two Overlay components on the same view coexist instead of evicting each other.
    private static readonly Dictionary<(uint Doc, Guid View, Guid Owner), OverlaySession> Sessions = [];
    private static readonly OverlayMouse Mouse = new();
    private static bool _started;
    public static event Action<OverlayInteraction>? Interaction;
    public static bool Busy { get; internal set; }
    internal static void Start()
    {
        if (_started) return;
        _started = true;
        RhinoDoc.CloseDocument += CloseDocument;
        RhinoView.Destroy += ViewDestroyed;
        WheelHook.Install();
        Mouse.Enabled = Sessions.Count > 0;
    }
    internal static void Stop()
    {
        Mouse.Enabled = false;
        _started = false;
        RhinoDoc.CloseDocument -= CloseDocument; RhinoView.Destroy -= ViewDestroyed;
        WheelHook.Uninstall();
        foreach (var s in Sessions.Values) s.Dispose(); Sessions.Clear();
    }
    private static void RemoveWhere(Func<(uint Doc, Guid View, Guid Owner), bool> match)
    {
        foreach (var key in Sessions.Keys.Where(match).ToArray()) { Sessions[key].Dispose(); Sessions.Remove(key); }
        Mouse.Enabled = Sessions.Count > 0;
    }
    private static void CloseDocument(object? sender, DocumentEventArgs e) => RemoveWhere(k => k.Doc == e.Document.RuntimeSerialNumber);
    private static void ViewDestroyed(object? sender, ViewEventArgs e) => RemoveWhere(k => k.View == e.View.MainViewport.Id);
    internal static OverlaySession Get(RhinoDoc doc, RhinoView view, Guid? owner = null)
    {
        UiContext.Ensure();
        Start();
        var key = (doc.RuntimeSerialNumber, view.MainViewport.Id, owner ?? Guid.Empty);
        if (!Sessions.TryGetValue(key, out var s)) Sessions.Add(key, s = new OverlaySession(doc, view, key.Item3));
        Mouse.Enabled = Sessions.Count > 0;
        return s;
    }
    public static async Task SetAsync(RhinoDoc doc, RhinoView view, Guid owner, IEnumerable<CardSpec> cards, RendererKind renderer)
    {
        // An overlay follows the active view: drop this owner's sessions in other views/documents so no stale cards remain.
        RemoveWhere(k => k.Owner == owner && (k.Doc != doc.RuntimeSerialNumber || k.View != view.MainViewport.Id));
        var s = Get(doc, view, owner);
        await s.UpdateAsync(cards.ToArray(), renderer);
        view.Redraw();
    }
    public static void Remove(Guid owner) => RemoveWhere(k => k.Owner == owner);
    internal static OverlaySession[] InView(RhinoDoc doc, RhinoView view) => Sessions.Values.Where(s => s.Document.RuntimeSerialNumber == doc.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();

    /// <summary>
    /// Starts async overlay work from a UI-thread callback (InvokeOnUiThread, mouse events). Continuations resume on
    /// the UI thread without replacing Rhino's own SynchronizationContext. <paramref name="work"/> must handle its errors.
    /// </summary>
    public static void RunOnUi(Func<Task> work)
    {
        UiContext.Ensure();
        using (UiContext.Scope()) _ = work();
    }

    private sealed class OverlayMouse : MouseCallback
    {
        private bool _pressed;
        // Active scrollbar-thumb drag: session, card, and where on the thumb it was grabbed (card-local CSS px).
        private (OverlaySession Session, string CardId, double Grab)? _drag;
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        protected override void OnMouseMove(MouseCallbackEventArgs e)
        {
            if (Busy) return;
            if (_drag is { } drag)
            {
                e.Cancel = true;
                var local = drag.Session.Local(drag.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                if (local is { } p && drag.Session.Card(drag.CardId) is { } card)
                    drag.Session.ScrollTo(drag.CardId, TableScroll.OffsetForThumbTop(card, drag.Session.Theme, p.Y - drag.Grab));
                return;
            }
            foreach (var s in InView(e.View))
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                s.SetHover(hit?.CardId, hit?.Key);
            }
        }
        protected override void OnMouseDown(MouseCallbackEventArgs e)
        {
            if (Busy || e.Button != System.Windows.Forms.MouseButtons.Left) return;
            foreach (var s in InView(e.View))
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                if (hit is null) continue;
                e.Cancel = true; _pressed = true;
                if (hit.Value.Key == OverlaySession.ScrollbarKey)
                {
                    var card = s.Card(hit.Value.CardId); var local = s.Local(hit.Value.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                    if (card is not null && local is { } p && TableScroll.Bar(card, s.Theme) is { } bar)
                    {
                        if (p.Y >= bar.Thumb.Y && p.Y <= bar.Thumb.Y + bar.Thumb.Height) _drag = (s, card.Id, p.Y - bar.Thumb.Y);
                        else s.ScrollBy(card.Id, p.Y < bar.Thumb.Y ? -TableScroll.Visible(card) : TableScroll.Visible(card)); // page
                    }
                    return;
                }
                if (!string.IsNullOrEmpty(hit.Value.Key))
                {
                    s.Select(hit.Value.CardId, hit.Value.Key);
                    Interaction?.Invoke(new(s.Owner, hit.Value.CardId, hit.Value.Key, "click"));
                }
                return;
            }
        }
        protected override void OnMouseUp(MouseCallbackEventArgs e)
        { _drag = null; if (_pressed) { e.Cancel = true; _pressed = false; } }
    }

    /// <summary>
    /// RhinoCommon's MouseCallback has no wheel event. A thread-local WH_GETMESSAGE hook on Rhino's UI thread sees
    /// WM_MOUSEWHEEL before any window does; over a scrollable table it scrolls and swallows the message so Rhino
    /// doesn't zoom. Everything else passes through untouched.
    /// </summary>
    private static class WheelHook
    {
        private const int WhGetMessage = 3, PmRemove = 1, WmMouseWheel = 0x020A, WmNull = 0;
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        private static readonly HookProc Proc = OnMessage; // rooted for the hook's lifetime
        private static IntPtr _hook;
        public static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WhGetMessage, Proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero) RhinoApp.WriteLine("Conduit+: mouse-wheel scrolling unavailable (hook failed).");
        }
        public static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero;
        }
        private static IntPtr OnMessage(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && wParam == PmRemove && !Busy && Sessions.Count > 0 && Marshal.ReadInt32(lParam, IntPtr.Size) == WmMouseWheel)
                {
                    var msgW = Marshal.ReadIntPtr(lParam, IntPtr.Size * 2).ToInt64();
                    var msgL = Marshal.ReadIntPtr(lParam, IntPtr.Size * 3).ToInt64();
                    var delta = (short)((msgW >> 16) & 0xFFFF);
                    var screen = new System.Drawing.Point((short)(msgL & 0xFFFF), (short)((msgL >> 16) & 0xFFFF));
                    if (TryScroll(screen, delta)) Marshal.WriteInt32(lParam, IntPtr.Size, WmNull); // swallow: no Rhino zoom
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ wheel: {ex.Message}"); }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
        private static bool TryScroll(System.Drawing.Point screen, short delta)
        {
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!s.View.ScreenRectangle.Contains(screen)) continue;
                var client = s.View.ScreenToClient(screen);
                if (s.Hit(client.X, client.Y) is not { } hit || s.Card(hit.CardId) is not { } card || !TableScroll.CanScroll(card)) continue;
                var notches = Math.Max(1, Math.Abs(delta) / 120);
                s.ScrollBy(card.Id, -Math.Sign(delta) * 3 * notches);
                return true;
            }
            return false;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}

internal static class UiContext
{
    public static void Ensure()
    {
        if (RhinoApp.InvokeRequired) throw new InvalidOperationException("Overlay must be updated on Rhino's UI thread.");
    }
    /// <summary>Scopes UI-thread continuation marshaling to our own async call chains.</summary>
    public static IDisposable Scope() => ConduitPlus.Rendering.CaptureUiContext.Enter();
    public static double Dpi(RhinoView view) => Math.Max(1, GetDpiForWindow(view.Handle) / 96d);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}

internal sealed class OverlaySession : IDisposable
{
    public Guid Owner { get; }
    public RhinoDoc Document { get; }
    public RhinoView View { get; }
    public RendererKind Renderer { get; private set; }
    public CardSpec[] Cards { get; private set; } = [];
    public Theme Theme { get; } = Theme.Paper;
    public double CaptureMultiplier { get; set; } = 1;
    public double Scale => UiContext.Dpi(View) * CaptureMultiplier;
    public double LastPaintMs { get; private set; }
    public Dictionary<string, double> LastCardPaintMs { get; } = [];
    public double LastLayoutMs { get; private set; }
    public long FrameCount { get; private set; }
    public bool Visible { get => _conduit.Enabled; set => _conduit.Enabled = value; }
    public uint BrowserProcessId => _web?.BrowserProcessId ?? 0;
    public object? RasterCaptureState => _raster is null ? null : new { scale = _raster.Scale, renders = _raster.RenderCount, cards = _raster.Prepared.Values.Select(p => new { id = p.Capture.Spec.Id, rev = p.Capture.Spec.Rev, width = p.Capture.Bitmap.Width, height = p.Capture.Bitmap.Height }).ToArray() };
    private readonly CardStore _store = new();
    private readonly SessionConduit _conduit;
    private readonly NativeCardRenderer _native = new();
    private WebCardRenderer? _web;
    private RasterCardRenderer? _raster;
    private bool _disposed;
    private bool _interactionRunning;
    private CardSpec[]? _pending;
    private readonly SemaphoreSlim _updates = new(1);
    private (string? Card, string? Key) _hover;
    /// <summary>Per-card scroll position. View state, so it is re-applied to every incoming spec (GH solves send 0).</summary>
    private readonly Dictionary<string, int> _scroll = new(StringComparer.Ordinal);
    internal const string ScrollbarKey = "\u0001scrollbar";
    public OverlaySession(RhinoDoc doc, RhinoView view, Guid owner)
    { Document = doc; View = view; Owner = owner; _conduit = new(this) { Enabled = true }; }

    public async Task UpdateAsync(CardSpec[] specs, RendererKind renderer)
    {
        await _updates.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            specs = ApplyViewState(specs);
            _store.Update(specs);
            var next = _store.Snapshot;
            if (renderer == RendererKind.Raster)
            {
                _raster ??= new RasterCardRenderer();
                _raster.Prepare(next, Theme, UiContext.Dpi(View));
            }
            if (renderer == RendererKind.Web)
            {
                if (_web is null)
                {
                    _web = new WebCardRenderer();
                    _web.BrowserFailed += () => { if (!_disposed && Renderer == RendererKind.Web) QueueInteraction(Cards); };
                }
                await _web.PrepareAsync(next, Theme, Scale);
            }
            if (_disposed) return;
            Cards = next; Renderer = renderer;
        }
        finally { _updates.Release(); }
    }

    private CardSpec[] ApplyViewState(CardSpec[] specs) => specs.Select(c =>
    {
        if (c.Kind == CardKind.Table && _scroll.TryGetValue(c.Id, out var offset)) c = c with { ScrollOffset = Math.Clamp(offset, 0, TableScroll.MaxOffset(c)) };
        // Only override hover while the pointer is over this session; explicit HoverKey specs (benchmarks) pass through otherwise.
        if (_hover.Card is not null) c = c with { HoverKey = c.Id == _hover.Card ? _hover.Key : null };
        return c;
    }).ToArray();

    public CardSpec? Card(string id) => (_pending ?? Cards).FirstOrDefault(c => c.Id == id);

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
        QueueInteraction(_pending ?? Cards); // re-render just this card: its ScrollOffset changes the revision
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
            var prepared = Renderer == RendererKind.Web ? _web?.Prepared : Renderer == RendererKind.Raster ? _raster?.Prepared : null;
            var hits = prepared is not null && prepared.TryGetValue(layout.Id, out var p) ? p.Capture.Layout.HitRegions : layout.HitRegions;
            var hit = hits.FirstOrDefault(h => h.Contains((x - layout.X) / UiContext.Dpi(View), (y - layout.Y) / UiContext.Dpi(View)));
            return (layout.Id, hit?.Key ?? "");
        }
        return null;
    }
    public void SetHover(string? card, string? key)
    {
        if (_hover == (card, key)) return;
        _hover = (card, key);
        QueueInteraction((_pending ?? Cards).Select(c => c with { HoverKey = c.Id == card ? key : null }).ToArray());
    }
    public void Select(string card, string key) => QueueInteraction((_pending ?? Cards).Select(c => c.Id == card ? c with { SelectedKeys = c.SelectedKeys.Contains(key) ? c.SelectedKeys.Where(k => k != key).ToArray() : [.. c.SelectedKeys, key] } : c).ToArray());
    private void QueueInteraction(CardSpec[] cards)
    {
        _pending = cards;
        if (_interactionRunning) return;
        _interactionRunning = true;
        OverlayManager.RunOnUi(DrainInteractionsAsync);
    }
    private async Task DrainInteractionsAsync()
    {
        try
        {
            while (_pending is { } next && !_disposed)
            {
                _pending = null; await UpdateAsync(next, Renderer);
                if (!_disposed) View.Redraw();
            }
        }
        catch (Exception e) { if (!_disposed) RhinoApp.WriteLine($"Conduit+ interaction: {e.Message}"); }
        finally { _interactionRunning = false; }
    }
    private void Draw(DrawEventArgs e)
    {
        if (_disposed || e.RhinoDoc.RuntimeSerialNumber != Document.RuntimeSerialNumber || e.Viewport.Id != View.MainViewport.Id) return;
        var watch = Stopwatch.StartNew();
        var size = e.Viewport.Size;
        var scale = UiContext.Dpi(View) * (e.Display.IsInViewCapture ? CaptureMultiplier : 1);
        // Rhino's viewport Size remains the source view size during ViewCapture.
        var viewport = e.Display.IsInViewCapture ? new Size2(View.ActiveViewport.Size.Width * CaptureMultiplier, View.ActiveViewport.Size.Height * CaptureMultiplier) : new Size2(size.Width, size.Height);
        var layouts = SlotLayout.Place(Cards, Theme, viewport, scale);
        LastLayoutMs = watch.Elapsed.TotalMilliseconds;
        // Synchronous GDI+ work only. A target-scale ViewCapture paints fresh pixels here,
        // including direct captures; the next screen draw lazily restores screen resolution.
        if (Renderer == RendererKind.Raster && _raster!.Scale != scale)
        {
            // Never let an exception escape a display conduit (e.g. GDI+ out of memory for a very large capture):
            // keep drawing the existing bitmaps, scaled as they are, and report once.
            try { _raster.Prepare(Cards, Theme, scale); }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or System.ComponentModel.Win32Exception or OverflowException)
            { RhinoApp.WriteLine($"Conduit+ raster at {scale:0.##}× failed; using screen-resolution cards. {ex.Message}"); }
        }
        foreach (var layout in layouts)
        {
            var start = watch.Elapsed.TotalMilliseconds;
            if (Renderer == RendererKind.Native) _native.Paint(e.Display, Cards.Single(c => c.Id == layout.Id), Theme, layout, scale);
            else
            {
                var prepared = Renderer == RendererKind.Raster ? _raster!.Prepared : _web!.Prepared;
                if (prepared.TryGetValue(layout.Id, out var p)) e.Display.DrawBitmap(p.Display, (int)Math.Round(layout.X), (int)Math.Round(layout.Y));
            }
            LastCardPaintMs[layout.Id] = watch.Elapsed.TotalMilliseconds - start;
        }
        LastPaintMs = watch.Elapsed.TotalMilliseconds;
        FrameCount++;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _conduit.Enabled = false;
        _web?.Dispose(); _web = null; _raster?.Dispose(); _raster = null; _pending = null;
    }
    private sealed class SessionConduit(OverlaySession session) : DisplayConduit
    { protected override void DrawForeground(DrawEventArgs e) => session.Draw(e); }
}
