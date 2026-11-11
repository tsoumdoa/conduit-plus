using System.Globalization;

namespace ConduitPlus.Core;

public sealed record TableColumn(double X, double Width, bool Numeric);

/// <summary>Display-only formatting and content-fit geometry shared by painting and header hits.</summary>
public static class TablePresentation
{
    public static bool TryNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public static void ValidateDecimals(int? decimals)
    {
        if (decimals is < 0 or > 12) throw new ArgumentException("Decimals must be 0–12.");
    }

    public static string Format(string? text, int? decimals)
    {
        ValidateDecimals(decimals);
        if (decimals is not { } d || !TryNumber(text, out var value)) return text ?? "—";
        var formatted = value.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        // Test the rounded display, so this uses exactly the formatter's rounding at midpoint values.
        if (formatted.StartsWith('-') && TryNumber(formatted, out var rounded) && rounded == 0) return "0";
        return formatted.Replace('-', '−');
    }

    public static TableColumn[] Columns(CardSpec c, Theme t)
    {
        if (c.Headers.Length == 0) return [];
        var count = c.Headers.Length;
        var available = c.Width - 2 * t.Padding;
        double Measure(string text) => TextMetrics.Measurer(text, t.FontSize, t.Font).Width;
        // Always reserve the sort glyph so cycling sort never moves column boundaries.
        var desired = Enumerable.Range(0, count).Select(i => Math.Max(Measure(c.Headers[i]) + 28,
            c.Rows.Select(r => Measure(Format(r.Cells[i], c.Decimals)) + 12).DefaultIfEmpty(0).Max())).ToArray();
        var minimum = Math.Min(32, available / count);
        var total = desired.Sum();
        var flexible = desired.Sum(w => Math.Max(0, w - minimum));
        var widths = total <= available
            ? desired.Select(w => w + (available - total) * w / total).ToArray()
            : desired.Select(w => minimum + (available - minimum * count) * Math.Max(0, w - minimum) / flexible).ToArray();
        var x = t.Padding;
        return widths.Select((w, i) =>
        {
            var column = new TableColumn(x, w, c.Rows.Length > 0 && c.Rows.All(r => TryNumber(r.Cells[i], out _)));
            x += w;
            return column;
        }).ToArray();
    }
}
