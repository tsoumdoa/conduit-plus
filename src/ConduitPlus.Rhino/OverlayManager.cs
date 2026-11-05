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
        Mouse.Enabled = Sessions.Count > 0;
    }
    internal static void Stop()
    {
        Mouse.Enabled = false;
        _started = false;
        RhinoDoc.CloseDocument -= CloseDocument; RhinoView.Destroy -= ViewDestroyed;
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
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        protected override void OnMouseMove(MouseCallbackEventArgs e)
        {
            if (Busy) return;
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
                if (!string.IsNullOrEmpty(hit.Value.Key))
                {
                    s.Select(hit.Value.CardId, hit.Value.Key);
                    Interaction?.Invoke(new(s.Owner, hit.Value.CardId, hit.Value.Key, "click"));
                }
                return;
            }
        }
        protected override void OnMouseUp(MouseCallbackEventArgs e)
        { if (_pressed) { e.Cancel = true; _pressed = false; } }
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
    private readonly CardStore _store = new();
    private readonly SessionConduit _conduit;
    private readonly NativeCardRenderer _native = new();
    private WebCardRenderer? _web;
    private bool _disposed;
    private bool _interactionRunning;
    private CardSpec[]? _pending;
    private readonly SemaphoreSlim _updates = new(1);
    private (string? Card, string? Key) _hover;
    public OverlaySession(RhinoDoc doc, RhinoView view, Guid owner)
    { Document = doc; View = view; Owner = owner; _conduit = new(this) { Enabled = true }; }

    public async Task UpdateAsync(CardSpec[] specs, RendererKind renderer)
    {
        await _updates.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _store.Update(specs);
            var next = _store.Snapshot;
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

    public (string CardId, string Key)? Hit(double x, double y)
    {
        var size = View.ActiveViewport.Size;
        foreach (var layout in SlotLayout.Place(Cards, Theme, new(size.Width, size.Height), UiContext.Dpi(View)).AsEnumerable().Reverse())
        {
            if (!new Rect2(layout.X, layout.Y, layout.W, layout.H).Contains(x, y)) continue;
            var hits = Renderer == RendererKind.Web && _web!.Prepared.TryGetValue(layout.Id, out var p) ? p.Capture.Layout.HitRegions : layout.HitRegions;
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
        foreach (var layout in layouts)
        {
            var start = watch.Elapsed.TotalMilliseconds;
            if (Renderer == RendererKind.Native) _native.Paint(e.Display, Cards.Single(c => c.Id == layout.Id), Theme, layout, scale);
            else if (_web!.Prepared.TryGetValue(layout.Id, out var p)) e.Display.DrawBitmap(p.Display, (int)Math.Round(layout.X), (int)Math.Round(layout.Y));
            LastCardPaintMs[layout.Id] = watch.Elapsed.TotalMilliseconds - start;
        }
        LastPaintMs = watch.Elapsed.TotalMilliseconds;
        FrameCount++;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _conduit.Enabled = false;
        _web?.Dispose(); _web = null; _pending = null;
    }
    private sealed class SessionConduit(OverlaySession session) : DisplayConduit
    { protected override void DrawForeground(DrawEventArgs e) => session.Draw(e); }
}
