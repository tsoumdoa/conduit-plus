namespace ConduitPlus.Core;

public sealed record TextLayout(string[] Lines, double Top, double LineHeight, double Height);

/// <summary>CSS-pixel metrics. Hosts register once, before layout; the fallback needs no graphics runtime.</summary>
public static class TextMetrics
{
    public const int MaxLines = 30;
    public static Func<string, double, string, Size2> Measurer { get; set; } = Estimate;
    public static Size2 Estimate(string text, double size, string font) => new(text.Length * size * .6, size * 1.5);
    public static TextLayout Layout(CardSpec c, Theme t) => Layout(c, t, Measurer, c.Form.Compact ? 2 : MaxLines);
    public static TextLayout Layout(CardSpec c, Theme t, Func<string, double, string, Size2> measure, int maxLines = MaxLines)
    {

        var width = c.Width - 2 * t.Padding;
        double W(string text) => measure(text, t.FontSize, t.Font).Width;
        var lines = Wrap().Take(maxLines + 1).ToList();
        if (lines.Count > maxLines)
        {
            lines.RemoveAt(maxLines);
            var last = lines[^1].TrimEnd();
            while (last.Length > 0 && W(last + "…") > width) last = last[..^1];
            lines[^1] = last + "…";
        }
        var top = string.IsNullOrEmpty(c.Title) ? t.Padding : 36;
        var lineHeight = Math.Ceiling(Math.Max(t.FontSize * 1.5, measure("Mg", t.FontSize, t.Font).Height));
        return new(lines.ToArray(), top, lineHeight, top + lines.Count * lineHeight + t.Padding);

        IEnumerable<string> Wrap()
        {
            foreach (var paragraph in c.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (W(line.Length == 0 ? word : line + " " + word) <= width) { line = line.Length == 0 ? word : line + " " + word; continue; }
                    if (line.Length > 0) { yield return line; line = ""; }
                    var rest = word;
                    while (W(rest) > width)
                    {
                        var low = 1; var high = rest.Length;
                        while (low < high) { var mid = (low + high + 1) / 2; if (W(rest[..mid]) <= width) low = mid; else high = mid - 1; }
                        yield return rest[..low]; rest = rest[low..];
                    }
                    line = rest;
                }
                yield return line;
            }
        }
    }
}

