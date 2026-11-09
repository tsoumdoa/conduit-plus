# Architectural example UX notes (2026-11-07)

Scope: `room-areas.gh` and `massing.gh`, generated from this checkout without
installing it. Findings are ranked by impact on an architectural review.

## Shared selection Scope B2 verification (2026-11-07)

The room example now includes a Canvas room table and one Scope wired to both
Canvas Card and Overlay. Select Bed 01/R01 in either table or the model: the same
member highlights in all three places. Clear affects the whole scope. Canvas and
viewport tables keep independent sort/scroll, and tooltips remain local.

- Debug build: zero warnings/errors; **255 tests passed**, including seven new
  adapter tests. Original unscoped fixtures still use independent authorities.
- RhinoCore 8.36 / .NET 8.0.33 checks exercise Canvas-to-Overlay/model and model-to-
  Canvas projection, whole-domain Selection, shared Canvas/Overlay event history,
  two Overlays, isolated owners, token-tree validation, scope changes, local tooltip
  targets and coalesced output solves. The production model picker remains unchanged.
- Host lifecycle checks cover Unloaded/Loaded without a solve, participant and
  Scope delete/undo, component lock/unlock, global solver pause/resume, GH close/
  reopen and pending callback invalidation, owned Rhino view close and an owned
  headless Rhino document disposal. Closed Rhino contributions are removed;
  interaction solves cannot redirect/rebind them. A normal data solve can rebind.
- Feedback fixture physically connects Canvas Selection back into its card filter.
  Grasshopper rejects a direct recursive evaluation dependency, so a harness-only
  snapshot parameter reads the authoritative Selection without recursively solving
  its source. It schedules nothing. The broker allows the action solution plus one
  reconciliation refresh, then warns on a second prune with no successor schedule.
  This is a verification adaptation, not a new general-purpose GH feedback component.
- 1× GH export and viewport captures are under
  `artifacts/screenshots-scope/verified/room-areas/` (`scope-canvas-1x.png`,
  `scope-viewport-1x.png`), visually inspected. Both tables show Bed 01 selected and
  the same room face is blue. Card/geometry layout is clear; Canvas Auto resolved
  to Night in this host, matching the viewport. Hover is omitted by existing export
  paths. A later final capture is recorded in the linked verification record.
- Physical mouse release/capture, native UI tab gestures, live Rhino 8.20 (this host
  is 8.36), mixed DPI and repeated interactive resource-plateau testing remain for
  normal Rhino. Core tests additionally cover stale leases/publications/generations,
  atomic union handoff, source replacement and bounded refresh decisions.

No commit, push or developer installation by this implementation sub-agent.
Reviewer should install the integrated checkout, open `room-areas.gh`, toggle
Bed 01 from Canvas and the model, try Bedroom/Clear, then change Plan scale.
Detailed files, commands and limitations: [scope verification](verification/selection-scope.md).

## Fixed

