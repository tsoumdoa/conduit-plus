using System.Collections.Immutable;
using System.Diagnostics;
using ConduitPlus.Core;
using Xunit;
using Xunit.Abstractions;

namespace ConduitPlus.Core.Tests;

public sealed class GroupKeyTests(ITestOutputHelper output)
{
    private static CardSpec Members(params string[] keys) => new()
    {
        Id = "members", Kind = CardKind.Bar, Keys = keys, Labels = keys,
        Values = keys.Select(_ => (double?)1).ToArray()
    };
    private static CardSpec Groups(params (string Key, string[] Members)[] groups) => new()
    {
        Id = "groups", Kind = CardKind.Bar, Keys = groups.Select(g => g.Key).ToArray(),
        Labels = groups.Select(g => g.Key).ToArray(), Values = groups.Select(_ => (double?)1).ToArray(),
        Groups = groups.ToImmutableDictionary(g => g.Key, g => new GroupDefinition(g.Members.ToImmutableArray()), StringComparer.Ordinal)
    };
    private static BrushState Brush(params CardSpec[] cards)
    { var brush = new BrushState(); brush.Prune(cards, []); return brush; }

    [Fact]
    public void DefinitionsAreImmutableOrdinalSetsAndRoundTripThroughCardStore()
    {
        var card = Groups(("group", ["b", "a", "b", "A"]));
        Assert.Equal(new[] { "A", "a", "b" }, card.Groups["group"].Members);
        var store = new CardStore(); store.Update([card]);
        var restored = Assert.Single(store.Snapshot);
        Assert.Equal<string>(card.Groups["group"].Members, restored.Groups["group"].Members);
        Assert.Empty(store.Update([card]));
        var brush = Brush(restored); Assert.True(brush.Toggle("group"));
        Assert.Equal(new[] { "A", "a", "b" }, brush.Snapshot.SelectedKeys);
        // Definitions own their immutable members even when the source entry arrays are later changed.
        card.Keys[0] = "changed";
        Assert.NotNull(brush.Current.Membership.Resolve("group"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\u0001clear")]
    [InlineData("\u0001table-sort:0")]
    public void BlankAndReservedGroupAndMemberKeysAreRejected(string key)
    {
        Assert.Throws<ArgumentException>(() => new GroupDefinition([key]));
        Assert.Throws<ArgumentException>(() => Groups((key, ["a"])).Validate());
    }

    [Fact]
    public void GroupsRequireAnEligibleExplicitEntryAndRejectNesting()
    {
        var group = Groups(("g", ["a"]));
        Assert.Throws<ArgumentException>(() => (group with { Keys = ["other"] }).Validate());
        Assert.Throws<ArgumentException>(() => (group with { Kind = CardKind.Line }).Validate());
        Assert.Throws<ArgumentException>(() => Groups(("g", ["h"]), ("h", ["a"])).Validate());
        Assert.Throws<ArgumentException>(() => Groups(("g", ["g"])).Validate());
        var gradient = group with { Kind = CardKind.Legend, Keys = [], Labels = [], Colors = ["#FFFFFF", "#000000"], Min = 0, Max = 1 };
        Assert.Throws<ArgumentException>(gradient.Validate);
    }

    [Fact]
    public void MetricSupportsIndividualOrGroupKeyAndCategoricalLegendSupportsGroups()
    {
        var metric = new CardSpec { Id = "metric", Kind = CardKind.Metric, Title = "Governing", Value = "99", Keys = ["a"] };
        metric.Validate(); var single = Brush(metric); Assert.True(single.Toggle("a"));
        Assert.Equal(new[] { "a" }, single.Project([metric])[0].SelectedKeys);
        Assert.Throws<ArgumentException>(() => (metric with { Keys = ["a", "b"] }).Validate());
        var group = metric with { Keys = ["g"], Groups = Groups(("g", ["a", "b"])).Groups };
        var legend = Groups(("g", ["b", "a"])) with { Id = "legend", Kind = CardKind.Legend, Colors = ["#FFFFFF"] };
        group.Validate(); legend.Validate();
        var brush = Brush(group, legend); brush.Toggle("g");
        Assert.All(brush.Project([group, legend]), c => Assert.True(c.GroupStates["g"].IsFullySelected));
        Assert.Equal("99", brush.Project([group])[0].Value);
    }

    [Fact]
    public void IdenticalRepeatedDefinitionsAreHarmlessAndConflictsAreLocalAndDiagnosed()
    {
        var first = Groups(("bad", ["a", "b"]), ("good", ["z"]));
        var duplicate = first with { Id = "duplicate", Labels = ["Other bad label", "Other good label"] };
        var brush = Brush(first, duplicate);
        Assert.Empty(brush.Current.Membership.Diagnostics);
        Assert.Equal(2, brush.Current.Membership.Groups.Count);
        Assert.Equal(new[] { "bad" }, brush.Current.Membership.ContainingGroups("a"));
        var conflict = Groups(("bad", ["c"])) with { Id = "conflict" };
        brush.Prune([first, duplicate, conflict, Members("individual")], []);
        Assert.Null(brush.Current.Membership.Resolve("bad")); Assert.False(brush.Toggle("bad"));
        Assert.True(brush.Toggle("good")); Assert.True(brush.Toggle("individual"));
        Assert.False(brush.Toggle("a")); // a exists only in invalid definitions
        Assert.Equal(new[] { "bad", "bad", "bad" }, brush.Current.Membership.Diagnostics.Select(d => d.Key));
        Assert.All(brush.Current.Membership.Diagnostics, d => Assert.Equal("inconsistent-definition", d.Code));
        Assert.False(brush.Project([first])[0].GroupStates["bad"].IsEnabled);
    }

    [Fact]
    public void IndividualAndLinkCollisionsDisableOnlyGroupTargetAndKeepIndividualsUsable()
    {
        var group = Groups(("g", ["a"]), ("valid", ["b"]));
        var brush = Brush(group, Members("g"));
        Assert.Equal(BrushTargetKind.Member, brush.Current.Membership.Resolve("g")!.Kind);
        Assert.True(brush.Toggle("g")); Assert.Equal(new[] { "g" }, brush.Snapshot.SelectedKeys);
        Assert.False(brush.Current.Membership.Groups.ContainsKey("g")); Assert.True(brush.Toggle("valid"));
        brush.Prune([group], ["g"]);
        Assert.Equal(BrushTargetKind.Member, brush.Current.Membership.Resolve("g")!.Kind);
        Assert.Single(brush.Current.Membership.Diagnostics);
        // Case-sensitive namespace.
        brush.Prune([group], ["G"]); Assert.Empty(brush.Current.Membership.Diagnostics);
    }

    [Fact]
    public void DisabledGroupHitsCannotActOnCollidingIndividualsAndReconciliationPreservesTargetKind()
    {
        var groups = Groups(("g", ["a"])); var individual = Members("g");
        var brush = Brush(groups); var origin = new BrushOrigin("one", "canvas", 1);
        brush.Hover(origin, "g", BrushTargetKind.Group);
        brush.Prune([groups, individual], []);
        Assert.Null(brush.Current.HoverKey); // original group disappeared, although the individual key is now live
        Assert.False(brush.Toggle("g", BrushTargetKind.Group)); Assert.Empty(brush.Current.Events);
        Assert.Null(brush.Current.Membership.Resolve("g", BrushTargetKind.Group));
        Assert.True(brush.Toggle("g", BrushTargetKind.Member));
        brush.Hover(origin, "g", BrushTargetKind.Member);
        Assert.Equal(BrushTargetKind.Member, brush.Current.HoverTargetKind);
        var projected = brush.Project([groups, individual]);
        Assert.Null(projected[0].HoverKey); Assert.Equal("g", projected[1].HoverKey);
        Assert.Empty(projected[0].SelectedKeys); Assert.False(projected[0].GroupStates["g"].IsEnabled);
        var events = brush.Current.EventRevision;
        Assert.False(brush.Toggle("g", BrushTargetKind.Group)); Assert.Equal(events, brush.Current.EventRevision);
    }

    [Fact]
    public void CrossCardNestingDisablesContributingGroupsWithoutRecursiveExpansion()
    {
        var brush = Brush(Groups(("g", ["h"])), Groups(("h", ["a"])) with { Id = "other" }, Members("a"));
        Assert.Empty(brush.Current.Membership.Groups);
        Assert.Equal(2, brush.Current.Membership.Diagnostics.Length);
        Assert.True(brush.Toggle("a")); Assert.False(brush.Toggle("g")); Assert.False(brush.Toggle("h"));
    }

    [Fact]
    public void OverlapToggleAddsPartialAndRemovesWholeSetWithoutProvenance()
    {
        var brush = Brush(Groups(("bedroom", ["a", "b"]), ("residential", ["b", "c"])));
        brush.Toggle("a"); brush.Toggle("outside");
        Assert.True(brush.Current.GroupStates["bedroom"].IsPartiallySelected);
        brush.Toggle("bedroom"); Assert.Equal(new[] { "a", "b" }, brush.Snapshot.SelectedKeys);
        brush.Toggle("residential"); Assert.Equal(new[] { "a", "b", "c" }, brush.Snapshot.SelectedKeys);
        brush.Toggle("bedroom"); Assert.Equal(new[] { "c" }, brush.Snapshot.SelectedKeys);
        Assert.True(brush.Current.GroupStates["residential"].IsPartiallySelected);
        brush.Toggle("residential"); Assert.Equal(new[] { "b", "c" }, brush.Snapshot.SelectedKeys);
        brush.Clear(); Assert.Empty(brush.Snapshot.SelectedKeys);
    }

    [Fact]
    public void EventsHaveOneActionSortedActualChangesAndImmutablePostActionSnapshots()
    {
        var brush = Brush(Groups(("g", ["b", "a"])), Members("z"));
        var source = new BrushActionSource(Guid.NewGuid(), "card", "canvas");
        brush.Toggle("a", source); brush.Toggle("z", source); brush.Toggle("g", source);
        var evt = brush.Current.Events[^1];
        Assert.Equal(source.Owner, evt.Owner); Assert.Equal("card", evt.CardId); Assert.Equal("canvas", evt.SourceSurface);
        Assert.Equal("g", evt.Key); Assert.Equal("click", evt.Kind); Assert.Equal(BrushTargetKind.Group, evt.TargetKind);
        Assert.Equal(new[] { "b" }, evt.AffectedKeys); Assert.Equal(new[] { "a", "b", "z" }, evt.SelectedKeys);
        brush.Toggle("g", source);
        Assert.Equal(new[] { "a", "b" }, brush.Current.Events[^1].AffectedKeys);
        Assert.Equal(new[] { "z" }, brush.Current.Events[^1].SelectedKeys);
        Assert.Equal(new[] { "a", "b", "z" }, evt.SelectedKeys);
        brush.Clear(source); var clear = brush.Current.Events[^1];
        Assert.Equal("clear", clear.Kind); Assert.Equal(BrushTargetKind.Domain, clear.TargetKind);
        Assert.Equal(new[] { "z" }, clear.AffectedKeys); Assert.Empty(clear.SelectedKeys);
        var json = CardJson.Serialize(evt);
        Assert.Contains("\"targetKind\":\"group\"", json); Assert.Contains("\"affectedKeys\":[\"b\"]", json);
    }

    [Fact]
    public void EmptyUnknownActionsAndPruningEmitNoEventsAndHistoryIsBounded()
    {
        var brush = Brush(Groups(("empty", [])), Members("a"));
        Assert.False(brush.Toggle("empty")); Assert.False(brush.Toggle("missing")); Assert.False(brush.Clear());
        Assert.Empty(brush.Current.Events);
        Assert.True(brush.Hover("empty")); Assert.Empty(brush.Current.HoveredKeys.Order(StringComparer.Ordinal));
        Assert.True(brush.Current.GroupStates["empty"].IsDirectTarget);
        Assert.Equal("0 members", GroupTooltip.Lines(brush.Current.GroupStates["empty"])[0]);
        for (var i = 0; i < 35; i++) brush.Toggle("a");
        Assert.Equal(32, brush.Current.Events.Length); Assert.Equal(35, brush.Current.EventRevision);
        var before = brush.Current;
        brush.Prune([], []);
        Assert.Empty(brush.Current.SelectedKeys); Assert.Null(brush.Current.HoverKey);
        Assert.Equal(before.EventRevision, brush.Current.EventRevision); Assert.Equal(before.Events, brush.Current.Events);
        Assert.True(brush.Current.SelectionRevision > before.SelectionRevision);
    }

    [Fact]
    public void HoverIsNonRecursiveWithCountsAndOriginalTargetPreserved()
    {
        var groups = Groups(("bedroom", ["a", "b"]), ("residential", ["b", "c"]));
        var members = Members("a", "b", "c"); var brush = Brush(groups, members);
        var origin = new BrushOrigin("participant", "canvas", 7);
        brush.Hover(origin, "b");
        Assert.Equal(new[] { "b" }, brush.Current.HoveredKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, brush.Current.GroupStates["bedroom"].HoveredMemberCount);
        Assert.Equal(1, brush.Current.GroupStates["residential"].HoveredMemberCount);
        Assert.All(brush.Current.GroupStates.Values, g => Assert.False(g.IsDirectTarget));
        brush.Hover(origin, "bedroom");
        Assert.Equal("bedroom", brush.Current.HoverKey); Assert.Equal(origin, brush.Current.HoverOrigin);
        Assert.Equal(new[] { "a", "b" }, brush.Current.HoveredKeys.Order(StringComparer.Ordinal)); Assert.DoesNotContain("c", brush.Current.HoveredKeys.Order(StringComparer.Ordinal));
        Assert.True(brush.Current.GroupStates["bedroom"].IsDirectTarget);
        Assert.Equal(2, brush.Current.GroupStates["bedroom"].HoveredMemberCount);
        Assert.Equal(1, brush.Current.GroupStates["residential"].HoveredMemberCount);
        var projected = brush.Project([groups, members]);
        Assert.Equal("bedroom", projected[0].HoverKey); Assert.Null(projected[1].HoverKey);
        Assert.Equal(new[] { "a", "b" }, projected[1].HoveredKeys);
        Assert.False(projected[0].GroupStates["residential"].IsDirectTarget);
    }

    [Fact]
    public void HoverOriginClearIsSafeAndSnapshotsRemainImmutable()
    {
        var brush = Brush(Groups(("g", ["a", "b"])));
        var first = new BrushOrigin("one", "canvas", 1); var later = new BrushOrigin("two", "overlay", 2);
        brush.Hover(first, "a"); var saved = brush.Current;
        brush.Hover(later, "g");
        Assert.False(brush.ClearHover(first)); Assert.Equal("g", brush.Current.HoverKey);
        Assert.False(brush.ClearHover(later with { Generation = 1 }));
        Assert.True(brush.ClearHover(later)); Assert.Null(brush.Current.HoverOrigin);
        Assert.Equal("a", saved.HoverKey); Assert.Equal(new[] { "a" }, saved.HoveredKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, saved.GroupStates["g"].HoveredMemberCount);
        Assert.Equal(0, brush.Current.GroupStates["g"].HoveredMemberCount);
    }

    [Fact]
    public void ReconcileUsesUnionAndNeverSelectsFutureMembersOrKeepsGroupIntent()
    {
        var groups = Groups(("g", ["a", "b"])); var brush = Brush(groups);
        brush.Toggle("g"); brush.Hover("g");
        var history = brush.Current.Events;
        brush.Prune([Groups(("g", ["b", "c"]))], ["a"]);
        Assert.Equal(new[] { "a", "b" }, brush.Current.SelectedKeys);
        Assert.Equal(new[] { "b", "c" }, brush.Current.HoveredKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, brush.Current.GroupStates["g"].SelectedMemberCount);
        Assert.Equal(history, brush.Current.Events);
        brush.Prune([Groups(("other", ["a"]))], ["b"]);
        Assert.Equal(new[] { "a", "b" }, brush.Current.SelectedKeys); Assert.Null(brush.Current.HoverKey);
        brush.Prune([Groups(("other", ["a"]))], []);
        Assert.Equal(new[] { "a" }, brush.Current.SelectedKeys);
        // A replaced group definition cannot revive its old intent.
        brush.Prune([Groups(("g", ["c"]))], []); Assert.Empty(brush.Current.SelectedKeys);
    }

    [Fact]
    public void AtomicRegistrationUnionRetainsOtherParticipantsAndPublishesOnce()
    {
        var brush = new BrushState(); var notifications = new List<BrushDomainSnapshot>(); brush.Changed += notifications.Add;
        var one = new BrushRegistration("one", [Groups(("g", ["a", "b"]))], []);
        var two = new BrushRegistration("two", [Members("b")], ["link"]);
        brush.ReplaceRegistrations([one, two]); brush.Toggle("g"); brush.Toggle("link");
        notifications.Clear();
        // a moves from the first participant to the second within a single union transaction.
        var replacementOne = new BrushRegistration("one", [Groups(("g", ["c"]))], []);
        var replacementTwo = new BrushRegistration("two", [Members("a", "b")], ["link"]);
        brush.ReplaceRegistrations([replacementOne, replacementTwo]);
        Assert.Single(notifications); Assert.Equal(new[] { "a", "b", "link" }, brush.Current.SelectedKeys);
        Assert.Equal(2, brush.Current.Events.Length);
        var previous = brush.Current;
        Assert.False(brush.ReplaceRegistrations([replacementTwo, replacementOne])); Assert.Same(previous, brush.Current);
        brush.ReplaceRegistrations([replacementTwo]); Assert.Equal(new[] { "a", "b", "link" }, brush.Current.SelectedKeys);
        Assert.Throws<ArgumentException>(() => brush.ReplaceRegistrations([one, one]));
        var isolated = new BrushState(); isolated.ReplaceRegistrations([replacementTwo]); Assert.Empty(isolated.Current.SelectedKeys);
    }

    [Fact]
    public void IndexIsReusedForViewDataAndInteractionChangesAndLabelsUpdateWithoutRebuildingEdges()
    {
        var card = Groups(("g", ["a", "b"])); var brush = Brush(card); var index = brush.Current.Membership;
        brush.Toggle("a"); brush.Hover("g"); brush.Project([card]);
        brush.Prune([card with { Values = [77], HoverKey = "g", SelectedKeys = ["a"], Rev = 99 }], []);
        Assert.Same(index, brush.Current.Membership);
        var selectedSet = brush.Current.SelectedMemberKeys; var selectedOutput = brush.Current.SelectedKeys;
        brush.Hover("a");
        Assert.Same(selectedSet, brush.Current.SelectedMemberKeys); Assert.Same(selectedOutput, brush.Current.SelectedKeys);
        var state = brush.Current;
        brush.Prune([card with { Labels = ["Bedroom"] }], []);
        Assert.Equal(index.Revision, brush.Current.MembershipRevision);
        Assert.Same(index.Groups, brush.Current.Membership.Groups); Assert.Same(index.MemberGroups, brush.Current.Membership.MemberGroups);
        Assert.True(brush.Current.Version > state.Version);
        Assert.Equal(new[] { "Bedroom" }, GroupTooltip.ForMember(brush.Current.Membership, "a").Labels);
        brush.Prune([Groups(("g", ["a", "c"]))], []);
        Assert.True(brush.Current.MembershipRevision > index.Revision);
    }

    [Fact]
    public void GroupTooltipLabelsAreDeduplicatedCappedAndCountsAreProjected()
    {
        var groups = Groups(Enumerable.Range(0, 8).Select(i => ($"g{i}", new[] { "member" })).ToArray()) with
        { Labels = ["A", "B", "C", "D", "E", "F", "G", "G"] };
        var brush = Brush(groups); brush.Toggle("member"); brush.Hover("member");
        var metadata = GroupTooltip.ForMember(brush.Current.Membership, "member");
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, metadata.Labels); Assert.Equal(2, metadata.RemainingCount);
        var counts = GroupTooltip.ForGroup(brush.Current, "g0")!;
        Assert.Equal(1, counts.TotalMemberCount); Assert.Equal(1, counts.SelectedMemberCount); Assert.Equal(1, counts.HoveredMemberCount);
        Assert.Empty(GroupTooltip.ForMember(brush.Current.Membership, "missing").Labels);
        Assert.Null(GroupTooltip.ForGroup(brush.Current, "missing"));
    }

    [Fact]
    public void ProjectionChangesOnlyTouchedCardsAndDomainSelectionEnablesClear()
    {
        var first = Groups(("g", ["a", "b"])); var second = Members("other");
        var brush = Brush(first, second); brush.Toggle("other");
        var store = new CardStore(); store.Update(brush.Project([first, second]));
        brush.Hover("a"); var dirty = store.Update(brush.Project([first, second]));
        Assert.Equal("groups", Assert.Single(dirty).Id);
        Assert.All(brush.Project([first, second]), c => Assert.True(c.OwnerHasSelection));
        Assert.Empty(brush.Project([first])[0].SelectedKeys); // no group tokens in member fields
        brush.Hover(null); store.Update(brush.Project([first, second]));
        Assert.Empty(store.Update(brush.Project([first, second])));
    }

    [Fact]
    public void IncrementalCountsMatchSetIntersectionAcrossMixedActionsAndMembershipRevisions()
    {
        var random = new Random(2411);
        var individuals = Members("a", "b", "c", "d", "outside");
        var card = Groups(("g", ["a", "b"]), ("h", ["b", "c"]), ("empty", []));
        var brush = Brush(card, individuals);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        string? hover = null;
        for (var i = 0; i < 250; i++)
        {
            var targets = new[] { "a", "b", "c", "d", "outside", "g", "h", "empty" };
            var key = targets[random.Next(targets.Length)];
            var members = card.Groups.TryGetValue(key, out var group) ? group.Members.ToArray() : new[] { key };
            switch (random.Next(5))
            {
                case 0:
                case 1:
                    if (members.Length > 0)
                    {
                        if (members.All(expected.Contains)) expected.ExceptWith(members); else expected.UnionWith(members);
                    }
                    brush.Toggle(key); break;
                case 2:
                    hover = key; brush.Hover(key); break;
                case 3:
                    expected.Clear(); brush.Clear(); break;
                default:
                    card = Groups(("g", i % 2 == 0 ? ["a", "b"] : ["c", "d"]), ("h", ["b", "c"]), ("empty", []));
                    brush.Prune([card, individuals], []); break;
            }
            var effectiveHover = hover is null ? [] : card.Groups.TryGetValue(hover, out var hoveredGroup) ? hoveredGroup.Members.ToArray() : new[] { hover };
            Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), brush.Current.SelectedKeys.ToArray());
            Assert.Equal(effectiveHover.Order(StringComparer.Ordinal), brush.Current.HoveredKeys.Order(StringComparer.Ordinal));
            foreach (var (groupKey, definition) in card.Groups)
            {
                var projection = brush.Current.GroupStates[groupKey];
                Assert.Equal(definition.Members.Count(expected.Contains), projection.SelectedMemberCount);
                Assert.Equal(definition.Members.Intersect(effectiveHover, StringComparer.Ordinal).Count(), projection.HoveredMemberCount);
                Assert.Equal(hover == groupKey, projection.IsDirectTarget);
            }
        }
    }

