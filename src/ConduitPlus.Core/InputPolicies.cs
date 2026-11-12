using System.Globalization;

namespace ConduitPlus.Core;

/// <summary>Positional identity preserves iteration zero and Grasshopper's native data matching.</summary>
public static class CardIdentity
{
    public static string ForIteration(string instanceId, int iteration) => iteration == 0 ? instanceId : $"{instanceId}:{iteration.ToString(CultureInfo.InvariantCulture)}";
    public static string Source(string id) => id.Length > 33 && id[32] == ':' && Guid.TryParseExact(id[..32], "N", out _) && int.TryParse(id[33..], NumberStyles.None, CultureInfo.InvariantCulture, out _) ? id[..32] : id;
}

public static class InputKeys
{
    public static string[] Numbered(int count, bool legacyBar = false) => Enumerable.Range(1, count)
        .Select(i => legacyBar ? $"M{i:00}" : i.ToString(CultureInfo.InvariantCulture)).ToArray();

    public static string[] FromLabels(IReadOnlyList<string> labels, IReadOnlyList<string> supplied, out bool numbered)
    {
        numbered = supplied.Count == 0 && labels.Distinct(StringComparer.Ordinal).Count() != labels.Count;
        return supplied.Count > 0 ? supplied.ToArray() : numbered ? Numbered(labels.Count) : labels.ToArray();
    }

    public static string? Repeated(IReadOnlyList<string> keys)
    {
        var repeats = keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' ×{g.Count()}").ToArray();
        return repeats.Length == 0 ? null : $"Keys repeat ({string.Join(", ", repeats)}); they must be unique in a card.";
    }

    public static string[] ForGeometry(IReadOnlyList<int> branches) => Numbered(branches.Count == 1 ? branches[0] : branches.Count);
}

public sealed record KeyMatchNotice(bool Warning, string Message);

/// <summary>Compares local geometry with effective members, ignoring unkeyed and disabled/empty groups.</summary>
public static class LinkDiagnostics
{
    public static KeyMatchNotice[] Compare(IReadOnlyList<CardSpec> cards, IEnumerable<string> linkedKeys, bool linksWired, MembershipIndex? membership = null)
    {
        if (!linksWired) return [];
        var links = linkedKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (links.Length == 0) return [];
        var linkSet = links.ToHashSet(StringComparer.Ordinal);
        membership ??= MembershipIndex.Create([new BrushRegistration("diagnostics", cards, links)]);
        var keyed = cards.Select(c => (Card: c, Keys: BrushState.Keys(c).SelectMany(k =>
            membership.Resolve(k, c.Groups.ContainsKey(k) ? BrushTargetKind.Group : BrushTargetKind.Member)?.Members.AsEnumerable() ?? [])
            .Distinct(StringComparer.Ordinal).ToArray())).Where(p => p.Keys.Length > 0).ToArray();
        if (keyed.Length == 0) return [];
        var unmatched = keyed.Where(p => !p.Keys.Any(linkSet.Contains)).ToArray();
        if (unmatched.Length == 0) return [];
        static string Examples(IEnumerable<string> keys) => string.Join(", ", keys.Take(2).Select(k => $"'{k}'"));
        static string Name(CardSpec card) => $"{card.Kind} \"{(string.IsNullOrWhiteSpace(card.Title) ? "Untitled" : card.Title)}\"";
        if (unmatched.Length == keyed.Length)
            return [new(true, $"Link keys don't match any card keys, so clicks won't highlight geometry. Link: {Examples(links)}. {Name(keyed[0].Card)}: {Examples(keyed[0].Keys)}. Keys are case-sensitive. Wire the same keys into Link Keys.")];
        return unmatched.Select(p => new KeyMatchNotice(false, $"{Name(p.Card)}: none of its member keys match linked geometry. Card: {Examples(p.Keys)}; Link: {Examples(links)}. Keys are case-sensitive. Wire the same keys into Link Keys.")).ToArray();
    }
}
