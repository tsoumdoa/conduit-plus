using System.Collections.Immutable;

namespace ConduitPlus.Core;

public static class BrushPresentation
{
    /// <summary>Exports retain selection, but omit every field derived from transient hover.</summary>
    public static CardSpec WithoutHover(CardSpec card) => card with
    {
        HoverKey = null, HoveredKeys = [],
        GroupStates = card.GroupStates.ToImmutableDictionary(p => p.Key,
            p => p.Value with { HoveredMemberCount = 0, IsDirectTarget = false }, StringComparer.Ordinal)
    };

    public static BrushTargetKind TargetKind(CardSpec card, string key) =>
        card.Groups.ContainsKey(key) ? BrushTargetKind.Group : BrushTargetKind.Member;

    /// <summary>Reveal at most one newly selected row, choosing current display order.</summary>
    public static string? FirstNewRow(CardSpec table, IReadOnlySet<string> previous)
    {
        var selected = table.SelectedKeys.ToHashSet(StringComparer.Ordinal);
        return (table.Kind == CardKind.Bar ? table.Keys : TableSort.Rows(table).Select(r => r.Key)).FirstOrDefault(key => selected.Contains(key) && !previous.Contains(key));
    }
}

