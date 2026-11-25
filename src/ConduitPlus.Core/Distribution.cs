using System.Globalization;
using System.Runtime.CompilerServices;

namespace ConduitPlus.Core;

public enum DistributionMode { Dots, Histogram }

public sealed record DistributionOptions(DistributionMode Default = DistributionMode.Dots,
    DistributionMode? Wired = null, double? BinWidth = null, bool Quartiles = true, bool ThresholdLines = true);

public static class Distribution
{
    public const string BinWidthWarning = "Bin width too small for this range; using Auto";
    public const string DotsKey = "\u0001distribution-dots";
    public const string HistogramKey = "\u0001distribution-histogram";
    public const string BinPrefix = "\u0001distribution-bin:";
    public const string EmptyKey = "\u0001distribution-empty";

    public sealed record Bin(double Min, double Max, int[] Members);
    public sealed record Mark(HitRegion Hit, int[] Members, Bin? Bin = null);
    public sealed record Layout(Rect2 Plot, double Min, double Max, Mark[] Marks, bool Forced, bool Fallback, int Count, DistributionMode Mode) { public double[] SortedValues { get; init; } = []; }
    private static readonly ConditionalWeakTable<double?[], Dictionary<LayoutKey, Layout>> Cache = new();
    private readonly record struct LayoutKey(Rect2 Plot, string[] Keys, double? Width, DistributionMode Mode) { public double[] SortedValues { get; init; } = []; }
    private static long _builds;
    public static long LayoutBuilds => Interlocked.Read(ref _builds);
    public static string ForcedRemark(int count) => $"Too many values for dots ({count}); showing histogram";
    public static string Number(double value) => NumericText.TrueMinus(value.ToString("G5", CultureInfo.InvariantCulture));
    public static DistributionMode? Choice(string key) => key == DotsKey ? DistributionMode.Dots : key == HistogramKey ? DistributionMode.Histogram : null;
    public static DistributionMode Resolve(bool forced, DistributionOptions options, DistributionMode? local) =>
        forced ? DistributionMode.Histogram : options.Wired ?? local ?? options.Default;

    public static CardSpec Create(CardSpec card, CardSpec? scale, ThresholdSet? thresholds)
    {
        if (scale is not null && (scale.Kind != CardKind.Legend || scale.Mapping is null))
            throw new ArgumentException("Scale needs a Legend from Colour Scale");
        card = card with { Values = card.Values.Select(v => v.HasValue && double.IsFinite(v.Value) ? v : null).ToArray() };
        card.Validate();
        var effective = thresholds ?? scale?.Thresholds ?? scale?.Mapping?.Thresholds;
        var mapping = scale?.Mapping;
        if (mapping is not null && thresholds is not null) mapping = mapping with { Thresholds = thresholds };
        var colors = card.Colors;
        if (colors.Length == 0 && (mapping is not null || effective is { Entries.Length: > 0 }))
            colors = card.Values.Select(v => mapping?.ColourAt(v) ?? (v is { } value ? effective?.WinnerFor(value)?.Colour : null) ?? "").ToArray();
        var finite = Enumerable.Range(0, card.Values.Length).Where(i => card.Values[i].HasValue).ToArray();
        if (finite.Length == card.Values.Length) return card with { Colors = colors, Thresholds = effective };
        return card with
        {
            Values = finite.Select(i => card.Values[i]).ToArray(),
            Labels = finite.Select(i => card.Labels[i]).ToArray(),
            Keys = finite.Select(i => card.Keys[i]).ToArray(),
            Colors = colors.Length == 0 ? [] : finite.Select(i => colors[i]).ToArray(),
            Thresholds = effective
        };
    }

    public static Layout Geometry(CardSpec c, Theme theme)
    {
        var options = c.Distribution ?? new();
        var plot = new Rect2(theme.Padding + 5, 48, c.Width - 2 * (theme.Padding + 5), 124);
        var key = new LayoutKey(plot, c.Keys, options.BinWidth, Resolve(false, options, c.DistributionChoice));
        var layouts = Cache.GetOrCreateValue(c.Values);
        lock (layouts)
        {
            if (layouts.TryGetValue(key, out var existing)) return existing;
            if (layouts.Count >= 16) layouts.Clear();
            return layouts[key] = Build(c, key);
        }
    }

