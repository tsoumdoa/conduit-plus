using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public sealed record OverlayInteraction(Guid Owner, string CardId, string Key, string Kind);

public static partial class OverlayManager
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
    public static void Set(RhinoDoc doc, RhinoView view, Guid owner, IEnumerable<CardSpec> cards)
    {
        UiContext.Ensure();
        // Following the active view removes this owner's previous conduit.
        RemoveWhere(k => k.Owner == owner && (k.Doc != doc.RuntimeSerialNumber || k.View != view.MainViewport.Id));
        Get(doc, view, owner).Update(cards.ToArray());
        view.Redraw();
    }
    public static void Remove(Guid owner) { UiContext.Ensure(); RemoveWhere(k => k.Owner == owner); }
    internal static OverlaySession[] InView(RhinoDoc doc, RhinoView view) => Sessions.Values.Where(s => s.Document.RuntimeSerialNumber == doc.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();

}
