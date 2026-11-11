using System.Globalization;

namespace ConduitPlus.Core;

public sealed record ColourScaleResult(string[] Colours, CardSpec Legend, ColourMessage[] Messages,
    ColourPalette Palette, double Min, double Max, double Mid)
{
    public string ColourAt(double? value)
    {
        if (value is not { } v || !double.IsFinite(v)) return Palettes.Missing;
        if (v <= Min) return Palette.Colours[0];
        if (v >= Max) return Palette.Colours[^1];
        return Palette.ColourAt(v <= Mid ? .5 * (v - Min) / (Mid - Min) : .5 + .5 * (v - Mid) / (Max - Mid));
    }
}

public static class ColourScale
{
    public static ColourScaleResult Create(IReadOnlyList<double?> values, IReadOnlyList<string>? palette = null,
        double? min = null, double? max = null, double? mid = null, string title = "Legend", string unit = "",
        string id = "colour-scale", Slot? slot = null)
    {
        var messages = new List<ColourMessage>();
        var scheme = Palettes.Resolve(palette, "Blue", messages);
        var finite = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).ToArray();
        if (finite.Length == 0 && (!min.HasValue || !max.HasValue))
            throw new ArgumentException("Colour Scale needs numbers in Values, or both Min and Max.");
        if (min is null || max is null)
        {
            double[] ticks;
            try { ticks = ChartTicks.Nice(finite.Min(), finite.Max()); }
            catch (Exception e) when (e is ArgumentException or OverflowException)
            { throw new ArgumentException("Values need a finite, representable scale range; set Min and Max."); }
            min ??= ticks[0]; max ??= ticks[^1];
        }
        static string F(double? v) => v?.ToString("G", CultureInfo.InvariantCulture) ?? "";
        if (!double.IsFinite(min.Value) || !double.IsFinite(max.Value) || !double.IsFinite(max.Value - min.Value))
            throw new ArgumentException("Min and Max must be finite and have a finite span.");
        if (min >= max) throw new ArgumentException($"Min ({F(min)}) must be less than Max ({F(max)}).");
        mid ??= scheme.Kind is PaletteKind.Diverging or PaletteKind.Status && min < 0 && max > 0 ? 0 : min.Value + (max.Value - min.Value) / 2;
        if (!double.IsFinite(mid.Value) || mid <= min || mid >= max)
            throw new ArgumentException($"Mid ({F(mid)}) must be between Min ({F(min)}) and Max ({F(max)}).");
        var legend = new CardSpec { Id = id, Kind = CardKind.Legend, Title = title, Unit = unit, Min = min, Max = max, WiredSlot = slot };
        var result = new ColourScaleResult([], legend, [], scheme, min.Value, max.Value, mid.Value);
        legend = legend with { Colors = Enumerable.Range(0, 17).Select(i => result.ColourAt(min.Value + (max.Value - min.Value) * i / 16)).ToArray() };
        legend.Validate();
        var missing = values.Count - finite.Length;
        if (missing > 0) messages.Add(new(ColourMessageLevel.Remark, $"{missing} missing values shown in grey ({Palettes.Missing})."));
        var outside = finite.Count(v => v < min || v > max);
        if (outside > 0) messages.Add(new(ColourMessageLevel.Remark, $"{outside} of {values.Count} values are outside {F(min)}–{F(max)} and use the end colours."));
        return result with { Colours = values.Select(result.ColourAt).ToArray(), Legend = legend, Messages = messages.ToArray() };
    }
}