    private static Layout Build(CardSpec c, LayoutKey key)
    {
        Interlocked.Increment(ref _builds);
        var plot = key.Plot;
        var members = Enumerable.Range(0, c.Values.Length).Where(i => c.Values[i].HasValue).OrderBy(i => c.Values[i]).ToArray();
        if (members.Length == 0) return new(plot, 0, 1, [], false, false, 0, key.Mode);
        double Value(int i) => c.Values[i]!.Value;
        var lo = Value(members[0]);
        var hi = Value(members[^1]);
        var bins = Bins(members, Value, lo, hi, key.Width, out var fallback);
        lo = Math.Min(lo, bins[0].Min);
        hi = Math.Max(hi, bins[^1].Max);
        double X(double v) => plot.X + (lo == hi ? .5 : Math.Clamp(ColourRange.Fraction(v, lo, hi), 0, 1)) * plot.Width;
        var bottom = plot.Y + plot.Height;
        var forced = members.Length > 400;
        Mark[] dots = [];
        if (!forced)
        {
            for (var diameter = 10d; ; diameter = Math.Max(3, diameter * .85))
            {
                var rows = new List<double>();
                var placed = new List<Mark>(members.Length);
                foreach (var member in members)
                {
                    var x = X(Value(member));
                    var row = rows.FindIndex(last => x - last >= diameter);
                    if (row < 0) { row = rows.Count; rows.Add(x); }
                    else rows[row] = x;
                    if ((row + 1) * diameter > plot.Height) break;
                    placed.Add(new(new(c.Keys[member], x - diameter / 2, bottom - (row + 1) * diameter, diameter, diameter) { Circular = true }, [member]));
                }
                if (placed.Count == members.Length) { dots = placed.ToArray(); break; }
                if (diameter == 3) { forced = true; break; }
            }
        }
        var mode = forced ? DistributionMode.Histogram : key.Mode;
        var maxCount = bins.Max(b => b.Members.Length);
        var marks = mode == DistributionMode.Dots ? dots : bins.Select((bin, i) =>
        {
            var h = plot.Height * bin.Members.Length / maxCount;
            var left = X(bin.Min); var right = X(bin.Max);
            return new Mark(new(BinPrefix + i, left, bottom - h, right > left ? right - left : 1, h), bin.Members, bin);
        }).ToArray();
        return new(plot, lo, hi, marks, forced, fallback, members.Length, mode) { SortedValues = members.Select(Value).ToArray() };
    }

    private static Bin[] Bins(int[] members, Func<int, double> value, double lo, double hi, double? requested, out bool fallback)
    {
        fallback = false;
        static double Bound(double v) => Math.Clamp(v, -double.MaxValue, double.MaxValue);
        if (lo == hi)
        {
            var half = (requested ?? 1) / 2;
            return [new(Bound(lo - half), Bound(hi + half), members)];
        }
        Bin[]? Aligned(double width)
        {
            var span = double.IsFinite(hi - lo) ? (hi - lo) / width : hi / width - lo / width;
            if (!double.IsFinite(span) || span > 10_000) return null;
            try
            {
                var groups = new SortedDictionary<long, List<int>>();
                foreach (var member in members)
                {
                    var ratio = value(member) / width;
                    var index = checked((long)Math.Floor(ratio));
                    if (value(member) == hi && hi == index * width && hi > lo && index > Math.Floor(lo / width))
                        index = checked(index - 1);
                    if (!groups.TryGetValue(index, out var group)) groups[index] = group = [];
                    group.Add(member);
                }
                return groups.Select(g => new Bin(Bound(g.Key * width), Bound(checked(g.Key + 1) * width), g.Value.ToArray())).ToArray();
            }
            catch (OverflowException) { return null; }
        }
        if (requested is { } width)
        {
            if (Aligned(width) is { } explicitBins) return explicitBins;
            fallback = true;
        }
        var raw = hi / 10 - lo / 10;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10) * power;
        return Aligned(step) ?? [new(lo, hi, members)];
    }

    public static HitRegion[] Hits(CardSpec card, Theme theme)
    {
        if (card.Kind != CardKind.Distribution) return [];
        var layout = Geometry(card, theme);
        var plot = layout.Plot;
        return [new(EmptyKey, plot.X, plot.Y, plot.Width, plot.Height), .. layout.Marks.Select(m => m.Hit), .. HeaderHits(card, theme)];
    }

    internal static HitRegion[] HeaderHits(CardSpec card, Theme theme)
    {
        var right = card.Width - theme.Padding - 44;
        return card.Distribution?.Wired is not null ? [] :
            [new(DotsKey, right - 108, 7, 34, 22), new(HistogramKey, right - 74, 7, 74, 22)];

    }

    public static string[]? Members(CardSpec card, Theme theme, string key) =>
        card.Kind == CardKind.Distribution && key.StartsWith(BinPrefix, StringComparison.Ordinal)
            ? Geometry(card, theme).Marks.FirstOrDefault(m => m.Hit.Key == key)?.Members.Select(i => card.Keys[i]).ToArray() : null;

    public static string[]? Tooltip(CardSpec card, Theme theme, string key)
    {
        var layout = Geometry(card, theme);
        if (Choice(key).HasValue) return layout.Forced ? [ForcedRemark(layout.Count)] : null;
        if (layout.Marks.FirstOrDefault(m => m.Hit.Key == key) is not { } mark) return null;
        var unit = string.IsNullOrWhiteSpace(card.Unit) ? "" : " " + card.Unit;
        if (mark.Bin is { } bin)
            return [$"{Number(bin.Min)}–{Number(bin.Max)}{unit}", FormattableString.Invariant($"{bin.Members.Length} of {layout.Count} ({100d * bin.Members.Length / layout.Count:0.#}%)")];
        var i = mark.Members[0];
        return [card.Labels[i], Number(card.Values[i]!.Value) + unit];
    }
}
