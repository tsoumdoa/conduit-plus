using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using ConduitPlus.Core;
using Grasshopper.Kernel;

namespace ConduitPlus.Grasshopper;

/// <summary>
/// GH UI-thread adapter. B2 attaches a binding and stages original registrations synchronously
/// in SolveInstance. Background/queued hosts must allocate a ticket before queuing, marshal to
/// the UI thread, then call Stage; no host may directly prune a shared BrushState.
/// </summary>
public sealed class ScopeBroker : IDisposable
{
    private static readonly ConditionalWeakTable<GH_Document, ScopeBroker> Brokers = new();
    private readonly GH_Document _document;
    private readonly InteractionDocument _state;
    private readonly InteractionRefreshQueue _refresh = new();
    private readonly Dictionary<ParticipantLease, ScopeBinding> _bindings = [];
    private readonly HashSet<ParticipantLease> _scheduledParticipants = [];
    private readonly HashSet<ParticipantLease> _suspendedParticipants = [];
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private bool _inSolution, _reconciliationOnly, _nextReconciliationOnly, _scheduled;
    private InteractionRefreshKind _resumeRefreshKind = InteractionRefreshKind.ReconciliationOnly;
    private long _callbackGeneration;
    public bool IsDisposed { get; private set; }
    public bool IsSuspended => _state.IsSuspended;
    public ulong DocumentId => _state.DocumentId;

    public static ScopeBroker ForDocument(GH_Document document)
    {
        if (document.Context == GH_DocumentContext.Close) throw new ObjectDisposedException(nameof(document));
        ConduitPlus.Rhino.OverlayManager.Start();
        return Brokers.GetValue(document, d => new(d));
    }
    private ScopeBroker(GH_Document document)
    {
        _document = document;
        _state = new(document.RuntimeID);
        _inSolution = document.SolutionDepth > 0;
        if (document.Context is GH_DocumentContext.Unloaded or GH_DocumentContext.Lock) _state.Suspend();
        document.SolutionStart += SolutionStart;
        document.SolutionEnd += SolutionEnd;
        document.ContextChanged += ContextChanged;
        document.ObjectsDeleted += ObjectsDeleted;
    }

    public ScopeToken DefineScope(Guid scopeId) { VerifyThread(); return _state.DefineScope(scopeId); }
    public bool IsValid(ScopeToken token) => _state.IsValid(token);
    public void InvalidateScope(Guid scopeId)
    {
        VerifyThread();
        _state.InvalidateScope(scopeId);
        foreach (var binding in _bindings.Values.Where(b => b.Lease.Token.ScopeId == scopeId).ToArray()) binding.Dispose();
    }