| Rank | Finding | Change and evidence |
|---|---|---|
| P1 | Category legends/GFA-by-use bars could not select a whole category; failure Metrics were passive. | A2 wires explicit group keys and dynamic Members for room types, building uses and failure Metrics. Shared painting distinguishes partial/full selection and effective hover while retaining category colors. One action expands to sorted member output and one event; table reveal chooses the first newly selected row in display order. |
| P1 | Selecting a room from the model could leave its table row out of view. | Minimal reveal in the current sorted display order. Only a newly selected key triggers reveal, so manual scrolling, hover, deselection and subsequent solves do not snap back. Pure Core tests cover visibility boundaries, missing keys, short/empty tables, stale offsets and sort order; the reopened room definition exercises unsorted and sorted selection. |
| P1 | Edge-only highlighting made planar rooms and solid masses hard to identify. | Translucent accent faces under the existing wires/markers for hover and selection, with depth testing/writing disabled. Highlighting reuses the Link's owned pick meshes; meshing/material creation stays outside drawing. PNG checks sample the selected face centre, away from edges, to prove fill is present. |
| P1 | Face-interior picking needed checking before these examples were useful. | Existing production shaded-mesh picking already worked; no picker algorithm change was necessary. RhinoCore checks hit all 17 room interiors and all six block roofs, plus explicit extrusion and mesh face probes. Table-originated selection is also checked before capturing the highlighted model face. |
| P2 | Metric numbers showed distracting precision. | Appended optional Decimals input (0–12), with invariant numeric formatting; unwired values and nonnumeric text retain their original strings. Missing values still show the em dash. Existing input order/GUID is unchanged. |
| P2 | Architectural metric fixtures still showed inconsistent area precision. | Regenerated both definitions with Decimals 0 for GIA and total/allowed GFA: `320`, `14976`, `16000`. FAR stays at two decimals (`1.87`); room percentages stay at one (`87.5`, `12.5`). Reopen verification asserts the displayed metric strings. |
| P2 | Paper's selected faces were too faint. | Lowered selected-face transparency from .65 to .40 and hover from .82 to .72 for every theme. Safe-mode 1× Paper, Smoke and Blueprint PNGs show stronger hall/block fills while retaining model edges; selected-face samples also check readable alpha without becoming opaque. |
| P2 | Structural base wires were too faint. | ShowBase now draws Theme.Muted wires at width 2 instead of 1. The structural capture shows readable secondary members; hover retains the accent at width 2 and selection the accent at width 4 with markers. |
| P3 | Smoke's shadow appeared as a hard grey band. | Removed in #13 after screenshot review; Smoke keeps its translucent surface and rounded corners without a shadow. |
| P2 | Signed area deficits need red despite being negative. | Appended optional per-bar Colors. Room delta bars use Theme.Bad for deficits and Theme.Good otherwise. Zero threshold is accepted for deltas; all-zero series have finite paint geometry. Existing default utilization coloring remains. |
| P2 | Six schedule columns were cramped at the table's fixed width. | Appended Width input (default remains 380 CSS px); examples use 600 px. Tables are manually placed bottom-right, leaving the centre of a large viewport available to the model. Layout bounds and card overlap are checked during capture. |
| P2 | Wheel scrolling left hover on the previous row until the pointer moved. | Re-hit the table at the wheel position after scrolling, updating brushing and tooltip state synchronously. Physical stationary-pointer behavior still needs GUI confirmation. |
| P3 | Longer architectural bar labels overlapped the bars. | Measure the longest label while rasterizing and reserve a capped label column, clipping extreme names. Residential/Office/Retail now fit without touching their bars. |
| P2 | A combined capture run could fail natively while changing GH definitions in one RhinoCore host. | The wrapper builds once, then runs each definition in a fresh host process. This retains the default all-examples command and isolates native host state. |
| P3 | A type/use legend should match the preview. | Demo components shade their default GH previews with the same palette as their legends. Room names are compact and keys remain independent/stable (R01–R17, B01–B06). |

## Still open

| Rank | Finding | Next action |
|---|---|---|
| P2 | Model framing and card layout compete on small or high-DPI viewports. | Current GH files save manual card slots, not the Rhino camera. Use Top + Zoom Extents for rooms and Perspective + Zoom Extents for massing, then drag cards if needed. Responsive layout/card resizing is larger work. |
| P2 | Tables have equal-width columns and can clip long names; tooltips contain full cells. | Consider per-column sizing in a later iteration. The new width input is a small interim improvement. |
| P2 | Full-scene 2×/4× capture, architectural PDF output and depth-disabled faces in PDF remain unverified here. | Validate in normal Rhino. The existing full-scene high-resolution RhinoCore native-failure limitation is unchanged. ExportChecks now accepts architectural document geometry for PDF rather than substituting the truss. |
| P2 | Physical mouse/card precedence, orbiting, selected preview colors and mixed-DPI behavior require normal GUI testing. | Try the steps below on Rhino 8.20+; the automated host here is 8.36. |
| P3 | Bars show height/limit ratios, not paired actual/limit bars; label space is capped. | Raw values are available in the table and demo output lists. Consider a comparison chart and resizable bars separately. |
| P3 | Room GIA has no wall thickness and massing GFA has no setbacks, voids or exclusions. | Keep the schematic assumptions visible. These fixtures are not area-measurement or planning-rule implementations. |

## Group keys A2 verification (2026-11-07)

A2 changes remain uncommitted for Claude's review; no developer installation.
The screenshot harness frames the model inside the open centre between cards;
the saved GH definitions still leave camera choice to Rhino. Each full-scene
screenshot runs in its own RhinoCore host because repeated captures in this host
can throw a native SEHException. Only wrapper-owned hosts are stopped after
successful overlay/view cleanup.

