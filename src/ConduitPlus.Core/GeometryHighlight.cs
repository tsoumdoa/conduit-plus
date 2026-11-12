namespace ConduitPlus.Core;

/// <summary>Screen-space geometry highlight policy; the host supplies geometry and the theme accent.</summary>
public sealed record GeometryHighlight(int CasingWidth, int AccentWidth, double WhiteWashOpacity)
{
    public static string Casing(string accent) => TitleMetrics.InkForBackground(ColorRgba.Parse(accent));
    public static GeometryHighlight Selected { get; } = new(7, 3, .2);
    public static GeometryHighlight Hover { get; } = new(4, 2, 0);
    public static GeometryHighlight? Resolve(bool selected, bool hovered) => selected ? Selected : hovered ? Hover : null;
}
