# Conduit+ next steps (2026-11-08)

The MVP roadmap (PRs #5–#10) and the post-MVP work (PRs #13–#23) are merged. The owner tested the MVP in Rhino 8: it works well. The post-MVP work has automated and headless RhinoCore checks only; there is no recorded test in normal Rhino yet.
This file is the to-do list from here. `docs/PLAN.md` holds the original plan, `docs/IDEAS.md` the longer-term ideas.

## What's in main now
Version 0.2.0-beta.2, published on public Yak as a prerelease (`conduit-plus`; tick **Include pre-releases** in Package Manager). Yak is the only install path, including during development. Last recorded result: 0 warnings, 328 tests passing.

| Area | What works |
|---|---|
| Cards | Metric (Decimals), Bar (per-bar Colors), Table (scroll, sortable columns, Width, reveals a newly selected row), Line, Text, Legend (categorical or gradient) |
| Canvas Card | Shows one card live on the GH canvas, like a Panel; local sort, scrollbar, selection/Clear and tooltips; GH image export. Deferred: boards, resize, wheel scrolling, Canvas Events |
| Themes | Auto, Paper, Night, Smoke and Blueprint; new components default to Auto, missing legacy saved themes to Paper; optional Overlay Theme input or saved, undoable menu; capture/PDF follow the resolved theme |
| Linking | Link component; hover/click a card ↔ the member lights up in the model (both directions), with shaded faces; selection shared across cards; Clear |
| Selection | Click replaces, Shift adds, Ctrl removes, Ctrl+Shift toggles; clicking empty viewport space clears; every selected element looks the same |
| Group keys | Bar, categorical Legend and Metric take Members, so one click selects a whole group; partial selection shows a dash |
| Scope | Wire one Scope into Canvas Cards and Overlays to share selection and hover between them; unwired displays stay private |
| Layout | Cards input order; Slot input picks the side |
| Inputs | Right-click Slot, Overlay Theme or Metric Decimals to pick a value from a list |
| Feedback | Tooltips after 250 ms; Overlay outputs Selection and Events; Canvas Card outputs Selection |
| Output | `ConduitPlusCapture` 1×/2×/4× PNG; experimental `ConduitPlusPdf` (vector cards) |
| Examples | Structural dashboard, room area check and massing/zoning with keyed demo geometry |

## Sample files for checking
All three are in `examples/`. Demo numbers and limits are fictitious. The files save card positions but not the Rhino camera, so set the view yourself and use a large viewport.

| File | Best for | Setup and what to try |
|---|---|---|
| `room-areas.gh` | Canvas Card, Scope, group keys | Metre-unit model, Top view, Zoom Extents. Click Bed 01 in the Canvas table, then its face in Rhino. Shift+click a second room, Ctrl+click to remove it, then click empty space to clear. Click the Bedroom swatch, Ctrl+click one room (partial dash), then Clear. Click "Rooms below minimum". Change Plan scale. Sort and scroll each table separately. Switch GH tabs, lock/unlock a display, disconnect Scope. |
| `massing.gh` | Group keys on 3D blocks, themes | Perspective view, Zoom Extents. Click Residential in the legend or "GFA by use". Click "Blocks above 30 m". Change Extra floors. Click block roofs. Try each theme from Overlay's right-click menu. |
| `structural-dashboard.gh` | Overlay basics, capture/PDF | Change Span and Load. Click a bar or truss member. Sort/scroll the member table. Run `ConduitPlusCapture` at 2× and 4×, and `ConduitPlusPdf` with a member selected. To check a file from before themes existed, use this file from commit `46de969`: it should open in Paper, with no missing-input archive errors. |

For the full Canvas Card checks, wire each card kind from these files into its own new Canvas Card; the samples only include a Canvas table.

## Next up (recommended order)
1. **Run the live checks in normal Rhino**, using the samples above and the checklists: [Canvas Card](verification/canvas-cards.md#pending-live-rhino-820-checks), [Scope](verification/selection-scope.md), [UX notes](ux-notes.md). Automated hosts ran RhinoCore 8.36, so Rhino 8.20 itself also needs a check.
2. **Use it on a real project.** Build a dashboard from real analysis output (e.g. Karamba) and note what's awkward. That feedback should drive the next items more than this list does.
3. **Small fixes the testing will probably ask for** (open findings: [ux-notes.md](ux-notes.md))
   - Bar: optional "worst first" sort and a "show top N" limit for long member lists.
4. **Pins.** Labels anchored to 3D points (e.g. a utilisation tag at each member's midpoint), drawn by the same raster path and clickable through Link keys.
5. **Release 0.2.0 (stable), after the live checks.** Run `./tools/Pack-Yak.ps1 -Version 0.2.0` (bumps, commits, builds, tests, packages) and test the exact `.yak` locally. Follow [the release workflow](releasing.md), including its checksum/validation record. (Never run `tools/Publish-Yak.ps1 -Server Public` without the owner's go-ahead.)

## Later (v1 interactive, from PLAN.md)
- **Inputs.** Viewport controls bound to GH sliders/toggles (commit on release, one undo step).
- **Inspector.** A docked panel for card settings and accessible controls.
- **More cards.** Histogram, Scatter, Data quality.
- **Peek and Recipes.** One-click dashboards.
- **Several viewports per Overlay.** One Overlay drawing to more than one viewport at once; View picks one today.
- **Gradient-aware Legend.** Reads a GH Gradient wire.
- **Card resize.**
- **Export.** PDF of the whole view (model + cards) and a multi-page PDF report (see IDEAS.md).

## Still unverified / known trade-offs
- **Mouse gestures on Canvas Cards and Scope.** Drag/select precedence, mouse capture and real GH tab switching were only exercised through API calls in RhinoCore.
- **Long-session resources.** Whether GDI handles and bitmap memory level off during repeated zooming, replacing inputs and closing documents.
- **Auto light/dark switching.** Uses RhinoCommon 8.20 `HostUtils.RunningInDarkMode`
  and `RhinoApp.AppSettingsChanged` (verified in the pinned NuGet DLL). Re-resolves
  on Overlay updates and Canvas Card paints too. Live mode changes, undo/reload
  and legacy no-theme files still need GUI verification; see [API notes](verification/canvas-cards.md#theme-api-and-auto-mode).
- **Capture at 2×/4×.** Full-dashboard capture at those scales was only tried in headless RhinoCore, where it failed natively. Check in the GUI.
- **PDF.** Whether linked selection wires appear in the PDF.
- **Mixed-DPI monitors.** Dragging Rhino between monitors with different scaling.
- **Referenced Rhino objects.** Clicking linked geometry that comes from Rhino objects selects the key, not the Rhino object.

## How rendering works (for whoever picks this up)
- **Core.** `ConduitPlus.Core` describes each card as a `CardSpec` record and paints it through `ICanvas2D` (`CardPainter`). Core has no Rhino dependency.
- **Diagrams and history.** [How charts are drawn](design/rendering.md) explains the current raster path, the former HTML/JS path and maintainability options.
- **Raster.** On Rhino, `GdiCanvas` paints each card into its own transparent, premultiplied-alpha GDI+ bitmap at the screen's DPI scale. The bitmap becomes a `DisplayBitmap`.
- **Caching.** `CardStore` gives a card a new revision when its spec or active theme changes (data, hover, selection, scroll or sort). The raster cache also compares the complete theme record and scale. Only dirty cards are re-painted.
- **Theme typography.** The concept uses IBM Plex Sans (Segoe UI fallback), 12px body / 11px headings, Archivo 20px metric values and IBM Plex Mono 10–11px annotations. These do not vary by overlay theme. Keep the installed Segoe UI and existing sizes (12px body / 28px metric) for stable measurements and Paper compatibility; no web fonts are bundled. Paper/Night have no border or shadow; Smoke has no shadow, Blueprint a 1px border.
- **Drawing.** Each Overlay has a `DisplayConduit`. In `DrawForeground` it:
  1. draws highlighted linked geometry as 3D wires, with depth testing off;
  2. places card bitmaps by `SlotLayout` and draws them as 2D screen bitmaps;
  3. adds the tooltip when present.
- **Capture and PDF.** Captures re-paint the cards at the capture scale inside the same callback. PDF export runs the same `CardPainter` through `PdfCanvas`, so the output is vectors.
- **Cost** (200% DPI, p95): card update ≈ 3–4 ms, hover ≈ 6–7 ms, model pick < 1 ms with 2,000 members.
