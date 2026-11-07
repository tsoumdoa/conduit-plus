# Shared selection Scope B2 verification — 2026-11-07

Base: `589f8ef`, **Group keys A2: Members inputs, group painting and model highlights (#19)**.
The implementation worktree was fetched and reset to `origin/main` before changes.
Changes remain uncommitted for Claude review. No push or developer installation.

## Implementation

Canvas Card appends optional tree input Scope at index 1 and Selection output at
index 0. Overlay appends optional tree input Scope at index 3; its existing outputs
retain their indices. All existing component/parameter GUIDs and input order remain.
One live typed token is required across the whole tree. Wired empty, invalid,
multiple and foreign-document tokens error and detach/disable brush interaction.

`IBrushBinding` is the Core-only adapter boundary. ScopeBinding implements it;
OverlayManager has no Grasshopper reference. Original cards and Link keys stage
synchronously during SolveInstance, and B1 commits the union at SolutionEnd.
Snapshots project/repaint without solving. Canvas source replacement resets its
local view state while explicit scope selection remains authoritative. Newly
selected remote Canvas rows reveal once, using that table's own display order.

Overlay retains its Rhino document/view/owner session, geometry Links, textures,
layout and local sort/scroll. Shared output refresh callbacks mark the existing
bound-view interaction publication path. Document closure disables the runtime
binding and stages contribution removal. A captured document that disappears
before its close notification follows the same cleanup path. Subsequent output
refreshes cannot republish the closed contribution or redirect to an active model;
a normal external data solve can rebind.

Origins include participant, surface and gesture generation. Cancellation clears
only the exact origin. Local Overlay tooltip targets require the originating
owner/surface; shared remote hover does not create tooltip targets. Existing
model/card hit priority and picker tie ordering are unchanged. Scoped Overlay
events come from the bounded domain history, including Canvas `sourceSurface`.
No snapshot callback appends an event.

Only changed selection/event output revisions expire participating outputs. Locked
outputs are skipped and dirty outputs can refresh on enable. Hover, sort, scroll,
projection and metadata alone never request an output solution. B1's single
reconciliation-refresh budget is retained, with a visible warning on a second prune.
Private Canvas Selection uses GH's coalesced schedule and reads its reconciled state
directly; its existing private selection/reset semantics remain.

## Commands and results

```powershell
dotnet build ConduitPlus.sln -c Debug
dotnet test ConduitPlus.sln -c Debug
./tools/examples/Build-Examples.ps1 -Example Rooms -Safe -Output artifacts/screenshots-scope/host
./tools/examples/Build-Examples.ps1 -VerifyOnly -Safe -Output artifacts/screenshots-scope/final-reopen
./tools/examples/Build-Examples.ps1 -Example Rooms -VerifyOnly -Safe -Exports -State Scope -Output artifacts/screenshots-scope/final-capture
git diff --check
```

Debug build: **0 warnings / 0 errors**. Full xUnit suite: **255 passed, 0 failed,
0 skipped** (248 baseline, seven new adapter tests). The room definition was
regenerated through tools/examples, then reopened and solved. Structural and
Massing fixtures retain their existing binaries and load with the appended inputs.
Host: RhinoCore **8.36.26296.17001**, .NET **8.0.33**, 200% display DPI.

The wrapper owns each host process and stops only that process after successful
overlay/view cleanup if RhinoCore shutdown stalls. No running user Rhino is closed
or killed. Captures run once per host, following the existing native-host limitation.

## Verification coverage

| Case | Evidence |
|---|---|
| Canvas action → Overlay cards and geometry selection | Real GH document and OverlaySession; immutable shared snapshot, projected table keys, selected-face PNG |
| Model action → Canvas | Production OverlaySession model action, Canvas projection/outputs; existing architectural checks exercise real face-interior ModelPicking |
| Two scoped Overlays | Distinct owner sessions, shared Selection/Events; one event for the winning owner's action |
| Private owners | Unscoped Overlay and Canvas remain independent with identical CardSpecs |
| Token validation | Real GH wired empty/text/foreign/multiple token trees; valid rewire and explicit Scope change recover |
| Tooltip/hover isolation | Real host tooltip target is absent on remote owners; remote miss cannot clear Canvas hover |
| Tab unload/reload without solving | Real GH Context Unloaded/Loaded notifications; rendering released/recreated, selection retained, solution count unchanged |
| Participant delete/undo | GH UndoUtil and UndoServer restore display/wires into surviving domain |
| Scope delete/undo | Immediate invalidation and disabled brush; restored wires start with empty transient state |
| Component lock/unlock | Locked origins rejected; unlock projects current state without a data solve |
| Paused solver | Global EnableSolutions false keeps committed brush state; outputs publish on resume |
| GH close/reopen and pending callbacks | Real GH close/dispose and archive extract; old binding/token invalid, callbacks inert, reopened wires empty |
| Rhino view close | Own temporary view closed; interaction outputs do not fall back to another view; normal data solve rebinds |
| Rhino document close | Own headless doc and view disposed; contribution removed, Canvas survives, queued interactions remain detached, normal external solve rebinds |
| Feedback bound | Real wired test document, harness snapshot input; action solve plus one refresh, second prune warns, negative ScheduleDelay (no successor) |
| Stale tickets, atomic union, isolation, source replacement, revision coalescing | Core B1 tests plus seven adapter facts; no RhinoCommon runtime in tests |

Context notifications and API-driven actions run in RhinoCore, not physical GUI
gestures. Physical mouse release/capture/drag precedence, user-driven tab switching,
Rhino 8.20 runtime behavior, mixed DPI and long-running interactive GDI/resource
plateau remain for normal Rhino. Compilation uses the pinned 8.20 API. No new claim
is made for the pre-existing experimental PDF or full-scene high-resolution captures.

## Feedback adaptation

GH refuses a direct recursive Selection → filter → Card evaluation dependency before
the display can solve. `ScopeFeedbackChecks` therefore physically wires Selection
into a harness-only parameter whose source collection samples the authoritative
Selection snapshot and whose dependency query breaks recursive evaluation. The
parameter schedules no work. This tests the production broker's actual GH scheduling,
output revisions and solution-end reconciliation against two successive key prunes.
The fixture is constructed in memory by tools/examples; no custom feedback component
is shipped, serialized into the room example or suggested as normal GH wiring.

The first prune leaves `b`; its one reconciliation refresh removes `b`; the broker
updates displays immediately, warns that outputs await an external solve and leaves
no successor scheduled. JSON records the two-solution count and the adaptation.
There are no scope semantic deviations; this host test adaptation is required by
GH's recursive dependency guard.

## PNG evidence

Two separate 1× images show **Bed 01 / R01** selected:

- `artifacts/screenshots-scope/final-capture/room-areas/scope-canvas-1x.png`
- `artifacts/screenshots-scope/final-capture/room-areas/scope-viewport-1x.png`

Both are visually inspected. GH exports use GenerateHiResImageTile at Zoom=1,
rendering the real Canvas Card attributes with disposable export bitmaps. Viewport
captures use the production capture command and export checks at 1× (1874 × 1729
physical pixels). Existing export paths strip all hover-derived state. Supporting
JSON/logs include `scope-checks.json`, `scope-feedback.json`, `scope-rhino-close.json`
and card-region/pixel evidence in the same host output tree. Artifacts are ignored
by git and intentionally left on disk for review.

## Files changed

- Core: `BrushBinding.cs` (new), `CanvasCards.cs`.
- Grasshopper: `ScopeInput.cs` (new), `CanvasCardComponent.cs`,
  `CanvasCardAttributes.cs`, `Components.cs`, `ScopeBroker.cs`, `ScopeComponent.cs`.
- Rhino: `OverlayManager.cs`, `OverlayInput.cs`, `OverlayTooltip.cs`.
- Tests: `tests/ConduitPlus.Core.Tests/ScopeAdapterTests.cs` (new).
- Tools: `tools/examples/Build-Examples.ps1`, `ArchitectureDashboard.cs`,
  `ScopeHostChecks.cs`, `ScopeFeedbackChecks.cs`, `ScopeRhinoCloseChecks.cs`
  (the three Scope check files are new).
- Example/docs: `examples/room-areas.gh`, `README.md`, `docs/NEXT.md`,
  `docs/ux-notes.md`, this verification record (new).

After the reviewing agent installs the integrated checkout, open room-areas.gh in
a metre-unit model, use a large Top viewport and Zoom Extents. Click Bed 01 in the
Canvas table, then its face in Rhino; compare viewport and Canvas Selection outputs.
Try Bedroom → one bedroom row → Clear, sort/scroll each table independently, change
Plan scale, switch GH tabs, lock/unlock a display and disconnect Scope to check
private isolation. Try two Overlays on the same Scope and inspect Canvas actions
in both Events outputs.
