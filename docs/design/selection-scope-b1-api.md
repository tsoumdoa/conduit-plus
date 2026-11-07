# Selection scope B1 implementation contract

B1 builds on A1 in main `b62995c` (Group keys A1, #17). It adds the Core transaction/lifetime
contract, a typed GH Scope component and a document broker. Canvas Card, Overlay, their
inputs and their existing private interaction paths remain unchanged. They join in B2.

## Core public API

All types below are in `ConduitPlus.Core`, without Rhino/GH dependencies. Collections in
published snapshots are A1's immutable collections. Calls are synchronous and must be
serialized by the host; no locks or background scheduling live in Core.

```csharp
public sealed record ScopeToken(ulong DocumentId, Guid ScopeId);
public sealed record ParticipantLease(ScopeToken Token, string ParticipantId, Guid Id);
public sealed record DomainPublication(ParticipantLease Participant, long Generation);
public enum InteractionChangeKind { Reconciliation, Selection, Hover }
public sealed record InteractionChange(BrushDomainSnapshot Snapshot, InteractionChangeKind Kind);

public sealed class InteractionDomain : IDisposable
{
    public InteractionDomain(ScopeToken token);
    public ScopeToken Token { get; }
    public BrushDomainSnapshot Current { get; }
    public int ParticipantCount { get; }
    public bool IsSuspended { get; }
    public bool IsDisposed { get; }
    public event Action<InteractionChange>? Changed;
    public ParticipantLease RegisterParticipant(string participantId);
    public bool Contains(ParticipantLease lease);
    public DomainPublication? BeginPublication(ParticipantLease lease);
    public bool Stage(DomainPublication publication, BrushRegistration? registration);
    public bool UnregisterParticipant(ParticipantLease lease);
    public bool Commit();
    public bool SetEnabled(ParticipantLease lease, bool enabled);
    public bool Toggle(ParticipantLease lease, string key, BrushTargetKind kind, BrushActionSource source);
    public bool Clear(ParticipantLease lease, BrushActionSource source);
    public bool Hover(ParticipantLease lease, BrushOrigin origin, string? key, BrushTargetKind? kind = null);
    public bool ClearHover(ParticipantLease lease, BrushOrigin origin);
    public void Suspend();
    public void Resume();
    public void Dispose();
}

public sealed class InteractionDocument : IDisposable
{
    public InteractionDocument(ulong documentId);
    public ulong DocumentId { get; }
    public bool IsSuspended { get; }
    public bool IsDisposed { get; }
    public IEnumerable<InteractionDomain> Domains { get; }
    public ScopeToken DefineScope(Guid scopeId);
    public bool IsValid(ScopeToken token);
    public InteractionDomain? GetDomain(ScopeToken token);
    public void InvalidateScope(Guid scopeId);
    public void ReleaseIfEmpty(ScopeToken token);
    public void Commit();
    public void Suspend();
    public void Resume();
    public void Dispose();
}

public enum InteractionRefreshKind { Interaction, ReconciliationOnly }
public sealed record InteractionRefresh(InteractionRefreshKind Kind, ImmutableArray<ParticipantLease> Participants);
public sealed record InteractionRefreshDecision(InteractionRefresh? Refresh, ImmutableArray<ParticipantLease> Warn);
public sealed class InteractionRefreshQueue
{
    public void Watch(ParticipantLease lease, BrushDomainSnapshot snapshot, bool includeEvents);
    public void Remove(ParticipantLease lease);
    public void Clear();
    public bool IsDirty(ParticipantLease lease);
    public void RecordOutputs(ParticipantLease lease, BrushDomainSnapshot snapshot);
    public void Observe(ParticipantLease lease, InteractionChange change);
    public InteractionRefreshDecision Take(bool reconciliationOnly = false);
}
```

Register once per display attachment. A lease includes a random attachment ID, so a deleted,
recreated participant/domain with identical document/component IDs cannot accept the old lease.
Issue a publication ticket **before** queuing work. Only the newest issued generation for that
lease can stage, once; foreign documents, other participants, old attachments, older generations
and replayed tickets are rejected. A newer ticket also supersedes an already staged result.
Capture `BrushRegistration` from original cards and Link keys before queuing (its membership
and labels own their data); never register projected cards.

`Stage(ticket, null)` explicitly removes an invalid/error/unbound contribution while retaining
the attachment. `UnregisterParticipant` invalidates the lease immediately and stages removal.
Unchanged participants retain their previous contribution. `Commit` calls A1's
`ReplaceRegistrations` once against the full union, then notifies once if the snapshot changed.
There is no pruning at Stage time. Source CardSpec ID replacement does not reset the explicit
domain's selection. B2 still resets the host's local sort/scroll where appropriate.

`SetEnabled(false)` disables selection/hover actions and clears only that participant's hover;
it leaves its membership/selection intact. Hosts keep disabled components' last registration
and do not publish replacement data for locked components. A miss or invalid Hover target is
an origin-matched clear, never an unconditional null hover. Lower gesture generations on the
same participant/surface cannot replace a newer hover. Latest valid updates from different
participants/surfaces still compete normally. Keep a generation constant within a gesture,
advance it after cancellation/re-enable/reload, and clear using the exact originating value.

Suspend clears hover, cancels queued/staged replacements and advances publication/known
gesture generations. Committed membership/selection and explicit pending removals survive.
Resume alone needs no data republication. Dispose drops registrations, selection, history and
subscriptions. Last-participant release drops the domain but leaves the Scope token available;
a later attachment gets a new empty domain. Scope invalidation removes that token's authority.
Undo can define the same token again, but attachment IDs and the new domain prevent stale work
from reviving its old selection. A reopened GH document has another runtime ID.

## GH adapter seam for B2

Types below are in `ConduitPlus.Grasshopper`. All broker/binding calls, callback registration,
and disposal run on the GH UI thread (enforced against the broker's creation thread).

```csharp
ScopeBroker ScopeBroker.ForDocument(GH_Document document); // static
ulong ScopeBroker.DocumentId { get; }
bool ScopeBroker.IsDisposed { get; }
bool ScopeBroker.IsSuspended { get; }
ScopeToken ScopeBroker.DefineScope(Guid scopeId); // Scope component only
bool ScopeBroker.IsValid(ScopeToken token);
void ScopeBroker.InvalidateScope(Guid scopeId);
ScopeBinding ScopeBroker.Attach(ScopeToken token, GH_Component owner,
    Action<BrushDomainSnapshot> snapshotChanged,
    bool selectionOutput = false, bool eventsOutput = false);
void ScopeBroker.Dispose();

ParticipantLease ScopeBinding.Lease { get; }
BrushDomainSnapshot ScopeBinding.Current { get; }
bool ScopeBinding.IsValid { get; }
bool ScopeBinding.IsActive { get; }
bool ScopeBinding.OutputRefreshWarning { get; }
event Action<bool>? ScopeBinding.ActivityChanged;
event Action? ScopeBinding.Invalidated;
DomainPublication? ScopeBinding.BeginPublication();
bool ScopeBinding.Stage(DomainPublication publication, BrushRegistration? registration);
bool ScopeBinding.Publish(IEnumerable<CardSpec> originalCards, IEnumerable<string> linkKeys);
void ScopeBinding.RecordOutputs();
bool ScopeBinding.Toggle(string key, BrushTargetKind kind, BrushActionSource source);
bool ScopeBinding.Clear(BrushActionSource source);
bool ScopeBinding.Hover(BrushOrigin origin, string? key, BrushTargetKind? kind = null);
bool ScopeBinding.ClearHover(BrushOrigin origin);
void ScopeBinding.SetEnabled(bool enabled);
void ScopeBinding.Dispose();
```

In normal `SolveInstance`, validate the whole Scope input tree (exactly one `ScopeGoo`, not
item iteration) and use `broker.IsValid(goo.Value)` to reject foreign/deleted tokens. Goo
`IsValid` checks structural presence only; the broker owns live token authority. Unwired
inputs retain the existing private paths. Invalid wired inputs detach and disable brushing.
No string/Guid conversions are implemented. Scope output uses a nonpersistent typed
`ScopeParam : GH_Param<ScopeGoo>`; only the component and normal wires serialize, never
runtime tokens or selection. Name is carried by the goo for display only.

Attach once with the snapshot callback that projects/repaints the host. Forward only Core
snapshot/action/delegate types into Rhino's adapter; the Rhino assembly must not reference
this GH broker. Use `binding.Publish(originalCards, linkKeys)` synchronously in SolveInstance.
For queued publication, call `BeginPublication`, capture `BrushRegistration` on the originating
solve, then marshal to UI and `Stage(ticket, captured)`. Invalid input/bound Rhino document
closure uses a fresh ticket with null; never redirect late work to another active Rhino document.
The broker commits at `GH_Document.SolutionEnd`. Publications/removals outside a solve request
a 1 ms scheduled solution as the next atomic transaction boundary.

One binding per display is enforced. For a Scope change, dispose the old binding and attach
the validated new token in the same UI-thread solve; no intermediate per-participant commit
occurs and selection is never copied. Subscribe to `Invalidated` to disable interactions when
Scope is deleted. Subscribe to `ActivityChanged` to release/recreate rendering subscriptions
and cancel gestures. **Retain the binding on Unloaded**; disposing it there would remove
membership and lose selection. Resume invokes the snapshot callback even without a solve.
Use `ClearHover(exactOrigin)` for navigation/drag cancellation and `SetEnabled` for a disabled
runtime binding. Locked components and disabled documents cannot originate through a binding.
Re-enable then project `Current`; nothing automatically selects newly appearing members.

An output participant opts in with `selectionOutput: true` (Canvas) and also
`eventsOutput: true` (Overlay). When producing outputs, read `Current`, return the whole
domain selection/history and call `RecordOutputs` in that solve. Hover/metadata-only snapshots
do not schedule solves. The broker coalesces all changed output participants across all scopes
into one document ScheduleSolution callback; it expires only still-dirty, unlocked output owners.
The callback reads current revisions, rather than retaining a click-time snapshot. B2 must
replace the host's old interaction scheduling path when using a binding, to avoid double solves.

After solution-end pruning, the first output refresh is tagged ReconciliationOnly. Pruning
again in that refresh updates snapshots immediately, adds a GH runtime warning and sets
`OutputRefreshWarning`; it schedules no successor. This remains suppressed through unrelated
hover/metadata updates and tab reload. The next external solve reads current outputs and clears
the flag via RecordOutputs. A new user action starts a new chain. Suspended pending output
callbacks are generation-canceled and rearmed on resume only when still needed; a warning's
dirty outputs are not silently rearmed. This is the tested feedback policy, not a claim that
revision comparison alone prevents loops.

## Lifecycle and pinned GH 8.20 validation

Inspected the local pinned NuGet
`Grasshopper/8.20.25147.11001-rc/lib/net7.0/Grasshopper.dll` with `ilspycmd`, alongside
its shipped XML; no online API documentation was used. Full build validates the signatures.

- `GH_Document.RuntimeID : ulong` is assigned from a runtime counter in the constructor and
  is not deserialized. Tokens use `(RuntimeID, ScopeComponent.InstanceGuid)` directly, rather
  than the document's serializable `DocumentID` or its filename.
- `GH_Document.ContextChanged(object, GH_DocContextEventArgs)` exposes Context. The Context
  setter informs top-level objects, then raises the event. It is an action notification, not
  a complete state model. Open/new document starts with a new, empty broker. Loaded/Open/Unlock
  resumes; Unloaded/Lock suspends and clears hover; Close disposes the entire broker and removes
  it from the ConditionalWeakTable. The table is keyed by actual GH_Document instance.
- `GH_DocumentServer.RemoveDocument` sets Context to Close before removal/disposal. Direct
  `GH_Document.Dispose` instead sets the backing field to Close and removes its objects without
  raising ContextChanged; the broker also recognizes Close during ObjectsDeleted, and never
  schedules a transaction boundary while the GH document is being disposed.
- `GH_Canvas.Document` setter disables the previous document and emits Unloaded; it enables
  the incoming document, emits Loaded, then calls `NewSolution(false)`. A previously solved
  component may not run again, so membership/selection stay managed across unload. Domain
  subscriptions remain broker-internal; external snapshot/render notifications are suppressed
  while suspended and restored from the latest snapshot on resume.
- `GH_Document.SolutionStart/SolutionEnd(object, GH_SolutionEventArgs)` delimit staging/commit.
  The broker can also be created during a solve, detected with `SolutionDepth > 0`.
- `ScheduleSolution(int, GH_Document.GH_ScheduleDelegate)` uses a timer for positive delay.
  `ScheduleCallDelegates` runs before SolutionStart, and drains the delegate list even if an
  external NewSolution intervenes. The broker can therefore expire outputs before evaluation
  and consume the pending refresh tag exactly once. It uses delay 1, never recursive delay 0.
- There is no public API to remove just one queued schedule delegate. Broker callbacks capture
  a weak broker reference plus generation; unload/close makes them inert without canceling
  other components' schedules. GH may still process its already queued empty solution.
- `GH_Document.ObjectsDeleted(object, GH_DocObjectEventArgs)` supplies `Objects` after removal.
  It detaches deleted participants and invalidates deleted Scopes. Scope's AddedToDocument /
  RemovedFromDocument hooks also define/invalidate authority, idempotently, including undo.
  Scope invalidation happens before consumer invalidation callbacks, preventing reattachment
  to a token while it is being deleted.

Scope component GUID: `41140e0a-5669-460b-a6a9-59e583ecbaca`.
Typed Scope parameter GUID: `d26bc4e7-6425-4706-9ac7-03b436ebcf91`.
All existing component GUIDs and inputs are untouched.

## Scope, interpretation and verification

Core/domain semantics follow selection-scope.md. Empty domains are materialized lazily on first
attachment. An additional attachment GUID distinguishes stale work after delete/undo/rejoin;
the public Scope token itself remains exactly the specified document/component identity pair.
The refresh queue and lifecycle registry are pure Core seams so tests never load RhinoCommon.
B1 supplies the scheduling/feedback mechanism for B2 to opt into, without changing existing
display behavior. Live Canvas/model sharing, rendering and output wiring remain B2 validation.

Scheduling interpretation: if an external GH solve arrives while a reconciliation callback
is queued, GH drains that callback before SolutionStart. That solve consumes the pending
reconciliation refresh budget conservatively; it reads the latest outputs, and a further
feedback prune warns instead of adding another automatic solve. The pinned callback API
does not identify the trigger of the incoming solution. This preserves the bounded rule
when a scheduled refresh is absorbed by another solution.

Only the requested README update modifies an existing file. All implementation, tests and
this adapter document are new files, avoiding A2's Components/painters/Overlay/Canvas files.
No commit, push or developer installation is performed by this implementation sub-agent.

Final local verification (2026-11-07): `dotnet build ConduitPlus.sln -c Debug` succeeded
with 0 warnings and 0 errors; `dotnet test ConduitPlus.sln -c Debug` passed all **219 tests**
(15 new B1 facts); `git diff --check` passed. The new tests cover captured registration union,
atomic handoff/replacement/removal, overlapping/repeated groups, foreign/stale/replayed
publications, origin-safe hover and gesture generations, suspended/deleted/closed lifetimes,
isolated tokens/documents, source replacement, unchanged versions, output coalescing and
the one-refresh/warn feedback rule. GH API/IL inspection and compilation validate the host
contract; live Rhino/GH lifecycle and B2 integration checks have not been run.
