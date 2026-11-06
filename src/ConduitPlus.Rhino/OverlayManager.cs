using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public sealed record OverlayInteraction(Guid Owner, string CardId, string Key, string Kind);

public static partial class OverlayManager
{
    // One session per (document, view, owner): two Overlay components on the same view coexist instead of evicting each other.
    private static readonly Dictionary<(uint Doc, Guid View, Guid Owner), OverlaySession> Sessions = [];
    private sealed class OwnerState(uint document, Guid view)
    {
        public uint DocumentSerial { get; } = document;
        public Guid? ViewId { get; set; } = view;
        public BrushState Brush { get; } = new();
        public bool ModelHover { get; set; }
    }
    private static readonly object StateLock = new();
    private static readonly Dictionary<(uint Doc, Guid Owner), OwnerState> States = [];
    private static readonly Dictionary<Guid, OwnerState> Bindings = [];
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
        Mouse.Enabled = Sessions.Count > 0 || Mouse.Pressed;
    }
    internal static void Stop()
    {
        Mouse.Reset();
        Mouse.Enabled = false;
        _started = false;
        RhinoDoc.CloseDocument -= CloseDocument; RhinoView.Destroy -= ViewDestroyed;
        WheelHook.Uninstall();
        foreach (var s in Sessions.Values) s.Dispose(); Sessions.Clear();
        lock (StateLock) { States.Clear(); Bindings.Clear(); }
    }
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
    public static void Set(RhinoDoc doc, RhinoView view, Guid owner, IEnumerable<CardSpec> cards, IEnumerable<Link>? links = null)
    {
        UiContext.Ensure();
        var specs = cards.ToArray(); var linked = links?.ToArray() ?? [];
        foreach (var c in specs) c.Validate();
        lock (StateLock)
        {
            var key = (doc.RuntimeSerialNumber, owner);
            if (!States.TryGetValue(key, out var state)) States.Add(key, state = new(doc.RuntimeSerialNumber, view.MainViewport.Id));
            state.ViewId = view.MainViewport.Id; Bindings[owner] = state;
            state.Brush.Prune(specs, linked.SelectMany(l => l.Keys));
        }
        // Following the active view removes this owner's previous conduit.
        RemoveWhere(k => k.Owner == owner && (k.Doc != doc.RuntimeSerialNumber || k.View != view.MainViewport.Id));
        var session = Get(doc, view, owner);
        session.SetLinks(linked);
        session.Update(specs);
        view.Redraw();
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
    public static BrushSnapshot Snapshot(Guid owner)
    { lock (StateLock) return Bindings.TryGetValue(owner, out var state) ? state.Brush.Snapshot : new([], null); }
    internal static CardSpec[] Project(OverlaySession session, CardSpec[] cards)
    {
        lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Brush.Project(cards) : cards;
    }
    internal static BrushSnapshot Snapshot(OverlaySession session)
    { lock (StateLock) return States.TryGetValue((session.Document.RuntimeSerialNumber, session.Owner), out var state) ? state.Brush.Snapshot : new([], null); }
    internal static void Hover(OverlaySession session, string? key, bool model = false) => Change(session, b =>
    {
        States[(session.Document.RuntimeSerialNumber, session.Owner)].ModelHover = model;
        return b.Hover(key);
    });
    internal static void ClearHover(OverlaySession session, bool model) => Change(session, b =>
        States[(session.Document.RuntimeSerialNumber, session.Owner)].ModelHover == model && b.Hover(null));
    internal static void Select(OverlaySession session, string card, string key)
    {
        var clear = key == CardMeasure.ClearKey;
        if (Change(session, b => clear ? b.Clear() : b.Toggle(key)))
            Interaction?.Invoke(new(session.Owner, card, key, clear ? "clear" : "click"));
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

}
