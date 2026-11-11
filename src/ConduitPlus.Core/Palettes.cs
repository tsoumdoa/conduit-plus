using System.Collections.Immutable;
using System.Globalization;

namespace ConduitPlus.Core;

public enum PaletteKind { Categorical, Sequential, Diverging, Status }
public enum ColourMessageLevel { Warning, Remark }
public sealed record ColourMessage(ColourMessageLevel Level, string Text);
public sealed record ColourPalette(string Name, PaletteKind Kind, ImmutableArray<string> Colours)
{
    public string ColourAt(double position)
    {
        var p = Math.Clamp(position, 0, 1) * (Colours.Length - 1);
        var i = Math.Min((int)p, Colours.Length - 2);
        var a = ColorRgba.Parse(Colours[i]); var b = ColorRgba.Parse(Colours[i + 1]);
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * (p - i), MidpointRounding.AwayFromZero);
        return $"#{Mix(a.R, b.R):X2}{Mix(a.G, b.G):X2}{Mix(a.B, b.B):X2}";
    }
}

/// <summary>Theme-independent, owner-approved palettes. Interpolation is linear in sRGB bytes.</summary>
public static class Palettes
{
    public const string Missing = "#B4B7BD";
    private static ColourPalette Make(string name, PaletteKind kind, string hex) => new(name, kind, [.. hex.Split(' ')]);
    public static ImmutableArray<ColourPalette> All { get; } = [
        Make("Categorical", PaletteKind.Categorical, "#2466C3 #9A9606 #B54303 #15A2B6 #8E7DEC #93518E #E36586 #2E7A50"),
        Make("Blue", PaletteKind.Sequential, "#CFE8FE #8CC0F2 #5196DF #296CBA #184687"),
        Make("Teal", PaletteKind.Sequential, "#C8EEE8 #7BCBC6 #31A5A5 #017B80 #095258"),
        Make("Heat", PaletteKind.Sequential, "#EFE6B1 #E3B25A #D37812 #B4410D #831826"),
        Make("Blue–Red", PaletteKind.Diverging, "#0B4C92 #6D9BD5 #DFDEDA #CF7F79 #862726"),
        Make("Purple–Green", PaletteKind.Diverging, "#633481 #AB87C7 #DFDEDA #69AA77 #015E28"),
        Make("Status", PaletteKind.Status, "#209993 #EBC75E #C83846")];

    public static ColourPalette Resolve(IReadOnlyList<string>? input, string fallback, ICollection<ColourMessage> messages)
    {
        if (input is null || input.Count == 0) input = [fallback];
        if (input.Count == 1 && !input[0].StartsWith('#') && !input[0].Contains(',') && !TryColour(input[0], out _))
        {
            var name = input[0].Trim();
            var reversed = name.EndsWith(" reversed", StringComparison.OrdinalIgnoreCase);
            if (reversed) name = name[..^9].TrimEnd();
            static string Normal(string s) => s.Replace('–', '-').ToUpperInvariant();
            var palette = All.FirstOrDefault(p => Normal(p.Name) == Normal(name));
            if (palette is null)
            {
                messages.Add(new(ColourMessageLevel.Warning, $"Unknown palette '{input[0]}'; using {fallback}. Right-click Palette for the list."));
                palette = All.Single(p => p.Name == fallback);
            }
            return reversed ? palette with { Name = palette.Name + " reversed", Colours = [.. palette.Colours.Reverse()] } : palette;
        }
        if (input.Count is < 2 or > 32 || input.Any(s => !TryColour(s, out _)))
            throw new ArgumentException($"Palette needs a name or 2–32 colours; got {input.Count} {(input.Count == 1 ? "colour" : "items")}.");
        return new("Custom", PaletteKind.Categorical, [.. input.Select(s => { TryColour(s, out var hex); return hex; })]);
    }

    // GH colour-to-text casts use comma-separated RGB (or ARGB); hex is also useful from panels.
    private static bool TryColour(string value, out string hex)
    {
        hex = "";
        if (value.StartsWith('#'))
        {
            try { var c = ColorRgba.Parse(value); hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; return true; }
            catch (FormatException) { return false; }
        }
        var alpha = value.IndexOf(" (", StringComparison.Ordinal);
        if (alpha >= 0) value = value[..alpha];
        var parts = value.Split(',');
        var bytes = parts.Select(p => byte.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? (int)b : -1).ToArray();
        if (bytes.Length is not (3 or 4) || bytes.Any(b => b < 0)) return false;
        hex = $"#{bytes[^3]:X2}{bytes[^2]:X2}{bytes[^1]:X2}";
        return true;
    }
}
