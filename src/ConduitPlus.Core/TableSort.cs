using System.Globalization;
using System.Runtime.CompilerServices;

namespace ConduitPlus.Core;

public static class TableSort
{
    private static readonly ConditionalWeakTable<TableRow[], Dictionary<(int Column, bool Descending), TableRow[]>> Cache = new();
    private const string Prefix = "\u0001sort:";
    public static string Key(int column) => Prefix + column.ToString(CultureInfo.InvariantCulture);
    public static int? Column(string key) => key.StartsWith(Prefix, StringComparison.Ordinal) &&
        int.TryParse(key.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var col) ? col : null;
    public static HitRegion[] Headers(CardSpec c, Theme t) => c.Kind != CardKind.Table ? [] :
        TablePresentation.Layout(c, t).Headers;
    public static CardSpec Cycle(CardSpec c, int column) => c with
    {
        SortColumn = c.SortColumn == column && c.SortDescending ? null : column,
        SortDescending = c.SortColumn == column && !c.SortDescending
    };
    /// <summary>Stable display order for immutable row/cell arrays; blank cells stay last in either direction.
    /// Input arrays are never changed. The returned order is shared and must not be mutated.</summary>
    public static TableRow[] Rows(CardSpec c)
    {
        if (c.Kind != CardKind.Table || c.SortColumn is not { } col || col < 0 || col >= c.Headers.Length) return c.Rows;
        var entries = Cache.GetOrCreateValue(c.Rows);
        lock (entries)
        {
            var key = (col, c.SortDescending);
            if (!entries.TryGetValue(key, out var rows))
                entries.Add(key, rows = c.Rows.OrderBy(r => r.Cells[col], Comparer<string?>.Create(Compare)).ToArray());
            return rows;
        }
        int Compare(string? a, string? b)
        {
            var emptyA = string.IsNullOrEmpty(a); var emptyB = string.IsNullOrEmpty(b);
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : emptyA ? 1 : -1;
            var comparison = TablePresentation.TryNumber(a, out var x) && TablePresentation.TryNumber(b, out var y)
                ? x.CompareTo(y) : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return c.SortDescending ? -comparison : comparison;
        }
    }
}
