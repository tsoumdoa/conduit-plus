namespace ConduitPlus.Core;

public sealed record DiagramPaletteResult(string[] Colours, ColourMessage[] Messages);

/// <summary>Stable table prefixes for diagram colours, independent of the card theme.</summary>
public static class DiagramPalette
{
    public static DiagramPaletteResult Create(string? style, int count, int shift = 0)
    {
        ColourPalette.ValidateCount(count);
        if (count == 0) return new([], []);
        var messages = new List<ColourMessage>();
        var name = string.IsNullOrWhiteSpace(style) ? "Programme" : style.Trim();
        var reversed = name.EndsWith(" reversed", StringComparison.OrdinalIgnoreCase);
        var lookup = reversed ? name[..^9].TrimEnd() : name;
        var palette = Palettes.All.FirstOrDefault(p => string.Equals(p.Name.Replace('–', '-'), lookup.Replace('–', '-'), StringComparison.OrdinalIgnoreCase));
        if (palette is null)
        {
            messages.Add(new(ColourMessageLevel.Warning, $"Unknown style '{style}'; using Programme. Right-click Style for the list."));
            palette = Palettes.Styles[0];
            reversed = false;
        }
        if (reversed) palette = palette with { Name = palette.Name + " reversed", Colours = [.. palette.Colours.Reverse()] };
        if (palette.Kind != PaletteKind.Categorical)
            messages.Add(new(ColourMessageLevel.Remark, $"{palette.Name} is a scale: sampled evenly; Shift is ignored."));
        else if (count > palette.Colours.Length)
            messages.Add(new(ColourMessageLevel.Warning, $"{palette.Name} has {palette.Colours.Length} colours; {count} were asked for, so they repeat from the {palette.Colours.Length + 1}{Ordinal(palette.Colours.Length + 1)}." +
                (palette.Colours.Length < 24 ? " Programme, Primary, Pop and Cool have 24." : "")));
        return new(palette.Take(count, shift), messages.ToArray());
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
