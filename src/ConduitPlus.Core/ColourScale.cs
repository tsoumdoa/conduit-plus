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
    public string ColourAt(double? value)
    {
        if (value is not { } v || !double.IsFinite(v)) return Palettes.Missing;
        // Work relative to each side: a global normalized pivot can round to 0 or 1
        // even though Mid is an interior double in the original domain.
        var position = Min == Max ? .5 : v <= Min ? 0 : v >= Max ? 1
            : Mid <= Min || Mid >= Max ? ColourRange.Fraction(v, Min, Max)
            : v <= Mid ? .5 * ColourRange.Fraction(v, Min, Mid)
            : .5 + .5 * ColourRange.Fraction(v, Mid, Max);
        return Palette.ColourAt(position);
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
        if (scheme.Name is "Categorical" or "Categorical reversed")
        {
            messages.Add(new(ColourMessageLevel.Warning, "Categorical is for categories; using Blue. Choose a sequential, diverging or Status palette."));
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
        var explicitMid = mid.HasValue;
        mid ??= scheme.Kind is PaletteKind.Diverging or PaletteKind.Status && min < 0 && max > 0 ? 0 : ColourRange.Lerp(min.Value, max.Value, .5);
        var legend = new CardSpec { Id = id, Kind = CardKind.Legend, Title = title, Unit = unit, Min = min, Max = max, WiredSlot = slot };
        var result = new ColourScaleResult([], legend, [], scheme, min.Value, max.Value, mid.Value);
        // Compute the breakpoint from the nearer side, without subtracting two
        // almost equal values at the far endpoint. Explicit controls must fit the
        // positioned-stop transport; never silently discard an accepted Mid.
        var lowerFraction = constant ? .5 : ColourRange.Fraction(mid.Value, min.Value, max.Value);
        var upperFraction = constant ? .5 : ColourRange.Fraction(mid.Value, max.Value, min.Value);
        var pivot = lowerFraction <= upperFraction ? lowerFraction : 1 - upperFraction;
        if (explicitMid && pivot is <= 0 or >= 1)
            throw new ArgumentException($"Mid ({F(mid)}) is too close to Min or Max to show; move it inside the range.");
        var interior = mid > min && mid < max;
        // A default zero can be interior in value space but closer to an end than
        // normalized doubles can express. Retain its colour at the nearest interior
        // legend position. Only a domain with no interior Mid uses the endpoint fallback.
        if (interior) pivot = Math.Clamp(pivot, double.Epsilon, Math.BitDecrement(1d));
        double Position(double p) => !interior ? p : p <= .5 ? pivot * (2 * p) : 1 - (1 - pivot) * (2 * (1 - p));
        var stops = new SortedDictionary<double, string>();
        for (var i = 0; i < scheme.Colours.Length; i++)
        {
            var p = (double)i / (scheme.Colours.Length - 1);
            var position = Position(p);
            // Sub-knots can round together near an endpoint. Reserve both ends and Mid.
            if (position > 0 && position < 1 && position != pivot)
                stops[position] = constant ? scheme.ColourAt(.5) : scheme.ColourAt(p);
        }
        stops[0] = constant ? scheme.ColourAt(.5) : scheme.Colours[0];
        stops[1] = constant ? scheme.ColourAt(.5) : scheme.Colours[^1];
        if (interior || constant) stops[pivot] = scheme.ColourAt(.5);
        legend = legend with { ColorPositions = stops.Keys.ToArray(), Colors = stops.Values.ToArray() };
        legend.Validate();
        var missing = values.Count - finite.Length;
        if (missing > 0) messages.Add(new(ColourMessageLevel.Remark, $"{missing} missing {(missing == 1 ? "value" : "values")} shown in grey ({Palettes.Missing})."));
        var outside = finite.Count(v => v < min || v > max);
        if (outside > 0) messages.Add(new(ColourMessageLevel.Remark, $"{outside} of {values.Count} {(values.Count == 1 ? "value" : "values")} {(outside == 1 ? "is" : "are")} outside {F(min)}–{F(max)} and {(outside == 1 ? "uses" : "use")} the end colours."));
        return result with { Colours = values.Select(result.ColourAt).ToArray(), Legend = legend, Messages = messages.ToArray() };
    }
}
