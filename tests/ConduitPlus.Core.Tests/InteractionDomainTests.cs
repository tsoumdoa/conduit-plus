using System.Collections.Immutable;
using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class InteractionDomainTests
{
    private static readonly BrushActionSource Source = new(Guid.NewGuid(), "card", "canvas");
    private static ScopeToken Token(ulong document = 1) => new(document, Guid.NewGuid());
    private static CardSpec Members(string id, params string[] keys) => new()
    { Id = id, Kind = CardKind.Bar, Keys = keys, Labels = keys, Values = keys.Select(_ => (double?)1).ToArray() };
    private static CardSpec Group(string id, string key, params string[] members) => new()
    {
        Id = id, Kind = CardKind.Metric, Keys = [key], Title = key,
        Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add(key, new(members.ToImmutableArray()))
    };
    private static void Stage(InteractionDomain domain, ParticipantLease lease, params CardSpec[] cards) =>
        Assert.True(domain.Stage(Assert.IsType<DomainPublication>(domain.BeginPublication(lease)), new(lease.ParticipantId, cards, [])));
    private static void RemoveContribution(InteractionDomain domain, ParticipantLease lease) =>
        Assert.True(domain.Stage(Assert.IsType<DomainPublication>(domain.BeginPublication(lease)), null));
    private static (InteractionDomain Domain, ParticipantLease Lease) Ready(params string[] keys)
    {
        var domain = new InteractionDomain(Token()); var lease = domain.RegisterParticipant("display");
        Stage(domain, lease, Members("card", keys)); domain.Commit();
        return (domain, lease);
    }

    [Fact]
    public void RegistrationUnionIsCapturedStagedAndPublishedOnce()
    {
        using var domain = new InteractionDomain(Token());
        var a = domain.RegisterParticipant("a"); var b = domain.RegisterParticipant("b");
        var card = Members("card", "a"); var notifications = new List<InteractionChange>(); domain.Changed += notifications.Add;
        var ticket = Assert.IsType<DomainPublication>(domain.BeginPublication(a));
        var captured = new BrushRegistration("a", [card], ["linked"]);
        card.Keys[0] = "mutated";
        Assert.True(domain.Stage(ticket, captured)); Stage(domain, b, Members("other", "b"));
        Assert.Empty(domain.Current.Membership.MemberKeys); Assert.Empty(notifications);
        Assert.True(domain.Commit()); Assert.False(domain.Commit());
        Assert.Equal(new[] { "a", "b", "linked" }, domain.Current.Membership.MemberKeys.Order(StringComparer.Ordinal));
        Assert.Equal(InteractionChangeKind.Reconciliation, Assert.Single(notifications).Kind);
    }

    [Fact]
    public void AtomicReplacementNeverPrunesInParticipantSolveOrder()
    {
        using var domain = new InteractionDomain(Token());
        var a = domain.RegisterParticipant("a"); var b = domain.RegisterParticipant("b");
        Stage(domain, a, Members("source", "move")); Stage(domain, b, Members("other", "stay")); domain.Commit();
        domain.Toggle(a, "move", BrushTargetKind.Member, Source);
        var before = domain.Current; var notifications = new List<InteractionChange>(); domain.Changed += notifications.Add;
        RemoveContribution(domain, a);
        Assert.Equal(new[] { "move" }, domain.Current.SelectedKeys);
        Stage(domain, b, Members("replacement", "stay", "move"));
        Assert.True(domain.Commit());
        Assert.Equal(before.SelectionRevision, domain.Current.SelectionRevision);
        Assert.Equal(before.EventRevision, domain.Current.EventRevision);
        Assert.Equal(new[] { "move" }, domain.Current.SelectedKeys); Assert.Single(notifications);
        // A later replacement retains unchanged participants and prunes once without manufacturing events.
        Stage(domain, a, Members("new-source", "fresh")); RemoveContribution(domain, b); domain.Commit();
        Assert.Empty(domain.Current.SelectedKeys); Assert.Equal(before.EventRevision, domain.Current.EventRevision);
        Assert.Equal(new[] { "fresh" }, domain.Current.Membership.MemberKeys);
    }

    [Fact]
    public void OverlappingAndRepeatedGroupsShareOneCanonicalSelection()
    {
        using var domain = new InteractionDomain(Token());
        var a = domain.RegisterParticipant("canvas"); var b = domain.RegisterParticipant("overlay");
        Stage(domain, a, Group("left", "g1", "a", "b"));
        Stage(domain, b, Group("duplicate", "g1", "b", "a"), Group("right", "g2", "b", "c")); domain.Commit();
        Assert.Empty(domain.Current.Membership.Diagnostics);
        domain.Toggle(a, "g1", BrushTargetKind.Group, Source); domain.Toggle(b, "g2", BrushTargetKind.Group, Source);
        Assert.Equal(new[] { "a", "b", "c" }, domain.Current.SelectedKeys);
        domain.Toggle(a, "g1", BrushTargetKind.Group, Source);
        Assert.Equal(new[] { "c" }, domain.Current.SelectedKeys);
        Assert.True(domain.Current.GroupStates["g2"].IsPartiallySelected);
    }

    [Fact]
    public void PublicationRejectsOldForeignReplayedAndDetachedGenerations()
    {
        using var domain = new InteractionDomain(Token()); var lease = domain.RegisterParticipant("a");
        var old = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        var latest = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        var registration = new BrushRegistration("a", [Members("card", "a")], []);
        Assert.False(domain.Stage(old, registration));
        Assert.False(domain.Stage(latest with { Participant = lease with { Token = lease.Token with { DocumentId = 99 } } }, registration));
        Assert.False(domain.Stage(latest with { Participant = lease with { Token = lease.Token with { ScopeId = Guid.NewGuid() } } }, registration));
        Assert.False(domain.Stage(latest with { Participant = lease with { ParticipantId = "other" } }, registration));
        Assert.True(domain.Stage(latest, registration)); Assert.False(domain.Stage(latest, registration));
        domain.Commit();
        var pending = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        Assert.True(domain.UnregisterParticipant(lease)); var replacement = domain.RegisterParticipant("a");
        Assert.False(domain.Stage(pending, registration)); Assert.False(domain.Toggle(lease, "a", BrushTargetKind.Member, Source));
        Stage(domain, replacement, Members("new", "b")); domain.Commit();
        Assert.Equal(new[] { "b" }, domain.Current.Membership.MemberKeys);
    }

    [Fact]
    public void NewTicketSupersedesAnAlreadyStagedPublication()
    {
        var (domain, lease) = Ready("a"); using var owned = domain;
        Stage(domain, lease, Members("stale", "stale"));
        var latest = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        Assert.False(domain.Commit()); Assert.Contains("a", domain.Current.Membership.MemberKeys.ToArray());
        Assert.Throws<ArgumentException>(() => domain.Stage(latest, new("different", [], [])));
        Assert.True(domain.Stage(latest, new(lease.ParticipantId, [Members("new", "b")], [])));
        domain.Commit(); Assert.Equal(new[] { "b" }, domain.Current.Membership.MemberKeys);
    }

    [Fact]
    public void HoverClearsOnlyItsExactOriginAndRejectsLatePointerUpdates()
    {
        var (domain, a) = Ready("a", "b"); using var owned = domain;
        var b = domain.RegisterParticipant("other");
        var first = new BrushOrigin(a.ParticipantId, "canvas", 1);
        var second = new BrushOrigin(b.ParticipantId, "model", 2);
        Assert.True(domain.Hover(a, first, "a")); Assert.True(domain.Hover(b, second, "b"));
        Assert.False(domain.ClearHover(a, first)); Assert.False(domain.Hover(a, first, null));
        Assert.False(domain.Hover(a, first, "invalid")); Assert.Equal("b", domain.Current.HoverKey);
        Assert.False(domain.Hover(a, second, "a"));
        Assert.True(domain.Hover(a, first with { Generation = 3 }, "a"));
        Assert.False(domain.Hover(a, first, "b")); Assert.False(domain.ClearHover(a, first));
        Assert.False(domain.SetEnabled(b, false) && domain.ClearHover(b, second));
        Assert.Equal("a", domain.Current.HoverKey);
        domain.SetEnabled(a, false); Assert.Null(domain.Current.HoverOrigin);
        Assert.False(domain.Hover(a, first with { Generation = 4 }, "a"));
        Assert.False(domain.Toggle(a, "a", BrushTargetKind.Member, Source));
        domain.SetEnabled(a, true); Assert.True(domain.Hover(a, first with { Generation = 4 }, "a"));
        Assert.True(domain.ClearHover(a, first with { Generation = 4 }));
    }

    [Fact]
    public void UnloadPreservesCommittedSelectionRejectsQueuedWorkAndClearsHover()
    {
        var (domain, lease) = Ready("a"); using var owned = domain;
        var origin = new BrushOrigin(lease.ParticipantId, "canvas", 1);
        domain.Toggle(lease, "a", BrushTargetKind.Member, Source); domain.Hover(lease, origin, "a");
        var ticket = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        domain.Suspend();
        Assert.Null(domain.Current.HoverKey); Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        Assert.Null(domain.BeginPublication(lease)); Assert.False(domain.Clear(lease, Source)); Assert.False(domain.Commit());
        domain.Resume();
        Assert.False(domain.Stage(ticket, null)); Assert.False(domain.Hover(lease, origin, "a"));
        Assert.True(domain.Hover(lease, origin with { Generation = 2 }, "a"));
        Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
    }

    [Fact]
    public void ParticipantRemovalSurvivesUnloadAndDisabledMembershipStaysFrozen()
    {
        var (domain, a) = Ready("a"); using var owned = domain;
        var b = domain.RegisterParticipant("b"); Stage(domain, b, Members("b", "b")); domain.Commit();
        domain.Toggle(a, "a", BrushTargetKind.Member, Source); domain.SetEnabled(a, false);
        Stage(domain, b, Members("b", "b", "c")); domain.Commit();
        Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        domain.UnregisterParticipant(a); domain.Suspend(); domain.Resume(); domain.Commit();
        Assert.Empty(domain.Current.SelectedKeys); Assert.DoesNotContain("a", domain.Current.Membership.MemberKeys.ToArray());
        domain.Suspend(); domain.UnregisterParticipant(b); domain.Resume(); domain.Commit();
        Assert.Empty(domain.Current.Membership.MemberKeys);
    }

    [Fact]
    public void TokensDocumentsDeletionUndoAndLastParticipantHaveSeparateLifetimes()
    {
        using var doc = new InteractionDocument(1); using var foreign = new InteractionDocument(2);
        var scopeId = Guid.NewGuid(); var token = doc.DefineScope(scopeId); var other = doc.DefineScope(Guid.NewGuid());
        Assert.Null(foreign.GetDomain(token));
        var domain = Assert.IsType<InteractionDomain>(doc.GetDomain(token));
        var a = domain.RegisterParticipant("a"); Stage(domain, a, Members("card", "a")); doc.Commit();
        domain.Toggle(a, "a", BrushTargetKind.Member, Source);
        Assert.Empty(Assert.IsType<InteractionDomain>(doc.GetDomain(other)).Current.SelectedKeys);
        var foreignDomain = Assert.IsType<InteractionDomain>(foreign.GetDomain(foreign.DefineScope(scopeId)));
        Assert.Empty(foreignDomain.Current.SelectedKeys);
        doc.Suspend(); Assert.True(domain.IsSuspended); Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        doc.Resume(); Assert.False(domain.IsSuspended);
        domain.UnregisterParticipant(a); doc.ReleaseIfEmpty(token);
        Assert.True(domain.IsDisposed); Assert.Empty(domain.Current.SelectedKeys);
        var fresh = Assert.IsType<InteractionDomain>(doc.GetDomain(token)); Assert.NotSame(domain, fresh);
        var freshLease = fresh.RegisterParticipant("a"); Assert.NotEqual(a, freshLease);
        Assert.False(fresh.Stage(new(a, 1), null));
        doc.InvalidateScope(scopeId); Assert.True(fresh.IsDisposed); Assert.Null(doc.GetDomain(token));
        var undoToken = doc.DefineScope(scopeId); Assert.Equal(token, undoToken);
        Assert.Empty(Assert.IsType<InteractionDomain>(doc.GetDomain(undoToken)).Current.SelectedKeys);
        doc.Dispose(); Assert.Empty(doc.Domains); Assert.False(doc.IsValid(token));
        using var reopened = new InteractionDocument(3);
        Assert.Null(reopened.GetDomain(token)); Assert.Empty(Assert.IsType<InteractionDomain>(reopened.GetDomain(reopened.DefineScope(scopeId))).Current.SelectedKeys);
    }

    [Fact]
    public void SourceReplacementKeepsExplicitSelectionAndIdenticalTransactionsCoalesceVersions()
    {
        var (domain, lease) = Ready("a", "b"); using var owned = domain;
        domain.Toggle(lease, "a", BrushTargetKind.Member, Source);
        var selected = domain.Current;
        Stage(domain, lease, Members("completely-different-source", "a", "b")); Assert.False(domain.Commit());
        Assert.Same(selected, domain.Current); Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        // Repeated group labels can change visual metadata without changing output revisions.
        Stage(domain, lease, Group("group", "g", "a", "b")); domain.Commit();
        var grouped = domain.Current;
        Stage(domain, lease, Group("group", "g", "a", "b") with { Title = "Renamed" }); Assert.True(domain.Commit());
        Assert.Equal(grouped.MembershipRevision, domain.Current.MembershipRevision);
        Assert.Equal(grouped.SelectionRevision, domain.Current.SelectionRevision);
        Assert.Equal(grouped.EventRevision, domain.Current.EventRevision);
        Stage(domain, lease, Group("group", "g", "a", "b") with { Title = "Renamed" });
        var version = domain.Current.Version; Assert.False(domain.Commit()); Assert.Equal(version, domain.Current.Version);
    }

    [Fact]
    public void DocumentCloseDropsPendingWorkAndCallbacks()
    {
        using var document = new InteractionDocument(1);
        var domain = Assert.IsType<InteractionDomain>(document.GetDomain(document.DefineScope(Guid.NewGuid())));
        var lease = domain.RegisterParticipant("a"); Stage(domain, lease, Members("card", "a")); document.Commit();
        domain.Toggle(lease, "a", BrushTargetKind.Member, Source);
        var ticket = Assert.IsType<DomainPublication>(domain.BeginPublication(lease));
        Assert.True(domain.Stage(ticket, new("a", [Members("pending", "b")], [])));
        var notifications = new List<InteractionChange>(); domain.Changed += notifications.Add;
        document.Dispose();
        Assert.Empty(domain.Current.Membership.MemberKeys); Assert.Empty(domain.Current.SelectedKeys); Assert.Empty(domain.Current.Events);
        Assert.False(domain.Commit()); Assert.Null(domain.BeginPublication(lease)); Assert.False(domain.Stage(ticket, null));
        Assert.False(domain.Toggle(lease, "a", BrushTargetKind.Member, Source));
        Assert.Throws<ObjectDisposedException>(() => domain.RegisterParticipant("a"));
        Assert.Empty(notifications);
    }

    [Fact]
    public void OutputQueueIgnoresHoverAndCoalescesChangesAcrossDomains()
    {
        var (a, aLease) = Ready("a"); using var ownedA = a;
        var (b, bLease) = Ready("b"); using var ownedB = b;
        var queue = new InteractionRefreshQueue(); queue.Watch(aLease, a.Current, false); queue.Watch(bLease, b.Current, true);
        a.Changed += change => queue.Observe(aLease, change); b.Changed += change => queue.Observe(bLease, change);
        a.Hover(aLease, new(aLease.ParticipantId, "canvas", 1), "a"); Assert.Null(queue.Take().Refresh);
        Stage(a, aLease, Members("card", "a", "new-unselected")); a.Commit(); Assert.Null(queue.Take().Refresh);
        a.Toggle(aLease, "a", BrushTargetKind.Member, Source); b.Toggle(bLease, "b", BrushTargetKind.Member, Source);
        a.Clear(aLease, Source);
        var refresh = Assert.IsType<InteractionRefresh>(queue.Take().Refresh);
        Assert.Equal(InteractionRefreshKind.Interaction, refresh.Kind); Assert.Equal(2, refresh.Participants.Length);
        Assert.Null(queue.Take().Refresh);
        a.Hover(aLease, new(aLease.ParticipantId, "canvas", 2), "a"); Assert.Null(queue.Take().Refresh);
        queue.RecordOutputs(aLease, a.Current); Assert.False(queue.IsDirty(aLease)); Assert.True(queue.IsDirty(bLease));
        queue.Remove(bLease); Assert.False(queue.IsDirty(bLease));
    }

    [Fact]
    public void OutputReadBeforeDispatchCancelsAnUnneededRefresh()
    {
        var (domain, lease) = Ready("a"); using var owned = domain;
        var queue = new InteractionRefreshQueue(); queue.Watch(lease, domain.Current, true);
        domain.Changed += change => queue.Observe(lease, change);
        domain.Toggle(lease, "a", BrushTargetKind.Member, Source); queue.RecordOutputs(lease, domain.Current);
        Assert.Null(queue.Take().Refresh); Assert.False(queue.IsDirty(lease));
    }

    [Fact]
    public void ReconciliationAllowsExactlyOneRefreshThenWarnsUntilExternalSolve()
    {
        var (domain, lease) = Ready("a", "b", "c"); using var owned = domain;
        foreach (var key in new[] { "a", "b", "c" }) domain.Toggle(lease, key, BrushTargetKind.Member, Source);
        var queue = new InteractionRefreshQueue(); queue.Watch(lease, domain.Current, true);
        domain.Changed += change => queue.Observe(lease, change);
        // Outputs have already read [a,b,c]; downstream feedback removes a at the data commit.
        Stage(domain, lease, Members("filtered", "b", "c")); domain.Commit();
        var first = queue.Take(); Assert.Empty(first.Warn);
        Assert.Equal(InteractionRefreshKind.ReconciliationOnly, Assert.IsType<InteractionRefresh>(first.Refresh).Kind);
        queue.RecordOutputs(lease, domain.Current);
        // Its automatic refresh reads [b,c], but feedback prunes b as well.
        Stage(domain, lease, Members("filtered", "c")); domain.Commit();
        var second = queue.Take(reconciliationOnly: true);
        Assert.Null(second.Refresh); Assert.Equal(lease, Assert.Single(second.Warn));
        Assert.Equal(new[] { "c" }, domain.Current.SelectedKeys); Assert.Equal(3, domain.Current.EventRevision);
        domain.Hover(lease, new(lease.ParticipantId, "canvas", 1), "c");
        Assert.Null(queue.Take().Refresh); Assert.True(queue.IsDirty(lease));
        Stage(domain, lease, Members("filtered", "c", "new-unselected")); domain.Commit();
        Assert.Null(queue.Take().Refresh); Assert.True(queue.IsDirty(lease));
        // The next external solve reads the accurate snapshot; it can start a fresh bounded chain.
        queue.RecordOutputs(lease, domain.Current); Assert.False(queue.IsDirty(lease));
        RemoveContribution(domain, lease); domain.Commit();
        Assert.Equal(InteractionRefreshKind.ReconciliationOnly, Assert.IsType<InteractionRefresh>(queue.Take().Refresh).Kind);
    }

    [Fact]
    public void UserActionDuringReconciliationRefreshStartsANewChain()
    {
        var (domain, lease) = Ready("a", "b"); using var owned = domain;
        domain.Toggle(lease, "a", BrushTargetKind.Member, Source);
        var queue = new InteractionRefreshQueue(); queue.Watch(lease, domain.Current, true);
        domain.Changed += change => queue.Observe(lease, change);
        Stage(domain, lease, Members("filtered", "b")); domain.Commit(); domain.Toggle(lease, "b", BrushTargetKind.Member, Source);
        var decision = queue.Take(reconciliationOnly: true); Assert.Empty(decision.Warn);
        Assert.Equal(InteractionRefreshKind.Interaction, Assert.IsType<InteractionRefresh>(decision.Refresh).Kind);
    }
}
