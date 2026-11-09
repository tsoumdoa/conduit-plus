# Viewport layout: targeting, proportional scale, flow, overflow and titles

Status: **design, revision 3 (2026-11-09).** Revised after two rounds of independent review by Astra (GPT-6 Astra)
and Opus 5.5 against the code and the pinned 8.20 APIs. Builds on [layout.md](layout.md), which covers drag editing.
Overlay code lives in `src/ConduitPlus.Display`; bare file names below refer to it, `Core`, or `ConduitPlus.Grasshopper`.
The behaviour was agreed with the user through interactive mocks. Values marked *tunable* are expected to move after Rhino testing.

## Problems today

| Problem | Cause |
|---|---|
| Cards run off the viewport and collide | `SlotLayout.Place` (`Core/Drawing.cs:67`) stacks each of six slots alone, with no check against the viewport or other slots. |
| Overlays collide | Each `OverlaySession` places its own cards; nothing shares space across owners. |
| Cards don't scale with the viewport | The only factor is `UiContext.Dpi`. |
| Overlay jumps between viewports | `Publish` binds to `doc.Views.ActiveView` on every data solve (`Components.cs:604`). |
| No overflow feedback | Nothing measures overflow. |
| No heading | The `Text` card is a note on a card in the side slots. |

## User decisions

1. Prefer smaller text over more compacting: zoom floor 0.625, so 7.5 px body text at 100% display scale.
2. Overlay gets **View**. Default **Active viewport**, for new and existing components. No "All viewports".
3. Overlays on one viewport **share one layout**.
4. Overflow shows a **count chip** in the viewport and a Grasshopper warning.
5. **Last-wired cards hide first.** No Priority input. "Last-wired" means last in the collected input order, not connection time.
6. Overlay gets **Size**, 0.75–1.5×.
7. New **Title** component: Title, Subtitle, Description, six positions.
8. Titles default to **plain text with a soft outline**; a card background is an option.
9. Top middle and bottom middle are **Title-only**.

Consequence of decision 1: at Size 1, any view below about 1000 × 563 CSS px starts at the floor, even with one card, so 4-up on
1080p at 100% draws at 7.5 px. Size above 1 raises that start point (960 × 540 at Size 1.5 starts at 0.9×), though Shrink can still pull it back.

## Guarantees

After a settled solve, every placed card and Title is inside the viewport and overlaps nothing, and anything left out is reported.
While a viewport is actively resizing, cards may be briefly clipped (see Resizing). No free-form positions, resizable cards,
keep-clear control, multi-viewport Overlays or legacy pixel mode.

## Data model

**Authored and presented specs stay separate.** Today every interaction feeds the projected `Cards` back into `Update`
(`OverlaySession.cs:73,108,126,155,162,165`, and `ReplaceTheme` in `OverlayManager.cs`). Compact state applied that way would overwrite its own source, and tables would never expand.

- Each session holds **authored specs**: originals after `LayoutEditing.Apply`. Solve only ever reads these.
- `CardSpec` gains a separate **`Form`** (compact level, effective rows, Title width and alignment). `MaxRows` stays authored; effective rows are `min(MaxRows, form limit)`, so a 3-row table never grows.
- Presentation is one direction: authored → form → scroll and sort → brush → store. `CardMeasure.Measure(spec)` remains the only measurement, used for solving and painting.

**Identity is `(owner, cardId)`** in placements, frames and chip lists. The same card can feed two Overlays.

**Frames.** A solve publishes one immutable `ViewportFrame`: generation, placements, hidden entries, forms, each owner's unit and the chip. Draw, hit testing, `Local`, drag and export read a frame. They never solve.

