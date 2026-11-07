using System.Collections.Immutable;

namespace ConduitPlus.Core;

public enum InteractionRefreshKind { Interaction, ReconciliationOnly }
public sealed record InteractionRefresh(InteractionRefreshKind Kind, ImmutableArray<ParticipantLease> Participants);
public sealed record InteractionRefreshDecision(InteractionRefresh? Refresh, ImmutableArray<ParticipantLease> Warn);

/// <summary>
/// One per host document, shared by all domains. Tracks output revisions, not snapshot versions.
/// Host calls Take after the atomic data commit or outside a solution for user actions.
/// </summary>
public sealed class InteractionRefreshQueue
{
    private sealed class Output(BrushDomainSnapshot snapshot, bool events)
    {
        public long Selection { get; set; } = snapshot.SelectionRevision;
        public long Events { get; set; } = snapshot.EventRevision;
        public long SeenSelection { get; set; } = snapshot.SelectionRevision;
        public long SeenEvents { get; set; } = snapshot.EventRevision;
        public bool IncludeEvents { get; } = events;
    }
    private readonly Dictionary<ParticipantLease, Output> _outputs = [];
    private readonly Dictionary<ParticipantLease, InteractionChangeKind> _pending = [];

    public void Watch(ParticipantLease lease, BrushDomainSnapshot snapshot, bool includeEvents)
    { _outputs.Add(lease, new(snapshot, includeEvents)); }
    public void Remove(ParticipantLease lease) { _outputs.Remove(lease); _pending.Remove(lease); }
    public void Clear() { _outputs.Clear(); _pending.Clear(); }
    public bool IsDirty(ParticipantLease lease) => _outputs.TryGetValue(lease, out var o) &&
        (o.Selection != o.SeenSelection || o.IncludeEvents && o.Events != o.SeenEvents);

    /// <summary>Call only when the output component actually reads this authoritative snapshot.</summary>
    public void RecordOutputs(ParticipantLease lease, BrushDomainSnapshot snapshot)
    {
        if (!_outputs.TryGetValue(lease, out var o)) return;
        o.Selection = o.SeenSelection = snapshot.SelectionRevision;
        o.Events = o.SeenEvents = snapshot.EventRevision;
        _pending.Remove(lease);
    }

    public void Observe(ParticipantLease lease, InteractionChange change)
    {
        if (!_outputs.TryGetValue(lease, out var o)) return;
        var snapshot = change.Snapshot;
        var changed = o.SeenSelection != snapshot.SelectionRevision || o.IncludeEvents && o.SeenEvents != snapshot.EventRevision;
        o.SeenSelection = snapshot.SelectionRevision; o.SeenEvents = snapshot.EventRevision;
        if (!changed || !IsDirty(lease)) return;
        if (!_pending.TryGetValue(lease, out var previous) || previous != InteractionChangeKind.Selection)
            _pending[lease] = change.Kind;
    }

    /// <summary>
    /// Consume one coalesced request. A prune during a reconciliation-only refresh warns without
    /// scheduling a successor. A fresh user action always starts a new automatic refresh chain.
    /// </summary>
    public InteractionRefreshDecision Take(bool reconciliationOnly = false)
    {
        var dirty = _pending.Where(p => IsDirty(p.Key)).ToArray();
        _pending.Clear();
        var warn = dirty.Where(p => reconciliationOnly && p.Value != InteractionChangeKind.Selection).Select(p => p.Key).ToImmutableArray();
        var ready = dirty.Where(p => !reconciliationOnly || p.Value == InteractionChangeKind.Selection).ToArray();
        if (ready.Length == 0) return new(null, warn);
        var kind = ready.Any(p => p.Value == InteractionChangeKind.Selection)
            ? InteractionRefreshKind.Interaction : InteractionRefreshKind.ReconciliationOnly;
        return new(new(kind, ready.Select(p => p.Key).ToImmutableArray()), warn);
    }
}
