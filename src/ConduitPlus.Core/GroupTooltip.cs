using System.Collections.Immutable;

namespace ConduitPlus.Core;

public sealed record MemberGroupLabels(ImmutableArray<string> Labels, int RemainingCount);

/// <summary>Metadata only; A2 chooses how each host formats/displays it.</summary>
public static class GroupTooltip
{
    public static MemberGroupLabels ForMember(MembershipIndex membership, string member)
    {
        var labels = membership.ContainingGroups(member).SelectMany(k => membership.GroupLabels[k])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(labels.Take(5).ToImmutableArray(), Math.Max(0, labels.Length - 5));
    }

    public static GroupProjection? ForGroup(BrushDomainSnapshot snapshot, string group) => snapshot.GroupStates.GetValueOrDefault(group);

    public static string[] Lines(GroupProjection group) =>
        [$"{group.TotalMemberCount} members", $"{group.SelectedMemberCount} of {group.TotalMemberCount} selected", $"{group.HoveredMemberCount} of {group.TotalMemberCount} hovered"];
}