    /// <summary>Exactly one binding per display. Dispose the previous binding before changing scope.</summary>
    public ScopeBinding Attach(ScopeToken token, GH_Component owner, Action<BrushDomainSnapshot> snapshotChanged,
        bool selectionOutput = false, bool eventsOutput = false)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (owner.OnPingDocument() != _document) throw new ArgumentException("Participant belongs to a different GH document.", nameof(owner));
        if (_bindings.Values.Any(b => b.Owner.InstanceGuid == owner.InstanceGuid))
            throw new InvalidOperationException("Dispose the display's previous scope binding before attaching another.");
        var domain = _state.GetDomain(token) ?? throw new ArgumentException("Foreign or deleted Scope token.", nameof(token));
        var lease = domain.RegisterParticipant(owner.InstanceGuid.ToString("N"));
        var binding = new ScopeBinding(this, domain, lease, owner, snapshotChanged);
        _bindings.Add(lease, binding);
        if (selectionOutput || eventsOutput) _refresh.Watch(lease, domain.Current, eventsOutput);
        return binding;
    }

    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("ScopeBroker must be called on its GH UI thread.");
    }
    internal bool CanInteract(ScopeBinding binding)
    {
        VerifyThread();
        var enabled = binding.InteractionEnabled && !binding.Owner.Locked && _document.Enabled;
        binding.Domain.SetEnabled(binding.Lease, enabled);
        return !IsDisposed && !IsSuspended && enabled && binding.Domain.Contains(binding.Lease);
    }
    internal bool Stage(ScopeBinding binding, DomainPublication publication, BrushRegistration? registration)
    {
        VerifyThread();
        if (publication.Participant != binding.Lease || !binding.Domain.Stage(publication, registration)) return false;
        EnsureBoundary();
        return true;
    }
    private void EnsureBoundary()
    {
        // Never reconcile a participant in isolation. Outside a solve, arrange a transaction boundary.
        if (!_inSolution && !IsDisposed && !IsSuspended && _document.Context != GH_DocumentContext.Close) Schedule(null);
    }
    internal void Changed(ScopeBinding binding, InteractionChange change)
    {
        _refresh.Observe(binding.Lease, change);
        if (!IsDisposed && !IsSuspended && !binding.Owner.Locked) binding.Notify(change.Snapshot);
        if (!_inSolution) Dispatch(_refresh.Take());
    }
    internal void RefreshEnabledOutputs(ScopeBinding binding)
    {
        if (!_inSolution && !IsDisposed && !IsSuspended && !binding.OutputRefreshWarning && _refresh.IsDirty(binding.Lease))
            Schedule(new(InteractionRefreshKind.Interaction, [binding.Lease]));
    }
    internal void RecordOutputs(ScopeBinding binding)
    {
        VerifyThread();
        _refresh.RecordOutputs(binding.Lease, binding.Current);
        binding.OutputRefreshWarning = false;
    }
    internal void Detach(ScopeBinding binding)
    {
        VerifyThread();
        if (!_bindings.Remove(binding.Lease)) return;
        _refresh.Remove(binding.Lease); _scheduledParticipants.Remove(binding.Lease); _suspendedParticipants.Remove(binding.Lease);
        binding.Domain.UnregisterParticipant(binding.Lease);
        _state.ReleaseIfEmpty(binding.Lease.Token);
        EnsureBoundary();
    }

    private void SolutionStart(object sender, GH_SolutionEventArgs e)
    {
        _inSolution = true;
        _reconciliationOnly = _nextReconciliationOnly;
        _nextReconciliationOnly = false;
    }
    private void SolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        if (IsDisposed || IsSuspended) { _inSolution = false; return; }
        // Changed callbacks during Commit only repaint/mark dirty. Dispatch once after all domains.
        _state.Commit();
        _inSolution = false;
        Dispatch(_refresh.Take(_reconciliationOnly));
        _reconciliationOnly = false;
    }
    private void Dispatch(InteractionRefreshDecision decision)
    {
        if (IsDisposed || IsSuspended) return;
        foreach (var lease in decision.Warn)
            if (_bindings.TryGetValue(lease, out var binding))
            {
                binding.OutputRefreshWarning = true;
                binding.Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Scope selection changed again during its reconciliation refresh; outputs await the next external solve.");
            }
        if (decision.Refresh is { } refresh) Schedule(refresh);
    }
    private void Schedule(InteractionRefresh? refresh)
    {
        if (refresh is not null)
        {
            foreach (var lease in refresh.Participants) _scheduledParticipants.Add(lease);
            if (!_scheduled) _nextReconciliationOnly = refresh.Kind == InteractionRefreshKind.ReconciliationOnly;
            else if (refresh.Kind == InteractionRefreshKind.Interaction) _nextReconciliationOnly = false;
        }
        else _nextReconciliationOnly = false; // A data publication is an external transaction.
        if (_scheduled) return;
        _scheduled = true;
        var generation = _callbackGeneration;
        // GH 8.20 calls these delegates before SolutionStart, including an intervening external solve.
        // There is no public delegate-removal API: the weak, generation-guarded closure is inert on close.
        var weak = new WeakReference<ScopeBroker>(this);
        _document.ScheduleSolution(1, _ =>
        {
            if (!weak.TryGetTarget(out var broker) || broker.IsDisposed || broker.IsSuspended || generation != broker._callbackGeneration) return;
            broker._scheduled = false;
            foreach (var lease in broker._scheduledParticipants.ToArray())
                if (broker._bindings.TryGetValue(lease, out var binding) && !binding.Owner.Locked && broker._refresh.IsDirty(lease))
                    { binding.NotifyOutputRefresh(); binding.Owner.ExpireSolution(false); }
            broker._scheduledParticipants.Clear();
        });
    }

    private void ContextChanged(object sender, GH_DocContextEventArgs e)
    {
        if (e.Context == GH_DocumentContext.Close) { Dispose(); return; }
        if (e.Context is GH_DocumentContext.Unloaded or GH_DocumentContext.Lock)
        {
            if (!IsSuspended)
            {
                _resumeRefreshKind = _nextReconciliationOnly ? InteractionRefreshKind.ReconciliationOnly : InteractionRefreshKind.Interaction;
                _suspendedParticipants.UnionWith(_scheduledParticipants);
            }
            CancelCallbacks();
            _state.Suspend();
            foreach (var binding in _bindings.Values.ToArray()) binding.NotifyActivity(false);
        }
        else if (e.Context is GH_DocumentContext.Loaded or GH_DocumentContext.Open or GH_DocumentContext.Unlock)
        {
            _state.Resume();
            foreach (var binding in _bindings.Values.ToArray())
            { binding.NotifyActivity(true); if (!binding.Owner.Locked) binding.Notify(binding.Current); }
            // Unload cancels callbacks, not the need to publish a committed selection change.
            var dirty = _suspendedParticipants.Where(p => _bindings.ContainsKey(p) && _refresh.IsDirty(p)).ToImmutableArray();
            _suspendedParticipants.Clear();
            if (dirty.Length > 0) Schedule(new(_resumeRefreshKind, dirty));
        }
    }
    private void ObjectsDeleted(object sender, GH_DocObjectEventArgs e)
    {
        // Direct GH_Document.Dispose sets Context's backing field without raising ContextChanged.
        if (_document.Context == GH_DocumentContext.Close) { Dispose(); return; }
        foreach (var obj in e.Objects)
        {
            if (obj is ScopeComponent) InvalidateScope(obj.InstanceGuid);
            foreach (var binding in _bindings.Values.Where(b => b.Owner.InstanceGuid == obj.InstanceGuid).ToArray()) binding.Dispose();
        }
    }
    private void CancelCallbacks()
    {
        _callbackGeneration++; _scheduled = false; _scheduledParticipants.Clear();
        _nextReconciliationOnly = false; _reconciliationOnly = false;
    }
    public void Dispose()
    {
        VerifyThread();
        if (IsDisposed) return;
        IsDisposed = true;
        CancelCallbacks();
        _document.SolutionStart -= SolutionStart; _document.SolutionEnd -= SolutionEnd;
        _document.ContextChanged -= ContextChanged; _document.ObjectsDeleted -= ObjectsDeleted;
        foreach (var binding in _bindings.Values.ToArray()) binding.Dispose();
        _refresh.Clear(); _suspendedParticipants.Clear(); _state.Dispose();
        Brokers.Remove(_document);
    }
}

