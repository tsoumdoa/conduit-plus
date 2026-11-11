using System.Globalization;

namespace ConduitPlus.Core;

public static class MetricFormatting
{
    /// <summary>Unspecified precision preserves existing formatted text. Nonnumeric labels are always preserved.</summary>
    public static string? Format(string? value, int? decimals = null)
    {
        if (decimals is < 0 or > 12) throw new ArgumentOutOfRangeException(nameof(decimals), "Decimals must be 0–12.");
        return decimals.HasValue && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? NumericFormatting.Fixed(number, decimals.Value) : value;
    }
}
