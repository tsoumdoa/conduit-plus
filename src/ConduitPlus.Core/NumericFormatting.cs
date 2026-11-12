using System.Globalization;

namespace ConduitPlus.Core;

/// <summary>Shared schedule precision: midpoint values round away from zero.</summary>
public static class NumericFormatting
{
    // Integral doubles print exactly with F0; others use the shortest round-trip text.
    public static string Fixed(double value, int decimals)
        => Fixed(double.IsFinite(value) && value == Math.Floor(value)
            ? value.ToString("F0", CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture), value, decimals);

    // Source text has already passed Table's strict invariant grammar. Preserve its digits
    // before rounding; a direct double-to-decimal cast retains only about 15 significant digits.
    internal static string Fixed(string source, double value, int decimals)
    {
        if (decimals is < 0 or > 12) throw new ArgumentOutOfRangeException(nameof(decimals), "Decimals must be 0–12.");
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        // Beyond decimal's range a double has no fractional digits to round.
        var text = decimal.TryParse(source.Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            ? Math.Round(exact, decimals, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture)
            : value.ToString(format, CultureInfo.InvariantCulture);
        return text.StartsWith('-') && double.Parse(text, CultureInfo.InvariantCulture) == 0 ? text[1..] : text;
    }

    /// <summary>Maximum places needed after rounding to two decimals and trimming trailing zeros.
    /// If a nonzero value disappears, retain two significant digits throughout, capped at six places.</summary>
    public static int ColumnDecimals(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        var decimals = finite.Select(value =>
        {
            var text = Fixed(value, 2).TrimEnd('0');
            var dot = text.IndexOf('.');
            return dot < 0 ? 0 : text.Length - dot - 1;
        }).DefaultIfEmpty(0).Max();
        return finite.Any(value => value != 0 && double.Parse(Fixed(value, decimals), CultureInfo.InvariantCulture) == 0)
            ? SignificantDecimals(finite, decimals) : decimals;
    }

    /// <summary>Table float-noise policy: at least two places, retaining two significant digits up to six places.</summary>
    public static int AutomaticDecimals(IEnumerable<double> values) => SignificantDecimals(values, 2);

    private static int SignificantDecimals(IEnumerable<double> values, int minimum)
    {
        var smallest = values.Where(v => double.IsFinite(v) && v != 0).Select(Math.Abs).DefaultIfEmpty(double.PositiveInfinity).Min();
        for (var decimals = minimum; decimals < 6; decimals++)
            if (smallest >= Math.Pow(10, 1 - decimals)) return decimals;
        return 6;
    }
}