/// <summary>B2 adapter seam. Retain through tab unload; dispose on detach/delete or scope change.</summary>
public sealed class ScopeBinding : IDisposable, IBrushBinding
{
    private readonly ScopeBroker _broker;
    private Action<BrushDomainSnapshot>? _snapshotChanged;
    internal GH_Component Owner { get; }
    internal InteractionDomain Domain { get; }
    internal bool InteractionEnabled { get; private set; } = true;
    public ParticipantLease Lease { get; }
    public string ParticipantId => Lease.ParticipantId;
    public event Action? OutputRefreshRequested;
    internal void NotifyOutputRefresh() => OutputRefreshRequested?.Invoke();
    public BrushDomainSnapshot Current => Domain.Current;
    public bool IsValid => Domain.Contains(Lease);
    public bool IsActive => _broker.CanInteract(this);
    public bool OutputRefreshWarning { get; internal set; }
    public event Action<bool>? ActivityChanged;
    public event Action? Invalidated;

    internal ScopeBinding(ScopeBroker broker, InteractionDomain domain, ParticipantLease lease, GH_Component owner,
        Action<BrushDomainSnapshot> snapshotChanged)
    {
        _broker = broker; Domain = domain; Lease = lease; Owner = owner; _snapshotChanged = snapshotChanged;
        domain.Changed += OnChanged;
    }
    private void OnChanged(InteractionChange change) => _broker.Changed(this, change);
    internal void Notify(BrushDomainSnapshot snapshot)
    {
        ReportDiagnostics();
        _snapshotChanged?.Invoke(snapshot);
    }
    internal void ReportDiagnostics()
    {
        foreach (var diagnostic in Current.Membership.Diagnostics.Where(d => d.ParticipantId == ParticipantId))
            Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Card {diagnostic.CardId}, key '{diagnostic.Key}': {diagnostic.Message}");
    }
    internal void NotifyActivity(bool active) => ActivityChanged?.Invoke(active);
    public DomainPublication? BeginPublication() { _broker.VerifyThread(); return Domain.BeginPublication(Lease); }
    public bool Stage(DomainPublication publication, BrushRegistration? registration) => _broker.Stage(this, publication, registration);
    public bool Publish(IEnumerable<CardSpec> originalCards, IEnumerable<string> linkKeys)
    {
        _broker.VerifyThread();
        var registration = new BrushRegistration(Lease.ParticipantId, originalCards, linkKeys);
        return BeginPublication() is { } publication && Stage(publication, registration);
    }
    public void RecordOutputs() => _broker.RecordOutputs(this);
    public bool Select(string key, BrushSelectMode mode, BrushTargetKind kind, BrushActionSource source) => IsActive && Domain.Select(Lease, key, mode, kind, source);
    public bool Toggle(string key, BrushTargetKind kind, BrushActionSource source) => IsActive && Domain.Toggle(Lease, key, kind, source);
    public bool Clear(BrushActionSource source) => IsActive && Domain.Clear(Lease, source);
    public bool Hover(BrushOrigin origin, string? key, BrushTargetKind? kind = null) => IsActive && Domain.Hover(Lease, origin, key, kind);
    public bool ClearHover(BrushOrigin origin) { _broker.VerifyThread(); return Domain.ClearHover(Lease, origin); }
    /// <summary>Host calls on lock/navigation/drag changes; membership and selection stay frozen.</summary>
    public void SetEnabled(bool enabled)
    {
        _broker.VerifyThread(); InteractionEnabled = enabled; Domain.SetEnabled(Lease, enabled);
        if (enabled) _broker.RefreshEnabledOutputs(this);
    }
    public void Dispose()
    {
        _broker.VerifyThread();
        if (_snapshotChanged is null) return;
        Domain.Changed -= OnChanged;
        _snapshotChanged = null;
        _broker.Detach(this);
        Invalidated?.Invoke(); Invalidated = null; ActivityChanged = null; OutputRefreshRequested = null;
    }
}