- `dotnet build ConduitPlus.sln -c Debug`: 0 warnings/errors.
- `dotnet test ConduitPlus.sln -c Debug`: 233 passed, 0 failed/skipped.
- Both architectural `.gh` files regenerated. All three examples reopened/solved
  in isolated RhinoCore 8.36.26296.17001 / .NET 8.0.33 hosts without runtime
  warnings/errors. Existing structural GUIDs and appended-input loading remain valid.
- Real GH input probes cover path-ordered and empty branches, wired-empty trees,
  explicit-key requirements, Metric Key alone, path/item errors and gradient rejection.
- Group actions verify selected member outputs, one event, full/partial toggling,
  sorted table reveal, failure Metric/Clear, private Canvas selection and changing
  geometry (rooms: eight failures to zero; massing: two failures to one).
- PNGs are 1× captures, 1874 × 1729 physical pixels at the host's 200% DPI:
  - `artifacts/screenshots-groups/room-areas/bedroom-selected.png`
  - `artifacts/screenshots-groups/room-areas/bedroom-partial.png`
  - `artifacts/screenshots-groups/massing/residential-selected.png`
- All three were visually inspected. All five bedroom faces/table rows and both
  Residential blocks/rows highlight together. The partial legend uses a dash in
  the right gutter; moving it there fixed an initial Metric dash that looked like
  a minus sign. Category swatches/bars remain unchanged; model and cards do not
  overlap in the final verification framing. No remaining group-paint defect was
  apparent. Smaller interactive viewports still need manual camera/card layout.
- Separate GroupHover captures compare to the selected captures with **zero
  differing RGBA pixels** in both fixtures, proving card and model hover omission.
  Evidence: `artifacts/screenshots-groups/hover-export-comparison.json`.
- All-example reopen logs: `artifacts/screenshots-groups/final-reopen/`.
- No group-semantic design deviations. Verification uses one capture per native
  host and a wider camera frame; it does not change the saved GH camera or layout.
  Live mouse/DPI behavior and experimental PDF remain for normal Rhino testing.

A2 files changed:

- Core: `GroupInputs.cs`, `BrushPresentation.cs`, `CanvasCards.cs`, `Drawing.cs`,
  `CardPainter.cs`, `LineCardPainter.cs`, `Tooltip.cs`.
- GH: `Components.cs`, `DemoArchitectureComponents.cs`, `CanvasCardComponent.cs`,
  `CanvasCardAttributes.cs`.
- Rhino: `OverlayManager.cs`, `OverlaySession.cs`, `OverlayInteraction.cs`,
  `OverlayTooltip.cs`, `PdfCommand.cs`, `Rendering/RasterCardRenderer.cs`.
- Tests: `GroupPresentationTests.cs`, `TooltipSortTests.cs`,
  `ConduitPlus.Core.Tests.csproj` (source-links the Rhino-free event wire record).
- Tools: `Build-Examples.ps1`, `Dashboard.cs`, `ArchitectureDashboard.cs`,
  `ArchitectureGroupChecks.cs`, `GroupInputChecks.cs` under `tools/examples`.
- Examples/docs: `room-areas.gh`, `massing.gh`, `README.md`, `NEXT.md`, `ux-notes.md`.

Try Bedroom in the room legend, then deselect one bedroom row: the legend should
show a partial dash. Click Bedroom to complete the group, then again to remove
it. Sort by Type to see all five bedroom rows together. Click Rooms below minimum,
change Plan scale to 2 (an inert zero-member group), then return to 1. In Massing,
click Residential in GFA by use or the legend, then Blocks above 30 m. Connect
the grouped cards to separate Canvas Cards to test the same local interactions;
selection stays private until the separate Scope work is integrated.

## Verification performed (earlier architectural UX work)

- `dotnet build ConduitPlus.sln -c Debug`: zero warnings/errors.
- `dotnet test ConduitPlus.sln -c Debug`: 171 passed, zero failed/skipped.
- Both new binary `.gh` files generated, reopened and solved with zero runtime
  warnings/errors in Rhino 8.36.26296.17001 / .NET 8.0.33 at 200% DPI.
- Room default: 17 planar faces, 320 m² schematic GIA, 280 m² net, 40 m²
  circulation, 87.5% net/gross, eight failing minima. Seven cards.
- Massing default: six blocks, 8000 m² site, 14,976 m² GFA vs 16,000 m² allowed,
  FAR 1.872, two height failures. Nine cards. Added floors cross the GFA allowance.
