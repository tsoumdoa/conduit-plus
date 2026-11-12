using System.Globalization;

namespace ConduitPlus.Core;

public sealed record TableInputResult(TableRow[] Rows, string? Warning);

/// <summary>Null means absent; a non-null empty tree (or empty branches) is supplied data.</summary>
public static class TableInputs
{
    public static TableInputResult Create(string[] headers, string?[][]? rows, string?[][]? columns, string[]? keys = null)
    {
        if (rows is not null && columns is not null) throw new ArgumentException("Use Rows or Columns, not both.");
        if (rows is null && columns is null) return new([], "Set Rows or Columns.");
        if (headers.Length == 0) throw new ArgumentException("Set at least one Header.");
        if (columns is not null)
        {
            if (columns.Length != headers.Length)
                throw new ArgumentException($"Columns has {columns.Length} {Plural(columns.Length, "branch", "branches")} but there {(headers.Length == 1 ? "is" : "are")} {headers.Length} {Plural(headers.Length, "Header", "Headers")}. Put each column in its own branch (e.g. Entwine the column lists).");
            if (columns.Select(c => c.Length).Distinct().Count() > 1)
            {
                var common = columns.GroupBy(c => c.Length).OrderByDescending(g => g.Count()).First().Key;
                var counts = columns.Select((c, i) => (Index: i, Count: c.Length)).ToArray();
                var different = counts.Where(c => c.Count != common).ToArray();
                var examples = counts.Where(c => c.Count == common).Take(1).Concat(different.Take(2));
                throw new ArgumentException($"Columns have unequal lengths ({columns.Length} columns): " +
                    string.Join(", ", examples.Select(c => $"'{Short(headers[c.Index])}' has {c.Count} {Plural(c.Count, "item", "items")}")) +
                    (different.Length > 2 ? $", … ({different.Length} differ from {common} items)" : "") + ". Make all columns the same length.");
            }
            rows = Enumerable.Range(0, columns[0].Length).Select(r => columns.Select(c => c[r]).ToArray()).ToArray();
        }
        else if (rows is { } rowData && rowData.Any(r => r.Length != headers.Length))
        {
            var bad = rowData.Select((r, i) => (Row: i, Count: r.Length)).Where(r => r.Count != headers.Length).ToArray();
            var examples = string.Join(", ", bad.Take(3).Select(r => $"row {r.Row} has {r.Count}"));
            var hint = rowData.Length == headers.Length ? " That looks like columns; wire the tree into Columns." : " Make each row match Headers.";
            throw new ArgumentException($"Rows must each have {headers.Length} {Plural(headers.Length, "cell", "cells")} to match Headers; {bad.Length} of {rowData.Length} {Plural(rowData.Length, "row", "rows")} {(bad.Length == 1 ? "doesn't" : "don't")} ({examples})." + hint);
        }
        var data = rows!;
        if (keys is not null && keys.Length != data.Length) throw new ArgumentException("Key count must match row count.");
        string? warning = null;
        if (keys is null)
        {
            var first = data.Select(r => r[0]).ToArray();
            var repeated = first.GroupBy(k => k, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (first.All(k => !string.IsNullOrWhiteSpace(k) && !CardMeasure.IsReserved(k)) && repeated is null)
                keys = first.Select(k => k!).ToArray();
            else
            {
                keys = Enumerable.Range(1, data.Length).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
                warning = repeated is not null && !string.IsNullOrWhiteSpace(repeated.Key) && !CardMeasure.IsReserved(repeated.Key)
                    ? $"First column repeats ('{repeated.Key}' ×{repeated.Count()}), so rows are keyed 1…n."
                    : first.Any(string.IsNullOrWhiteSpace)
                        ? "First column has empty cells, so rows are keyed 1…n. Set Keys to use other keys."
                        : "First column has reserved keys, so rows are keyed 1…n. Set Keys to use other keys.";
            }
        }
        return new(data.Select((r, i) => new TableRow(keys[i], r.ToArray())).ToArray(), warning);
    }

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;
    private static string Short(string text) => text.Length <= 32 ? text : text[..31] + "…";
}
