using System.Diagnostics;
using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

public enum RendererKind { Native, Raster, Web }

/// <summary>DisplayPipeline adapter. Coordinates are CSS pixels until this final transform.</summary>
internal sealed class RhinoCanvas(DisplayPipeline pipeline, double x, double y, double scale) : ICanvas2D
{
    private Rect2 _clip = new(0, 0, 100000, 100000);
    private static Color ColorOf(string c) => ColorTranslator.FromHtml(c);
    private PointF P(Point2 p) => new((float)(x + p.X * scale), (float)(y + p.Y * scale));
    public Size2 MeasureText(string text, double size, string font)
    {
        var rect = pipeline.Measure2dText(text, Point2d.Origin, false, 0, (int)Math.Round(size * scale), font);
        return new(rect.Width / scale, rect.Height / scale);
    }
    public void Rect(Rect2 r, string fill)
    {
        var left = Math.Max(r.X, _clip.X); var top = Math.Max(r.Y, _clip.Y);
        var right = Math.Min(r.X + r.Width, _clip.X + _clip.Width); var bottom = Math.Min(r.Y + r.Height, _clip.Y + _clip.Height);
        if (right <= left || bottom <= top) return;
        var color = ColorOf(fill);
        pipeline.Draw2dRectangle(Rectangle.FromLTRB((int)Math.Round(x + left * scale), (int)Math.Round(y + top * scale), (int)Math.Round(x + right * scale), (int)Math.Round(y + bottom * scale)), color, 0, color);
    }
    public void RoundedRect(Rect2 r, double radius, string fill)
    {
        var rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        Rect(new(r.X + rad, r.Y, r.Width - 2 * rad, r.Height), fill);
        Rect(new(r.X, r.Y + rad, r.Width, r.Height - 2 * rad), fill);
        // One-pixel horizontal strips keep the rounded silhouette in native 2D primitives.
        var step = 1 / scale;
        for (double dy = 0; dy < rad; dy += step)
        {
            var inset = rad - Math.Sqrt(Math.Max(0, rad * rad - Math.Pow(rad - dy - step / 2, 2)));
            Rect(new(r.X + inset, r.Y + dy, r.Width - 2 * inset, step), fill);
            Rect(new(r.X + inset, r.Y + r.Height - dy - step, r.Width - 2 * inset, step), fill);
        }
    }
    public void Line(Point2 a, Point2 b, string color, double width = 1)
    {
        // All pilot lines are axis-aligned; constrain them to the active card/cell clip.
        if (a.X == b.X)
        {
            if (a.X < _clip.X || a.X > _clip.X + _clip.Width) return;
            a = a with { Y = Math.Clamp(a.Y, _clip.Y, _clip.Y + _clip.Height) }; b = b with { Y = Math.Clamp(b.Y, _clip.Y, _clip.Y + _clip.Height) };
        }
        else if (a.Y == b.Y)
        {
            if (a.Y < _clip.Y || a.Y > _clip.Y + _clip.Height) return;
            a = a with { X = Math.Clamp(a.X, _clip.X, _clip.X + _clip.Width) }; b = b with { X = Math.Clamp(b.X, _clip.X, _clip.X + _clip.Width) };
        }
        else if (!_clip.Contains(a.X, a.Y) || !_clip.Contains(b.X, b.Y)) return;
        pipeline.Draw2dLine(P(a), P(b), ColorOf(color), (float)(width * scale));
    }
    public void Text(string text, Point2 p, double size, string color, string font)
    {
        if (p.X < _clip.X || p.Y < _clip.Y || p.Y + size > _clip.Y + _clip.Height) return;
        var available = _clip.X + _clip.Width - p.X;
        if (MeasureText(text, size, font).Width > available)
        {
            while (text.Length > 0 && MeasureText(text + "…", size, font).Width > available) text = text[..^1];
            text += "…";
        }
        pipeline.Draw2dText(text, ColorOf(color), new Point2d(x + p.X * scale, y + (p.Y + size) * scale), false, Math.Max(1, (int)Math.Round(size * scale)), font);
    }
    public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1)
    { for (var i = 1; i < points.Count; i++) Line(points[i - 1], points[i], color, width); }
    // Rhino 8's 2D API has no filled path: retain its outline as an explicit degraded fallback.
    public void FillPolygon(IReadOnlyList<Point2> points, string fill)
    {
        if (points.Count < 3) return;
        Polyline(points, fill); Line(points[^1], points[0], fill);
    }
    public IDisposable Clip(Rect2 r)
    {
        var old = _clip;
        var left = Math.Max(old.X, r.X); var top = Math.Max(old.Y, r.Y);
        _clip = new(left, top, Math.Max(0, Math.Min(old.X + old.Width, r.X + r.Width) - left), Math.Max(0, Math.Min(old.Y + old.Height, r.Y + r.Height) - top));
        return new Restore(() => _clip = old);
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}

internal sealed class BitmapCard(CapturedCard capture) : IDisposable
{
    public CapturedCard Capture { get; } = capture;
    public DisplayBitmap Display { get; } = new(capture.Bitmap);
    public void Dispose() { Display.Dispose(); Capture.Dispose(); }
}

internal sealed class WebCardRenderer : IDisposable
{
    private readonly WebSurface _surface = new();
    private Dictionary<string, BitmapCard> _prepared = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, BitmapCard> Prepared => _prepared;
    public uint BrowserProcessId => _surface.BrowserProcessId;
    public bool NeedsResync { get; private set; }
    public event Action? BrowserFailed;
    public WebCardRenderer() { _surface.BrowserFailed += () => { NeedsResync = true; BrowserFailed?.Invoke(); }; }
    public async Task PrepareAsync(CardSpec[] cards, Theme theme, double scale)
    {
        var dirty = cards.Where(c => NeedsResync || !_prepared.TryGetValue(c.Id, out var p) || p.Capture.Spec.Rev != c.Rev || p.Capture.Scale != scale).ToArray();
        var captures = await _surface.CaptureAsync(dirty, theme, scale);
        var replacements = new Dictionary<string, BitmapCard>();
        try { foreach (var c in captures) replacements.Add(c.Spec.Id, new BitmapCard(c)); }
        catch { foreach (var p in replacements.Values) p.Display.Dispose(); foreach (var c in captures) c.Dispose(); throw; }
        var next = new Dictionary<string, BitmapCard>(_prepared, StringComparer.Ordinal);
        foreach (var item in replacements) next[item.Key] = item.Value;
        var live = cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in next.Keys.Where(k => !live.Contains(k)).ToArray()) next.Remove(id);
        var old = _prepared;
        _prepared = next; // bitmap, spec, hit regions and revision become visible together, only on UI thread.
        NeedsResync = false;
        foreach (var p in old) if (!next.TryGetValue(p.Key, out var keep) || !ReferenceEquals(keep, p.Value)) p.Value.Dispose();
    }
    public void Dispose() { foreach (var p in _prepared.Values) p.Dispose(); _prepared.Clear(); _surface.Dispose(); }
}

internal sealed class NativeCardRenderer
{
    public void Paint(DisplayPipeline pipeline, CardSpec spec, Theme theme, CardLayout layout, double scale) => NativeCardPainter.Paint(new RhinoCanvas(pipeline, layout.X, layout.Y, scale), spec, theme);
}
