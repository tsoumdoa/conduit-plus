# Architectural example UX notes (2026-11-07)

Scope: `room-areas.gh` and `massing.gh`, generated from this checkout without
installing it. Findings are ranked by impact on an architectural review.

## Fixed

| Rank | Finding | Change and evidence |
|---|---|---|
| P1 | Selecting a room from the model could leave its table row out of view. | Minimal reveal in the current sorted display order. Only a newly selected key triggers reveal, so manual scrolling, hover, deselection and subsequent solves do not snap back. Pure Core tests cover visibility boundaries, missing keys, short/empty tables, stale offsets and sort order; the reopened room definition exercises unsorted and sorted selection. |
| P1 | Edge-only highlighting made planar rooms and solid masses hard to identify. | Translucent accent faces under the existing wires/markers for hover and selection, with depth testing/writing disabled. Highlighting reuses the Link's owned pick meshes; meshing/material creation stays outside drawing. PNG checks sample the selected face centre, away from edges, to prove fill is present. |
| P1 | Face-interior picking needed checking before these examples were useful. | Existing production shaded-mesh picking already worked; no picker algorithm change was necessary. RhinoCore checks hit all 17 room interiors and all six block roofs, plus explicit extrusion and mesh face probes. Table-originated selection is also checked before capturing the highlighted model face. |
| P2 | Metric numbers showed distracting precision. | Appended optional Decimals input (0–12), with invariant numeric formatting; unwired values and nonnumeric text retain their original strings. Missing values still show the em dash. Existing input order/GUID is unchanged. |
| P2 | Signed area deficits need red despite being negative. | Appended optional per-bar Colors. Room delta bars use Theme.Bad for deficits and Theme.Good otherwise. Zero threshold is accepted for deltas; all-zero series have finite paint geometry. Existing default utilization coloring remains. |
| P2 | Six schedule columns were cramped at the table's fixed width. | Appended Width input (default remains 380 CSS px); examples use 600 px. Tables are manually placed bottom-right, leaving the centre of a large viewport available to the model. Layout bounds and card overlap are checked during capture. |
| P2 | Wheel scrolling left hover on the previous row until the pointer moved. | Re-hit the table at the wheel position after scrolling, updating brushing and tooltip state synchronously. Physical stationary-pointer behavior still needs GUI confirmation. |
| P3 | Longer architectural bar labels overlapped the bars. | Measure the longest label while rasterizing and reserve a capped label column, clipping extreme names. Residential/Office/Retail now fit without touching their bars. |
| P2 | A combined capture run could fail natively while changing GH definitions in one RhinoCore host. | The wrapper builds once, then runs each definition in a fresh host process. This retains the default all-examples command and isolates native host state. |
| P3 | A type/use legend should match the preview. | Demo components shade their default GH previews with the same palette as their legends. Room names are compact and keys remain independent/stable (R01–R17, B01–B06). |

## Still open

| Rank | Finding | Next action |
|---|---|---|
| P1 | Category legends and GFA-by-use bars cannot select all rooms/blocks in a category. | Implement group-key semantics in BrushState (already in NEXT.md). Examples deliberately leave category legends unkeyed; room/block table and individual bar keys link both ways. |
| P2 | Model framing and card layout compete on small or high-DPI viewports. | Current GH files save manual card slots, not the Rhino camera. Use Top + Zoom Extents for rooms and Perspective + Zoom Extents for massing, then drag cards if needed. Responsive layout/card resizing is larger work. |
| P2 | Tables have equal-width columns and can clip long names; tooltips contain full cells. | Consider per-column sizing in a later iteration. The new width input is a small interim improvement. |
| P2 | Full-scene 2×/4× capture, architectural PDF output and depth-disabled faces in PDF remain unverified here. | Validate in normal Rhino. The existing full-scene high-resolution RhinoCore native-failure limitation is unchanged. ExportChecks now accepts architectural document geometry for PDF rather than substituting the truss. |
| P2 | Physical mouse/card precedence, orbiting, selected preview colors and mixed-DPI behavior require normal GUI testing. | Try the steps below on Rhino 8.20+; the automated host here is 8.36. |
| P3 | Bars show height/limit ratios, not paired actual/limit bars; label space is capped. | Raw values are available in the table and demo output lists. Consider a comparison chart and resizable bars separately. |
| P3 | Room GIA has no wall thickness and massing GFA has no setbacks, voids or exclusions. | Keep the schematic assumptions visible. These fixtures are not area-measurement or planning-rule implementations. |

## Verification performed

- `dotnet build ConduitPlus.sln -c Debug`: zero warnings/errors.
- `dotnet test ConduitPlus.sln -c Debug`: 129 passed, zero failed/skipped.
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
- Rhino: `Link.cs`, `OverlaySession.cs`, `OverlayInput.cs` in `src/ConduitPlus.Rhino`.
  `ModelPicking.cs`/`LinkPicking.cs` needed no algorithm changes.
- Generator: `tools/examples/Build-Examples.ps1` and `Dashboard.cs`,
  `ArchitectureDashboard.cs`, `ExportChecks.cs` in `tools/examples/ConduitPlus.Examples`.
- Tests: `ArchitectureTests.cs`, `RevealAndFormatTests.cs`, `CoreTests.cs` in
  `tests/ConduitPlus.Core.Tests`.
- Artifacts/docs: `examples/room-areas.gh`, `examples/massing.gh`, `README.md`,
  `docs/NEXT.md`, `docs/ux-notes.md`.
