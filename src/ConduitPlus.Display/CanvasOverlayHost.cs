using System.Reflection;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Publications and fail-safe boundary; no excluded GH widget types escape the adapter assembly.</summary>
public interface ICanvasOverlayDisplay
{
    Guid OwnerId { get; }
    object? Document { get; }
    CanvasCardState State { get; }
    Theme Theme { get; }
    bool Disabled { get; }
    bool CardsVisible { get; }
    bool BrushEnabled { get; }
    void SetNotice(string kind, string? message, bool error = false);
}
public static class CanvasOverlayHost
{
    private static readonly HashSet<ICanvasOverlayDisplay> Publications = [];
    public static IReadOnlyCollection<ICanvasOverlayDisplay> Displays => Publications.ToArray();
    public static event Action<bool>? Changed;
    public static event Action? ShowRequested;
    private static IDisposable? _adapter;
    private static bool _started;
    public static string? Error { get; private set; }
    public static void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            var assembly = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(typeof(CanvasOverlayHost).Assembly.Location)!, "ConduitPlus.CanvasWidgets.dll"));
            _adapter = (IDisposable)assembly.GetType("ConduitPlus.CanvasWidgets.WidgetService", true)!.GetMethod("Start")!.Invoke(null, null)!;
            AppDomain.CurrentDomain.ProcessExit += Shutdown;
            global::Rhino.RhinoApp.Closing += Shutdown;
        }
        catch (Exception e) { Fail("Canvas Overlay widget adapter unavailable: " + (e.InnerException ?? e).Message); }
    }
    public static void Publish(ICanvasOverlayDisplay display)
    {
        Publications.Add(display);
        if (Error is { } error) display.SetNotice("adapter", error, true);
        Invalidate(true);
    }
    public static void Remove(ICanvasOverlayDisplay display) { Publications.Remove(display); Invalidate(true); }
    public static void Invalidate(bool cancelGesture = false) => Changed?.Invoke(cancelGesture);
    public static void Show() => ShowRequested?.Invoke();
    public static void Fail(string message)
    {
        Error ??= message;
        foreach (var display in Displays) display.SetNotice("adapter", Error, true);
    }
    private static void Shutdown(object? sender, EventArgs e)
    {
        AppDomain.CurrentDomain.ProcessExit -= Shutdown; global::Rhino.RhinoApp.Closing -= Shutdown;
        _adapter?.Dispose(); _adapter = null;
    }
}