**Units.** Cards stay authored in design units (today's "CSS px"); `Width` means units at 1×. `unit = dpi × zoom` device pixels per design unit.
Canonical viewport size is `MainViewport.Size` in all paths; today there are three sources.

## Solver

`ViewportLayout.Solve` in Core is pure, with no Rhino types. The request carries viewport, DPI, participants and an explicit text measurer, because `TextMetrics.Measurer` is a global that parallel tests overwrite. Each rung runs only if the previous one couldn't fit everything: **stay big, wrap before shrinking, shrink before compacting, compact before hiding.**

**1. Scale.** `base = min(cssW / 1600, cssH / 900)`. For each owner, `zoom = snap(clamp(base × size, 0.625, 2.0))`, with `snap` rounding to 1/32 (*tunable*) so small resizes don't re-raster.

**2. Wrap.** Two rails: left (`tl, lc, bl`), right (`tr, rc, br`). Within a rail, slot order then `LayoutEditing.Stack` order. Fill columns inward. Columns keep anchors: top cards from the top, bottom cards from the bottom, centre cards centred and clamped between. Columns stay out of the **keep-clear** area, 35% of viewport width (*tunable*).
A card wider than the keep-clear budget may sit alone in its rail's first column if it overlaps nothing; otherwise a legal `Width` of 1100 would be hidden on every landscape view.
Portrait viewports (`width < height`) rotate: left rail to a top band, right rail to a bottom band, filled as rows, keep-clear on height.
A fixed top inset of 30 px × dpi keeps the viewport label clear and does not scale with zoom (*tunable*).

**3. Shrink.** A common multiplier steps down by 1/32 toward the floor: `zoomᵢ = max(0.625, snap(initialᵢ × m))`. Owners converge at the floor, so Size ratios intentionally disappear under pressure.

**4. Compact.** At the floor, apply one form at a time, retrying after each:

| Order | Change |
|---|---|
| 1 | Tables to 5 visible rows |
| 2 | Bars to 10 visible rows |
| 3 | Title descriptions to 2 lines |
| 4 | Line cards 220 → 140 units tall |
| 5 | Gradient legends 112 → 72 units tall |
| 6 | Text cards to 2 lines |
| 7 | Metrics to one line, title left and value right (92 → 48 units) |
| 8 | Bars to 6 rows |
| 9 | Tables to 3 rows |
| 10 | Drop Title descriptions |

**5. Hide.** Reserve the chip first (34 units, bottom right, also clearing any `br` Title). Then:

- Remove cards last-first, round-robin across owners by each owner's last-wired rank, so one Overlay isn't emptied before the other is touched. Owner tie-break is `Guid.ToString("N")` ordinal; copying an Overlay can change its tie-break.
- Then try re-inserting each hidden card, earliest to latest, so one wide early card can't force many others out.
- This is deterministic, not minimal. Cost is O(n²) packing with `Measure` memoised by card and form.
- **Titles hide last**, after every card, and are counted in the chip. Before hiding, a Title truncates: description, then subtitle, then to one title line.

## Coordinator and lifecycle

A per-(document, view) coordinator lives in `OverlayManager`, on the existing single foreground pass (`OverlayManager.cs:285`). No new conduit. `Stop`, document close and view destroy dispose coordinators and their timers directly. Unregistered benchmark sessions (`PickBenchmark.cs:16`) get a private single-owner frame.

**`Settle(view)`** runs outside draw, because `Update` can cancel gestures and redraw. It is called from every session update, participant add or remove, Size or theme change, and the resize timer. It:

1. Builds a **layout signature**: owner order; per card its id, kind, width, index, resolved slot and order fields, text inputs, row, value and colour counts, `Min.HasValue`, authored `MaxRows`; theme metrics; Size; visible participants; viewport; DPI. Hover, selection, scroll and sort are excluded.
2. If the signature changed, solves.
3. Applies forms and calls `Prepare(unit)` on every participant, including `OverlaySession.cs:61`, without touching gestures or tooltips unless geometry changed.
4. Publishes the frame and redraws affected views, including a view an owner just left (`RemoveWhere` settles too, except on view-destroy and document-close paths).
5. Reports overflow once, when settled.

Presentation changes (hover, scroll, sort) refresh rasters without re-solving. When a card becomes hidden, its card-origin hover and tooltip clear; selection and model-origin hover stay.

**Draw order and hit testing.** Cards by session, then shown card, chip and list, then tooltips. One `HitTest(view, x, y)` follows the reverse of that order and is used by all `OverlayInput` paths, replacing per-session loops. Card-local coordinates divide by the owner's `unit`.

**Resizing.** While size is changing, reuse the last settled zoom, forms and hidden set, and only reposition against the viewport edges. `DrawBitmap` can't scale, so bitmaps are reused as they are and cards may clip briefly. One UI timer per coordinator settles about 120 ms after the last change and is disposed with the view. A draw that sees a size mismatch only arms the timer. Overflow isn't reported until settled. `DrawSprite(bitmap, point, w, h)` scales and is worth trying later, but its alpha behaviour is unverified.

**Export.** A capture settles first if needed, then snapshots eligible participants and the frame **before** suppressing conduits, because `PdfCommand` sets every session invisible first (`:41`). Export scales that snapshot, keeps the chip reservation so nothing reflows, and omits chip, list and shown cards. The command prints how many cards were omitted. `PdfCanvas` font size becomes `size × unit × 72 / (96 × dpi)`, because it's hard-coded today (`PdfCanvas.cs:104`). Capture never changes live overflow, drag or scroll state. PDF has no Title halo, so exported text stays searchable.

**Tooltips** stay at DPI scale so they remain readable at the floor.

## View input

Appended as input 4, Optional, item access. Read it before every early `QueuePublish` return in `SolveInstance`.

| Input value | Mode |
|---|---|
| Unset, or wired null, empty or whitespace | Active |
| The text `Active viewport`, case-insensitive | Active |
| Any other text, trimmed | Named |

Item access re-runs the solve per item, so several values warn and use the last.

**Option list.** `Param_TextOption` gains `Func<IReadOnlyList<InputOption>>? OptionsProvider`, evaluated only when the menu opens. `ApplyOption` compares by value against that list, or accepts the clicked option when a provider is set, so a name that vanished after the menu opened is stored and then reports as not open. Overlay's provider uses its bound document, else `RhinoDoc.ActiveDoc`: **Active viewport** (no value), each distinct `MainViewport.Name` from `GetStandardRhinoViews` (`Find` also matches pages), then a stored name not currently open.

**Resolution** is pure logic in Core over `(id, name)` pairs, with the currently bound id: keep the bound view while its name still matches ordinal-ignore-case, otherwise take the first match. It reports whether a name is duplicated.

**Which view to use.**

| Path | Active mode | Named mode |
|---|---|---|
| Data solve | `ActiveView`, as today | Resolve name in the document |
| Interaction solve | Bound view, as today | Resolve name in the document |
| Activate, ScopeActivity, rebind | Bound view, as today | Resolve name in the document |
| No match | Unchanged | `Detach`, warn, retain publication |

The document is `pending.DocumentSerial`, else `RhinoDoc.ActiveDoc` for a first publish, as today. A retained named intent stays in its document; only a new data solve with no binding can bind another one. `PendingPublish` gains `RequestedView`. Every publish, resolved or not, writes `_lastPublish` with the document serial; unresolved writes `ViewId = null`. Rebinds use `_selectedTheme`, since `ApplyTheme` doesn't update `_lastPublish` and `ReplaceTheme` does nothing without sessions.

**`OverlayManager.Detach(doc, owner)`** ensures `OwnerState` and `Bindings` exist for the document, sets `ViewId = null`, removes the owner's sessions and redraws the views they left. Unlike `Remove`, selection and layout survive, and `Binding` returns `(doc, null)`. Because state exists even for a view that never resolved, document close still fires `BoundDocumentClosed` for it.

**Rebinding.** The manager owns the events, next to `Destroy` in `Start`, removed in `Stop`: `RhinoView.Create`, `Rename` and `Modified` if the pinned API has it, so a `_SetView` retitle is caught whatever event it fires. Each coalesces into one `RhinoApp.Idle` one-shot, also removed in `Stop`. That hook compares a snapshot of `(document, id, name)` and raises public `ViewsChanged` only if it differs. Overlay subscribes in `Activate` and unsubscribes in `Deactivate`, like `BoundDocumentClosed`. It re-publishes only when `_active && !_rhinoSuspended && !Locked && _lastPublish` exists and the mode is Named. Generation checks alone don't cover Locked or `Stop`.

**Warnings**, as notices (see Overflow notice):
- `View "Detail A" isn't open, so nothing is drawn. Open it or pick another View.`
- `More than one viewport is named "Top". Drawing in one of them.`

## Size input

Appended as input 5, Optional. A new hidden `Param_Number` subclass with a stable GUID and a right-click list (0.75, 1, 1.25, 1.5); `Param_Number` has no named-value support. Default 1. Non-finite or out-of-range values clamp and warn.

## Overflow notice

**Notices.** Grasshopper can add a runtime message outside a solve (`Components.cs:482` already does), but can't remove one, and clearing wipes every message. Overlay gets `SetNotice(kind, level, text?)`, where `text: null` removes that kind. View kinds arrive in PR 1 and overflow in PR 3.

- Overlay overrides the virtual `AddRuntimeMessage` to record solve messages in a list, except during replay (replay goes through `base.AddRuntimeMessage` behind a guard). `ClearData` resets that list but keeps current notices.
- After each solve, `AfterSolveInstance` re-issues current notices, so an Overlay click can't lose them. Every click runs `ExpireSolution`.
- `SetNotice` outside a solve clears messages, replays the recorded list and notices, and invalidates the canvas. During a solve it defers to one `GH_Document.SolutionEnd` hook, removed in `Deactivate`. It never expires a solution. `CanvasCardComponent.cs:210` is the existing clear-and-re-add pattern.

Overflow text: "3 of 9 cards hidden in Perspective (640 × 400). Enlarge the viewport or remove cards." It says "items" instead of "cards" when a Title is included.

**Chip.** One per view, drawn after cards, reading "+N cards", or "+N items" when a Title is included, with a passive form first (see Delivery). Clicking opens a list grouped by Overlay. Rows use the card title, or kind and number when untitled, and the list has a height limit. A hidden Title's row shows its text, and clicking floats it with Card styling. Owner labels come from the component name passed in `Set`.

**Shown card.** Clicking a row floats that card in the keep-clear area, clamped to the viewport, using normal hover, scroll and selection. It isn't draggable. It closes on clicking its row again, on Escape (`RhinoApp.EscapeKeyPressed`, without swallowing command cancel), or on a geometry change, not on any revision. It isn't part of exports.

## Title component

New component, new GUID, under **Cards**. Inputs: Title, Subtitle, Description (at least one required), Position (`tl tc tr bl bc br`, its own option list), Background (Plain or Card). `InputOptions.Slots` stays at six.

- New `CardKind.Title`. `Slot` gains `Tc` and `Bc`, appended, with `Zone` handling them.
- `Resolve` is kind-aware: Title defaults to `Tl`, has no `WiredSlot` unless set, and is **excluded from the automatic `index % 6`**, so adding a Title doesn't move other cards. Stored values count as wired today (`ReadSlot` doesn't check `SourceCount`), so an unset Position must stay unset to remain draggable.
- Validation and `Resolve` ignore Title-only slots on ordinary cards and vice versa, so stale overrides are harmless. `Enum.GetValues<Slot>()` in `NearestSlot` and `DrawLayoutEditing` becomes a per-card allowed list.
- Titles at one position **stack across owners** in participant order, and stay out of card stack ranks.
- Sizes in units, all scaled by `unit`: title 26/32 semibold, 2 lines; subtitle 14/20, 2 lines; description 12/18, 4 lines. Measurement and drawing gain a font-weight parameter; `GdiCanvas` only makes regular fonts today.
- Width comes from the solver via `Form` (shrink-wrapped): sides up to 45% of the viewport, or 30% when a middle Title shares the row; middle up to 60%. Titles are exempt from the 180-unit minimum. Canvas Card uses a fixed width and Card styling.
- Text alignment follows the position column.
- Space: a side Title pushes only its rail. A middle Title wraps to fit between rails and side Titles, without pushing rails. In portrait, Titles sit above or below the bands.
- `CardPainter` handles Title before the surface, shadow and border.
- Outline: a default interface method on `ICanvas2D` that falls back to `Text`, so PDF and test canvases are unchanged. `GdiCanvas` builds one alpha mask (`GraphicsPath.AddString` with a round-join pen), blurs it, composites one solid colour correctly premultiplied, then draws the fill. Stacked low-alpha strokes already failed for shadows (`Cards.cs:173`). Raster margin includes halo extent; it's 0 today without a shadow.
- Interaction: Titles report text regions, used for drag and hit testing, so subtitle-only Titles work and transparent space passes through to Rhino.

