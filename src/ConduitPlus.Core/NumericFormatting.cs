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
        var text = decimal.TryParse(source.Replace('−', '-'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var exact)
            ? Math.Round(exact, decimals, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture)
            : value.ToString(format, CultureInfo.InvariantCulture);
        return text.StartsWith('-') && double.Parse(text, CultureInfo.InvariantCulture) == 0 ? text[1..] : text;
    }

    /// <summary>Fewest places from 2–6 retaining two significant digits in every nonzero value, capped at six.</summary>
    public static int AutomaticDecimals(IEnumerable<double> values)
    {
        var smallest = values.Where(v => v != 0).Select(Math.Abs).DefaultIfEmpty(double.PositiveInfinity).Min();
        for (var decimals = 2; decimals < 6; decimals++)
            if (smallest >= Math.Pow(10, 1 - decimals)) return decimals;
        return 6;
    }
}
