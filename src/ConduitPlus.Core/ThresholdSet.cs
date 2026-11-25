using System.Collections.Immutable;
using System.Globalization;

namespace ConduitPlus.Core;

public enum ThresholdSide { Above, Below }

public sealed record ThresholdEntry(double Value, string Colour, ThresholdSide Side, int Count = 0)
{
    public string Label => (Side == ThresholdSide.Above ? "≥ " : "≤ ") + Value.ToString("G4", CultureInfo.InvariantCulture);
}

public sealed record ThresholdSet(ImmutableArray<ThresholdEntry> Entries)
{
    public ThresholdEntry? WinnerFor(double value) => double.IsFinite(value) ? WinnerBetween(value, value) : null;

    // Above can win throughout an interval from its lower edge, Below from its upper edge.
    internal ThresholdEntry? WinnerBetween(double lower, double upper)
    {
        ThresholdEntry? below = null;
        foreach (var entry in Entries)
        {
            if (entry.Side == ThresholdSide.Above && lower >= entry.Value) return entry;
            if (entry.Side == ThresholdSide.Below && upper <= entry.Value) below = entry;
        }
        return below;
    }

    public static ThresholdSet Resolve(IReadOnlyList<double> values, IReadOnlyList<string>? colours,
        IReadOnlyList<string>? sides, ICollection<ColourMessage> messages)
    {
        void Warn(string text) => messages.Add(new(ColourMessageLevel.Warning, text));
        if (values.Count == 0)
        {
            if (colours is not null) Warn("Threshold colours has no Thresholds");
            if (sides is not null) Warn("Threshold sides has no Thresholds");
            return new([]);
        }
        if (values.Any(v => !double.IsFinite(v))) throw new ArgumentException("Thresholds must be finite.");
        if (colours is null || colours.Count == 0 || colours.Count != 1 && colours.Count != values.Count)
            throw new ArgumentException("Threshold colours needs one colour or one per threshold.");
        if (sides is { Count: > 1 } && sides.Count != values.Count)
            throw new ArgumentException("Threshold sides needs zero entries, one entry or one per threshold.");

        var entries = new Dictionary<(double, ThresholdSide), ThresholdEntry>();
        for (var i = 0; i < values.Count; i++)
        {
            var text = sides is { Count: > 0 } ? sides[sides.Count == 1 ? 0 : i]?.Trim() ?? "" : "Above";
            var side = text.ToUpperInvariant() switch
            {
                "ABOVE" => ThresholdSide.Above,
                "BELOW" => ThresholdSide.Below,
                _ => throw new ArgumentException($"Unknown threshold side '{text}'; choose Above or Below.")
            };
            var colour = colours[colours.Count == 1 ? 0 : i];
            if (colour is not { Length: 7 } || colour[0] != '#' || !colour.Skip(1).All(Uri.IsHexDigit))
                throw new ArgumentException("Threshold colours must be #RRGGBB strings.");
            var entry = new ThresholdEntry(values[i], colour, side);
            if (!entries.TryAdd((entry.Value, side), entry)) Warn($"Duplicate threshold {entry.Label}; using the first colour.");
        }
        var sorted = entries.Values.OrderByDescending(e => e.Value).ThenBy(e => e.Side).ToImmutableArray();
        var above = sorted.LastOrDefault(e => e.Side == ThresholdSide.Above);
        var below = sorted.FirstOrDefault(e => e.Side == ThresholdSide.Below);
        if (above is not null && below is not null && above.Value <= below.Value)
            Warn($"Thresholds {above.Label} and {below.Label} overlap; Above wins.");
        return new(sorted);
    }

    public ThresholdSet WithCounts(IEnumerable<double> values)
    {
        var counts = new Dictionary<ThresholdEntry, int>();
        foreach (var value in values)
            if (WinnerFor(value) is { } entry) counts[entry] = counts.GetValueOrDefault(entry) + 1;
        return new(Entries.Select(e => e with { Count = counts.GetValueOrDefault(e) }).ToImmutableArray());
    }
}