## Compact and Bar forms

Each form needs geometry, not only a height:

- Compact Metric: one row, the whole body is its hit region, with a defined Clear position. Today's body hit becomes zero height (`Drawing.cs:54`) and the 32-unit title band would cover it.
- Line and Legend painters have hard-coded layout that must take the form.
- Bars gain `BarScroll` with view offsets, wheel and scrollbar. Rows map to data through an offset, because painting assumes data index equals row index (`CardPainter.cs:46`). Reveal works for Bars and re-runs when a form changes.
- Visual scale stays uniform. The scrollbar gets an undrawn hit margin of at least 12 px × dpi; row targets don't expand.

## Drag editing with flow

- A drag freezes the **whole view's frame**. Any geometry-affecting change from any participant cancels it, matching layout.md's rule.
- Drag geometry uses placed cards only; `.First` over stacks would throw on hidden cards (`LayoutEditing.cs:23`, `OverlayLayout.cs:43,123`). `Drop` ranking still includes hidden cards, with a defined mapping from visible insertion to the owner's full stack.
- `InsertionIndex` uses reading order: nearest column then Y, or nearest row then X in portrait. One zone scale. Portrait draws zones as bands.
- Persisted changes stay owner-local and use the existing proposal and undo path.

## Compatibility

- Overlay and card GUIDs and layout JSON version are unchanged.
- **Appending an input needs a read fix.** `GH_ComponentParamServer.Read` adds an "Input parameter chunk is missing. Archive is corrupt." error per missing input, and PR #26 hit exactly this. Mandatory in PR 1: before `base.Read` in Overlay, walk `Params.Input` from the end while `!reader.ChunkExists("param_input", i)`, calling `Params.UnregisterInputParameter(p, false)`. In `finally`, restore in ascending order with `RegisterInputParam(p, i)`. The loop stays generic, so Size in PR 3 and pre-Scope files need nothing more.
- **Frozen fixture.** Save `git show 03cb08d:examples/structural-dashboard.gh` under `tools/examples/fixtures/`, outside `examples/`, so regeneration can't erase the regression. The harness loads it and calls `ArchiveClean`, then asserts five inputs, input 4 unset, wires and layout intact, Active viewport behaviour, and a clean save and reopen with View present.
- The pre-commit hook and `Pack-Yak.ps1` don't run this harness, so `Build-Examples.ps1` is a PR 1 gate.
- Existing dashboards keep their slots and render at the new scale. This is intended.
- Update README (lines 157 and 191 at least), the Overlay description in `Components.cs`, `NEXT.md:31`, and `ExportChecks`, which calls `Place`.

