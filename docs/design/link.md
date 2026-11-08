# Link + highlight (design, 2026-11-06)

Reviewed by Astra (GPT-6 Astra). Split into two PRs.

## PR A — brushing + card → model highlight (this PR)
- **Link component** (Conduit+ > Display): `Geometry` (tree) + `Keys` (list, required). Mapping precedence:
  1. branch count == key count → branch *i* ↔ `Keys[i]` (all items in the branch share it; empty branches allowed);
  2. else flattened item count == key count → item *i* ↔ `Keys[i]`;
  3. else error. Null/invalid geometry and empty/whitespace keys are errors with path/index in the message.
  Keys are ordinal, case-sensitive. Repeated keys concatenate geometry (also across several Links).
  Bar keys default to labels and Table keys to branch paths, so they don't match automatically: wire explicit keys.
- Supported geometry: points, lines, polylines, curves, breps, meshes, extrusions (normalised explicitly; Rhino types stay out of Core).
  The Link output owns duplicated geometry; overlays never dispose a shared snapshot.
- **Overlay** gets an optional `Links` input.
- **One selection + hover state per (document, owner)**, surviving view rebinding. Projected onto every card that has the key,
  so a bar and a table row with the same key brush together. Pruned against the union of card and link keys on update.
  The GH Overlay mirrors the authoritative snapshot (no independent toggling).
- **Gestures:** plain click replaces selection; Shift adds, Ctrl removes, Ctrl+Shift toggles. A small "Clear" control on cards with a selection. Unlinked model clicks pass through (PR B adds linked model clicks).
- **Highlight:** drawn in the existing session conduit, in the same viewport as the HUD, x-ray (no depth test) before the card bitmaps:
  hover = thin accent wire, selected = thick accent wire + translucent faces (dots only for real points). Geometry bounds feed `CalculateBoundingBox`.
- **Solve lifecycle:** hover never triggers a GH solve. Selection/clear schedule one coalesced, non-recursive solve.
  Publish targets the bound doc/view, not whatever is active when the callback runs.
- Selection is transient (no undo, not saved in `.gh`).

## PR B — model → card picking (implemented)
- Hover/click linked geometry sets the same owner brush state and selection path as cards; model clicks use card id `model`
  and emit the existing `click` event. Clearing model hover only clears hover originating in the model.
- Link construction caches point/line segments, NURBS (one `ToNurbsCurve` per non-polyline wire), meshes and per-key world bbox corners.
  Breps, surfaces and extrusions use `Mesh.CreateFromBrep` with `MeshingParameters.FastRenderMesh`, the non-obsolete coarse preset.
  Existing highlight wires remain cached. No conversion or meshing occurs during picking.
- The manually built `PickContext` uses the view, `PointPick`, `GetFrustumLine`, `SetPickTransform` and `UpdateClippingPlanes`.
  `GetPickTransform(point)` uses Rhino's configured pick radius; clipping X/Y are scaled to six pixels (rectangle fallback for a zero radius).
  Projected key bboxes plus six pixels prefilter candidates; boxes crossing the camera plane conservatively pass the filter.
- Points/wires within six pixels beat mesh faces and sort by screen distance; mesh faces sort by depth (larger = nearer).
  Equal hits sort by owner Guid then ordinal key. Mesh edges and brep edges participate as wires.
- Visible cards in any session in the view take priority. Model picking requires a visible session in its bound document/view,
  `OverlayManager.Busy == false` and `Command.InCommand() == false`. Hover is capped near 30 Hz with a trailing cursor update;
  mouse down always re-picks. Only accepted model hits consume left down and its matching left up; other model clicks pass through.
- `ConduitPlusBench` measures the same picker for 2,000 lines + 200 brep boxes, 64 warmups and three 40×30 grid passes.
  `samples-pick.json` and `results-rhino.md` report nearest-rank p50/p95 and the ≤ 5 ms p95 target. Timing excludes Link construction,
  card raster updates and redraw. Pure segment-distance and ordering tests require no Rhino runtime.
- Headless Debug measurement on 2026-11-06 (Rhino 8.36.26296.17001, .NET 8.0.33, 1874×1729 viewport):
  p50 0.580 ms / p95 0.743 ms; 687 wire hits and 6 face hits across 3,600 samples. Target met in this scene;
  this does not establish interactive input latency. The existing wrapper stopped only its own process after successful cleanup
  because safe-mode RhinoCore shutdown stalled.
- API signatures are checked against the pinned RhinoCommon 8.20 NuGet XML (and parameter names in its reference metadata).
  Interactive callback cancellation, command/card precedence, clipping planes, multi-view/DPI behavior and Rhino 8.20 runtime
  behavior still need live validation; the headless benchmark can run against a newer installed Rhino.

## Later
Metric "governing member" keys (group hover), reveal selected row in a scrolled table, Rhino-object selection sync, persistence.
