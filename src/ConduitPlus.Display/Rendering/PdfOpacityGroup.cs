using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Bridges Rhino's bundled PDF writer without shipping a second copy of its backend.</summary>
internal sealed class PdfOpacityGroup
{
    private readonly Assembly _backend;
    private readonly dynamic _form;
    private readonly dynamic _target;
    private readonly double _scale, _x, _y, _width, _height, _opacity;
    private const double Margin = 1;

    internal PdfOpacityGroup(object document, int page, double scale, double x, double y, Rect2 bounds, double opacity)
    {
        _backend = document.GetType().Assembly;
        _target = ((dynamic)document).Pages[page - 1].Graphics;
        _scale = scale;
        _x = x - Margin * scale;
        _y = y - Margin * scale;
        _width = (bounds.Width + 2 * Margin) * scale;
        _height = (bounds.Height + 2 * Margin) * scale;
        _opacity = opacity;
        _form = Create("PdfFormXObject", _width, _height);
        dynamic group = Create("PdfTransparencyGroup");
        group.Isolated = true;
        _form.TransparencyGroup = group;
    }

    private dynamic Create(string type, params object[] args) =>
        Activator.CreateInstance(_backend.GetType("Xfinium.Pdf.Graphics." + type, throwOnError: true)!, args)!;

    private dynamic State(double opacity)
    {
        dynamic state = Create("PdfExtendedGraphicState");
        state.FillAlpha = opacity;
        state.StrokeAlpha = opacity;
        return state;
    }

    private dynamic ColorValue(Color color) => Create("PdfRgbColor", color.R, color.G, color.B);

    internal void Path(IReadOnlyList<Point2> points, Color color, double? stroke = null)
    {
        dynamic path = Create("PdfPath");
        path.StartSubpath((points[0].X + Margin) * _scale, (points[0].Y + Margin) * _scale);
        foreach (var point in points.Skip(1))
            path.AddLineTo((point.X + Margin) * _scale, (point.Y + Margin) * _scale);
        path.CloseSubpath();
        dynamic graphics = _form.Graphics;
        graphics.SetExtendedGraphicState(State(color.A / 255d));
        if (stroke is double width) graphics.DrawPath(Create("PdfPen", ColorValue(color), width * _scale), path);
        else graphics.DrawPath(Create("PdfBrush", ColorValue(color)), path);
    }

    internal void Text(string text, Point2 position, double size, Color color, string family)
    {
        using var font = new Font(family, (float)(size * _scale), GraphicsUnit.Point);
        dynamic embedded = Create("PdfUnicodeTrueTypeFont", font, true);
        dynamic graphics = _form.Graphics;
        graphics.SetExtendedGraphicState(State(color.A / 255d));
        graphics.DrawString(text, embedded, Create("PdfBrush", ColorValue(color)),
            (position.X + Margin) * _scale, (position.Y + Margin) * _scale);
    }

    internal void Draw()
    {
        _target.SaveGraphicsState();
        try
        {
            _target.SetExtendedGraphicState(State(_opacity));
            _target.DrawFormXObject(_form, _x, _y, _width, _height);
        }
        finally { _target.RestoreGraphicsState(); }
    }
}