## Delivery

Small stacked PRs. Each ends with a Yak package.

1. **View input:** Core name resolution and tests, binding intent, `Detach`, `ViewsChanged`, notices, the Overlay read fix and frozen fixture. Gate: `Build-Examples.ps1`, and a hand check that `_SetView` keeps a named Overlay on the right viewport.
2. **Pure Core solver:** rungs, hide, signature. No host change.
3. **Cutover:** coordinator, authored/presented split, frames, `unit` raster, all `Place` callers, PDF, drag rework, Size, hide with passive count chip and warning. Hide never ships without its notice.
4. **Compact forms and Bar scrolling.**
5. **Hidden-card list and shown card.**
6. **Title component**, including `Tc`/`Bc` and the per-card allowed-slot list, so the slot enum and `InputOptions.Slots` tests don't change earlier.

## Verification

Core xUnit (put logic here, since tests can't load RhinoCommon): no placed item outside the viewport and no overlaps, across sizes, DPI, card sets, Sizes and owners; rung order; authored specs survive repeated projection; solve is idempotent; compact then expand restores; effective rows never exceed authored; `Measure` agrees with hit rects for every form; anchors in wrapped columns; portrait bands; adversarial hide (wide early card, tall early card); round-robin determinism; same card id on two owners; hidden cards in drag and insertion; Title defaults, stale overrides, truncation order, stacking; snap and floor invariants; settle and overflow dedupe; PDF point-size formula; name resolution.

Harness: frozen old `.gh` fixture; menu persistence and undo; non-finite Size. `DisplayLifecycleChecks`: constructing `OverlayComponent` doesn't start Display, `Detach` keeps the binding, `Stop` clears it, a pending rebind doesn't survive `Stop`, document close or lock.

Rhino by hand: 4-up, maximised, floating and DPI change; resize smoothness and the settle timer; named view close, reopen, rename, rename-away; whether `_SetView` retitles a viewport and fires `Rename`; runtime-message refresh; chip clicks versus model picks; drag at the floor; shared viewport; scope selection while hidden; halo over light and dark geometry; PNG and PDF match the screen and restore state after failure.

## Review record

Second round (revision 3): missing-chunk error confirmed by PR #26 and made a mandatory PR 1 fix (both); view choice per publish path and intent surviving without a session (both); event ownership under `Start`/`Stop` and lock (both); notices replacing the retained-message idea (both); name resolution moved to PR 1 (both); `Tc`/`Bc` moved to PR 6 (Opus).

First round: compact state overwriting itself (both); shared solve propagation and the existing foreground pass (both); hidden cards throwing during drag (both); cache key (both); PDF text size and invisible sessions (both); runtime message removal (both); unresolved view lifecycle (both); Titles never hidden being infeasible (both); greedy hide over-hiding (both); Title width and automatic-slot conflicts (Opus); old-file load messages (Opus, to confirm); keep-clear hiding a lone wide card (Opus); Size param type (Opus); owner-qualified identity (Astra); capture snapshot (both).
