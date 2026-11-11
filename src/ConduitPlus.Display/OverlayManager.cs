using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;


public static partial class OverlayManager
{
    // One session per (document, view, owner): two Overlay components on the same view coexist instead of evicting each other.
    private static readonly Dictionary<(uint Doc, Guid View, Guid Owner), OverlaySession> Sessions = [];
    private sealed class OwnerState(uint document, Guid? view)
    {
        public uint DocumentSerial { get; } = document;
        public Guid? ViewId { get; set; } = view;
        public BrushState Brush { get; } = new();
        public IBrushBinding? Domain { get; set; }
        public bool Enabled { get; set; } = true;
        public long Gesture { get; set; } = Interlocked.Increment(ref _gestureGeneration);
        public BrushDomainSnapshot Current => Domain?.Current ?? Brush.Current;
    }
    private static long _gestureGeneration;
    public static event Action? ViewsChanged;
    private static bool _viewsPending;
    private static string _viewSnapshot = "";
    private static string ViewSnapshot() => string.Join(";", RhinoDoc.OpenDocuments().SelectMany(d => d.Views.GetStandardRhinoViews().Select(v => $"{d.RuntimeSerialNumber}:{v.MainViewport.Id}:{v.MainViewport.Name}")).OrderBy(v => v, StringComparer.Ordinal));
    private static void ViewChanged(object? sender, ViewEventArgs e)
    { if (!_started || _viewsPending) return; _viewsPending = true; RhinoApp.Idle += ViewsIdle; }
    private static void ViewsIdle(object? sender, EventArgs e)
    {
        RhinoApp.Idle -= ViewsIdle; _viewsPending = false;
        if (!_started) return;
        var snapshot = ViewSnapshot(); if (snapshot == _viewSnapshot) return;
        _viewSnapshot = snapshot; ViewsChanged?.Invoke();
    }
    public static event Action<Guid>? BoundDocumentClosed;
    private static readonly object StateLock = new();
    private static readonly Dictionary<(uint Doc, Guid Owner), OwnerState> States = [];
    private static readonly Dictionary<Guid, OwnerState> Bindings = [];
    private static readonly OverlayMouse Mouse = new();
    private static bool _started, _stopping;
    public static event Action<OverlayInteraction>? Interaction;
    public static bool Busy { get; internal set; }
    internal static void Start()
    {
        // GH's normal solve/document callbacks are on the UI thread; other hosts may race
        // them. Marshal synchronously before text measurement, without holding StateLock.
        // All lifecycle mutations are then serialized on Rhino's UI thread.
        if (RhinoApp.InvokeRequired) { RhinoApp.InvokeOnUiThread((Action)Start); return; }
        if (_started || _stopping || RhinoApp.IsClosing) return;
        ConduitPlus.Rendering.GdiCanvas.RegisterTextMetrics();
        _started = true;
        RhinoDoc.CloseDocument += CloseDocument;
        RhinoView.Destroy += ViewDestroyed;
        RhinoView.Create += ViewChanged; RhinoView.Rename += ViewChanged; RhinoView.Modified += ViewChanged;
        _viewSnapshot = ViewSnapshot();
        RhinoApp.AppSettingsChanged += AppearanceChanged;
        RhinoApp.Closing += AppClosing; RhinoApp.EscapeKeyPressed += HiddenEscape;
        global::Rhino.Commands.Command.BeginCommand += CommandStarted;
        WheelHook.Install();
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
    }
    internal static void Stop()
    {
        if (RhinoApp.InvokeRequired) { RhinoApp.InvokeOnUiThread((Action)Stop); return; }
        if (!_started) return;
        _started = false;
        _stopping = true;
        RhinoDoc.CloseDocument -= CloseDocument; RhinoView.Destroy -= ViewDestroyed;
        RhinoView.Create -= ViewChanged; RhinoView.Rename -= ViewChanged; RhinoView.Modified -= ViewChanged;
        RhinoApp.Idle -= ViewsIdle; _viewsPending = false;
        RhinoApp.AppSettingsChanged -= AppearanceChanged;
        RhinoApp.Closing -= AppClosing; RhinoApp.EscapeKeyPressed -= HiddenEscape;
        global::Rhino.Commands.Command.BeginCommand -= CommandStarted;
        try
        {
            // A shutdown callback must not prevent the remaining native resources being released.
            Cleanup(Mouse.Reset);
            Cleanup(() => Mouse.Enabled = false);
            Cleanup(WheelHook.Uninstall);
            DisposeCoordinators(_ => true);
            var sessions = Sessions.Values.ToArray(); Sessions.Clear();
            foreach (var s in sessions) Cleanup(s.Dispose);
            lock (StateLock) { States.Clear(); Bindings.Clear(); }
            Busy = false;
        }
        finally { _stopping = false; }

        static void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine($"Conduit+ shutdown: {e}"); }
        }
    }
    // No sticky flag: if the user cancels the close, the next first-use call restarts Display.
    private static void AppClosing(object? sender, EventArgs e) => Stop();
    private static void CommandStarted(object? sender, global::Rhino.Commands.CommandEventArgs e)
    {
        foreach (var session in Sessions.Values.ToArray()) { session.HideTooltip(); CancelBrushHover(session); }
    }
    private static void AppearanceChanged(object? sender, EventArgs e) => RhinoApp.InvokeOnUiThread((Action)(() =>
    {
        if (!_started) return;
        foreach (var session in Sessions.Values.ToArray()) session.RefreshTheme();
    }));
    private static void RemoveWhere(Func<(uint Doc, Guid View, Guid Owner), bool> match, bool settle = true)
    {
        var affected = Sessions.Where(p => match(p.Key)).Select(p => (p.Value.Document, p.Value.View)).Distinct().ToArray();
        foreach (var key in Sessions.Keys.Where(match).ToArray())
        {
            CancelBrushHover(Sessions[key]); Mouse.Forget(Sessions[key]); Sessions[key].Dispose(); Sessions.Remove(key);
        }
        if (settle) foreach (var (doc, view) in affected) Settle(doc, view);
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
    }
    private static void CloseDocument(object? sender, DocumentEventArgs e)
    {
        var serial = e.Document.RuntimeSerialNumber;
        RemoveWhere(k => k.Doc == serial, false);
        DisposeCoordinators(k => k.Doc == serial);
        lock (StateLock)
        {
            foreach (var key in States.Keys.Where(k => k.Doc == serial).ToArray()) States.Remove(key);
            foreach (var owner in Bindings.Where(p => p.Value.DocumentSerial == serial).Select(p => p.Key).ToArray())
            { Bindings.Remove(owner); BoundDocumentClosed?.Invoke(owner); }
        }
    }
    private static void ViewDestroyed(object? sender, ViewEventArgs e)
    {
        ViewChanged(sender, e);
        var id = e.View.MainViewport.Id;
        RemoveWhere(k => k.View == id, false);
        DisposeCoordinators(k => k.View == id);
        lock (StateLock)
            foreach (var state in States.Values.Where(s => s.ViewId == id)) state.ViewId = null;
    }
    internal static OverlaySession Get(RhinoDoc doc, RhinoView view, Guid? owner = null)
    {
        UiContext.Ensure();
        Start();
        if (!_started || doc.IsClosing || RhinoDoc.FromRuntimeSerialNumber(doc.RuntimeSerialNumber) is null)
            throw new ObjectDisposedException(nameof(doc));
        var key = (doc.RuntimeSerialNumber, view.MainViewport.Id, owner ?? Guid.Empty);
        if (!Sessions.TryGetValue(key, out var s)) Sessions.Add(key, s = new OverlaySession(doc, view, key.Item3));
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
        return s;
    }
    public static void Set(RhinoDoc doc, RhinoView view, Guid owner, IEnumerable<CardSpec> cards, IEnumerable<Link>? links = null, Theme? theme = null, IBrushBinding? domain = null, bool brushEnabled = true, double size = 1, string? label = null)
    {
        UiContext.Ensure();
        Start();
        // Subscribe before retaining any binding, and never recreate state for a closed document.
        if (!_started || doc.IsClosing || RhinoDoc.FromRuntimeSerialNumber(doc.RuntimeSerialNumber) is null) return;
        var specs = cards.ToArray(); var linked = links?.ToArray() ?? [];
        foreach (var c in specs) c.Validate();
        lock (StateLock)
        {
            var key = (doc.RuntimeSerialNumber, owner);
            if (!States.TryGetValue(key, out var state)) States.Add(key, state = new(doc.RuntimeSerialNumber, view.MainViewport.Id));
            state.ViewId = view.MainViewport.Id; Bindings[owner] = state;
            state.Domain = domain; state.Enabled = brushEnabled;
            if (domain is null) state.Brush.Prune(specs, linked.SelectMany(l => l.Keys));
        }
        // Following the active view removes this owner's previous conduit.
        RemoveWhere(k => k.Owner == owner && (k.Doc != doc.RuntimeSerialNumber || k.View != view.MainViewport.Id));
        var session = Get(doc, view, owner);
        session.Size = size; session.Label = label ?? "Overlay";
        session.SetLinks(linked);
        session.Update(specs, theme: theme ?? Theme.Paper);
        Settle(doc, view);
        view.Redraw();
    }
    public static void ReplaceTheme(Guid owner, Theme theme)
    {
        UiContext.Ensure();
        Start();
        foreach (var session in Sessions.Values.Where(s => s.Owner == owner).ToArray())
        {
            session.Update(session.Cards, theme: theme);
            Settle(session.Document, session.View);
        }
    }
    public static void Detach(RhinoDoc doc, Guid owner)
    {
        UiContext.Ensure(); Start();
        if (!_started || doc.IsClosing || RhinoDoc.FromRuntimeSerialNumber(doc.RuntimeSerialNumber) is null) return;
        lock (StateLock)
        {
            var key = (doc.RuntimeSerialNumber, owner);
            if (!States.TryGetValue(key, out var state)) States.Add(key, state = new(doc.RuntimeSerialNumber, null));
            state.ViewId = null; Bindings[owner] = state;
        }
        var views = Sessions.Values.Where(s => s.Owner == owner).Select(s => s.View).Distinct().ToArray();
        RemoveWhere(k => k.Owner == owner);
        foreach (var view in views) view.Redraw();
    }
    public static void Remove(Guid owner)
    {
        UiContext.Ensure();
        var views = Sessions.Values.Where(s => s.Owner == owner).Select(s => s.View).Distinct().ToArray();
        RemoveWhere(k => k.Owner == owner);
        lock (StateLock) { foreach (var key in States.Keys.Where(k => k.Owner == owner).ToArray()) States.Remove(key); Bindings.Remove(owner); }
        foreach (var view in views) view.Redraw();
    }
    public static (uint DocumentSerial, Guid? ViewId)? Binding(Guid owner)
    { Start(); lock (StateLock) return Bindings.TryGetValue(owner, out var state) ? (state.DocumentSerial, state.ViewId) : null; }
    internal static void CancelGesture(OverlaySession session) => Mouse.Forget(session);
    private static void CancelBrushHover(OverlaySession session)
    {
        lock (StateLock)
            if (!States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var current) || current.Domain is null) return;
        ClearHover(session, false); ClearHover(session, true);
        lock (StateLock)
            if (States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state))
                state.Gesture = Interlocked.Increment(ref _gestureGeneration);
    }
    private static BrushSnapshot Snapshot(OwnerState state) => new(state.Current.SelectedKeys.ToArray(), state.Current.HoverKey)
    { HoveredKeys = state.Current.HoveredKeys.ToArray(), HoverOrigin = state.Current.HoverOrigin, HoverTargetKind = state.Current.HoverTargetKind };
    public static BrushSnapshot Snapshot(Guid owner)
    { Start(); lock (StateLock) return Bindings.TryGetValue(owner, out var state) ? Snapshot(state) : new([], null); }
    public static void SetInteractionEnabled(Guid owner, bool enabled)
    {
        UiContext.Ensure();
        Start();
        lock (StateLock)
            if (Bindings.TryGetValue(owner, out var state)) state.Enabled = enabled;
        if (!enabled)
            foreach (var session in Sessions.Values.Where(s => s.Owner == owner).ToArray())
            { ClearHover(session, false); ClearHover(session, true); }
    }
    public static void RefreshDomain(Guid owner)
    {
        UiContext.Ensure();
        Start();
        foreach (var session in Sessions.Values.Where(s => s.Owner == owner).ToArray()) session.RefreshInteraction();
    }
    internal static CardSpec[] Project(OverlaySession session, CardSpec[] cards)
    {
        lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Current.Project(cards) : cards;
    }
    internal static BrushSnapshot Snapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? Snapshot(state) : new([], null); }
    internal static BrushDomainSnapshot? DomainSnapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Current : null; }
    internal static bool LocalHover(OverlaySession session) => DomainSnapshot(session)?.HoverOrigin is { } origin && origin.ParticipantId == session.Owner.ToString("N") && origin.Surface is "model" or "card";
    internal static bool ModelHover(OverlaySession session) => LocalHover(session) && DomainSnapshot(session)?.HoverOrigin?.Surface == "model";
    private static BrushOrigin Origin(OverlaySession session, bool model)
    {
        lock (StateLock) return new(session.Owner.ToString("N"), model ? "model" : "card",
            States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Gesture : 0);
    }
    internal static bool TooltipBlocked => Busy || Mouse.Interacting;
    internal static void Hover(OverlaySession session, string? key, bool model = false, string? card = null)
    {
        var origin = Origin(session, model);
        var spec = card is null ? null : session.Card(card);
        var kind = spec is null || key is null ? BrushTargetKind.Member : BrushPresentation.TargetKind(spec, key);
        Change(session, b => key is null ? b.ClearHover(origin) : b.Hover(origin, key, kind),
            b => key is null ? b.ClearHover(origin) : b.Hover(origin, key, kind));
        session.SyncTooltip();
    }
    internal static void ClearHover(OverlaySession session, bool model)
    {
        Change(session, b => b.ClearHover(Origin(session, model)), b => b.ClearHover(Origin(session, model)), clearing: true);
        session.SyncTooltip();
    }
    internal static void Select(OverlaySession session, string card, string key, BrushSelectMode mode)
    {
        var clear = key == CardMeasure.ClearKey;
        if (!clear && CardMeasure.IsReserved(key)) return;
        var source = new BrushActionSource(session.Owner, card, card == "model" ? "model" : "card");
        var kind = session.Card(card) is { } spec ? BrushPresentation.TargetKind(spec, key) : BrushTargetKind.Member;
        if (Change(session, b => clear ? b.Clear(source) : b.Select(key, mode, source, kind),
            b => clear ? b.Clear(source) : b.Select(key, mode, kind, source)))
        {
            if (DomainSnapshot(session)?.Events.LastOrDefault() is { } action) Interaction?.Invoke(OverlayInteraction.From(action));
        }
    }
    private static bool Change(OverlaySession session, Func<BrushState, bool> change, Func<IBrushBinding, bool> shared, bool clearing = false)
    {
        lock (StateLock)
        {
            if (!States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state)) return false;
            if (!clearing && !state.Enabled) return false;
            if (!(state.Domain is { } domain ? shared(domain) : change(state.Brush))) return false;
        }
        RefreshDomain(session.Owner);
        return true;
    }
    internal static OverlaySession[] InView(RhinoDoc doc, RhinoView view) => Sessions.Values.Where(s => s.Document.RuntimeSerialNumber == doc.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();

    internal static void DrawForeground(OverlaySession source, DrawEventArgs e)
    {
        if (e.RhinoDoc.RuntimeSerialNumber != source.Document.RuntimeSerialNumber || e.Viewport.Id != source.View.MainViewport.Id) return;
        var sessions = InView(source.Document, source.View).Where(s => s.Visible).ToArray();
        // One foreground pass per viewport puts tooltips above every owner's cards.
        // Standalone benchmark sessions are intentionally not registered in Sessions.
        if (!sessions.Contains(source)) sessions = [source];
        if (sessions[0] != source) return;
        if (sessions.Contains(source) && Sessions.Values.Contains(source)) CheckFrame(source);
        foreach (var session in sessions) session.Draw(e);
        if (!e.Display.IsInViewCapture) foreach (var session in sessions.Where(s => ShownId(s) is not null)) session.DrawFloating(e);
        if (!e.Display.IsInViewCapture && Coordinators.TryGetValue((source.Document.RuntimeSerialNumber, source.View.MainViewport.Id), out var coordinator) && coordinator.Frame is { Chip: { } chip } frame)
        {
            DrawHiddenList(source.View, coordinator, e);
            var n = frame.Hidden.Count;
            var unit = frame.Unit.Values.Min();
            e.Display.DrawRoundedRectangle(new System.Drawing.PointF((float)(chip.X + chip.Width / 2), (float)(chip.Y + chip.Height / 2)), (float)chip.Width, (float)chip.Height, (float)(chip.Height / 2), System.Drawing.Color.FromArgb(229, 163, 61), 0, System.Drawing.Color.FromArgb(229, 163, 61));
            e.Display.Draw2dText($"+{n} {(sessions.Any(s => s.Cards.Any(c => c.Kind == CardKind.Title && frame.Hidden.Any(h => h.Owner == s.Owner && h.CardId == c.Id))) ? "items" : n == 1 ? "card" : "cards")}", System.Drawing.Color.FromArgb(23, 25, 30), new global::Rhino.Geometry.Point2d(chip.X + chip.Width / 2, chip.Y + chip.Height / 2), true, (int)Math.Round(11 * unit));
        }
        if (!e.Display.IsInViewCapture)
            foreach (var session in sessions) session.DrawTooltip(e.Display);
    }

}


