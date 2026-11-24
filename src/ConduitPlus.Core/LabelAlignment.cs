namespace ConduitPlus.Core;

public readonly record struct LabelSource(Guid Component, string Path);
public readonly record struct LabelIdentity(Guid Owner, LabelSource Source, string? LinkKey, int? PointIndex);
public sealed record LabelStyle(string Text, string? Key, string? ShortText, int? Swatch, double Priority);

public static class LabelAlignment
{
    public static LabelStyle[] Align(string path, IReadOnlyList<string>? linkKeys, int pointCount,
        IReadOnlyList<string> text, IReadOnlyList<string> keys, IReadOnlyList<string> shortText,
        IReadOnlyList<int> colours, IReadOnlyList<double> priority)
    {
        var count = linkKeys?.Count ?? pointCount;
        if (linkKeys is not null && pointCount != 0) throw Error("Supply either Link or Points.");
        if (linkKeys is not null && keys.Count != 0) throw Error("Keys cannot override Link keys.");
        if (linkKeys is null && count == 0) throw Error("Points are required without a Link.");
        Check("Text", text.Count, broadcast: true, optional: linkKeys is not null);
        Check("Keys", keys.Count, broadcast: false, optional: true);
        Check("Short text", shortText.Count, broadcast: true, optional: true);
        Check("Colours", colours.Count, broadcast: true, optional: true);
        Check("Priority", priority.Count, broadcast: true, optional: true);
        var styles = new LabelStyle[count];
        for (var i = 0; i < count; i++)
        {
            var label = text.Count == 0 ? linkKeys![i] : At(text, i);
            var key = linkKeys?[i] ?? (keys.Count == 0 ? null : keys[i]);
            var rank = priority.Count == 0 ? 0 : At(priority, i);
            if (string.IsNullOrWhiteSpace(label)) throw Error($"Text [{i}] must be nonempty.");
            if (key is not null && string.IsNullOrWhiteSpace(key)) throw Error($"Keys [{i}] must be nonempty.");
            if (!double.IsFinite(rank)) throw Error($"Priority [{i}] must be finite.");
            styles[i] = new(label, key, shortText.Count == 0 ? null : At(shortText, i),
                colours.Count == 0 ? null : At(colours, i), rank);
        }
        return styles;

        ArgumentException Error(string message) => new($"Path {path}: {message}");
        void Check(string input, int length, bool broadcast, bool optional)
        {
            if (optional && length == 0 || length == count && length != 0 || broadcast && length == 1) return;
            throw Error($"{input} has {length} values; expected {(broadcast ? $"one or {count}" : count.ToString())}.");
        }
    }

    private static T At<T>(IReadOnlyList<T> values, int index) => values[values.Count == 1 ? 0 : index];
}
