namespace ConduitPlus.Core;

/// <summary>
/// Pure, host-neutral decisions behind the macOS adapter. Hosts read native state (NSEvent flags,
/// scroll phases, view geometry) and pass plain values here, so the policy is unit-tested on Windows.
/// Windows callers keep their original code paths; nothing here changes Windows behaviour.
/// </summary>
public readonly record struct PointerIntent(bool Primary, bool Shift, bool Toggle);

public static class PointerModifiers
{
    /// <summary>
    /// Windows: left button selects; Shift adds; Ctrl toggles (reported by Rhino).
    /// macOS: Command toggles (the platform's Ctrl equivalent). Control-click is the secondary click,
    /// so it never selects and is left to Rhino (context menu), whatever button Rhino reports.
    /// </summary>
    public static PointerIntent Resolve(bool mac, bool leftButton, bool shift, bool ctrlReported, bool command = false, bool physicalControl = false)
        => mac ? new(leftButton && !physicalControl, shift, command) : new(leftButton, shift, ctrlReported);
}

/// <summary>Native modifier flags, as host-neutral bits (NSEvent.modifierFlags masks on macOS).</summary>
[Flags]
public enum MacModifierFlags : ulong
{
    None = 0,
    Shift = 1UL << 17,
    Control = 1UL << 18,
    Option = 1UL << 19,
    Command = 1UL << 20,
}

public static class MacModifierMapping
{
    /// <summary>
    /// Map macOS flags to the WinForms-style modifier vocabulary used by the Grasshopper adapters:
    /// Command → Control (selection toggle), Option → Alt, Shift → Shift. Physical Control (secondary
    /// click) maps to <see cref="CardModifiers.Other"/> so it never counts as a selection gesture.
    /// </summary>
    public static CardModifiers ToCard(MacModifierFlags flags) =>
        ((flags & MacModifierFlags.Shift) != 0 ? CardModifiers.Shift : 0) |
        ((flags & MacModifierFlags.Command) != 0 ? CardModifiers.Control : 0) |
        ((flags & MacModifierFlags.Option) != 0 ? CardModifiers.Alt : 0) |
        ((flags & MacModifierFlags.Control) != 0 ? CardModifiers.Other : 0);
}

/// <summary>NSEventPhase bits (scroll gesture and momentum phases).</summary>
[Flags]
public enum ScrollPhase : ulong
{
    None = 0,
    Began = 1,
    Stationary = 2,
    Changed = 4,
    Ended = 8,
    Cancelled = 16,
    MayBegin = 32,
}

public static class MacScroll
{
    /// <summary>Accumulator units per table row on macOS (fine enough for trackpad pixel deltas).</summary>
    public const int UnitsPerRow = 1200;
    /// <summary>Rows per line-based (classic mouse wheel) delta unit.</summary>
    public const double RowsPerLine = 1;

    /// <summary>
    /// Convert NSEvent.scrollingDeltaY to accumulator units. Positive deltas reveal earlier rows, like a
    /// Windows wheel rotated away from the user; the system "natural scrolling" setting is already applied.
    /// Precise (trackpad / Magic Mouse) deltas are in points: one row of finger travel moves one row.
    /// </summary>
    public static int Units(double scrollingDeltaY, bool precise, double rowHeightPoints)
    {
        if (!double.IsFinite(scrollingDeltaY) || scrollingDeltaY == 0) return 0;
        var rows = precise ? scrollingDeltaY / Math.Max(1, rowHeightPoints) : scrollingDeltaY * RowsPerLine;
        return (int)Math.Clamp(Math.Round(rows * UnitsPerRow), int.MinValue / 2, int.MaxValue / 2);
    }
}

/// <summary>
/// Decides once per trackpad gesture who owns it, so a two-finger scroll (and its momentum) that starts over a
/// scrollable card never leaks into Rhino navigation, and one that starts elsewhere never jumps into a card.
/// Classic wheel events (no phases) are hit-tested individually, exactly like Windows wheel messages.
/// </summary>
public sealed class ScrollGestureLatch<T> where T : class
{
    private T? _target;
    private bool _active, _mayBegin;
    public bool Active => _active;
    public T? Target => _active ? _target : null;

    public T? Decide(ScrollPhase phase, ScrollPhase momentum, Func<T?> hit)
    {
        if (phase == ScrollPhase.None && momentum == ScrollPhase.None)
        {
            Reset();
            return hit();
        }
        if ((phase & ScrollPhase.MayBegin) != 0)
        {
            _target = hit(); _active = true; _mayBegin = true;
            return _target;
        }
        if ((phase & ScrollPhase.Began) != 0)
        {
            if (!_mayBegin || !_active) _target = hit();
            _active = true; _mayBegin = false;
            return _target;
        }
        if (!_active)
        {
            // A phase we never saw begin (e.g. monitor installed mid-gesture): leave it to Rhino.
            return null;
        }
        var result = _target;
        if ((phase & ScrollPhase.Cancelled) != 0 && momentum == ScrollPhase.None && _mayBegin) Reset();
        else if ((momentum & (ScrollPhase.Ended | ScrollPhase.Cancelled)) != 0) Reset();
        return result;
    }

