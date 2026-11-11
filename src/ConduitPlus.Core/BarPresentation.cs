using System.Globalization;
using System.Runtime.CompilerServices;

namespace ConduitPlus.Core;

public enum BarGoodWhen { Below, Above }
public enum BarOrder { Original, WorstFirst, BestFirst }
public sealed record BarTextLayout(string[] Values, double[] ValueWidths, double LabelWidth, double ValueWidth);

/// <summary>Bar display policy. Permutations refer to incoming indices, preserving every row's identity.</summary>
public static class BarPresentation
{
    public const int DefaultVisibleRows = 12;
    public static BarGoodWhen ParseGoodWhen(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "below" => BarGoodWhen.Below,
        "above" => BarGoodWhen.Above,
        _ => throw new ArgumentException("Good when must be Below or Above.")
    };
    public static BarOrder ParseSort(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "original" => BarOrder.Original,
        "worst first" => BarOrder.WorstFirst,
        "best first" => BarOrder.BestFirst,
        _ => throw new ArgumentException("Sort must be Original, Worst first or Best first.")
    };
    public static void Validate(CardSpec c)
    {
        if (!Enum.IsDefined(c.GoodWhen)) throw new ArgumentException("Good when must be Below or Above.");
        if (!Enum.IsDefined(c.BarSort)) throw new ArgumentException("Sort must be Original, Worst first or Best first.");
        if (c.MaxRows is < 0 or > 200) throw new ArgumentException("Visible rows must be 0–200.");
    }
    public static bool IsGood(double value, double threshold, BarGoodWhen direction) =>
        direction == BarGoodWhen.Above ? value >= threshold : value <= threshold;
    public static string Color(CardSpec c, Theme t, int index) => c.Colors.Length > index ? c.Colors[index] :
        !c.HasThreshold ? t.Accent : IsGood(c.Values[index]!.Value, c.Threshold, c.GoodWhen) ? t.Good : t.Bad;

    /// <summary>Original leaves data untouched. Sorted modes are stable, with missing values last in either direction.</summary>
    public static int[] Order(CardSpec c)
    {
        var indices = Enumerable.Range(0, c.Values.Length);
        if (c.BarSort == BarOrder.Original) return indices.ToArray();
        var worstAscending = c.HasThreshold && c.GoodWhen == BarGoodWhen.Above;
        var ascending = c.BarSort == BarOrder.WorstFirst ? worstAscending : !worstAscending;
        return indices.OrderBy(i => !c.Values[i].HasValue)
            .ThenBy(i => c.Values[i], Comparer<double?>.Create((a, b) => ascending ? Nullable.Compare(a, b) : Nullable.Compare(b, a))).ToArray();
    }

    /// <summary>Fewest decimals preserving the rounded values (and thus distinct values), capped at two.</summary>
    public static int Precision(IEnumerable<double?> values)
    {
        var numbers = values.OfType<double>().Distinct().ToArray();
        for (var d = 0; d < 2; d++)
            if (numbers.All(v => Math.Round(v, d) == Math.Round(v, 2))) return d;
        return 2;
    }
    public static string Format(double? value, int precision) => value is { } v
        ? NumericText.TrueMinus(v.ToString(precision == 0 ? "0" : "0." + new string('#', precision), CultureInfo.InvariantCulture)) : "—";

    // Presentation copies keep the data arrays. Weak keys allow dead revisions to be collected;
    // scroll/hover/selection and sort never remeasure a dataset. Hosts supply the same CSS measurer as Table.
    private static readonly ConditionalWeakTable<double?[], ConditionalWeakTable<string[], Dictionary<MetricsKey, BarTextLayout>>> Cache = new();
    private sealed record MetricsKey(double Width, double Padding, double FontSize, string Font, Func<string, double, string, Size2> Measure);
    public static BarTextLayout Layout(CardSpec c, Theme t)
    {
        var entries = Cache.GetOrCreateValue(c.Values).GetOrCreateValue(c.Labels);
        var key = new MetricsKey(c.Width, t.Padding, t.FontSize, t.Font, TextMetrics.Measurer);
        lock (entries)
        {
            if (entries.TryGetValue(key, out var cached)) return cached;
            double Measure(string text) => key.Measure(text, t.FontSize, t.Font).Width;
            var precision = Precision(c.Values);
            var texts = c.Values.Select(v => Format(v, precision)).ToArray();
            var widths = texts.Select(Measure).ToArray();
            var available = c.Width - 2 * t.Padding;
            var valueWidth = Math.Min(available * .45, widths.DefaultIfEmpty(0).Max() + 12);
            var labelWidth = Math.Min((available - valueWidth) * .55,
                Math.Max(32, c.Labels.Select(label => Measure(label) + 12).DefaultIfEmpty(32).Max()));
            return entries[key] = new(texts, widths, labelWidth, valueWidth);
        }
    }
}

public static class NumericText
{
    /// <summary>Replace a numeric sign without changing punctuation in user-authored labels.</summary>
    public static string TrueMinus(string text)
    {
        var normalized = text.Replace('−', '-');
        if (!TablePresentation.TryNumber(normalized, out var number)) return text;
        if (number == 0 && normalized.TrimStart().StartsWith('-')) return "0";
        return text.Replace('-', '−');
    }
}