    [Fact]
    public void TwoThousandMemberOverlappingGroupCoreBenchmark()
    {
        var members = Members(Enumerable.Range(0, 2000).Select(i => $"m{i:0000}").ToArray());
        var groups = Groups(Enumerable.Range(0, 200).Select(i => ($"g{i:000}",
            Enumerable.Range(0, 100).Select(j => $"m{(i * 10 + j) % 2000:0000}").ToArray())).ToArray());
        var build = Stopwatch.StartNew(); var brush = Brush(members, groups); build.Stop();
        var index = brush.Current.Membership;
        Assert.Equal(2000, index.MemberKeys.Count); Assert.Equal(20000, index.Groups.Values.Sum(g => g.Members.Length));
        for (var i = 0; i < 30; i++) { brush.Toggle(groups.Keys[i]); brush.Hover(groups.Keys[i]); brush.Project([members, groups]); }
        var interaction = new double[300]; var projection = new double[300];
        for (var i = 0; i < interaction.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            brush.Toggle(groups.Keys[i % 200]); brush.Hover(groups.Keys[i % 200]);
            interaction[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            brush.Project([members, groups]);
            projection[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(interaction); Array.Sort(projection);
        var p95 = (int)Math.Ceiling(interaction.Length * .95) - 1;
        output.WriteLine($"Core only; Debug; 2,000 members, 200 overlapping groups, 20,000 edges; 30 warmups, 300 samples. Index build {build.Elapsed.TotalMilliseconds:F3} ms. Toggle+hover p50 {interaction[150]:F3}, p95 {interaction[p95]:F3} ms. Projection p50 {projection[150]:F3}, p95 {projection[p95]:F3} ms.");
        Assert.Same(index, brush.Current.Membership);
        // Generous regression guard for loaded developer machines; report the design's 5 ms target separately.
        Assert.InRange(interaction[p95], 0, 100); Assert.InRange(projection[p95], 0, 100);
    }
}
