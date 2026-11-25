using System.Globalization;

namespace ConduitPlus.Core;

/// <summary>Canvas colors use CSS hex order: #RRGGBB or #RRGGBBAA (straight alpha).</summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static ColorRgba Parse(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
            throw new FormatException("Colors must be #RRGGBB or #RRGGBBAA.");
        byte Part(int offset) => byte.Parse(value.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(Part(1), Part(3), Part(5), value.Length == 9 ? Part(7) : (byte)255);
    }
    public ColorRgba WithAlpha(byte alpha) => this with { A = alpha };
    /// <summary>
    /// This colour at <paramref name="alpha"/> composited over <paramref name="background"/>'s RGB, keeping the
    /// background's alpha. Lets translucent tints on a card surface paint identically on canvases without alpha (PDF).
    /// </summary>
    public ColorRgba Over(ColorRgba background, byte alpha)
    {
        byte Mix(byte fg, byte bg) => (byte)Math.Round((fg * alpha + bg * (255 - alpha)) / 255.0);
        return new(Mix(R, background.R), Mix(G, background.G), Mix(B, background.B), background.A);
    }
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
