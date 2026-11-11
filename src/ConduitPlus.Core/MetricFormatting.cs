using System.Globalization;

namespace ConduitPlus.Core;

public static class MetricFormatting
{
    /// <summary>Unspecified precision preserves digits; numeric signs use a true minus. Nonnumeric labels are preserved.</summary>
    public static string? Format(string? value, int? decimals = null)
    {
        if (decimals is < 0 or > 12) throw new ArgumentOutOfRangeException(nameof(decimals), "Decimals must be 0–12.");
        var formatted = decimals.HasValue && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number.ToString($"F{decimals.Value}", CultureInfo.InvariantCulture) : value;
        return formatted is null ? null : NumericText.TrueMinus(formatted);
    }
}