    public void Reset() { _target = null; _active = false; _mayBegin = false; }
}

public static class FontFallback
{
    /// <summary>Families tried, in order, for a family macOS doesn't have (Helvetica Neue ships with every macOS).</summary>
    public static readonly string[] MacFamilies = ["Helvetica Neue", "Helvetica", "Arial"];

    /// <summary>Windows: unchanged. macOS: keep installed families, otherwise the first installed fallback.</summary>
    public static string Resolve(string family, bool mac, Func<string, bool> installed)
    {
        if (!mac) return family;
        if (!string.IsNullOrWhiteSpace(family) && !string.Equals(family, "Segoe UI", StringComparison.OrdinalIgnoreCase) && Safe(installed, family)) return family;
        foreach (var candidate in MacFamilies) if (Safe(installed, candidate)) return candidate;
        return MacFamilies[0];
    }
    private static bool Safe(Func<string, bool> installed, string family) { try { return installed(family); } catch { return false; } }
}

public static class HostScale
{
    /// <summary>
    /// macOS viewport scale: device pixels of the Rhino viewport per AppKit point of its view.
    /// When Rhino reports the viewport in backing pixels this is the Retina factor (usually 2); when it reports
    /// points it is 1. Snaps to quarter steps, clamps to [1, 4], and falls back to 1 when geometry is unusable.
    /// An explicit override (CONDUITPLUS_MAC_VIEW_SCALE) wins, for on-Mac diagnosis.
    /// </summary>
    public static double FromViewGeometry(double viewportPixels, double boundsPoints, double? overrideScale = null)
    {
        if (overrideScale is { } o && double.IsFinite(o) && o > 0) return Math.Clamp(o, .5, 8);
        if (!double.IsFinite(viewportPixels) || !double.IsFinite(boundsPoints) || viewportPixels <= 0 || boundsPoints <= 0) return 1;
        var ratio = viewportPixels / boundsPoints;
        return Math.Clamp(Math.Round(ratio * 4) / 4, 1, 4);
    }

    /// <summary>Parse a positive finite number from an environment value; null when absent or invalid.</summary>
    public static double? ParseOverride(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v > 0 ? v : null;
}

/// <summary>
/// Maps a pointer location measured natively (AppKit) into Rhino's viewport coordinates, learning the
/// constant offset between the two from MouseCallback samples. If the native mapping is exact the
/// correction stays near zero; if Rhino's viewport is inset in its view (e.g. a title band) it absorbs that.
/// </summary>
public sealed class PointerCalibration
{
    /// <summary>Offsets within this many pixels agree (rounding, sub-pixel motion between samples).</summary>
    public const double Tolerance = 2;
    private Point2? _candidate;
    public Point2 Correction { get; private set; }
    public int Samples { get; private set; }
    public double LastError { get; private set; }
    /// <summary>
    /// A sample pairs Rhino's event point with the live cursor, so one taken after a UI stall can be off. A changed
    /// offset is adopted only once two consecutive samples agree on it; the first sample is adopted directly.
    /// </summary>
    public void Observe(Point2 rhino, Point2 native)
    {
        var c = new Point2(rhino.X - native.X, rhino.Y - native.Y);
        LastError = Distance(c, Correction);
        if (Samples == 0 || LastError <= Tolerance || _candidate is { } k && Distance(c, k) <= Tolerance) { Correction = c; _candidate = null; }
        else _candidate = c;
        Samples++;
    }
    public Point2 Apply(Point2 native) => new(native.X + Correction.X, native.Y + Correction.Y);
    public void Reset() { Correction = default; Samples = 0; LastError = 0; _candidate = null; }
    private static double Distance(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}

public static class CanvasScale
{
    /// <summary>
    /// Extra raster density for a Grasshopper canvas on macOS. GH's Graphics transform gives device units per canvas
    /// unit; if that already carries the Retina factor (device scale ≈ zoom × backing) no extra density is needed,
    /// otherwise rasters are drawn at the backing factor into the same point-sized rectangle so they stay sharp.
    /// </summary>
    public static double Ratio(double deviceScale, double zoom, double backing, double? overrideRatio = null)
    {
        if (overrideRatio is { } o && double.IsFinite(o) && o > 0) return Math.Clamp(o, 1, 4);
        if (!double.IsFinite(backing) || backing <= 1) return 1;
        if (double.IsFinite(deviceScale) && double.IsFinite(zoom) && zoom > 0 && deviceScale / zoom >= backing * .9) return 1;
        return Math.Clamp(backing, 1, 4);
    }
}
