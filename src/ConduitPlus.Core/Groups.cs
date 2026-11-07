using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ConduitPlus.Core;

/// <summary>An ordinal, deduplicated, immutable set of member keys. Empty groups are valid but inert.</summary>
public sealed record GroupDefinition
{
    public ImmutableArray<string> Members { get; }

    [JsonConstructor]
    public GroupDefinition(ImmutableArray<string> members)
    {
        if (members.IsDefault) members = [];
        foreach (var key in members) ValidateKey(key);
        Members = members.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || CardMeasure.IsReserved(key))
            throw new ArgumentException("Member/group keys must be nonblank and non-reserved.");
    }

    internal static void Validate(CardSpec card)
    {
        if (card.Kind == CardKind.Metric && card.Keys.Length > 1)
            throw new ArgumentException("Metric accepts at most one Key.");
        if (card.Groups.Count == 0) return;
        if (card.Kind is not (CardKind.Bar or CardKind.Metric or CardKind.Legend) || card.Kind == CardKind.Legend && card.Min.HasValue)
            throw new ArgumentException("Only Bar, Metric and categorical Legend accept groups.");
        var groupKeys = card.Groups.Keys.ToHashSet(StringComparer.Ordinal);
        var entryKeys = card.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var (key, definition) in card.Groups)
        {
            ValidateKey(key);
            if (!entryKeys.Contains(key))
                throw new ArgumentException($"Group '{key}' must have an explicit entry Key.");
            if (definition is null) throw new ArgumentException($"Group '{key}' has no definition.");
            if (definition.Members.Any(groupKeys.Contains))
                throw new ArgumentException($"Group '{key}' contains a group key; nested groups are not supported.");
        }
    }
}

public sealed record GroupContribution(string CardId, string Key, string Label, GroupDefinition Definition);
public sealed record MembershipDiagnostic(string ParticipantId, string CardId, string Key, string Code, string Message);

/// <summary>Captured, unprojected membership of one participant. Safe to retain across host solutions.</summary>
public sealed class BrushRegistration
{
    public string ParticipantId { get; }
    public ImmutableArray<string> IndividualKeys { get; }
    public ImmutableArray<GroupContribution> Groups { get; }

    public BrushRegistration(string participantId, IEnumerable<CardSpec> cards, IEnumerable<string> linkKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        ParticipantId = participantId;
        var individuals = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<GroupContribution>();
        foreach (var card in cards)
        {
            card.Validate();
            foreach (var key in BrushState.Keys(card))
                if (!card.Groups.ContainsKey(key)) individuals.Add(key);
            if (card.Groups.Count == 0) continue;
            var entryLabels = card.Keys.Select((key, i) => (key, label: card.Kind == CardKind.Metric ? card.Title : i < card.Labels.Length ? card.Labels[i] : key))
                .ToDictionary(p => p.key, p => p.label, StringComparer.Ordinal);
            foreach (var (key, definition) in card.Groups.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var label = entryLabels[key];
                groups.Add(new(card.Id, key, string.IsNullOrWhiteSpace(label) ? key : label, definition));
            }
        }
        // Preserve the existing host contract: ignored reserved Link control keys cannot become targets.
        foreach (var key in linkKeys)
            if (!string.IsNullOrWhiteSpace(key) && !CardMeasure.IsReserved(key)) individuals.Add(key);
        IndividualKeys = individuals.Order(StringComparer.Ordinal).ToImmutableArray();
        Groups = groups.OrderBy(g => g.CardId, StringComparer.Ordinal).ThenBy(g => g.Key, StringComparer.Ordinal).ToImmutableArray();
    }

    internal bool SameMembership(BrushRegistration other) => ParticipantId == other.ParticipantId &&
        IndividualKeys.SequenceEqual(other.IndividualKeys, StringComparer.Ordinal) && Groups.Length == other.Groups.Length &&
        Groups.Zip(other.Groups).All(p => p.First.CardId == p.Second.CardId && p.First.Key == p.Second.Key &&
            p.First.Definition.Members.SequenceEqual(p.Second.Definition.Members, StringComparer.Ordinal));
    internal bool SameLabels(BrushRegistration other) => Groups.Select(g => g.Label).SequenceEqual(other.Groups.Select(g => g.Label), StringComparer.Ordinal);
}

public sealed record IndexedGroup(string Key, ImmutableArray<string> Members);
public sealed record ResolvedBrushTarget(BrushTargetKind Kind, ImmutableArray<string> Members);
public enum BrushTargetKind { Member, Group, Domain }

/// <summary>Immutable membership/reverse index. Revision changes only when membership declarations change.</summary>
public sealed class MembershipIndex
{
    private readonly ImmutableArray<BrushRegistration> _registrations;
    public long Revision { get; }
    public FrozenSet<string> MemberKeys { get; }
    public FrozenDictionary<string, IndexedGroup> Groups { get; }
    public FrozenDictionary<string, ImmutableArray<string>> MemberGroups { get; }
    public FrozenDictionary<string, ImmutableArray<string>> GroupLabels { get; }
    public ImmutableArray<MembershipDiagnostic> Diagnostics { get; }

