using ConduitPlus.Core;
using Rhino.Display;

namespace ConduitPlus.Platform;

/// <summary>
/// RhinoView geometry on macOS. Screen positions are AppKit global points (bottom-left origin) carried in a
/// System.Drawing.Point at quarter-point precision; they are opaque outside this class and HostView.
/// Viewport coordinates are Rhino's (MainViewport.Size units, top-left origin), derived from the view's
/// NSView bounds and then corrected by the offset learnt from Rhino's own MouseCallback points.
/// </summary>
internal static class MacView
{
    private const double Precision = 4;
    private static readonly Dictionary<uint, PointerCalibration> Calibrations = [];
    internal static double? ScaleOverride { get; } = HostScale.ParseOverride(Environment.GetEnvironmentVariable("CONDUITPLUS_MAC_VIEW_SCALE"));

    internal static System.Drawing.Point Encode(ObjC.CGPoint global) => new((int)Math.Round(global.X * Precision), (int)Math.Round(global.Y * Precision));
    internal static ObjC.CGPoint Decode(System.Drawing.Point screen) => new(screen.X / Precision, screen.Y / Precision);
    internal static System.Drawing.Point CurrentScreen() => Encode(AppKit.MouseLocation);

    internal static IntPtr NsView(RhinoView view) => AppKit.ViewOf(view.Handle);

    internal static double Scale(RhinoView view)
    {
        if (ScaleOverride is { } o) return HostScale.FromViewGeometry(0, 0, o);
        try
        {
            var ns = NsView(view);
            if (ns == IntPtr.Zero) return 1;
            return HostScale.FromViewGeometry(view.MainViewport.Size.Width, AppKit.Bounds(ns).Size.Width);
        }
        catch (Exception e) { MacDiagnostics.Fault("view scale", e); return 1; }
    }

    /// <summary>Native (uncalibrated) viewport point for a window-base point of the view's window.</summary>
    internal static Point2? FromWindow(RhinoView view, IntPtr ns, ObjC.CGPoint windowPoint)
    {
        var bounds = AppKit.Bounds(ns);
        if (bounds.Size.Width <= 0 || bounds.Size.Height <= 0) return null;
        var p = AppKit.WindowToView(ns, windowPoint);
        var x = p.X - bounds.Origin.X;
        var y = AppKit.IsFlipped(ns) ? p.Y - bounds.Origin.Y : bounds.Size.Height - (p.Y - bounds.Origin.Y);
        var size = view.MainViewport.Size;
        return new(x * size.Width / bounds.Size.Width, y * size.Height / bounds.Size.Height);
    }

    internal static Point2? Native(RhinoView view, System.Drawing.Point screen)
    {
        var ns = NsView(view); var window = AppKit.Window(ns);
        if (window == IntPtr.Zero) return null;
        return FromWindow(view, ns, AppKit.ScreenToWindow(window, Decode(screen)));
    }

    internal static PointerCalibration Calibration(RhinoView view)
    {
        if (!Calibrations.TryGetValue(view.RuntimeSerialNumber, out var c)) Calibrations[view.RuntimeSerialNumber] = c = new();
        return c;
    }
    internal static void Forget(RhinoView view) { Calibrations.Remove(view.RuntimeSerialNumber); AppKit.Forget(view.Handle); }

    /// <summary>Learn the native→Rhino offset from a MouseCallback point (main thread).</summary>
    internal static void Observe(RhinoView view, System.Drawing.Point rhinoPoint)
    {
        try
        {
            MacDiagnostics.MouseCallbackMoves++;
            if (Native(view, CurrentScreen()) is { } native) Calibration(view).Observe(new(rhinoPoint.X, rhinoPoint.Y), native);
        }
        catch (Exception e) { MacDiagnostics.Fault("pointer calibration", e); }
    }

    /// <summary>Calibrated viewport point, or null when AppKit can't map it (callers keep Rhino's own point).</summary>
    internal static System.Drawing.Point? ToClient(RhinoView view, System.Drawing.Point screen)
    {
        try
        {
            if (Native(view, screen) is not { } native) return null;
            var p = Calibration(view).Apply(native);
            return new((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }
        catch (Exception e) { MacDiagnostics.Fault("screen to client", e); return null; }
    }

    /// <summary>Topmost-window and view hit test: the AppKit equivalent of WindowFromPoint + IsChild.</summary>
    internal static bool InView(RhinoView view, System.Drawing.Point screen)
    {
        try
        {
            var ns = NsView(view);
            if (ns == IntPtr.Zero) return true; // Unknown handle kind: rely on Rhino's MouseCallback stream.
            var window = AppKit.Window(ns);
            if (window == IntPtr.Zero) return false;
            var global = Decode(screen);
            if (AppKit.WindowNumberAt(global) != AppKit.WindowNumber(window)) return false;
            var windowPoint = AppKit.ScreenToWindow(window, global);
            var local = AppKit.WindowToView(ns, windowPoint); var bounds = AppKit.Bounds(ns);
            if (local.X < bounds.Origin.X || local.Y < bounds.Origin.Y || local.X >= bounds.Origin.X + bounds.Size.Width || local.Y >= bounds.Origin.Y + bounds.Size.Height) return false;
            var hit = AppKit.HitTest(window, windowPoint);
            return hit == ns || AppKit.IsDescendant(hit, ns);
        }
        catch (Exception e) { MacDiagnostics.Fault("pointer in view", e); return false; }
    }
}

/// <summary>Platform dispatch for viewport pointer geometry. Windows paths are the original calls.</summary>
internal static class HostView
{
    internal static System.Drawing.Point PointerScreen => HostInput.IsMac ? MacView.CurrentScreen() : WinPointer();
    private static System.Drawing.Point WinPointer() => System.Windows.Forms.Control.MousePosition;
    internal static System.Drawing.Point? ToClient(RhinoView view, System.Drawing.Point screen) => HostInput.IsMac ? MacView.ToClient(view, screen) : view.ScreenToClient(screen);
}
