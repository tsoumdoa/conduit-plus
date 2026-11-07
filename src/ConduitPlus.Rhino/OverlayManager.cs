using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public sealed record LayoutProposal(Guid Owner, IReadOnlyDictionary<string, LayoutOverride> Changes);

public static partial class OverlayManager
{
    // One session per (document, view, owner): two Overlay components on the same view coexist instead of evicting each other.
    private static readonly Dictionary<(uint Doc, Guid View, Guid Owner), OverlaySession> Sessions = [];
    private sealed class OwnerState(uint document, Guid view)
    {
        public uint DocumentSerial { get; } = document;
        public Guid? ViewId { get; set; } = view;
        public BrushState Brush { get; } = new();
        public Dictionary<string, LayoutOverride> Layout { get; set; } = new(StringComparer.Ordinal);
        public CardSpec[] Originals { get; set; } = [];
    }
    private static readonly object StateLock = new();
    private static readonly Dictionary<(uint Doc, Guid Owner), OwnerState> States = [];
    private static readonly Dictionary<Guid, OwnerState> Bindings = [];
    private static readonly OverlayMouse Mouse = new();
    private static bool _started;
    public static event Action<OverlayInteraction>? Interaction;
    public static event Action<LayoutProposal>? LayoutProposed;
    public static bool Busy { get; internal set; }
    internal static void Start()
    {
        if (_started) return;
        ConduitPlus.Rendering.GdiCanvas.RegisterTextMetrics();
        _started = true;
        RhinoDoc.CloseDocument += CloseDocument;
        RhinoView.Destroy += ViewDestroyed;
        RhinoApp.AppSettingsChanged += AppearanceChanged;
        global::Rhino.Commands.Command.BeginCommand += CommandStarted;
        WheelHook.Install();
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
    }
    internal static void Stop()
    {
        Mouse.Reset();
        Mouse.Enabled = false;
        _started = false;
        RhinoDoc.CloseDocument -= CloseDocument; RhinoView.Destroy -= ViewDestroyed;
        RhinoApp.AppSettingsChanged -= AppearanceChanged;
        global::Rhino.Commands.Command.BeginCommand -= CommandStarted;
        WheelHook.Uninstall();
        foreach (var s in Sessions.Values) s.Dispose(); Sessions.Clear();
        lock (StateLock) { States.Clear(); Bindings.Clear(); }
    }
    private static void CommandStarted(object? sender, global::Rhino.Commands.CommandEventArgs e)
    {
        foreach (var session in Sessions.Values.ToArray()) session.HideTooltip();
    }
    private static void AppearanceChanged(object? sender, EventArgs e) => RhinoApp.InvokeOnUiThread((Action)(() =>
    {
        if (!_started) return;
        foreach (var session in Sessions.Values.ToArray()) session.RefreshTheme();
    }));
    private static void RemoveWhere(Func<(uint Doc, Guid View, Guid Owner), bool> match)
    {
        foreach (var key in Sessions.Keys.Where(match).ToArray())
        {
            Mouse.Forget(Sessions[key]); Sessions[key].Dispose(); Sessions.Remove(key);
        }
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
    }
    private static void CloseDocument(object? sender, DocumentEventArgs e)
    {
        var serial = e.Document.RuntimeSerialNumber;
        RemoveWhere(k => k.Doc == serial);
        lock (StateLock)
        {
            foreach (var key in States.Keys.Where(k => k.Doc == serial).ToArray()) States.Remove(key);
            foreach (var owner in Bindings.Where(p => p.Value.DocumentSerial == serial).Select(p => p.Key).ToArray()) Bindings.Remove(owner);
        }
    }
    private static void ViewDestroyed(object? sender, ViewEventArgs e)
    {
        var id = e.View.MainViewport.Id;
        RemoveWhere(k => k.View == id);
        lock (StateLock)
            foreach (var state in States.Values.Where(s => s.ViewId == id)) state.ViewId = null;
    }
    internal static OverlaySession Get(RhinoDoc doc, RhinoView view, Guid? owner = null)
    {
        UiContext.Ensure();
        Start();
        var key = (doc.RuntimeSerialNumber, view.MainViewport.Id, owner ?? Guid.Empty);
        if (!Sessions.TryGetValue(key, out var s)) Sessions.Add(key, s = new OverlaySession(doc, view, key.Item3));
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
        return s;
    }
    public static void Set(RhinoDoc doc, RhinoView view, Guid owner, IEnumerable<CardSpec> cards, IEnumerable<Link>? links = null, IReadOnlyDictionary<string, LayoutOverride>? layout = null, Theme? theme = null)
    {
        UiContext.Ensure();
        var specs = cards.ToArray(); var linked = links?.ToArray() ?? [];
        foreach (var c in specs) c.Validate();
        lock (StateLock)
        {
            var key = (doc.RuntimeSerialNumber, owner);
            if (!States.TryGetValue(key, out var state)) States.Add(key, state = new(doc.RuntimeSerialNumber, view.MainViewport.Id));
            state.ViewId = view.MainViewport.Id; Bindings[owner] = state;
            state.Originals = specs;
            if (layout is not null) state.Layout = new(layout, StringComparer.Ordinal);
            state.Brush.Prune(specs, linked.SelectMany(l => l.Keys));
        }
        // Following the active view removes this owner's previous conduit.
        RemoveWhere(k => k.Owner == owner && (k.Doc != doc.RuntimeSerialNumber || k.View != view.MainViewport.Id));
        var session = Get(doc, view, owner);
        session.SetLinks(linked);
        session.Update(LayoutEditing.Apply(specs, LayoutSnapshot(session)), theme: theme ?? Theme.Paper);
        view.Redraw();
    }
    public static void ReplaceTheme(Guid owner, Theme theme)
    {
        UiContext.Ensure();
        foreach (var session in Sessions.Values.Where(s => s.Owner == owner).ToArray())
        {
            session.Update(session.Cards, theme: theme);
            session.View.Redraw();
        }
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
    { lock (StateLock) return Bindings.TryGetValue(owner, out var state) ? (state.DocumentSerial, state.ViewId) : null; }
    internal static Dictionary<string, LayoutOverride> LayoutSnapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? new(state.Layout, StringComparer.Ordinal) : []; }
    public static void ReplaceLayout(Guid owner, IReadOnlyDictionary<string, LayoutOverride> layout) => ReplaceLayout(owner, layout, null);
    private static void ReplaceLayout(Guid owner, IReadOnlyDictionary<string, LayoutOverride> layout, uint? documentSerial)
    {
        UiContext.Ensure();
        lock (StateLock)
            foreach (var state in States.Where(p => p.Key.Owner == owner && (!documentSerial.HasValue || p.Key.Doc == documentSerial)).Select(p => p.Value)) state.Layout = new(layout, StringComparer.Ordinal);
        foreach (var session in Sessions.Values.Where(s => s.Owner == owner && (!documentSerial.HasValue || s.Document.RuntimeSerialNumber == documentSerial)).ToArray())
        {
            OwnerState state;
            lock (StateLock) state = States[(session.Document.RuntimeSerialNumber, owner)];
            session.Update(LayoutEditing.Apply(state.Originals, state.Layout), session.CommittingLayout);
            session.View.Redraw();
        }
    }
    internal static void ProposeLayout(OverlaySession session, IReadOnlyDictionary<string, LayoutOverride> changes)
    {
        UiContext.Ensure();
        if (changes.Count == 0) return;
        if (session.Owner != Guid.Empty) { LayoutProposed?.Invoke(new(session.Owner, changes)); return; }
        var next = LayoutSnapshot(session);
        foreach (var pair in changes) next[pair.Key] = pair.Value;
        ReplaceLayout(session.Owner, next, session.Document.RuntimeSerialNumber);
    }
    internal static void CancelGesture(OverlaySession session) => Mouse.Forget(session);
    public static BrushSnapshot Snapshot(Guid owner)
    { lock (StateLock) return Bindings.TryGetValue(owner, out var state) ? state.Brush.Snapshot : new([], null); }
    internal static CardSpec[] Project(OverlaySession session, CardSpec[] cards)
    {
        lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Brush.Project(cards) : cards;
    }
    internal static BrushSnapshot Snapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Brush.Snapshot : new([], null); }
    internal static BrushDomainSnapshot? DomainSnapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Brush.Current : null; }
    internal static bool ModelHover(OverlaySession session) => DomainSnapshot(session)?.HoverOrigin?.Surface == "model";
    private static BrushOrigin Origin(OverlaySession session, bool model) => new(session.Owner.ToString("N"), model ? "model" : "card", 0);
    internal static bool TooltipBlocked => Busy || Mouse.Interacting;
    internal static void Hover(OverlaySession session, string? key, bool model = false, string? card = null)
    {
        var origin = Origin(session, model);
        var spec = card is null ? null : session.Card(card);
        Change(session, b => key is null ? b.ClearHover(origin) : b.Hover(origin, key,
            spec is null ? BrushTargetKind.Member : BrushPresentation.TargetKind(spec, key)));
        session.SyncTooltip();
    }
    internal static void ClearHover(OverlaySession session, bool model)
    {
        Change(session, b => b.ClearHover(Origin(session, model)));
        session.SyncTooltip();
    }
    internal static void Select(OverlaySession session, string card, string key)
    {
        var clear = key == CardMeasure.ClearKey;
        if (!clear && CardMeasure.IsReserved(key)) return;
        BrushEvent? action = null;
        if (Change(session, b =>
        {
            var source = new BrushActionSource(session.Owner, card, card == "model" ? "model" : "card");
            var kind = session.Card(card) is { } spec ? BrushPresentation.TargetKind(spec, key) : BrushTargetKind.Member;
            var changed = clear ? b.Clear(source) : b.Toggle(key, kind, source);
            if (changed) action = b.Current.Events[^1];
            return changed;
        })) Interaction?.Invoke(OverlayInteraction.From(action!));
    }
    private static bool Change(OverlaySession session, Func<BrushState, bool> change)
    {
        lock (StateLock)
            if (!States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) || !change(state.Brush)) return false;
        foreach (var s in Sessions.Values.Where(s => s.Document.RuntimeSerialNumber == session.Document.RuntimeSerialNumber && s.Owner == session.Owner).ToArray())
            s.RefreshInteraction();
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
        foreach (var session in sessions) session.Draw(e);
        if (!e.Display.IsInViewCapture)
            foreach (var session in sessions) session.DrawTooltip(e.Display);
    }

}
