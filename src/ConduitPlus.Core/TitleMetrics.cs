namespace ConduitPlus.Core;

public sealed record TitleLayout(string[] Title, string[] Subtitle, string[] Description, double ContentWidth, Size2 Size);
public static class TitleMetrics
{
    /// <summary>Inset of plain title text from its slot edge, in design units.</summary>
    public const double PlainPadding = 4;
    /// <summary>Text colour of a plain Title with no Colour set.</summary>
    public const string PlainInk = "#000000";
    public sealed record InkChoice(string Ink, string? Halo);
    private static double Luminance(ColorRgba color)
    {
        static double Linear(byte channel)
        {
            var v = channel / 255d;
            return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    /// <summary>Choose the higher-contrast black or white ink using relative sRGB luminance.</summary>
    public static string InkForBackground(ColorRgba background) =>
        Luminance(background) > Math.Sqrt(.0525) - .05 ? PlainInk : "#FFFFFF";

    /// <summary>Maximise worst-case contrast over gradient corners / render background colours.
    /// Mixed light and dark backgrounds need a narrow opaque surface halo at the glyph edges.</summary>
    public static InkChoice ChooseInk(IEnumerable<ColorRgba> backgrounds, ColorRgba surface)
    {
        var luminances = backgrounds.Select(Luminance).ToArray();
        if (luminances.Length == 0) throw new ArgumentException("At least one background is required.", nameof(backgrounds));
        var black = (luminances.Min() + .05) / .05;
        var white = 1.05 / (luminances.Max() + .05);
        var ink = black == white ? InkForBackground(surface) : black > white ? PlainInk : "#FFFFFF";
        if (Math.Max(black, white) >= 3) return new(ink, null);
        var surfaceContrast = ink == PlainInk ? (Luminance(surface) + .05) / .05 : 1.05 / (Luminance(surface) + .05);
        var halo = surfaceContrast >= 3 ? surface.WithAlpha(255).ToHex() : ink == PlainInk ? "#FFFFFF" : "#12151C";
        return new(ink, halo);
    }
    public static TitleLayout Layout(CardSpec c, Theme t, Func<string, double, string, Size2> measure)
    {
        var padding = c.Background == TitleBackground.Plain ? PlainPadding : t.Padding;
        var max = c.Form.Width ?? 400;
        var width = Math.Max(0, max - 2 * padding);
        string[] Block(string text, double fontSize, string font, int count)
        {
            if (string.IsNullOrWhiteSpace(text) || count <= 0) return [];
            return TextMetrics.Layout(new CardSpec { Id = c.Id, Kind = CardKind.Text, Text = text, Width = width + 2 * padding },
                t with { Padding = padding, FontSize = fontSize, Font = font }, measure, count).Lines;
        }
        var title = Block(c.Title, 26, t.Font + " Semibold", c.Form.TitleLines ?? 2);
        var subtitle = Block(c.Subtitle, 14, t.Font, c.Form.DropSubtitle ? 0 : 2);
        var description = Block(c.Text, 12, t.Font, c.Form.DescriptionLines ?? 4);
        var widest = title.Select(s => measure(s, 26, t.Font + " Semibold").Width)
            .Concat(subtitle.Select(s => measure(s, 14, t.Font).Width))
            .Concat(description.Select(s => measure(s, 12, t.Font).Width)).DefaultIfEmpty().Max();
        var height = 2 * padding + title.Length * 32 + subtitle.Length * 20 + description.Length * 18
            + (title.Length > 0 && subtitle.Length > 0 ? 2 : 0)
            + (description.Length > 0 && title.Length + subtitle.Length > 0 ? 4 : 0);
        return new(title, subtitle, description, Math.Min(width, widest), new(Math.Min(max, widest + 2 * padding), height));
    }
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        var layout = Layout(c, t, TextMetrics.Measurer);
        var size = layout.Size;
        if (c.Background == TitleBackground.Card) ThemePainter.Shadow(canvas, size, t);
        using var clip = canvas.Clip(new(0, 0, size.Width, size.Height));
        if (c.Background == TitleBackground.Card)
        {
            canvas.RoundedRect(new(0, 0, size.Width, size.Height), t.Radius, t.Surface);
            ThemePainter.Border(canvas, size, t);
        }
        var padding = c.Background == TitleBackground.Plain ? PlainPadding : t.Padding;
        var y = padding;
        void Block(string[] lines, double fontSize, double lineHeight, string font, string color)
        {
            foreach (var line in lines)
            {
                var w = canvas.MeasureText(line, fontSize, font).Width;
                var x = c.WiredSlot switch { Slot.Tc or Slot.Bc => (size.Width - w) / 2, Slot.Tr or Slot.Br => size.Width - padding - w, _ => padding };
                using var lineClip = canvas.Clip(new(padding - 1, y - 1, layout.ContentWidth + 2, lineHeight + 2));
                if (c.Background == TitleBackground.Plain && c.Colors.Length == 0 && t.PlainTitleHalo is { } halo)
                    foreach (var offset in new Point2[] { new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-.7, -.7), new(.7, -.7), new(-.7, .7), new(.7, .7) })
                        canvas.Text(line, new(x + offset.X, y + offset.Y), fontSize, halo, font);
                canvas.Text(line, new(x, y), fontSize, color, font);
                y += lineHeight;
            }
        }
        // Explicit Colour wins; plain text follows its host background independently of the card palette.
        var ink = c.Colors.FirstOrDefault() ?? (c.Background == TitleBackground.Card ? t.Ink : t.PlainTitleInk);
        Block(layout.Title, 26, 32, t.Font + " Semibold", ink);
        if (layout.Title.Length > 0 && layout.Subtitle.Length > 0) y += 2;
        Block(layout.Subtitle, 14, 20, t.Font, ink);
        if (layout.Description.Length > 0 && layout.Title.Length + layout.Subtitle.Length > 0) y += 4;
        Block(layout.Description, 12, 18, t.Font, c.Colors.Length == 0 && c.Background == TitleBackground.Card ? t.Muted : ink);
    }
}
