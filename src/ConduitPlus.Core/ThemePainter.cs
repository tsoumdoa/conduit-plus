namespace ConduitPlus.Core;

/// <summary>Frame decoration shared by screen cards, PNG captures and vector PDF.</summary>
public static class ThemePainter
{
    public static void Shadow(ICanvas2D canvas, Size2 size, Theme theme)
    {
        if (!theme.Shadow) return;
        // Nested, low-alpha outlines soften toward the outside without putting opaque
        // shadow underneath a translucent surface. Raster adapters reserve this margin.
        for (var inset = 1; inset <= theme.ShadowExtent; inset++)
            canvas.Polyline(Outline(new(-inset, -inset, size.Width + 2 * inset, size.Height + 2 * inset), theme.Radius + inset),
                theme.ShadowColor, (theme.ShadowExtent - inset + 1) / theme.ShadowExtent);
    }
    public static void Border(ICanvas2D canvas, Size2 size, Theme theme)
    {
        if (theme.BorderWidth <= 0) return;
        var half = theme.BorderWidth / 2;
        canvas.Polyline(Outline(new(half, half, size.Width - 2 * half, size.Height - 2 * half), Math.Max(0, theme.Radius - half)), theme.Border, theme.BorderWidth);
    }
    private static Point2[] Outline(Rect2 rect, double radius)
    {
        if (radius <= 0) return [new(rect.X, rect.Y), new(rect.X + rect.Width, rect.Y), new(rect.X + rect.Width, rect.Y + rect.Height), new(rect.X, rect.Y + rect.Height), new(rect.X, rect.Y)];
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        Point2[] centers = [new(rect.X + r, rect.Y + r), new(rect.X + rect.Width - r, rect.Y + r), new(rect.X + rect.Width - r, rect.Y + rect.Height - r), new(rect.X + r, rect.Y + rect.Height - r)];
        var points = new List<Point2>();
        for (var corner = 0; corner < 4; corner++)
        for (var i = 0; i <= 12; i++)
        {
            var angle = (180 + corner * 90 + i * 90d / 12) * Math.PI / 180;
            points.Add(new(centers[corner].X + r * Math.Cos(angle), centers[corner].Y + r * Math.Sin(angle)));
        }
        return [.. points, points[0]];
    }
}
