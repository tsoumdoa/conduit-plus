using System.Globalization;

namespace ConduitPlus.Core;

/// <summary>Shared schedule precision: midpoint values round away from zero.</summary>
public static class NumericFormatting
{
    public static string Fixed(double value, int decimals)
    {
        if (decimals is < 0 or > 12) throw new ArgumentOutOfRangeException(nameof(decimals), "Decimals must be 0–12.");
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        string text;
        try
        {
            text = Math.Round((decimal)value, decimals, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            // Beyond decimal's range a double has no fractional digits to round.
            text = Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture);
        }
        return text.StartsWith('-') && double.Parse(text, CultureInfo.InvariantCulture) == 0 ? text[1..] : text;
    }

    /// <summary>Fewest places, up to two, that preserve distinctions between authored values.</summary>
    public static int AutomaticDecimals(IEnumerable<double> values)
    {
        var distinct = values.Distinct().ToArray();
        for (var decimals = 0; decimals < 2; decimals++)
            if (distinct.Select(v => Fixed(v, decimals)).Distinct(StringComparer.Ordinal).Count() == distinct.Length) return decimals;
        return 2;
    }
}