- Production face picking, sorted row reveal and table-to-model selection checked
  in the reopened definitions. The mesh/extrusion probes use the same production picker.
- Safe-mode 1× transparent PNG captures: 1874 × 1729 px, every card inside bounds,
  no card overlap, opaque interiors containing ink, and blue fill at the selected
  face centre. Logs/JSON/PNGs are under ignored `artifacts/examples-rooms-final`
  and `artifacts/examples-massing-final`.
- The final all-examples `-VerifyOnly -Safe -Exports` invocation passed for all
  three definitions using isolated hosts; combined logs/results are under
  `artifacts/examples-all-final`.
- Screenshot tidy verification: both architectural definitions regenerated, then
  all three definitions reopened/solved in safe-mode hosts with zero warnings/errors.
  The harness accepts `-Theme Paper`, `-Theme Smoke` or `-Theme Blueprint` without
  changing the saved definition's theme. Capture checks use the active theme's
  layout, surface alpha and contrasting ink rather than assuming opaque Paper.
  PNGs were visually inspected at 1× (1874 × 1729); logs, metric strings, card
  regions and face pixel samples are under ignored `artifacts/screenshots-tidy/`:
  - `paper/captures/raster-1x.png` (structural base wires).
  - `paper/room-areas/captures/raster-1x.png` and `paper/massing/captures/raster-1x.png`.
  - `smoke/room-areas/captures/raster-1x.png` and `smoke/massing/captures/raster-1x.png`.
  - `blueprint/room-areas/captures/raster-1x.png` and `blueprint/massing/captures/raster-1x.png`.
- RhinoCore's pre-existing shutdown stall occurred after successful workload and
  overlay/view cleanup; the wrapper stopped only its own child processes.
- The existing structural `.gh` also reopened/solved with zero warnings/errors
  after appending the new optional inputs; its binary was not regenerated.
- No installation, commits or pushes were performed.

## Try in Rhino after the reviewing agent installs this checkout

1. Use a metre-unit Rhino document. Open only `examples/room-areas.gh`, use a
   large Top viewport and Zoom Extents.
   Change Plan scale from 1 to 1.2; geometry/areas grow, requirements stay fixed,
   keys remain stable and failures fall. Return to 1 for the default results.
2. Sort the room table by delta or area. Wheel-scroll it away from Hall/R17, then
   click the hall interior or its bar: its row should become visible. Click its
   row: the whole hall face and boundary highlight; click again to deselect.
   Hover an interior, then a row/bar, and compare the shared highlight.
3. Keep the pointer still over a room table row and turn the wheel. Hover and the
   delayed tooltip should follow the row now under the pointer. Try scrollbar
   drag/track paging and Clear as well.
4. Open only `examples/massing.gh`, use Perspective and Zoom Extents. Click the
   middle of a roof/side, then its row/bar. Orbit: hover and selection should
   keep the translucent faces and wires visible even through other blocks.
   At zero extra floors, B02/B05 are above 30 m; add one floor to cross GFA 16,000.
5. Set a Metric's Decimals to 0, 1 and 2; unplug it to restore the incoming text.
   Check PNG capture at 1× and, in the GUI, 2×/4×. Try the experimental PDF if useful.

## Changed files

- Core: `DemoRooms.cs`, `DemoMassing.cs`, `MetricFormatting.cs`, `Cards.cs`,
  `CardPainter.cs` in `src/ConduitPlus.Core`.
- Grasshopper: `DemoArchitectureComponents.cs`, `Components.cs` in
  `src/ConduitPlus.Grasshopper`. New fixed component GUIDs end in `ae210` (Rooms)
  and `ae211` (Massing); existing GUIDs and input ordering are preserved.
- Rhino: `Link.cs`, `OverlaySession.cs`, `OverlayInput.cs` in `src/ConduitPlus.Display`.
  `ModelPicking.cs`/`LinkPicking.cs` needed no algorithm changes.
- Generator: `tools/examples/Build-Examples.ps1` and `Dashboard.cs`,
  `ArchitectureDashboard.cs`, `ExportChecks.cs` in `tools/examples/ConduitPlus.Examples`.
- Tests: `ArchitectureTests.cs`, `RevealAndFormatTests.cs`, `CoreTests.cs` in
  `tests/ConduitPlus.Core.Tests`.
- Artifacts/docs: `examples/room-areas.gh`, `examples/massing.gh`, `README.md`,
  `docs/NEXT.md`, `docs/ux-notes.md`.
