namespace ConduitPlus.Core;
public sealed record HiddenParticipant(string Label, Guid Owner, IReadOnlyList<CardSpec> Cards, IReadOnlySet<string> Hidden);
public sealed record HiddenRow(Guid Owner, string? CardId, string Text);
public sealed record HiddenPanel(Rect2 Bounds, Rect2[] Rows, int More);
public static class HiddenList
{
    public static HiddenRow[] Build(IEnumerable<HiddenParticipant> participants)
    {
        var all = participants.ToArray(); var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var grouped = all.Count(p => p.Hidden.Count > 0) > 1; var rows = new List<HiddenRow>();
        foreach (var p in all)
        {
            labels.TryGetValue(p.Label, out var count); labels[p.Label] = ++count;
            if (p.Hidden.Count == 0) continue;
            if (grouped) rows.Add(new(p.Owner, null, p.Label + (count > 1 ? $" ({count})" : "")));
            for (var i = 0; i < p.Cards.Count; i++)
            { var c = p.Cards[i]; if (p.Hidden.Contains(c.Id)) rows.Add(new(p.Owner, c.Id, string.IsNullOrWhiteSpace(c.Title) ? $"{c.Kind} {i + 1}" : c.Title)); }
        }
        return rows.ToArray();
    }
    /// <summary>Shortens text with an ellipsis so its measured width fits; measure returns width in the same units as maxWidth.</summary>
    public static string Fit(string text, double maxWidth, Func<string, double> measure)
    {
        if (measure(text) <= maxWidth) return text;
        var cut = text;
        while (cut.Length > 0 && measure(cut + "…") > maxWidth) cut = cut[..^1];
        return cut.Length == 0 ? "" : cut.TrimEnd() + "…";
    }
    public static HiddenPanel Layout(Rect2 chip, double availableHeight, double rowHeight, int count)
    {
        var slots = Math.Max(0, (int)(Math.Min(chip.Y, availableHeight) / rowHeight));
        var visible = Math.Min(count, Math.Max(0, slots - 1));
        var more = count - visible;
        if (more > 0) visible = Math.Max(0, visible - 1);
        more = count - visible;
        var height = Math.Min(Math.Min(chip.Y, availableHeight), (1 + visible + (more > 0 ? 1 : 0)) * rowHeight);
        var width = Math.Min(chip.X + chip.Width, 280 * rowHeight / 22);
        var bounds = new Rect2(chip.X + chip.Width - width, chip.Y - height, width, height);
        return new(bounds, Enumerable.Range(0, visible).Select(i => new Rect2(bounds.X, bounds.Y + (i + 1) * rowHeight, width, rowHeight)).ToArray(), more);
    }
}
