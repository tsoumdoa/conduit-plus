using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ConduitPlus.Core;

public sealed record TableColumn(double X, double Width, bool Numeric);

/// <summary>Display-only formatting and content-fit geometry shared by painting and header hits.</summary>
public static partial class TablePresentation
{
    // Saved text has one meaning on every machine. Grouping, whitespace and units remain text.
    [GeneratedRegex(@"\A[+\-−]?[0-9]+(?:\.[0-9]+)?(?:[eE][+\-−]?[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
    public static bool TryNumber(string? text, out double value)
    {
        value = 0;
        return text is not null && NumberPattern().IsMatch(text) &&
            double.TryParse(text.Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static void ValidateDecimals(int? decimals)
    {
        if (decimals is < 0 or > 12) throw new ArgumentException("Decimals must be 0–12.");
    }

    public static string Format(string? text, int? decimals)
    {
        ValidateDecimals(decimals);
        if (decimals is not { } d || !TryNumber(text, out var value)) return text ?? "—";
        return NumericFormatting.Fixed(text!, value, d).Replace('-', '−');
    }

    // Count source places (including trailing zeroes), adjusted for scientific notation.
    // Called only after TryNumber has validated the grammar and finite value.
    private static bool HasFloatNoise(string text)
    {
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var dot = mantissa.IndexOf('.');
        var places = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var exponent = exponentIndex < 0 ? 0 : double.Parse(text[(exponentIndex + 1)..].Replace('−', '-'), CultureInfo.InvariantCulture);
        return places - exponent > 4;
    }

    // CardStore carries its owned arrays across snapshots when authored table content is equal.
    // Weak ownership releases all measurements with the content, rather than retaining old revisions.
    private static readonly ConditionalWeakTable<TableRow[], ConditionalWeakTable<string[], ContentCache>> Cache = new();
    private sealed record ContentKey(int? Decimals, string Font, double FontSize,
        Func<string, double, string, Size2> Measure);
    private sealed class ContentCache
    {
        public Dictionary<ContentKey, Content> Entries { get; } = [];
    }

    public static TableColumn[] Columns(CardSpec c, Theme t) => Layout(c, t).Columns;

    internal static TableLayout Layout(CardSpec c, Theme t)
    {
        // Empty tables can share Array.Empty<TableRow>(); headers must have weak ownership too.
        var cache = Cache.GetValue(c.Rows, _ => new()).GetValue(c.Headers, _ => new());
        lock (cache)
        {
            var key = new ContentKey(c.Decimals, t.Font, t.FontSize, TextMetrics.Measurer);
            if (!cache.Entries.TryGetValue(key, out var content)) cache.Entries.Add(key, content = new(c, t, key.Measure));
            return content.Layout(c.Width, t.Padding, t.RowHeight);
        }
    }

    internal sealed class Content
    {
        private const int WidthSampleRows = 128;
        private readonly string[] headers;
        private readonly double[] desired;
        private readonly bool[] numeric;
        private readonly Dictionary<TableRow, string[]> cells = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, double> measured = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Text, double Width), FittedCell> fitted = [];
        private readonly Dictionary<(double Width, double Padding, double RowHeight), TableLayout> layouts = [];
        private readonly Func<string, double> measure;

        public Content(CardSpec c, Theme t, Func<string, double, string, Size2> measurer)
        {
            headers = c.Headers;
            measure = text => measurer(text, t.FontSize, t.Font).Width;
            desired = headers.Select(h => Measure(h) + 12).ToArray();
            numeric = Enumerable.Repeat(c.Rows.Length > 0, headers.Length).ToArray();
            foreach (var row in c.Rows)
                for (var col = 0; col < headers.Length; col++) numeric[col] &= TryNumber(row.Cells[col], out _);
            var precision = Enumerable.Repeat(c.Decimals, headers.Length).ToArray();
            if (c.Decimals is null)
                for (var col = 0; col < headers.Length; col++)
                    if (numeric[col] && c.Rows.Any(r => HasFloatNoise(r.Cells[col]!)))
                    {
                        var values = c.Rows.Select(r => { TryNumber(r.Cells[col], out var v); return v; }).ToArray();
                        precision[col] = NumericFormatting.AutomaticDecimals(values);
                    }
            var longest = new string[headers.Length];
            var formatted = new Dictionary<(string Raw, int? Decimals), string>();
            foreach (var row in c.Rows)
            {
                var display = new string[headers.Length];
                for (var col = 0; col < headers.Length; col++)
                {
                    var raw = row.Cells[col];
                    if (raw is null) display[col] = "—";
                    else
                    {
                        var key = (raw, precision[col]);
                        if (!formatted.TryGetValue(key, out var text)) formatted.Add(key, text = Format(raw, precision[col]));
                        display[col] = text;
                    }
                    if (longest[col] is null || display[col].Length > longest[col].Length) longest[col] = display[col];
                }
                cells[row] = display;
            }
            for (var col = 0; col < headers.Length; col++)
                if (longest[col] is { } text) desired[col] = Math.Max(desired[col], Measure(text) + 12);
            // Evenly sample the full schedule (including its last row); offscreen text still
            // participates in numeric classification. Also measure each column's longest display
            // string so an outlier between sample rows gets space without thousands of GDI calls.
            var sample = Math.Min(WidthSampleRows, c.Rows.Length);
            for (var i = 0; i < sample; i++)
            {
                var row = c.Rows[sample == 1 ? 0 : (int)((long)i * (c.Rows.Length - 1) / (sample - 1))];
                for (var col = 0; col < headers.Length; col++) desired[col] = Math.Max(desired[col], Measure(cells[row][col]) + 12);
            }
        }

        private double Measure(string text)
        {
            if (!measured.TryGetValue(text, out var width)) measured.Add(text, width = measure(text));
            return width;
        }

        public TableLayout Layout(double width, double padding, double rowHeight)
        {
            var key = (width, padding, rowHeight);
            if (layouts.TryGetValue(key, out var layout)) return layout;
            var available = width - 2 * padding;
            var headerWidths = headers.Select(h => Measure(h) + 12).ToArray();
            var headerTotal = headerWidths.Sum();
            var minimum = headerTotal <= available ? headerWidths : headerWidths.Select(w => w * available / headerTotal).ToArray();
            var remaining = Math.Max(0, available - minimum.Sum());
            var total = desired.Sum();
            var flexible = desired.Select((w, i) => Math.Max(0, w - minimum[i])).Sum();
            var widths = total <= available
                ? desired.Select(w => w + (available - total) * w / total)
                : desired.Select((w, i) => minimum[i] + (flexible == 0 ? 0 : remaining * Math.Max(0, w - minimum[i]) / flexible));
            var x = padding;
            var columns = widths.Select((w, i) =>
            {
                var column = new TableColumn(x, w, numeric[i]);
                x += w;
                return column;
            }).ToArray();
            layouts.Add(key, layout = new(this, columns, columns.Select((col, i) =>
                new HitRegion(TableSort.Key(i), col.X, CardMeasure.BodyTop, col.Width, rowHeight)).ToArray()));
            return layout;
        }

        public string Cell(TableRow row, int col) => cells[row][col];

        public FittedCell Fit(string text, double available)
        {
            lock (this)
            {
                if (fitted.TryGetValue((text, available), out var result)) return result;
                var width = Measure(text);
                if (width <= available) result = new(text, width);
                else if (Measure("…") > available) result = new("", 0);
                else
                {
                    // Search whole text elements, preserving surrogate pairs and combining marks.
                    var boundaries = StringInfo.ParseCombiningCharacters(text);
                    var low = 0; var high = boundaries.Length;
                    while (low < high)
                    {
                        var mid = (low + high + 1) / 2;
                        var end = mid == boundaries.Length ? text.Length : boundaries[mid];
                        if (Measure(text[..end] + "…") <= available) low = mid;
                        else high = mid - 1;
                    }
                    var length = low == boundaries.Length ? text.Length : boundaries[low];
                    var display = text[..length] + "…";
                    result = new(display, Measure(display));
                }
                fitted.Add((text, available), result);
                return result;
            }
        }
    }
}

internal sealed record FittedCell(string Text, double Width);
internal sealed record TableLayout(TablePresentation.Content Content, TableColumn[] Columns, HitRegion[] Headers);
