namespace ConduitPlus.Core;

public sealed record ScopeToken(ulong DocumentId, Guid ScopeId);
public sealed record ParticipantLease(ScopeToken Token, string ParticipantId, Guid Id);
public sealed record DomainPublication(ParticipantLease Participant, long Generation);
public enum InteractionChangeKind { Reconciliation, Selection, Hover }
public sealed record InteractionChange(BrushDomainSnapshot Snapshot, InteractionChangeKind Kind);

/// <summary>
/// Single-threaded interaction authority. Hosts stage captured, unprojected registrations and
/// call Commit once at their transaction boundary, never after each participant publication.
/// </summary>
public sealed class InteractionDomain(ScopeToken token) : IDisposable
{
    private sealed class Participant(ParticipantLease lease)
    {
        public ParticipantLease Lease { get; } = lease;
        public long PublicationGeneration { get; set; }
        public long PublishedGeneration { get; set; }
        public bool Enabled { get; set; } = true;
        public Dictionary<string, long> Gestures { get; } = new(StringComparer.Ordinal);
    }

    private BrushState _brush = new();
    private readonly Dictionary<string, Participant> _participants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrushRegistration> _committed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrushRegistration?> _staged = new(StringComparer.Ordinal);
    public ScopeToken Token { get; } = token;
    public BrushDomainSnapshot Current => _brush.Current;
    public int ParticipantCount => _participants.Count;
    public bool IsSuspended { get; private set; }
    public bool IsDisposed { get; private set; }
    public event Action<InteractionChange>? Changed;

    public ParticipantLease RegisterParticipant(string participantId)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        if (_participants.ContainsKey(participantId))
            throw new InvalidOperationException("Detach the existing participant before registering it again.");
        var lease = new ParticipantLease(Token, participantId, Guid.NewGuid());
        _participants.Add(participantId, new(lease));
        return lease;
    }

    public bool Contains(ParticipantLease lease) => !IsDisposed && lease.Token == Token &&
        _participants.TryGetValue(lease.ParticipantId, out var participant) && participant.Lease == lease;

    /// <summary>Allocate before queuing work. Issuing a newer ticket invalidates all older tickets.</summary>
    public DomainPublication? BeginPublication(ParticipantLease lease)
    {
        if (!Contains(lease) || IsSuspended) return null;
        var participant = _participants[lease.ParticipantId];
        // A newer solve also supersedes any staged result from an earlier solve.
        _staged.Remove(lease.ParticipantId);
        return new(lease, ++participant.PublicationGeneration);
    }

    /// <summary>Null explicitly removes an errored/unbound participant's contribution, retaining its lease.</summary>
    public bool Stage(DomainPublication publication, BrushRegistration? registration)
    {
        var lease = publication.Participant;
        if (!Contains(lease) || IsSuspended) return false;
        var participant = _participants[lease.ParticipantId];
        if (publication.Generation != participant.PublicationGeneration || publication.Generation <= participant.PublishedGeneration) return false;
        if (registration is not null && registration.ParticipantId != lease.ParticipantId)
            throw new ArgumentException("Registration identity must match its publication lease.", nameof(registration));
        _staged[lease.ParticipantId] = registration;
        participant.PublishedGeneration = publication.Generation;
        return true;
    }

    public bool UnregisterParticipant(ParticipantLease lease)
    {
        if (!Contains(lease)) return false;
        ClearParticipantHover(lease.ParticipantId);
        _participants.Remove(lease.ParticipantId);
        _staged[lease.ParticipantId] = null;
        return true;
    }

    public bool Commit()
    {
        if (IsDisposed || IsSuspended || _staged.Count == 0) return false;
        var next = new Dictionary<string, BrushRegistration>(_committed, StringComparer.Ordinal);
        foreach (var (id, registration) in _staged)
            if (registration is null) next.Remove(id); else next[id] = registration;
        // BrushState validates the entire union before mutating. Keep our transaction staged on failure.
        var changed = _brush.ReplaceRegistrations(next.Values);
        _committed.Clear();
        foreach (var pair in next) _committed.Add(pair.Key, pair.Value);
        _staged.Clear();
        if (changed) Publish(InteractionChangeKind.Reconciliation);
        return changed;
    }

    public bool SetEnabled(ParticipantLease lease, bool enabled)
    {
        if (!Contains(lease)) return false;
        _participants[lease.ParticipantId].Enabled = enabled;
        if (!enabled) ClearParticipantHover(lease.ParticipantId);
        return true;
    }

    public bool Select(ParticipantLease lease, string key, BrushSelectMode mode, BrushTargetKind kind, BrushActionSource source) =>
        Interact(lease, () => _brush.Select(key, mode, source, kind), InteractionChangeKind.Selection);
    public bool Toggle(ParticipantLease lease, string key, BrushTargetKind kind, BrushActionSource source) =>
        Interact(lease, () => _brush.Toggle(key, kind, source), InteractionChangeKind.Selection);
    public bool Clear(ParticipantLease lease, BrushActionSource source) =>
        Interact(lease, () => _brush.Clear(source), InteractionChangeKind.Selection);

    public bool Hover(ParticipantLease lease, BrushOrigin origin, string? key, BrushTargetKind? kind = null)
    {
        if (!CanInteract(lease) || origin.ParticipantId != lease.ParticipantId) return false;
        var gestures = _participants[lease.ParticipantId].Gestures;
        if (gestures.TryGetValue(origin.Surface, out var generation) && origin.Generation < generation) return false;
        gestures[origin.Surface] = origin.Generation;
        // A miss/leave must never erase another participant's winning hover.
        if (key is null || Current.Membership.Resolve(key, kind) is null) return ClearHover(lease, origin);
        return Interact(lease, () => _brush.Hover(origin, key, kind), InteractionChangeKind.Hover);
    }

    public bool ClearHover(ParticipantLease lease, BrushOrigin origin) =>
        origin.ParticipantId == lease.ParticipantId && Contains(lease) &&
        PublishIf(_brush.ClearHover(origin), InteractionChangeKind.Hover);

    public void Suspend()
    {
        if (IsDisposed || IsSuspended) return;
        IsSuspended = true;
        // Keep explicit removals, including participants deleted while the canvas is unloading.
        // Other uncommitted work is canceled; only the last committed membership survives unload.
        foreach (var id in _staged.Where(p => p.Value is not null).Select(p => p.Key).ToArray()) _staged.Remove(id);
        foreach (var participant in _participants.Values)
        {
            participant.PublicationGeneration++;
            foreach (var surface in participant.Gestures.Keys.ToArray()) participant.Gestures[surface]++;
        }
        if (Current.HoverOrigin is { } origin) PublishIf(_brush.ClearHover(origin), InteractionChangeKind.Hover);
    }

    public void Resume() { if (!IsDisposed) IsSuspended = false; }

    private bool CanInteract(ParticipantLease lease) => Contains(lease) && !IsSuspended && _participants[lease.ParticipantId].Enabled;
    private bool Interact(ParticipantLease lease, Func<bool> action, InteractionChangeKind kind) =>
        CanInteract(lease) && PublishIf(action(), kind);
    private bool PublishIf(bool changed, InteractionChangeKind kind)
    { if (changed) Publish(kind); return changed; }
    private void Publish(InteractionChangeKind kind) => Changed?.Invoke(new(Current, kind));
    private void ClearParticipantHover(string id)
    {
        if (Current.HoverOrigin is { } origin && origin.ParticipantId == id)
            PublishIf(_brush.ClearHover(origin), InteractionChangeKind.Hover);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _participants.Clear(); _committed.Clear(); _staged.Clear(); Changed = null;
        _brush = new();
    }
}

