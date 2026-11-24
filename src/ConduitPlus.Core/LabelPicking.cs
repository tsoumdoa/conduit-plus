namespace ConduitPlus.Core;

/// <summary>Pointer geometry for a completed label frame, in CSS pixels.</summary>
public static class LabelPicking
{
    public static LabelPlacement? Hit(IReadOnlyList<LabelPlacement> frame, Point2 point,
        bool current, bool hudBlocked, Func<LabelIdentity, int>? dotPaintOrder = null, double dpi = 1)
    {
        if (!current || hudBlocked) return null;
        LabelPlacement? nearest = null;
        LabelPlacement? pill = null;
        var distance = 25d;
        for (var i = frame.Count - 1; i >= 0; i--)
        {
            var label = frame[i];
            if (pill is null && label.Form != LabelForm.Dot && PaintedPill(label.Pill, dpi).Contains(point.X, point.Y))
                pill = label;
            var dx = point.X - label.Dot.X - label.Dot.Width / 2;
            var dy = point.Y - label.Dot.Y - label.Dot.Height / 2;
            var squared = dx * dx + dy * dy;
            if (squared > distance) continue;
            if (squared == distance && nearest is { } tied &&
                (dotPaintOrder is null || dotPaintOrder(label.Identity) <= dotPaintOrder(tied.Identity))) continue;
            nearest = label;
            distance = squared;
        }
        return nearest ?? pill;
    }

    public static Rect2 PaintedPill(Rect2 pill, double dpi) =>
        new(Math.Round(pill.X * dpi) / dpi, Math.Round(pill.Y * dpi) / dpi,
            Math.Ceiling(pill.Width * dpi) / dpi, Math.Ceiling(pill.Height * dpi) / dpi);

    public static bool Retains(Rect2 dot, Rect2 pill, Point2 point) =>
        Near(dot, point) || Near(pill, point);

    private static bool Near(Rect2 rect, Point2 point) =>
        new Rect2(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6).Contains(point.X, point.Y);

    public static (Rect2 Pill, bool Left) Popover(Rect2 dot, double width, Size2 viewport, bool left)
    {
        Rect2 OnSide(bool onLeft) => new(onLeft ? dot.X - 4 - width : dot.X + dot.Width + 4,
            Math.Clamp(dot.Y + dot.Height / 2 - 9, 0, Math.Max(0, viewport.Height - 18)), width, 18);
        var pill = OnSide(left);
        if (pill.X < 0 || pill.X + width > viewport.Width)
        {
            var other = OnSide(!left);
            if (other.X >= 0 && other.X + width <= viewport.Width) return (other, !left);
            pill = pill with { X = Math.Clamp(pill.X, 0, Math.Max(0, viewport.Width - width)) };
        }
        return (pill, left);
    }
}
