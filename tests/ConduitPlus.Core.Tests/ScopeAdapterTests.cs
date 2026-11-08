using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ScopeAdapterTests
{
    private sealed class Binding(InteractionDomain domain, string participant) : IBrushBinding
    {
        internal ParticipantLease Lease { get; } = domain.RegisterParticipant(participant);
        public string ParticipantId => Lease.ParticipantId;
        public BrushDomainSnapshot Current => domain.Current;
        public bool IsActive => domain.Contains(Lease) && !domain.IsSuspended;
        public bool Select(string key, BrushSelectMode mode, BrushTargetKind kind, BrushActionSource source) => domain.Select(Lease, key, mode, kind, source);
        public bool Toggle(string key, BrushTargetKind kind, BrushActionSource source) => domain.Toggle(Lease, key, kind, source);
        public bool Clear(BrushActionSource source) => domain.Clear(Lease, source);
        public bool Hover(BrushOrigin origin, string? key, BrushTargetKind? kind = null) => domain.Hover(Lease, origin, key, kind);
        public bool ClearHover(BrushOrigin origin) => domain.ClearHover(Lease, origin);
        internal void Publish(CardSpec card) => domain.Stage(domain.BeginPublication(Lease)!, new(ParticipantId, [card], []));
    }
    private static CardSpec Table(string id, params string[] keys) => new()
    { Id = id, Kind = CardKind.Table, Headers = ["Key"], Rows = keys.Select(k => new TableRow(k, [k])).ToArray(), MaxRows = 1 };
    private static InteractionDomain Domain() => new(new(1, Guid.NewGuid()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanvasModesAndNoOpsPublishOnlyEffectiveChanges(bool scoped)
    {
        using var domain = Domain(); var binding = new Binding(domain, "canvas");
        var card = Table("selection", "a", "b"); binding.Publish(card); domain.Commit();
        var state = new CanvasCardState(); if (scoped) state.Bind(binding, Guid.NewGuid()); state.Replace(card);
        state.Activate("a"); state.Activate("b", BrushSelectMode.Add);
        Assert.Equal(new[] { "a", "b" }, state.Current.SelectedKeys);
        var revision = state.Current.SelectionRevision; var events = state.Current.EventRevision;
        state.Activate("a", BrushSelectMode.Add); state.Activate("missing", BrushSelectMode.Remove);
        Assert.Equal(revision, state.Current.SelectionRevision); Assert.Equal(events, state.Current.EventRevision);
        state.Activate("a", BrushSelectMode.Remove); Assert.Equal(new[] { "b" }, state.Current.SelectedKeys);
        state.Activate("a", BrushSelectMode.Toggle); state.Activate("b", BrushSelectMode.Toggle);
        Assert.Equal(new[] { "a" }, state.Current.SelectedKeys);
        state.Activate("b"); Assert.Equal(new[] { "b" }, state.Current.SelectedKeys);
        revision = state.Current.SelectionRevision; events = state.Current.EventRevision;
        state.Activate("b"); state.Activate("a", BrushSelectMode.Remove);
        Assert.Equal(revision, state.Current.SelectionRevision); Assert.Equal(events, state.Current.EventRevision);
    }

    [Fact]
    public void CanvasProjectsCommittedUnionAndDoesNotPruneOtherParticipants()
    {
        using var domain = Domain();
        var canvas = new Binding(domain, "canvas"); var overlay = new Binding(domain, "overlay");
        var table = Table("table", "a"); var other = Table("other", "b");
        var state = new CanvasCardState(); state.Bind(canvas, Guid.NewGuid()); state.Replace(table);
        domain.Changed += _ => state.Refresh();
        canvas.Publish(table); overlay.Publish(other);
        Assert.Empty(domain.Current.Membership.MemberKeys);
        domain.Commit();
        overlay.Toggle("b", BrushTargetKind.Member, new(Guid.NewGuid(), "model", "model"));
        Assert.Equal(new[] { "b" }, state.Current.SelectedKeys);
        Assert.True(state.Spec!.OwnerHasSelection); Assert.Empty(state.Spec.SelectedKeys);
        state.Refresh(); state.Scroll(20); state.Activate(TableSort.Key(0));
        Assert.Equal(new[] { "b" }, domain.Current.SelectedKeys);
        state.Activate(CardMeasure.ClearKey);
        Assert.Empty(domain.Current.SelectedKeys);
        Assert.Equal("canvas", domain.Current.Events[^1].SourceSurface);
    }

    [Fact]
    public void SharedSourceReplacementResetsViewStateButPreservesDomainSelection()
    {
        using var domain = Domain(); var binding = new Binding(domain, "canvas");
        var card = Table("original", "a", "b", "c"); binding.Publish(card); domain.Commit();
        var state = new CanvasCardState(); state.Bind(binding, Guid.NewGuid()); state.Replace(card);
        state.Activate("a"); state.Activate(TableSort.Key(0)); state.Scroll(2);
        state.Replace(Table("replacement", "b", "c"));
        Assert.Null(state.Spec!.SortColumn); Assert.Equal(0, state.Spec.ScrollOffset);
        Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        binding.Publish(Table("replacement", "b", "c"));
        Assert.Equal(new[] { "a" }, domain.Current.SelectedKeys);
        domain.Commit(); state.Refresh(); Assert.Empty(state.Current.SelectedKeys);
        Assert.Single(domain.Current.Events);
    }

    [Fact]
    public void CanvasActionOriginAndRemoteProjectionDoNotEchoActionsOrOutputSolves()
    {
        using var domain = Domain(); var binding = new Binding(domain, "canvas");
        var card = Table("same", "a", "b"); binding.Publish(card); domain.Commit();
        var owner = Guid.NewGuid(); var state = new CanvasCardState(); state.Bind(binding, owner); state.Replace(card);
        var queue = new InteractionRefreshQueue(); queue.Watch(binding.Lease, domain.Current, false);
        domain.Changed += change => { state.Refresh(); queue.Observe(binding.Lease, change); };
        state.Hover("a"); state.Scroll(1); state.Activate(TableSort.Key(0)); state.Refresh();
        Assert.Null(queue.Take().Refresh); Assert.Empty(domain.Current.Events);
        state.Activate("a"); state.Activate("b");
        Assert.Equal(binding.Lease, Assert.Single(queue.Take().Refresh!.Participants));
        Assert.Equal(2, domain.Current.Events.Length);
        Assert.All(domain.Current.Events, e => { Assert.Equal(owner, e.Owner); Assert.Equal("canvas", e.SourceSurface); });
        queue.RecordOutputs(binding.Lease, domain.Current);
        state.Refresh(); state.CancelHover(); Assert.Null(queue.Take().Refresh);
    }

    [Fact]
    public void InvalidBindingDisablesBrushAndSeparatePrivateStatesStayIsolated()
    {
        var a = new CanvasCardState(); var b = new CanvasCardState();
        a.Replace(Table("same", "a")); b.Replace(Table("same", "a"));
        a.Activate("a"); Assert.Empty(b.Current.SelectedKeys);
        a.Bind(null, Guid.NewGuid(), false); a.Activate(CardMeasure.ClearKey); a.Hover("a");
        Assert.Equal(new[] { "a" }, a.Current.SelectedKeys); Assert.Null(a.Current.HoverKey);
        a.Bind(null, Guid.NewGuid()); a.Activate(CardMeasure.ClearKey); Assert.Empty(a.Current.SelectedKeys);
    }

    [Fact]
    public void PrivateReplacementClearsSelectionWithMonotonicRevision()
    {
        var state = new CanvasCardState(); state.Replace(Table("one", "a")); state.Activate("a");
        var revision = state.Current.SelectionRevision;
        var changes = new List<InteractionChange>(); state.Changed += changes.Add;
        state.Replace(Table("two", "a"));
        Assert.Empty(state.Current.SelectedKeys); Assert.True(state.Current.SelectionRevision > revision);
        Assert.Equal(InteractionChangeKind.Reconciliation, Assert.Single(changes).Kind);
    }

    [Fact]
    public void CanvasLeaveCannotClearRemoteHoverAndReloadAdvancesOrigin()
    {
        using var domain = Domain(); var a = new Binding(domain, "canvas"); var b = new Binding(domain, "overlay");
        var card = Table("table", "a", "b"); a.Publish(card); b.Publish(card); domain.Commit();
        var state = new CanvasCardState(); state.Bind(a, Guid.NewGuid()); state.Replace(card);
        state.Hover("a"); var first = domain.Current.HoverOrigin!;
        var remote = new BrushOrigin("overlay", "model", 1); b.Hover(remote, "b");
        state.CancelHover(); Assert.Equal(remote, domain.Current.HoverOrigin);
        domain.Suspend(); state.CancelHover(); domain.Resume(); state.Hover("a");
        Assert.True(domain.Current.HoverOrigin!.Generation > first.Generation);
        Assert.False(a.ClearHover(first)); Assert.Equal("a", domain.Current.HoverKey);
    }

    [Fact]
    public void RemoteSelectionRevealsOnceInCanvasDisplayOrder()
    {
        using var domain = Domain(); var a = new Binding(domain, "canvas"); var b = new Binding(domain, "overlay");
        var card = Table("table", "a", "b", "c"); a.Publish(card); b.Publish(card); domain.Commit();
        var state = new CanvasCardState(); state.Bind(a, Guid.NewGuid()); state.Replace(card);
        domain.Changed += _ => state.Refresh();
        b.Toggle("c", BrushTargetKind.Member, new(Guid.NewGuid(), "model", "model"));
        Assert.Equal(2, state.Spec!.ScrollOffset);
        state.Scroll(0); state.Hover("a"); state.Refresh();
        Assert.Equal(0, state.Spec.ScrollOffset);
        state.Activate(TableSort.Key(0)); state.Activate(TableSort.Key(0)); // descending
        b.Toggle("b", BrushTargetKind.Member, new(Guid.NewGuid(), "model", "model"));
        Assert.Equal(1, state.Spec.ScrollOffset);
    }
}
