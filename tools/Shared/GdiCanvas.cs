using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

public sealed class GdiCanvas(Graphics graphics) : ICanvas2D
{
    private static readonly StringFormat Format = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
    private static Color C(string value) => ColorTranslator.FromHtml(value);
    public Size2 MeasureText(string text, double size, string font)
    {
        using var f = new Font(font, (float)size, FontStyle.Regular, GraphicsUnit.Pixel);
        var measured = graphics.MeasureString(text, f, int.MaxValue, Format); return new(measured.Width, measured.Height);
    }
    public void Rect(Rect2 r, string fill)
    { using var brush = new SolidBrush(C(fill)); graphics.FillRectangle(brush, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height); }
    public void RoundedRect(Rect2 r, double radius, string fill)
    {
        if (radius <= 0) { Rect(r, fill); return; }
        using var path = new GraphicsPath(); var d = (float)(radius * 2); var x = (float)r.X; var y = (float)r.Y; var w = (float)r.Width; var h = (float)r.Height;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90); path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90); path.CloseFigure();
        using var brush = new SolidBrush(C(fill)); graphics.FillPath(brush, path);
    }
    public void Line(Point2 a, Point2 b, string color, double width = 1)
    { using var pen = new Pen(C(color), (float)width); graphics.DrawLine(pen, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y); }
    public void Text(string text, Point2 p, double size, string color, string font)
    {
        using var f = new Font(font, (float)size, FontStyle.Regular, GraphicsUnit.Pixel); using var brush = new SolidBrush(C(color));
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; graphics.DrawString(text, f, brush, (float)p.X, (float)p.Y, Format);
    }
    public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1)
    { for (var i = 1; i < points.Count; i++) Line(points[i - 1], points[i], color, width); }
    public IDisposable Clip(Rect2 r)
    {
        var old = graphics.Save(); graphics.SetClip(new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height), CombineMode.Intersect);
        return new Restore(() => graphics.Restore(old));
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
