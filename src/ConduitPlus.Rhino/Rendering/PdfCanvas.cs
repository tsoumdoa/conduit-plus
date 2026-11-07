using System.Drawing;
using ConduitPlus.Core;
using Rhino.DocObjects;
using Rhino.FileIO;

namespace ConduitPlus.Rendering;

/// <summary>FilePdf takes positions/strokes in page dots, but font heights in points (72/in).</summary>
internal sealed class PdfCanvas(FilePdf pdf, int page, double x, double y, double dotsPerCssPixel) : ICanvas2D, IDisposable
{
    private readonly Bitmap _measureBitmap = new(1, 1);
    private Rect2 _clip = new(-100000, -100000, 200000, 200000);
    private PointF P(Point2 p) => new((float)(x + p.X * dotsPerCssPixel), (float)(y + p.Y * dotsPerCssPixel));
    private static Color C(string color) => GdiCanvas.ParseColor(color);
    public Size2 MeasureText(string text, double size, string font)
    {
        using var graphics = Graphics.FromImage(_measureBitmap);
        using var canvas = new GdiCanvas(graphics);
        return canvas.MeasureText(text, size, font);
    }
    public void Rect(Rect2 r, string fill) => FillPolygon([new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height), new(r.X, r.Y + r.Height)], fill);
    public void Gradient(Rect2 r, IReadOnlyList<string> colors)
    {
        const int bands = 128;
        for (var i = 0; i < bands; i++)
        {
            var stop = (i + .5) / bands * (colors.Count - 1); var index = Math.Min(colors.Count - 2, (int)stop); var fraction = stop - index;
            var a = C(colors[index]); var b = C(colors[index + 1]);
            int Mix(int from, int to) => (int)Math.Round(from + (to - from) * fraction);
            var color = new ColorRgba((byte)Mix(a.R, b.R), (byte)Mix(a.G, b.G), (byte)Mix(a.B, b.B), (byte)Mix(a.A, b.A));
            Rect(new(r.X + i * r.Width / bands, r.Y, r.Width / bands, r.Height), color.ToHex());
        }
    }
    public void RoundedRect(Rect2 r, double radius, string fill)
    {
        if (radius <= 0) { Rect(r, fill); return; }
        // FilePdf exposes polylines, not Bezier paths: approximate each quarter circle by 12 segments.
        var rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        var points = new List<Point2>();
        var centers = new[] { new Point2(r.X + rad, r.Y + rad), new Point2(r.X + r.Width - rad, r.Y + rad), new Point2(r.X + r.Width - rad, r.Y + r.Height - rad), new Point2(r.X + rad, r.Y + r.Height - rad) };
        for (var corner = 0; corner < 4; corner++)
        for (var i = 0; i <= 12; i++)
        {
            var angle = (180 + corner * 90 + i * 90d / 12) * Math.PI / 180;
            points.Add(new(centers[corner].X + rad * Math.Cos(angle), centers[corner].Y + rad * Math.Sin(angle)));
        }
        FillPolygon(points, fill);
    }
    public void FillPolygon(IReadOnlyList<Point2> points, string fill)
    {
        var clipped = points.ToList();
        // Sutherland-Hodgman clipping; FilePdf has no public save/clip/restore API.
        Cut(p => p.X - _clip.X);
        Cut(p => _clip.X + _clip.Width - p.X);
        Cut(p => p.Y - _clip.Y);
        Cut(p => _clip.Y + _clip.Height - p.Y);
        if (clipped.Count >= 3) pdf.DrawPolyline(page, clipped.Append(clipped[0]).Select(P).ToArray(), C(fill), Color.Transparent, 0);
        void Cut(Func<Point2, double> distance)
        {
            var input = clipped; clipped = [];
            if (input.Count == 0) return;
            var a = input[^1]; var da = distance(a);
            foreach (var b in input)
            {
                var db = distance(b);
                if ((da >= 0) != (db >= 0))
                {
                    var fraction = da / (da - db);
                    clipped.Add(new(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction));
                }
                if (db >= 0) clipped.Add(b);
                a = b; da = db;
            }
        }
    }
    public void Line(Point2 a, Point2 b, string color, double width = 1)
    {
        // Liang-Barsky clipping in CSS coordinates, before conversion to page dots.
        var dx = b.X - a.X; var dy = b.Y - a.Y; double low = 0, high = 1;
        if (!Edge(-dx, a.X - _clip.X) || !Edge(dx, _clip.X + _clip.Width - a.X) ||
            !Edge(-dy, a.Y - _clip.Y) || !Edge(dy, _clip.Y + _clip.Height - a.Y)) return;
        pdf.DrawLine(page, P(new(a.X + low * dx, a.Y + low * dy)), P(new(a.X + high * dx, a.Y + high * dy)), C(color), (float)(width * dotsPerCssPixel));
        bool Edge(double p, double q)
        {
            if (p == 0) return q >= 0;
            var ratio = q / p;
            if (p < 0) low = Math.Max(low, ratio); else high = Math.Min(high, ratio);
            return low <= high;
        }
    }
    public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1)
    { for (var i = 1; i < points.Count; i++) Line(points[i - 1], points[i], color, width); }
    public void Text(string text, Point2 topLeft, double size, string color, string font)
    {
        if (topLeft.X < _clip.X || topLeft.Y < _clip.Y || topLeft.Y + size * 1.2 > _clip.Y + _clip.Height) return;
        var available = _clip.X + _clip.Width - topLeft.X;
        if (MeasureText(text, size, font).Width > available)
        {
            while (text.Length > 0 && MeasureText(text + "…", size, font).Width > available) text = text[..^1];
            text = MeasureText("…", size, font).Width <= available ? text + "…" : "";
        }
        if (text.Length == 0) return;
        var p = P(topLeft);
        pdf.DrawText(page, text, p.X, p.Y, (float)(size * 72 / 96), new global::Rhino.DocObjects.Font(font), C(color), Color.Transparent, 0, 0, TextHorizontalAlignment.Left, TextVerticalAlignment.Top);
    }
    public void DrawBitmap(Bitmap bitmap, Rect2 r)
    {
        var p = P(new(r.X, r.Y));
        pdf.DrawBitmap(page, bitmap, p.X, p.Y, (float)(r.Width * dotsPerCssPixel), (float)(r.Height * dotsPerCssPixel), 0);
    }
    public IDisposable Clip(Rect2 r)
    {
        var old = _clip;
        var left = Math.Max(old.X, r.X); var top = Math.Max(old.Y, r.Y);
        _clip = new(left, top, Math.Max(0, Math.Min(old.X + old.Width, r.X + r.Width) - left), Math.Max(0, Math.Min(old.Y + old.Height, r.Y + r.Height) - top));
        return new Restore(() => _clip = old);
    }
    public void Dispose() => _measureBitmap.Dispose();
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
