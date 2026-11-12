using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ConduitPlus.Grasshopper;

/// <summary>Component artwork: one white glyph on the dark rounded tile shared with Panel+ and HopperFormat.</summary>
public enum ComponentIcon { Metric, Bar, Line, Title, Text, Legend, KeyLegend, Table, Overlay, CanvasCard, Link, Scope, Rooms, Massing, Truss, ColourScale, ColourCategories }

/// <summary>Draws the icons in a 24-unit grid (the plug-in logo in a 64-unit grid), so they stay crisp at any size.
/// Only System.Drawing is used: tools/Generate-Icons.ps1 compiles this file to regenerate assets/icon.png.</summary>
public static class ConduitIcons
{
    private static readonly Color Tile = Color.FromArgb(24, 30, 26);
    private static readonly Color Ink = Color.White;
    private static readonly Color Soft = Color.FromArgb(128, 255, 255, 255);
    private const float W = 1.75f;

    public static Bitmap Create(ComponentIcon icon, int size = 24) => Render(size, 24, 5.25f, g => Glyph(g, icon));

    /// <summary>The C+ plug-in mark, drawn like Panel+'s P+.</summary>
    public static Bitmap Logo(int size = 64) => Render(size, 64, 14, g =>
    {
        using (var pen = new Pen(Ink, 7)) g.DrawArc(pen, 25.55f - 11.5f, 32 - 11.5f, 23, 23, 46.9f, 266.2f);
        using var brush = new SolidBrush(Ink);
        g.FillPolygon(brush, Points(44, 25, 50, 25, 50, 30, 55, 30, 55, 36, 50, 36, 50, 41, 44, 41, 44, 36, 39, 36, 39, 30, 44, 30));
    });

