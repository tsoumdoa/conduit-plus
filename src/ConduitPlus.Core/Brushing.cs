using System.Collections.Frozen;
using System.Collections.Immutable;

namespace ConduitPlus.Core;

// Retained for existing hosts; arrays are detached copies. New domain consumers use Current.
public sealed record BrushSnapshot(string[] SelectedKeys, string? HoverKey)
{
    public string[] HoveredKeys { get; init; } = [];
    public BrushOrigin? HoverOrigin { get; init; }
    public BrushTargetKind? HoverTargetKind { get; init; }
}

public sealed record BrushOrigin(string ParticipantId, string Surface, long Generation);
public sealed record BrushActionSource(Guid Owner, string CardId, string? SourceSurface = null);
public sealed record BrushEvent(Guid Owner, string CardId, string Key, string Kind, BrushTargetKind TargetKind,
    ImmutableArray<string> AffectedKeys, ImmutableArray<string> SelectedKeys, string? SourceSurface = null);
public sealed record GroupProjection(int TotalMemberCount, int SelectedMemberCount, int HoveredMemberCount, bool IsDirectTarget, bool IsEnabled = true)
{
    public bool IsFullySelected => TotalMemberCount > 0 && SelectedMemberCount == TotalMemberCount;
    public bool IsPartiallySelected => SelectedMemberCount > 0 && SelectedMemberCount < TotalMemberCount;
}

/// <summary>Versioned immutable publication for a future shared-domain broker; contains no host objects.</summary>
public sealed record BrushDomainSnapshot(long Version, long SelectionRevision, long EventRevision, MembershipIndex Membership,
    ImmutableSortedSet<string> SelectedKeys, FrozenSet<string> SelectedMemberKeys, string? HoverKey, BrushOrigin? HoverOrigin, BrushTargetKind? HoverTargetKind, FrozenSet<string> HoveredKeys,
    ImmutableDictionary<string, GroupProjection> GroupStates, ImmutableArray<BrushEvent> Events)
{
    public long MembershipRevision => Membership.Revision;

    public CardSpec[] Project(IEnumerable<CardSpec> cards) => cards.Select(c =>
    {
        var keys = BrushState.Keys(c).ToArray();
        var groups = c.Groups.Keys.ToImmutableDictionary(k => k,
            k => GroupStates.GetValueOrDefault(k) ?? new GroupProjection(0, 0, 0, false, false), StringComparer.Ordinal);
        return c with
        {
            // These remain member keys. Full/partial group targets are represented by GroupStates.
            SelectedKeys = keys.Where(k => !c.Groups.ContainsKey(k) && SelectedMemberKeys.Contains(k)).ToArray(),
            HoveredKeys = keys.Where(k => !c.Groups.ContainsKey(k) && HoveredKeys.Contains(k)).ToImmutableArray(),
            GroupStates = groups,
            HoverKey = HoverKey is not null && keys.Contains(HoverKey, StringComparer.Ordinal) &&
                (!c.Groups.ContainsKey(HoverKey) || Membership.Groups.ContainsKey(HoverKey)) ? HoverKey : null,
            OwnerHasSelection = keys.Length > 0 && SelectedKeys.Count > 0
        };
    }).ToArray();
}

/// <summary>
/// Canonical member selection and indexed group interaction. The host supplies synchronization/lifetime.
/// ReplaceRegistrations is a complete, atomic union transaction: B1 stages participants externally and commits once.
/// </summary>
public sealed class BrushState
{
    private static readonly BrushOrigin LegacyOrigin = new("private", "legacy", 0);
    private static readonly BrushActionSource LegacySource = new(Guid.Empty, "");
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _selectedCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _hoverCounts = new(StringComparer.Ordinal);
    private MembershipIndex _membership = MembershipIndex.Create([]);
    private ImmutableDictionary<string, GroupProjection> _groups = ImmutableDictionary<string, GroupProjection>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableSortedSet<string> _selectedSnapshot = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
    private FrozenSet<string> _selectedMemberKeys = FrozenSet<string>.Empty;
    private FrozenSet<string> _hovered = FrozenSet<string>.Empty;
    private ImmutableArray<BrushEvent> _events = [];
    private string? _hover;
    private BrushOrigin? _origin;
    private BrushTargetKind? _hoverKind;
    private long _version, _selectionRevision, _eventRevision;
    public BrushDomainSnapshot Current { get; private set; }
    public event Action<BrushDomainSnapshot>? Changed;

    public BrushState() => Current = MakeSnapshot();
    public BrushSnapshot Snapshot => new(Current.SelectedKeys.ToArray(), _hover) { HoveredKeys = _hovered.Order(StringComparer.Ordinal).ToArray(), HoverOrigin = _origin, HoverTargetKind = _hoverKind };
    public static IEnumerable<string> Keys(CardSpec c) => c.Kind switch
    {
        CardKind.Table => c.Rows.Select(r => r.Key),
        CardKind.Metric or CardKind.Bar or CardKind.Line => c.Keys,
        CardKind.Legend when !c.Min.HasValue => c.Keys,
        _ => []
    };

    /// <summary>Compatibility entry point for one private owner. No rebuild occurs for unchanged membership.</summary>
    public void Prune(IEnumerable<CardSpec> cards, IEnumerable<string> linkKeys) => ReplaceRegistrations([new("private", cards, linkKeys)]);

