using System.Globalization;

namespace ConduitPlus.Core;

/// <summary>Finite-domain arithmetic shared by mapping, sampling and axis placement.</summary>
public static class ColourRange
{
    public static double Lerp(double min, double max, double t) => t <= 0 ? min : t >= 1 ? max : min * (1 - t) + max * t;
    public static double Fraction(double value, double min, double max) => double.IsFinite(max - min)
        ? (value - min) / (max - min) : (value / 2 - min / 2) / (max / 2 - min / 2);
}

public sealed record ColourScaleResult(string[] Colours, CardSpec Legend, ColourMessage[] Messages,
    ColourPalette Palette, double Min, double Max, double Mid)
{
    public double PalettePosition(double position)
    {
        if (Min == Max) return .5;
        var pivot = ColourRange.Fraction(Mid, Min, Max);
        // A subnormal range may have no representable interior midpoint.
        return pivot is <= 0 or >= 1 ? position : position <= pivot ? .5 * position / pivot : .5 + .5 * (position - pivot) / (1 - pivot);
    }
    public string ColourAt(double? value)
    {
        if (value is not { } v || !double.IsFinite(v)) return Palettes.Missing;
        return Palette.ColourAt(PalettePosition(Min == Max ? .5 : v <= Min ? 0 : v >= Max ? 1 : ColourRange.Fraction(v, Min, Max)));
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
        if (scheme.Kind == PaletteKind.Categorical && scheme.Name != "Custom")
        {
            messages.Add(new(ColourMessageLevel.Warning, $"'{scheme.Name}' is a category palette; using Blue. Choose a sequential, diverging or Status palette."));
            scheme = Palettes.Resolve(["Blue"], "Blue", []);
        }
        var finite = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).ToArray();
        if (finite.Length == 0 && (!min.HasValue || !max.HasValue))
            throw new ArgumentException("Connect Values (or set Min and Max).");
        static string F(double? v) => v?.ToString("G6", CultureInfo.InvariantCulture) ?? "";
        if (min.HasValue && !double.IsFinite(min.Value) || max.HasValue && !double.IsFinite(max.Value))
            throw new ArgumentException("Min and Max must be finite.");
        if (mid.HasValue && !double.IsFinite(mid.Value)) throw new ArgumentException("Mid must be finite.");
        if (min.HasValue && max.HasValue && min >= max)
            throw new ArgumentException($"Min ({F(min)}) must be less than Max ({F(max)}).");
        if (mid.HasValue && min.HasValue && mid <= min)
            throw new ArgumentException($"Mid ({F(mid)}) must be greater than Min ({F(min)}).");
        if (mid.HasValue && max.HasValue && mid >= max)
            throw new ArgumentException($"Mid ({F(mid)}) must be less than Max ({F(max)}).");
        var constant = min is null && max is null && mid is null && finite.Min() == finite.Max();
        if (constant) { min = finite[0]; max = finite[0]; }
        else if (min is null || max is null)
        {
            // Explicit controls participate in automatic bounds; only explicit contradictions error.
            var extent = finite.Concat(new[] { min, max, mid }.Where(v => v.HasValue).Select(v => v!.Value)).ToArray();
            var ticks = ChartTicks.Nice(extent.Min(), extent.Max());
            var lo = ticks[0]; var hi = ticks[^1];
            var step = ticks.Length > 1 ? ticks[1] - ticks[0] : 1;
            if (mid is { } m)
            {
                if (min is null && lo >= m) lo = double.IsFinite(lo - step) && lo - step < m ? lo - step : Math.BitDecrement(m);
                if (max is null && hi <= m) hi = double.IsFinite(hi + step) && hi + step > m ? hi + step : Math.BitIncrement(m);
            }
            if (min is { } lower && hi <= lower) hi = double.IsFinite(hi + step) && hi + step > lower ? hi + step : Math.BitIncrement(lower);
            if (max is { } upper && lo >= upper) lo = double.IsFinite(lo - step) && lo - step < upper ? lo - step : Math.BitDecrement(upper);
            min ??= lo; max ??= hi;
        }
        if (!double.IsFinite(min!.Value) || !double.IsFinite(max!.Value))
            throw new ArgumentException("Automatic bounds cannot include these controls; set finite Min and Max around Mid.");
        mid ??= scheme.Kind is PaletteKind.Diverging or PaletteKind.Status && min < 0 && max > 0 ? 0 : ColourRange.Lerp(min.Value, max.Value, .5);
        var legend = new CardSpec { Id = id, Kind = CardKind.Legend, Title = title, Unit = unit, Min = min, Max = max, WiredSlot = slot };
        var result = new ColourScaleResult([], legend, [], scheme, min.Value, max.Value, mid.Value);
        // Sample every knot, including the Mid slope change for even custom stop counts.
        // Positioned stops give exact piecewise interpolation with at most 33 samples,
        // even when an arbitrary Mid cannot fit any bounded uniform sampling grid.
        var pivot = constant ? .5 : ColourRange.Fraction(mid.Value, min.Value, max.Value);
        double Position(double p) => pivot is <= 0 or >= 1 ? p : p <= .5 ? pivot * (2 * p) : pivot + (1 - pivot) * (2 * p - 1);
        var positions = Enumerable.Range(0, scheme.Colours.Length).Select(i => Position((double)i / (scheme.Colours.Length - 1)))
            .Append(Position(.5)).Distinct().Order().ToArray();
        legend = legend with { ColorPositions = positions, Colors = positions.Select(p => scheme.ColourAt(result.PalettePosition(p))).ToArray() };
        legend.Validate();
        var missing = values.Count - finite.Length;
        if (missing > 0) messages.Add(new(ColourMessageLevel.Remark, $"{missing} missing {(missing == 1 ? "value" : "values")} shown in grey ({Palettes.Missing})."));
        var outside = finite.Count(v => v < min || v > max);
        if (outside > 0) messages.Add(new(ColourMessageLevel.Remark, $"{outside} of {values.Count} {(values.Count == 1 ? "value" : "values")} {(outside == 1 ? "is" : "are")} outside {F(min)}–{F(max)} and {(outside == 1 ? "uses" : "use")} the end colours."));
        return result with { Colours = values.Select(result.ColourAt).ToArray(), Legend = legend, Messages = messages.ToArray() };
    }
}
