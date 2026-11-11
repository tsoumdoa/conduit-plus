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
                throw new ArgumentException($"Columns has {columns.Length} branches but there are {headers.Length} Headers.");
            if (columns.Select(c => c.Length).Distinct().Count() > 1)
                throw new ArgumentException("Columns have unequal lengths: " + Counts(columns) + ".");
            rows = Enumerable.Range(0, columns[0].Length).Select(r => columns.Select(c => c[r]).ToArray()).ToArray();
        }
        else if (rows!.Any(r => r.Length != headers.Length))
            throw new ArgumentException($"Rows must each have {headers.Length} cells to match Headers: {Counts(rows!)}.");
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
                    : "First-column values are not unique valid keys, so rows are keyed 1…n. Set Keys to use other keys.";
            }
        }
        return new(data.Select((r, i) => new TableRow(keys[i], r.ToArray())).ToArray(), warning);
    }

    private static string Counts(string?[][] branches) => string.Join(", ", branches.Select((b, i) => $"branch {i}: {b.Length}"));
}
