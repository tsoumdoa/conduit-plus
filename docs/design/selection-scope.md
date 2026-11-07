# Shared selection scope — design, 2026-11-07

Basis: main 2e10e9c and canvas-cards.md's explicit-sharing deferral. Share brush state, while retaining each host's rendering, layout, sort, scroll and gesture ownership. Group semantics are defined in group-keys.md.

## Wiring and lifetime

Add **Scope** under Display, with optional Name (display label only) and one typed Scope output. Its token uses the component InstanceGuid; GH document runtime identity completes the broker key. Append optional Scope inputs to Canvas Card and Overlay, accepting exactly one token across the input tree, not GH item iteration. A token carries its source document identity; reject foreign-document tokens. Invalid/multiple wired tokens produce an error and detach the display from sharing, disabling brush interaction until corrected. Unwired inputs retain current isolated behavior.

Wire one Scope output to every participating display. Equal names, CardSpec IDs or keys never imply sharing. Give the new component a new fixed GUID; preserve existing GUIDs/input indices. A copied Scope component obtains a new InstanceGuid, while copying only displays connected to the existing Scope intentionally retains sharing. Save normal GH components/wires, not live brush state. No implicit conversion from text or GUID strings.

Rejected: text names silently couple dashboards through spelling collisions; deriving scope from card/Link identity defeats explicit intent.

The GH assembly owns one broker per GH document instance and one BrushState per token. Open starts empty; switching GH tabs/unloading suspends subscriptions/rendering and clears hover but retains managed membership and selection, because GH 8.20 unloads tabs without necessarily solving them on return. Actual document close deletes all broker state and callbacks. Delete the last participant and release that scope state; rejoining starts empty. Scope deletion invalidates its token registrations. Undo restores wires/components through GH, but does not restore transient selection. Scope changes detach/attach atomically without copying the old selection. Source-card replacement resets local sort/scroll, but must not reset an explicit shared selection.

Rejected: serialization/undo of selection adds analysis-state history users have not requested; treating every Unloaded notification as Close loses selection during tab switching.

## Broker and host boundary

Use a Rhino/GH-free Core interaction-domain abstraction exposing registration replacement, immutable versioned snapshot, Hover(origin,target), ClearHover(origin), Toggle(target), Clear and change notifications. Implement pure membership/reconciliation logic in Core; GH broker supplies UI-thread scheduling and lifetime. OverlayManager accepts an optional domain binding/callback adapter, never references Grasshopper types. Its standalone/demo and unwired paths retain private BrushState ownership. CanvasCardState similarly accepts the domain instead of privately toggling/pruning shared state.

Each participant registers original, unprojected card specs and its Link keys under a unique participant identity. The domain aggregates their live keys and group definitions. Stage all replacements during a GH solution and commit once at solution end on the UI thread; retain unchanged participants' contributions. Apply removals/errors explicitly. Reject stale queued publications using document, participant and generation identifiers. Never prune after each participant update: upstream/downstream solve order otherwise erases valid selection. Conflict handling follows group-keys.md, deduplicating identical definitions across displays.

Rejected: copying snapshots between independent BrushStates introduces competing authorities; calling existing per-owner Prune on a shared state deletes other participants' keys.

Only brush state is shared. Overlay retains its (Rhino document, view, owner) session, textures, layout and Links. A Canvas click changes the broker; all subscribed Overlays repaint their own cards and matching linked geometry in their existing bound views. No Link is transferred or meshed by Canvas Card. Closing a Rhino view disposes its rendering only; closing the bound Rhino document suspends that Overlay's runtime binding and removes its active contribution, while other GH participants survive. Rebinding requires a subsequent normal data solve, never a queued interaction redirected to the active document. Existing model-pick priority/tie ordering stays intact: the winning owner sends exactly one action to the domain.

Rejected: merging owner sessions would also merge layout and geometry lifetimes; automatic cross-document/view rebinding could highlight an unintended model.

Track hover origin as participant + surface + gesture generation. Latest valid pointer update wins; a leave/clear only clears matching origin. Document switching, navigation, drag, disable and detach clear that participant's hover. Disabled components retain frozen membership/selection but cannot originate interaction; re-enable reprojects the latest snapshot. Other sessions must not clear a winning hover while their own hit tests miss. Tooltips remain local to the pointer's host; shared highlights never spawn remote tooltips.

Rejected: the current model/card boolean alone cannot distinguish two Canvas Cards or owners; broadcasting unconditional hover-null causes flicker.

## Outputs and solve discipline

Append **Selection** to Canvas Card, matching Overlay's sorted unique effective member keys for its whole domain (private when unwired). Do not add Canvas Events yet. Existing Overlay Selection also returns the whole explicit scope, removing its current local-card/Link intersection. Shared-scope Overlay Events mirror the same bounded domain action history, including Canvas actions, with original source owner/card and an additive sourceSurface field. Snapshot notifications never manufacture events.

On committed user selection/clear, repaint immediately and schedule one coalesced GH solution per document, expiring only participating output components whose selection or event revision changed. Schedule outside SolveInstance/render; read the latest authoritative snapshot when producing outputs. Preserve Overlay's bound-view interaction-publication path. Hover, sort, scroll, projection and identical snapshots never schedule a solution.

Data reconciliation can prune selection after outputs were evaluated. Permit one coalesced output-refresh solution after that data transaction, tagged reconciliation-only. If its feedback wiring prunes again, update the broker/display immediately but do not schedule another automatic refresh; warn that outputs await the next external solve. No newly arriving keys are auto-selected. This bounds automatic feedback, while an ordinary prune publishes accurate outputs after one refresh. Test this explicitly; revision equality alone does not prove loop freedom.

Rejected: outputting only Canvas-local keys prevents a useful shared selection filter; scheduling on every snapshot/solve creates feedback loops; silently retaining stale output after an ordinary prune misleads downstream consumers.

## Verification and delivery

Pure tests: registration union, atomic replacement, overlapping groups, stale generations, origin-safe hover clearing, isolated tokens/documents, source replacement, version coalescing and bounded reconciliation feedback. Host validation: Canvas → model and model → Canvas, two Overlays sharing one token, isolated owners, repeated CardSpecs, tab unload/reload without a solve, delete/undo, close/reopen, locked components, paused solver, pending callbacks during close and Selection wired back into card filtering. Validate pinned GH lifecycle/scheduling APIs during implementation. Existing zoom cache/export rules apply; strip all transient group-hover fields in exports.

PR B1: Core domain/registration contract and GH Scope/broker lifecycle tests. PR B2: Canvas and Overlay adapters, Selection/Events publication, solve-loop guards and live validation. Agree A1/B1 interfaces first; implement A2 and B1 in parallel in separate files where possible, then integrate B2 after A2. Overlap includes Linking.cs, CanvasCards.cs, Components.cs, CanvasCardComponent/Attributes.cs and OverlayManager/Session.cs; nominate one integrator, with scope owning lifetime/output changes and groups owning projection/paint. Two implementers can work in parallel, but not independently replace shared state paths. Final reviewer builds/tests and installs the integrated checkout under AGENTS.md; this design-only work requires no installation or commit.