/// <summary>Document-instance lifetime and explicit token authority; no state is serialized.</summary>
public sealed class InteractionDocument(ulong documentId) : IDisposable
{
    private readonly HashSet<Guid> _scopes = [];
    private readonly Dictionary<Guid, InteractionDomain> _domains = [];
    public ulong DocumentId { get; } = documentId;
    public bool IsSuspended { get; private set; }
    public bool IsDisposed { get; private set; }
    public IEnumerable<InteractionDomain> Domains => _domains.Values;

    public ScopeToken DefineScope(Guid scopeId)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _scopes.Add(scopeId);
        return new(DocumentId, scopeId);
    }
    public bool IsValid(ScopeToken token) => !IsDisposed && token.DocumentId == DocumentId && _scopes.Contains(token.ScopeId);
    public InteractionDomain? GetDomain(ScopeToken token)
    {
        if (!IsValid(token)) return null;
        if (!_domains.TryGetValue(token.ScopeId, out var domain))
        {
            domain = new(token);
            if (IsSuspended) domain.Suspend();
            _domains.Add(token.ScopeId, domain);
        }
        return domain;
    }
    public void InvalidateScope(Guid scopeId)
    {
        _scopes.Remove(scopeId);
        if (_domains.Remove(scopeId, out var domain)) domain.Dispose();
    }
    public void ReleaseIfEmpty(ScopeToken token)
    {
        if (token.DocumentId == DocumentId && _domains.TryGetValue(token.ScopeId, out var domain) && domain.ParticipantCount == 0)
        { _domains.Remove(token.ScopeId); domain.Dispose(); }
    }
    public void Commit() { foreach (var domain in _domains.Values.ToArray()) domain.Commit(); }
    public void Suspend()
    { IsSuspended = true; foreach (var domain in _domains.Values.ToArray()) domain.Suspend(); }
    public void Resume()
    { if (IsDisposed) return; IsSuspended = false; foreach (var domain in _domains.Values) domain.Resume(); }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        foreach (var domain in _domains.Values) domain.Dispose();
        _domains.Clear(); _scopes.Clear();
    }
}
