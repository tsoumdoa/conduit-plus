using System.Globalization;

namespace ConduitPlus.Core;

public readonly record struct LabelPalette(string Surface, string Ink, string Data, string Font)
{
    public static LabelPalette FromTheme(Theme theme) =>
        new(ColorRgba.Parse(theme.Surface).WithAlpha(249).ToHex(), theme.Ink, theme.Data, theme.Font);
}

public readonly record struct LabelRasterKey(string Text, int? Swatch, LabelPalette Palette,
    double Dpi, LabelForm Form, double Width, double Opacity);

public static class LabelPainter
{
    public const double FontSize = 11;
    public static double Width(string text, int? swatch, string font, Func<string, double, string, Size2> measure) =>
        Math.Ceiling(measure(text, FontSize, font).Width) + (swatch.HasValue ? 16 : 8);

    public static (string Text, double Width) Fit(string text, int? swatch, string font,
        Func<string, double, string, Size2> measure)
    {
        const double maximum = 160;
        var width = Width(text, swatch, font, measure);
        if (width <= maximum) return (text, width);

        var offsets = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = offsets.Length - 1;
        var best = "…";
        var bestWidth = Width(best, swatch, font, measure);
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var probe = text[..offsets[middle]] + "…";
            width = Width(probe, swatch, font, measure);
            if (width > maximum) high = middle - 1;
            else
            {
                best = probe;
                bestWidth = width;
                low = middle + 1;
            }
        }
        return (best, bestWidth);
    }

    public static string DataColor(int? argb, LabelPalette palette) => argb is int value
        ? new ColorRgba((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)(value >> 24)).ToHex()
        : palette.Data;

    public static string Fade(string color, double opacity)
    {
        var rgba = ColorRgba.Parse(color);
        return rgba.WithAlpha((byte)Math.Round(rgba.A * opacity)).ToHex();
    }

    public static void Paint(ICanvas2D canvas, LabelRasterKey key)
    {
        var swatch = DataColor(key.Swatch, key.Palette);
        if (key.Form == LabelForm.Dot)
        {
            canvas.RoundedRect(new(0, 0, 5, 5), 2.5, key.Palette.Surface);
            canvas.RoundedRect(new(1, 1, 3, 3), 1.5, swatch);
            return;
        }
        canvas.RoundedRect(new(0, 0, key.Width, 18), 6, key.Palette.Surface);
        if (key.Swatch.HasValue) canvas.RoundedRect(new(4, 7, 4, 4), 1, swatch);
        canvas.Text(key.Text, new(key.Swatch.HasValue ? 12 : 4, 1), FontSize, key.Palette.Ink, key.Palette.Font);
    }
}

/// <summary>Byte-budgeted cache. Entries own their resources until eviction or disposal.</summary>
public sealed class LabelRasterCache<T>(long capacity = 16 * 1024 * 1024) : IDisposable where T : IDisposable
{
    private readonly LinkedList<(LabelRasterKey Key, T Value, long Bytes)> _order = [];
    private readonly Dictionary<LabelRasterKey, LinkedListNode<(LabelRasterKey Key, T Value, long Bytes)>> _entries = [];
    public long Bytes { get; private set; }

    public bool TryGet(LabelRasterKey key, out T value)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Add(LabelRasterKey key, T value, long bytes)
    {
        if (_entries.TryGetValue(key, out var previous)) Remove(previous);
        var node = _order.AddFirst((key, value, bytes));
        _entries.Add(key, node);
        Bytes += bytes;
        while (Bytes > capacity) Remove(_order.Last!);
    }

    public void Retain(Func<LabelRasterKey, bool> keep)
    {
        for (var node = _order.First; node is not null;)
        {
            var next = node.Next;
            if (!keep(node.Value.Key)) Remove(node);
            node = next;
        }
    }

    private void Remove(LinkedListNode<(LabelRasterKey Key, T Value, long Bytes)> node)
    {
        _entries.Remove(node.Value.Key);
        _order.Remove(node);
        Bytes -= node.Value.Bytes;
        node.Value.Value.Dispose();
    }

    public void Dispose() => Retain(_ => false);
}