    private static Bitmap Render(int size, float unit, float corner, Action<Graphics> draw)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / unit, size / unit);
        using (var tile = Rounded(0, 0, unit, unit, corner)) using (var brush = new SolidBrush(Tile)) g.FillPath(brush, tile);
        draw(g);
        return bitmap;
    }

    private static void Glyph(Graphics g, ComponentIcon icon)
    {
        switch (icon)
        {
            case ComponentIcon.Metric: // A big value under a short label.
                Line(g, Soft, W * 0.75f, 6, 5.5f, 11, 5.5f);
                Lines(g, Ink, W * 1.1f, 10, 8.5f, 5.5f, 15, 11.5f, 15);
                Line(g, Ink, W * 1.1f, 10, 11.5f, 10, 18.5f);
                using (var two = new GraphicsPath())
                {
                    two.AddBezier(13.5f, 11, 13.5f, 7.8f, 18.5f, 7.8f, 18.5f, 11);
                    two.AddBezier(18.5f, 11, 18.5f, 13.5f, 13.5f, 15, 13.5f, 18.5f);
                    two.AddLine(13.5f, 18.5f, 18.5f, 18.5f);
                    using var pen = Round(Ink, W * 1.1f); g.DrawPath(pen, two);
                }
                break;
            case ComponentIcon.Bar:
                Line(g, Ink, W * 0.8f, 5, 4.5f, 5, 19.5f);
                Bar(g, Ink, 5.5f, 19, 7.5f); Bar(g, Ink, 5.5f, 14.5f, 12); Bar(g, Soft, 5.5f, 11, 16.5f);
                break;
            case ComponentIcon.Line:
                Lines(g, Ink, W * 0.8f, 4.5f, 4.5f, 4.5f, 19.5f, 19.5f, 19.5f);
                Lines(g, Ink, W, 7.5f, 15.5f, 10.5f, 11, 13.5f, 13.5f, 18, 7);
                Dot(g, Ink, 18, 7, W * 0.9f);
                break;
            case ComponentIcon.Title:
                Line(g, Ink, W * 1.6f, 5, 7, 19, 7);
                Line(g, Ink, W, 5, 11.5f, 14, 11.5f);
                Line(g, Soft, W * 0.7f, 5, 15.5f, 19, 15.5f);
                Line(g, Soft, W * 0.7f, 5, 18.5f, 15, 18.5f);
                break;
            case ComponentIcon.Text: // A note with a folded corner.
                using (var pen = Round(Ink, W * 0.85f))
                {
                    g.DrawPolygon(pen, Points(6, 4.5f, 14, 4.5f, 18, 8.5f, 18, 19.5f, 6, 19.5f));
                    g.DrawLines(pen, Points(14, 4.5f, 14, 8.5f, 18, 8.5f));
                }
                Line(g, Ink, W * 0.75f, 9, 11.5f, 15, 11.5f); Line(g, Ink, W * 0.75f, 9, 14.5f, 15, 14.5f); Line(g, Ink, W * 0.75f, 9, 17.5f, 12.5f, 17.5f);
                break;
            case ComponentIcon.Legend: // A gradient scale with ticks.
                using (var scale = Rounded(5.5f, 4.5f, 4, 15, 1.2f))
                using (var gradient = new LinearGradientBrush(new RectangleF(5.5f, 4.4f, 4, 15.2f), Ink, Soft, 90f))
                    g.FillPath(gradient, scale);
                Line(g, Ink, W * 0.75f, 12.5f, 5.5f, 19, 5.5f); Line(g, Ink, W * 0.75f, 12.5f, 12, 17, 12); Line(g, Ink, W * 0.75f, 12.5f, 18.5f, 19, 18.5f);
                break;
            case ComponentIcon.ColourScale: // Horizontal ramp and its middle tick.
                using (var ramp = Rounded(4.5f, 7, 15, 6, 1.3f))
                using (var gradient = new LinearGradientBrush(new RectangleF(4.4f, 7, 15.2f, 6), Soft, Ink, 0f))
                    g.FillPath(gradient, ramp);
                Line(g, Soft, W * 0.7f, 5, 16.5f, 19, 16.5f);
                Line(g, Ink, W * 0.85f, 12, 15, 12, 19.5f);
                break;
            case ComponentIcon.ColourCategories: // Three distinct swatches, no implied order.
                Fill(g, Ink, 4.5f, 6.5f, 6, 6, 1.1f);
                Fill(g, Soft, 13.5f, 6.5f, 6, 6, 1.1f);
                Stroke(g, Ink, W * 0.85f, 9, 15, 6, 5, 1.1f);
                break;
            case ComponentIcon.KeyLegend: // Swatches with labels.
                Fill(g, Ink, 5, 5, 4.2f, 4.2f, 1); Fill(g, Ink, 5, 10, 4.2f, 4.2f, 1); Fill(g, Soft, 5, 15, 4.2f, 4.2f, 1);
                Line(g, Ink, W * 0.75f, 12, 7.1f, 19, 7.1f); Line(g, Ink, W * 0.75f, 12, 12.1f, 17, 12.1f); Line(g, Ink, W * 0.75f, 12, 17.1f, 18, 17.1f);
                break;
            case ComponentIcon.Table:
                using (var header = new GraphicsPath())
                {
                    header.AddArc(4.5f, 5.5f, 3, 3, 180, 90); header.AddArc(16.5f, 5.5f, 3, 3, 270, 90);
                    header.AddLine(19.5f, 9.5f, 4.5f, 9.5f); header.CloseFigure();
                    using var brush = new SolidBrush(Ink); g.FillPath(brush, header);
                }
                Stroke(g, Ink, W * 0.8f, 4.5f, 5.5f, 15, 13, 1.5f);
                Line(g, Ink, W * 0.7f, 4.5f, 14, 19.5f, 14); Line(g, Ink, W * 0.7f, 10.5f, 9.5f, 10.5f, 18.5f);
                break;
            case ComponentIcon.Overlay: // Cards in the corners of a viewport.
                Stroke(g, Ink, W * 0.85f, 3.5f, 5, 17, 14, 1.8f);
                Fill(g, Ink, 6, 7.5f, 6, 4.2f, 0.9f);
                Fill(g, Soft, 13.5f, 13.3f, 4.5f, 3.4f, 0.8f);
                Lines(g, Color.FromArgb(153, Ink), W * 0.6f, 8, 16.5f, 10, 15.2f, 12, 16.5f);
                break;
            case ComponentIcon.CanvasCard: // A card with wire grips, as a node on the canvas.
                Stroke(g, Ink, W * 0.85f, 7, 5.5f, 10, 13, 2);
                Line(g, Ink, W * 0.85f, 2.8f, 12, 5.2f, 12); Line(g, Ink, W * 0.85f, 18.8f, 12, 21.2f, 12);
                Dot(g, Ink, 7, 12, 1.7f); Dot(g, Ink, 17, 12, 1.7f);
                Line(g, Ink, W * 0.8f, 10, 9.5f, 14, 9.5f); Line(g, Ink, W * 0.8f, 10, 12.5f, 13, 12.5f);
                Line(g, Soft, W * 0.6f, 10, 15.5f, 14, 15.5f);
                break;
            case ComponentIcon.Link: // Two chain links.
                var state = g.Save();
                using (var turn = new Matrix()) { turn.RotateAt(-45, new PointF(12, 12)); g.MultiplyTransform(turn); }
                Stroke(g, Ink, W, 3.2f, 8.8f, 10.2f, 6.4f, 3.2f);
                Stroke(g, Ink, W, 10.6f, 8.8f, 10.2f, 6.4f, 3.2f);
                g.Restore(state);
                break;
            case ComponentIcon.Scope:
                using (var pen = Round(Ink, W * 0.85f))
                {
                    g.DrawEllipse(pen, 6, 6, 12, 12);
                    g.DrawLine(pen, 12, 3.5f, 12, 7.5f); g.DrawLine(pen, 12, 16.5f, 12, 20.5f);
                    g.DrawLine(pen, 3.5f, 12, 7.5f, 12); g.DrawLine(pen, 16.5f, 12, 20.5f, 12);
                }
                Dot(g, Ink, 12, 12, 1.9f);
                break;
            case ComponentIcon.Rooms: // A floor plan with one room filled.
                Fill(g, Color.FromArgb(230, Ink), 5.6f, 5.6f, 5.4f, 5.4f, 0.4f);
                Stroke(g, Ink, W * 0.85f, 4.5f, 4.5f, 15, 15, 0.8f);
                using (var pen = Round(Ink, W * 0.85f))
                {
                    g.DrawLine(pen, 12, 4.5f, 12, 9.5f); g.DrawLine(pen, 12, 13, 12, 19.5f);
                    g.DrawLine(pen, 12, 12.5f, 19.5f, 12.5f); g.DrawLine(pen, 4.5f, 12, 8, 12);
                }
                break;
            case ComponentIcon.Massing: // A block stacked on a podium.
                using (var brush = new SolidBrush(Ink)) g.FillPolygon(brush, Points(9, 7.5f, 12, 6, 15, 7.5f, 15, 12, 12, 13.5f, 9, 12));
                Lines(g, Tile, W * 0.6f, 9, 7.5f, 12, 9, 15, 7.5f); Line(g, Tile, W * 0.6f, 12, 9, 12, 13.5f);
                using (var pen = Round(Ink, W * 0.75f))
                {
                    g.DrawPolygon(pen, Points(6, 12, 12, 9, 18, 12, 12, 15));
                    g.DrawLines(pen, Points(6, 12, 6, 17, 12, 20, 18, 17, 18, 12));
                    g.DrawLine(pen, 12, 15, 12, 20);
                }
                break;
            case ComponentIcon.Truss:
                Line(g, Ink, W, 3.5f, 17, 20.5f, 17); Line(g, Ink, W, 7, 8, 17, 8);
                Lines(g, Ink, W * 0.7f, 3.5f, 17, 7, 8, 12, 17, 17, 8, 20.5f, 17);
                Dot(g, Ink, 7, 8, 1.4f); Dot(g, Ink, 12, 17, 1.4f); Dot(g, Ink, 17, 8, 1.4f);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(icon), icon, null);
        }
    }

    private static Pen Round(Color color, float width) =>
        new(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    private static void Line(Graphics g, Color color, float width, float x1, float y1, float x2, float y2)
    { using var pen = Round(color, width); g.DrawLine(pen, x1, y1, x2, y2); }

    private static void Lines(Graphics g, Color color, float width, params float[] xy)
    { using var pen = Round(color, width); g.DrawLines(pen, Points(xy)); }

    /// <summary>A flat-ended horizontal bar, as in the Bar card.</summary>
    private static void Bar(Graphics g, Color color, float x1, float x2, float y)
    { using var pen = new Pen(color, W * 1.7f); g.DrawLine(pen, x1, y, x2, y); }

    private static void Dot(Graphics g, Color color, float cx, float cy, float r)
    { using var brush = new SolidBrush(color); g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r); }

    private static void Fill(Graphics g, Color color, float x, float y, float w, float h, float r)
    { using var path = Rounded(x, y, w, h, r); using var brush = new SolidBrush(color); g.FillPath(brush, path); }

    private static void Stroke(Graphics g, Color color, float width, float x, float y, float w, float h, float r)
    { using var path = Rounded(x, y, w, h, r); using var pen = Round(color, width); g.DrawPath(pen, path); }

    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var d = 2 * r; var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    private static PointF[] Points(params float[] xy)
    {
        var points = new PointF[xy.Length / 2];
        for (var i = 0; i < points.Length; i++) points[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
        return points;
    }
}
