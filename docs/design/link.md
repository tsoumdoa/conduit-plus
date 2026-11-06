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
- **Gestures:** plain click toggles a key (unchanged). A small "Clear" control on cards with a selection. Model clicks pass through.
- **Highlight:** drawn in the existing session conduit, in the same viewport as the HUD, x-ray (no depth test) before the card bitmaps:
  hover = thin accent wire, selected = thick accent wire + point markers. Geometry bounds feed `CalculateBoundingBox`.
- **Solve lifecycle:** hover never triggers a GH solve. Selection/clear schedule one coalesced, non-recursive solve.
  Publish targets the bound doc/view, not whatever is active when the callback runs.
- Selection is transient (no undo, not saved in `.gh`).

## PR B — model → card picking (next)
- Hover/click linked geometry in the viewport. Cached pick proxies (NURBS/lines/meshes; no meshing on mouse move),
  bbox prefilter, `PickContext` with pick line + clipping. Target ≤ 5 ms p95 pick, 30 Hz hover at 2,000 members.
- Cancel the mouse-down/up only for an accepted hit. Deterministic owner/key tie-break. No hit while a Rhino command runs.

## Later
Metric "governing member" keys (group hover), reveal selected row in a scrolled table, Rhino-object selection sync, persistence.