    private MembershipIndex(long revision, ImmutableArray<BrushRegistration> registrations, FrozenSet<string> members,
        FrozenDictionary<string, IndexedGroup> groups, FrozenDictionary<string, ImmutableArray<string>> reverse,
        FrozenDictionary<string, ImmutableArray<string>> labels, ImmutableArray<MembershipDiagnostic> diagnostics)
    {
        Revision = revision; _registrations = registrations; MemberKeys = members; Groups = groups;
        MemberGroups = reverse; GroupLabels = labels; Diagnostics = diagnostics;
    }

    public static MembershipIndex Create(IEnumerable<BrushRegistration> registrations, MembershipIndex? previous = null)
    {
        var inputs = registrations.OrderBy(r => r.ParticipantId, StringComparer.Ordinal).ToImmutableArray();
        if (inputs.Select(r => r.ParticipantId).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
            throw new ArgumentException("Participant identities must be unique in a membership transaction.");
        var same = previous is not null && inputs.Length == previous._registrations.Length &&
            inputs.Zip(previous._registrations).All(p => p.First.SameMembership(p.Second));
        if (same && inputs.Zip(previous!._registrations).All(p => p.First.SameLabels(p.Second))) return previous!;
        var declarations = inputs.SelectMany(r => r.Groups.Select(g => (Registration: r, Group: g)))
            .GroupBy(p => p.Group.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var labels = declarations.ToFrozenDictionary(p => p.Key,
            p => p.Value.Select(v => v.Group.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);
        if (same)
        {
            return new(previous!.Revision, inputs, previous.MemberKeys, previous.Groups, previous.MemberGroups, labels, previous.Diagnostics);
        }
        var individuals = inputs.SelectMany(r => r.IndividualKeys).ToHashSet(StringComparer.Ordinal);
        var usedAsMembers = declarations.Values.SelectMany(d => d).SelectMany(d => d.Group.Definition.Members).ToHashSet(StringComparer.Ordinal);
        var invalid = new Dictionary<string, (string Code, string Message)>(StringComparer.Ordinal);
        foreach (var (key, definitions) in declarations)
        {
            if (definitions.Skip(1).Any(d => !d.Group.Definition.Members.SequenceEqual(definitions[0].Group.Definition.Members, StringComparer.Ordinal)))
                invalid[key] = ("inconsistent-definition", $"Group '{key}' has inconsistent member sets.");
            else if (individuals.Contains(key) || usedAsMembers.Contains(key))
                invalid[key] = ("group-key-collision", $"Group '{key}' is also declared as an individual/member key.");
        }
        // An entry that refers to any declared group token is a nested definition, even if that target is disabled.
        foreach (var (key, definitions) in declarations)
            if (definitions.Any(d => d.Group.Definition.Members.Any(declarations.ContainsKey)))
                invalid[key] = ("nested-group", $"Group '{key}' contains a group key; nested groups are not supported.");
        var diagnostics = declarations.Where(p => invalid.ContainsKey(p.Key)).SelectMany(p => p.Value.Select(d =>
            new MembershipDiagnostic(d.Registration.ParticipantId, d.Group.CardId, p.Key, invalid[p.Key].Code, invalid[p.Key].Message)))
            .Distinct().OrderBy(d => d.ParticipantId, StringComparer.Ordinal).ThenBy(d => d.CardId, StringComparer.Ordinal)
            .ThenBy(d => d.Key, StringComparer.Ordinal).ToImmutableArray();
        var groups = declarations.Where(p => !invalid.ContainsKey(p.Key)).ToFrozenDictionary(p => p.Key,
            p => new IndexedGroup(p.Key, p.Value[0].Group.Definition.Members), StringComparer.Ordinal);
        var live = new HashSet<string>(individuals, StringComparer.Ordinal);
        var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, group) in groups)
            foreach (var member in group.Members)
            {
                live.Add(member);
                if (!reverse.TryGetValue(member, out var containing)) reverse.Add(member, containing = []);
                containing.Add(key);
            }
        return new((previous?.Revision ?? 0) + 1, inputs, live.ToFrozenSet(StringComparer.Ordinal), groups,
            reverse.ToFrozenDictionary(p => p.Key, p => p.Value.Order(StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal), labels, diagnostics);
    }

    /// <summary>Pass an explicit kind for card hits: disabled group targets must not fall back to a colliding individual.</summary>
    public ResolvedBrushTarget? Resolve(string key, BrushTargetKind? expectedKind = null)
    {
        if (expectedKind is null or BrushTargetKind.Group && Groups.TryGetValue(key, out var group))
            return new(BrushTargetKind.Group, group.Members);
        return expectedKind is null or BrushTargetKind.Member && MemberKeys.Contains(key) ? new(BrushTargetKind.Member, [key]) : null;
    }

    public ImmutableArray<string> ContainingGroups(string member) => MemberGroups.GetValueOrDefault(member, []);
}