    /// <summary>Commit the entire active participant union once, retaining no caller-owned mutable arrays.</summary>
    public bool ReplaceRegistrations(IEnumerable<BrushRegistration> registrations)
    {
        var next = MembershipIndex.Create(registrations, _membership);
        if (ReferenceEquals(next, _membership)) return false;
        var changedMembership = next.Revision != _membership.Revision;
        _membership = next;
        if (changedMembership)
        {
            var oldCount = _selected.Count;
            _selected.IntersectWith(next.MemberKeys);
            if (oldCount != _selected.Count)
            {
                _selectionRevision++;
                CacheSelection();
            }
            var target = _hover is null ? null : next.Resolve(_hover, _hoverKind);
            if (target is null) { _hover = null; _origin = null; _hoverKind = null; }
            _hovered = (target?.Members ?? []).ToFrozenSet(StringComparer.Ordinal);
            _selectedCounts.Clear(); _hoverCounts.Clear();
            CountMembers(_selected, _selectedCounts, 1);
            CountMembers(_hovered, _hoverCounts, 1);
            _groups = next.Groups.Keys.ToImmutableDictionary(k => k, Projection, StringComparer.Ordinal);
        }
        Publish();
        return true;
    }

    public bool Toggle(string key) => Toggle(key, LegacySource);
    public bool Toggle(string key, BrushActionSource source) => Toggle(key, source, null);
    public bool Toggle(string key, BrushTargetKind targetKind, BrushActionSource? source = null) => Toggle(key, source ?? LegacySource, targetKind);
    private bool Toggle(string key, BrushActionSource source, BrushTargetKind? expectedKind)
    {
        var target = _membership.Resolve(key, expectedKind);
        if (target is null || target.Members.Length == 0) return false;
        var remove = target.Members.All(_selected.Contains);
        var affected = target.Members.Where(k => remove || !_selected.Contains(k)).ToImmutableArray();
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in affected)
        {
            if (remove) _selected.Remove(member); else _selected.Add(member);
            CountMember(member, _selectedCounts, remove ? -1 : 1, touched);
        }
        RefreshGroups(touched);
        _selectionRevision++;
        AddEvent(source, key, "click", target.Kind, affected);
        Publish(); return true;
    }

    public bool Clear() => Clear(LegacySource);
    public bool Clear(BrushActionSource source)
    {
        if (_selected.Count == 0) return false;
        var affected = _selected.Order(StringComparer.Ordinal).ToImmutableArray();
        var touched = _selectedCounts.Keys.ToArray();
        _selected.Clear(); _selectedCounts.Clear(); RefreshGroups(touched);
        _selectionRevision++;
        AddEvent(source, CardMeasure.ClearKey, "clear", BrushTargetKind.Domain, affected);
        Publish(); return true;
    }

    public bool Hover(string? key) => Hover(LegacyOrigin, key);
    public bool Hover(BrushOrigin origin, string? key, BrushTargetKind? targetKind = null)
    {
        var target = key is null ? null : _membership.Resolve(key, targetKind);
        if (target is null) key = null;
        var nextOrigin = key is null ? null : origin;
        if (_hover == key && _origin == nextOrigin && _hoverKind == target?.Kind) return false;
        var touched = new HashSet<string>(_hoverCounts.Keys, StringComparer.Ordinal);
        if (_hover is not null && _membership.Groups.ContainsKey(_hover)) touched.Add(_hover);
        _hover = key; _origin = nextOrigin; _hoverKind = target?.Kind;
        _hovered = (target?.Members ?? []).ToFrozenSet(StringComparer.Ordinal);
        _hoverCounts.Clear();
        foreach (var member in _hovered) CountMember(member, _hoverCounts, 1, touched);
        if (key is not null && _membership.Groups.ContainsKey(key)) touched.Add(key);
        RefreshGroups(touched);
        Publish(); return true;
    }

    /// <summary>A stale leave from another participant/surface/gesture cannot clear the winning hover.</summary>
    public bool ClearHover(BrushOrigin origin) => _origin == origin && Hover(origin, null);
    public CardSpec[] Project(IEnumerable<CardSpec> cards) => Current.Project(cards);

    private void CountMembers(IEnumerable<string> members, Dictionary<string, int> counts, int delta)
    { foreach (var member in members) CountMember(member, counts, delta, null); }
    private void CountMember(string member, Dictionary<string, int> counts, int delta, HashSet<string>? touched)
    {
        foreach (var group in _membership.ContainingGroups(member))
        {
            var count = counts.GetValueOrDefault(group) + delta;
            if (count == 0) counts.Remove(group); else counts[group] = count;
            touched?.Add(group);
        }
    }
    private GroupProjection Projection(string key) => new(_membership.Groups[key].Members.Length,
        _selectedCounts.GetValueOrDefault(key), _hoverCounts.GetValueOrDefault(key), _hover == key);
    private void RefreshGroups(IEnumerable<string> touched)
    {
        var builder = _groups.ToBuilder();
        foreach (var key in touched) builder[key] = Projection(key);
        _groups = builder.ToImmutable();
    }
    private void AddEvent(BrushActionSource source, string key, string kind, BrushTargetKind targetKind, ImmutableArray<string> affected)
    {
        CacheSelection();
        var item = new BrushEvent(source.Owner, source.CardId, key, kind, targetKind, affected,
            _selectedSnapshot.ToImmutableArray(), source.SourceSurface);
        if (_events.Length == 32) _events = _events.RemoveAt(0);
        _events = _events.Add(item); _eventRevision++;
    }
    private void CacheSelection()
    {
        _selectedSnapshot = _selected.ToImmutableSortedSet(StringComparer.Ordinal);
        _selectedMemberKeys = _selected.ToFrozenSet(StringComparer.Ordinal);
    }
    private BrushDomainSnapshot MakeSnapshot() => new(_version, _selectionRevision, _eventRevision, _membership,
        _selectedSnapshot, _selectedMemberKeys, _hover, _origin, _hoverKind, _hovered, _groups, _events);
    private void Publish()
    {
        _version++; Current = MakeSnapshot(); Changed?.Invoke(Current);
    }
}
