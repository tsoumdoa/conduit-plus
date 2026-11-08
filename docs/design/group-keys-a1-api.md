# Group keys A1 implementation contract

Implemented against main `2e10e9c`, following `group-keys.md` and leaving A2/B1/B2 host work separate. All additions are Rhino/GH-free. Existing BrushState entry points and the two-argument BrushSnapshot constructor remain source-compatible.

## Public API

The following signatures omit existing unchanged CardSpec properties and methods. Collection contents are immutable; string identity and ordering are ordinal and case-sensitive.

```csharp
// CardSpec additions. Metric uses zero/one existing Keys entries for optional Key.
ImmutableDictionary<string, GroupDefinition> CardSpec.Groups { get; init; }
ImmutableArray<string> CardSpec.HoveredKeys { get; init; }
ImmutableDictionary<string, GroupProjection> CardSpec.GroupStates { get; init; }

public sealed record GroupDefinition
{
    public GroupDefinition(ImmutableArray<string> members);
    public ImmutableArray<string> Members { get; }
}
public enum BrushTargetKind { Member, Group, Domain }
public sealed record GroupContribution(string CardId, string Key, string Label, GroupDefinition Definition);
public sealed record MembershipDiagnostic(string ParticipantId, string CardId, string Key, string Code, string Message);
public sealed record IndexedGroup(string Key, ImmutableArray<string> Members);
public sealed record ResolvedBrushTarget(BrushTargetKind Kind, ImmutableArray<string> Members);

public sealed class BrushRegistration
{
    public BrushRegistration(string participantId, IEnumerable<CardSpec> cards, IEnumerable<string> linkKeys);
    public string ParticipantId { get; }
    public ImmutableArray<string> IndividualKeys { get; }
    public ImmutableArray<GroupContribution> Groups { get; }
}
public sealed class MembershipIndex
{
    public static MembershipIndex Create(IEnumerable<BrushRegistration> registrations, MembershipIndex? previous = null);
    public ResolvedBrushTarget? Resolve(string key, BrushTargetKind? expectedKind = null);
    public ImmutableArray<string> ContainingGroups(string member);
    public long Revision { get; }
    public FrozenSet<string> MemberKeys { get; }
    public FrozenDictionary<string, IndexedGroup> Groups { get; }
    public FrozenDictionary<string, ImmutableArray<string>> MemberGroups { get; }
    public FrozenDictionary<string, ImmutableArray<string>> GroupLabels { get; }
    public ImmutableArray<MembershipDiagnostic> Diagnostics { get; }
}

public sealed record BrushOrigin(string ParticipantId, string Surface, long Generation);
public sealed record BrushActionSource(Guid Owner, string CardId, string? SourceSurface = null);
public sealed record BrushEvent(Guid Owner, string CardId, string Key, string Kind,
    BrushTargetKind TargetKind, ImmutableArray<string> AffectedKeys,
    ImmutableArray<string> SelectedKeys, string? SourceSurface = null);
public sealed record GroupProjection(int TotalMemberCount, int SelectedMemberCount,
    int HoveredMemberCount, bool IsDirectTarget, bool IsEnabled = true)
{
    public bool IsFullySelected { get; }
    public bool IsPartiallySelected { get; }
}
public sealed record BrushDomainSnapshot(long Version, long SelectionRevision, long EventRevision,
    MembershipIndex Membership, ImmutableSortedSet<string> SelectedKeys,
    FrozenSet<string> SelectedMemberKeys, string? HoverKey, BrushOrigin? HoverOrigin,
    BrushTargetKind? HoverTargetKind, FrozenSet<string> HoveredKeys,
    ImmutableDictionary<string, GroupProjection> GroupStates, ImmutableArray<BrushEvent> Events)
{
    public long MembershipRevision { get; }
    public CardSpec[] Project(IEnumerable<CardSpec> cards);
}

// BrushState additions, alongside existing Prune, Keys, Toggle(string),
// Clear(), Hover(string?), Project and Snapshot entry points.
BrushDomainSnapshot BrushState.Current { get; }
event Action<BrushDomainSnapshot>? BrushState.Changed;
bool BrushState.ReplaceRegistrations(IEnumerable<BrushRegistration> registrations);
bool BrushState.Toggle(string key, BrushActionSource source);
bool BrushState.Toggle(string key, BrushTargetKind targetKind, BrushActionSource? source = null);
bool BrushState.Clear(BrushActionSource source);
bool BrushState.Hover(BrushOrigin origin, string? key, BrushTargetKind? targetKind = null);
bool BrushState.ClearHover(BrushOrigin origin);

// Legacy snapshot additions; all its arrays are detached copies.
string[] BrushSnapshot.HoveredKeys { get; init; }
BrushOrigin? BrushSnapshot.HoverOrigin { get; init; }
BrushTargetKind? BrushSnapshot.HoverTargetKind { get; init; }

public sealed record MemberGroupLabels(ImmutableArray<string> Labels, int RemainingCount);
public static class GroupTooltip
{
    public static MemberGroupLabels ForMember(MembershipIndex membership, string member);
    public static GroupProjection? ForGroup(BrushDomainSnapshot snapshot, string group);
    public static string[] Lines(GroupProjection group);
}
```

## B1 integration

Own one BrushState per explicit domain. Retain a map of captured BrushRegistrations by participant. At solution end, apply all staged replacements/removals to that map and call `ReplaceRegistrations(map.Values)` exactly once. Unchanged participants stay in the map. Capture registrations from original specs, never projected ones. Capture owns all relevant membership/label data even if callers later mutate CardSpec arrays. A failed registration/index construction does not partially change BrushState.

