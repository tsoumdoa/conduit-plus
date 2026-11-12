using System.Collections.Immutable;
using System.Globalization;

namespace ConduitPlus.Core;

public enum PaletteKind { Categorical, Sequential, Diverging, Status }
public enum ColourMessageLevel { Warning, Remark }
public sealed record ColourMessage(ColourMessageLevel Level, string Text);
public sealed record ColourPalette(string Name, PaletteKind Kind, ImmutableArray<string> Colours)
{
    public const int MaximumCount = 256;

    internal static void ValidateCount(int count)
    {
        if (count is < 0 or > MaximumCount) throw new ArgumentException("Count must be 0–256.");
    }

    /// <summary>Accepts 0–256 colours. Zero returns empty; discrete tables wrap; ordered palettes sample evenly.</summary>
    public string[] Take(int count, int shift = 0)
    {
        ValidateCount(count);
        if (count == 0) return [];
        var start = ((long)shift % Colours.Length + Colours.Length) % Colours.Length;
        return Enumerable.Range(0, count).Select(i => Kind == PaletteKind.Categorical
            ? Colours[(int)((start + i) % Colours.Length)]
            : ColourAt(count == 1 ? .5 : (double)i / (count - 1))).ToArray();
    }

    public string ColourAt(double position)
    {
        var p = Math.Clamp(position, 0, 1) * (Colours.Length - 1);
        var i = Math.Min((int)p, Colours.Length - 2);
        var a = ColorRgba.Parse(Colours[i]); var b = ColorRgba.Parse(Colours[i + 1]);
        var c = ColourGradient.Interpolate(a, b, p - i);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}

/// <summary>Theme-independent, owner-approved palettes. Interpolation is linear in sRGB bytes.</summary>
public static class Palettes
{
    public const string Missing = "#B4B7BD";
    private static ColourPalette Make(string name, PaletteKind kind, string hex) => new(name, kind, [.. hex.Split(' ')]);
    public static ImmutableArray<ColourPalette> Styles { get; } = [
        Make("Programme", PaletteKind.Categorical, "#F5C63C #3D7FC4 #E2553F #6DB36A #8750A8 #F0903A #2E4A75 #8A929B #45B6C8 #E98AB2 #A9C43E #A9714A #811F11 #B9D3F9 #FFBFB3 #636363 #7B5F00 #6D304B #C7DD81 #F48875 #4E6486 #D1D1D1 #00515C #7EAFE5"),
        Make("Primary", PaletteKind.Categorical, "#E4322B #1F56C8 #FFD500 #111111 #0F8F4A #FF8A1F #38B6E8 #9B9B9B #6B3FA0 #F29AB8 #950006 #0086B1 #4D4D4D #A5C5FF #73DF95 #49B870 #689BFF #7A7A7A #3972E7 #005571 #005D2D #E7C100 #FFAF77 #B085ED"),
        Make("Pop", PaletteKind.Categorical, "#FF2E93 #00B4F0 #FFE600 #8F4BFF #FF6A13 #2FD67B #1437E0 #FFA8D8 #00D1B2 #FF3B30 #B6F000 #008CBB #618EFF #00957F #57F396 #97DCFF #00B260 #009952 #EB201B #00A3D9 #C976A5 #FF834A #FFBBD1 #E08CBB"),
        Make("Pastel", PaletteKind.Categorical, "#FEB2C3 #ACD6FF #F7EA9B #AEF4D0 #BFAFF2 #BEDE97 #86D7E0 #FFD0AE #F2BDEE #CFCAC2 #E1AA82 #E5F2FF #A796E0 #EE95AC #E6C76E #D9A8D8"),
        Make("Earth", PaletteKind.Categorical, "#B85C38 #D2A24C #8A9A6B #5E7480 #D8C3A5 #4E342A #8C3B2A #3F5E5A #86A3B8 #C99A8E #5F6B3A #A39E93 #CADAE4 #80725F #313F47 #F5D299"),
        Make("Accent", PaletteKind.Categorical, "#E8372C #111111 #8C8C8C #D2D2D2 #4A4A4A #F3A59E #ECECEC #6B6B6B #9E1F17 #2B2B2B #C4574E #3A3A3A #5A5A5A #9E9E9E #FBD0CB #7B7B7B"),
        Make("Cool", PaletteKind.Categorical, "#1F3A6E #2F6FDE #7CC0F2 #1F9E9A #FF7A59 #9ADBC5 #2E7D5B #9A95EE #CFE6F5 #00442C #475863 #1B4FA9 #7BBB9B #6580B1 #CCCBFF #504A8F #226347 #97ACB9 #2A3A44 #518F71 #8FABDF #FFC2B0 #C1D4E1 #003080")];

    public static ImmutableArray<ColourPalette> All { get; } = [
        Make("Categorical", PaletteKind.Categorical, "#2466C3 #9A9606 #B54303 #15A2B6 #8E7DEC #93518E #E36586 #2E7A50"),
        Make("Blue", PaletteKind.Sequential, "#CFE8FE #8CC0F2 #5196DF #296CBA #184687"),
        Make("Teal", PaletteKind.Sequential, "#C8EEE8 #7BCBC6 #31A5A5 #017B80 #095258"),
        Make("Heat", PaletteKind.Sequential, "#EFE6B1 #E3B25A #D37812 #B4410D #831826"),
        Make("Blue–Red", PaletteKind.Diverging, "#0B4C92 #6D9BD5 #DFDEDA #CF7F79 #862726"),
        Make("Purple–Green", PaletteKind.Diverging, "#633481 #AB87C7 #DFDEDA #69AA77 #015E28"),
        Make("Status", PaletteKind.Status, "#209993 #EBC75E #C83846"),
        .. Styles];

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
