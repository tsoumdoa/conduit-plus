namespace ConduitPlus.Core;

public static class CardImageExport
{
    /// <summary>Menu labels: the card title, or "Untitled Kind"; repeated labels are numbered so each entry is distinguishable.</summary>
    public static string[] MenuLabels(IReadOnlyList<CardSpec> cards)
    {
        var labels = cards.Select(c => string.IsNullOrWhiteSpace(c.Title) ? $"Untitled {c.Kind}" : c.Title.Trim()).ToArray();
        var totals = labels.GroupBy(l => l, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return labels.Select(l =>
        {
            if (totals[l] == 1) return l;
            seen[l] = seen.GetValueOrDefault(l) + 1;
            return $"{l} ({seen[l]})";
        }).ToArray();
    }

    // Translucent themes use the same RGB surface as their opaque clipboard backing.
    public static ColorRgba Background(Theme theme) => ColorRgba.Parse(theme.Surface).WithAlpha(255);

    /// <summary>Theme for the opaque fallback: automatic plain-title ink was chosen for the host background
    /// (viewport/canvas), so re-choose it for the surface it is flattened onto. Explicit title colours are kept.</summary>
    public static Theme OpaqueTheme(Theme theme) =>
        theme with { PlainTitleInk = TitleMetrics.InkForBackground(Background(theme)), PlainTitleHalo = null };
}
