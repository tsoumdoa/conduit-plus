using System.Globalization;

namespace ConduitPlus.Core;

public static class TableSort
{
    private const string Prefix = "\u0001sort:";
    public static string Key(int column) => Prefix + column.ToString(CultureInfo.InvariantCulture);
    public static int? Column(string key) => key.StartsWith(Prefix, StringComparison.Ordinal) &&
        int.TryParse(key.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var col) ? col : null;
    public static HitRegion[] Headers(CardSpec c, Theme t) => c.Kind != CardKind.Table ? [] :
        c.Headers.Select((_, i) => new HitRegion(Key(i), t.Padding + i * (c.Width - 2 * t.Padding) / c.Headers.Length,
            CardMeasure.BodyTop, (c.Width - 2 * t.Padding) / c.Headers.Length, t.RowHeight)).ToArray();
    public static CardSpec Cycle(CardSpec c, int column) => c with
    {
        SortColumn = c.SortColumn == column && c.SortDescending ? null : column,
        SortDescending = c.SortColumn == column && !c.SortDescending
    };
    /// <summary>Stable display order; blank cells stay last in either direction. Input arrays are never changed.</summary>
    public static TableRow[] Rows(CardSpec c)
    {
        if (c.Kind != CardKind.Table || c.SortColumn is not { } col || col < 0 || col >= c.Headers.Length) return c.Rows;
        return c.Rows.OrderBy(r => r.Cells[col], Comparer<string?>.Create(Compare)).ToArray();
        int Compare(string? a, string? b)
        {
            var emptyA = string.IsNullOrEmpty(a); var emptyB = string.IsNullOrEmpty(b);
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : emptyA ? 1 : -1;
            var comparison = double.TryParse(a, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(b, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var y)
                ? x.CompareTo(y) : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return c.SortDescending ? -comparison : comparison;
        }
    }
}
