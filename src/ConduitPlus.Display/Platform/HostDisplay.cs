using ConduitPlus.Core;

namespace ConduitPlus.Platform;

/// <summary>Display density for Grasshopper canvas rasters. Windows: always 1 (unchanged behaviour).</summary>
public static class HostDisplay
{
    private static readonly double? CanvasOverride = HostScale.ParseOverride(Environment.GetEnvironmentVariable("CONDUITPLUS_MAC_CANVAS_RATIO"));

    /// <summary>Backing scale factor of the window that hosts <paramref name="control"/> (macOS), else 1.</summary>
    public static double Backing(System.Windows.Forms.Control? control)
    {
        if (!HostInput.IsMac) return 1;
        try
        {
            var view = control is { IsHandleCreated: true } ? AppKit.ViewOf(control.Handle) : IntPtr.Zero;
            var window = AppKit.Window(view);
            return window != IntPtr.Zero ? AppKit.BackingScale(window) : AppKit.MainScreenBackingScale();
        }
        catch (Exception e) { MacDiagnostics.Fault("canvas backing scale", e); return 1; }
    }

    /// <summary>Extra raster density for inline canvas cards, given the Graphics device scale and canvas zoom.</summary>
    public static double CanvasRatio(System.Windows.Forms.Control? control, double deviceScale, double zoom) =>
        HostInput.IsMac ? CanvasScale.Ratio(deviceScale, zoom, Backing(control), CanvasOverride) : 1;

    /// <summary>Extra raster density for pinned (screen-space) canvas cards.</summary>
    public static double PinnedRatio(System.Windows.Forms.Control? control) =>
        HostInput.IsMac ? CanvasScale.Ratio(double.NaN, double.NaN, Backing(control), CanvasOverride) : 1;
}