`Current` and `Changed` publish an immutable, versioned union. Equal transactions publish nothing; label-only changes publish a new Version while retaining MembershipRevision and sharing membership/reverse dictionaries. SelectionRevision changes for accepted selection actions and actual pruning; EventRevision changes only for user actions. Hover changes neither output revision. Origin-only hover changes can publish a Version without changing selection or events. A broker can coalesce output solves against these distinct revisions.

For card hits, use `Select(key, mode, source, card.Groups.ContainsKey(key) ? BrushTargetKind.Group : BrushTargetKind.Member)` and the corresponding kind-aware Hover overload. Plain string overloads retain legacy automatic resolution. Explicit kinds prevent a disabled group target from falling back to an individually declared key with the same spelling. Reconciliation preserves the original hover target kind; disappearance of that target clears hover even if another kind now uses that spelling.

Pass participant/surface/gesture generation in BrushOrigin. Latest accepted Hover wins; ClearHover only clears an exact matching origin. Source owner/card/surface are carried by BrushActionSource into the bounded domain event history. The broker must reject stale document/participant/publication generations before committing; origin equality protects clears, not late host publications. Thread scheduling, Scope token/document identity, attachment/lifetime, disabled-origin eligibility and bounded reconciliation feedback remain B1/B2 responsibilities. No GH broker, generation queue or solve loop is implemented here.

## Projection and A2 integration

Canonical selection is `Current.SelectedKeys` (sorted unique members). Geometry can use the cached `SelectedMemberKeys` and `HoveredKeys` hash sets for constant-time membership checks. Selection hash/sorted snapshots are retained across hover updates. Only reverse edges touched by effective hover/selection are counted during interaction; there are no key/group all-pairs scans. Whole-card projection is linear in the supplied entry count plus group-target dictionary lookups.

Projected CardSpec.SelectedKeys contains selected individual entries, never group tokens. CardSpec.HoveredKeys contains effective hovered individual entries in entry order. HoverKey retains the originating target only on cards containing that enabled target. GroupStates supplies full/partial selection, hovered/total counts and direct-target status. Disabled group targets have IsEnabled=false and zero effective counts. Empty enabled groups retain a direct hover/zero-member tooltip but cannot change selection or create events. OwnerHasSelection enables Clear on every keyed card, including keyed Metric, when any member in the domain is selected.

Group labels come from Bar/Legend entry labels or Metric title, falling back to the key. Repeated definitions may have different display labels; member metadata deduplicates labels, sorts them ordinally and keeps five plus RemainingCount. GroupTooltip.Lines includes member, selected and hovered counts.

The current Overlay/Canvas hosts and painters remain unchanged. A2 must consume effective sets/projections, add input/hit/painter/tooltip behavior, forward action sources/events, and strip **all** hover-derived fields for captures (HoverKey, HoveredKeys, group HoveredMemberCount and IsDirectTarget). Core BrushEvent has the additive fields now; the existing host OverlayInteraction wire record/output adapter remains for A2/B2 to migrate. Metric values are never recomputed. No inputs or TableScroll.Reveal changes overlap PR #14.

## Validation choices and design interpretation

No semantic departures from the group brush contract. Local malformed definitions throw ArgumentException (including nesting within one card); cross-card namespace collisions, nesting and inconsistent definitions produce participant/card diagnostics and disable the affected group targets. Individual keys survive collisions. Members declared only by disabled definitions are absent from the live universe unless independently declared elsewhere. Reserved Link control keys continue to be ignored by the compatibility path, as before; blank/reserved card/group/member keys are rejected. GH branch counts/path diagnostics remain A2.

MembershipRevision covers registration membership declarations and diagnostic provenance, not numeric values, source Rev, sort/scroll, projection or tooltip labels. Label-only updates share the existing membership/reverse index. There is no remembered selection provenance and no automatic selection of future members. Reconciliation intersects selection once against the complete committed union and emits no event.

## Verification

Run the full warnings-as-errors build and solution tests. Existing tests are unchanged. New tests cover validation/roundtrip, repeated and conflicting definitions, typed collision handling, overlap, event contents/bounds, empty groups, nonrecursive hover/origins, atomic union reconciliation, immutable snapshots, revision/index reuse, tooltip caps, dirty-card projection and mixed-action set-intersection invariants.

The `TwoThousandMemberOverlappingGroupCoreBenchmark` test uses 2,000 members, 200 overlapping groups and 20,000 edges, 30 warmups and 300 measured samples. It reports index construction, Toggle+Hover and full projection separately through xUnit output. Its generous p95 regression bound is 100 ms for each interaction/projection measurement; the design's 5 ms interaction target is assessed from reported measurements, not enforced as a machine-independent latency promise. Raster and geometry are excluded.
Final local validation (2026-11-07): `dotnet build ConduitPlus.sln -c Debug` succeeded with 0 warnings/errors; `dotnet test ConduitPlus.sln -c Debug` passed all 172 tests. `git diff --check` passed. Final isolated Debug benchmark: index construction 32.589 ms; Toggle+Hover p50 0.451 ms / p95 0.868 ms; projection p50 0.242 ms / p95 0.451 ms. These are local Core-only measurements, not an end-to-end Rhino latency claim. No commit, push or developer installation was performed.
