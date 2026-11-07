# Conduit+ next steps (2026-11-07)

The MVP roadmap is done and merged (PRs #5–#10), and the owner tested it in Rhino 8: it works well.
This file is the to-do list from here. `docs/PLAN.md` holds the original plan, `docs/IDEAS.md` the longer-term ideas.

## Canvas Card PR1 (implemented in this worktree; awaiting review/integration)
- One measured card per GH canvas component, six existing kinds, custom attributes,
  bounded raster cache, shared zoom-settle scheduler and synchronous GH image export.
- Local keyed selection/Clear, table sorting/scrollbar and GH hover tooltips; no
  interaction solves, wheel interception or implicit sharing with Overlay.
- Persisted per-component theme menu with undo, using the shared Core presets.
  Auto (new-component default) follows Rhino's light/dark UI as Paper/Night without
  a solve; explicit Paper/Smoke/Blueprint choices and legacy no-theme files are preserved.
- Build and Core/source-linked GDI tests cover transforms, actual allocation bounds,
  budget sharing/LRU, revision/theme invalidation and local state reconciliation.
- Before merge, reviewing agent installs from the integration checkout and runs the
  [live verification checklist](verification/canvas-cards.md): GH drag/select
  precedence, DPI/zoom, disabled/hidden states, undo/reload, export and cache/GDI
  lifetime. These GUI checks are still pending.
- Deferred by [approved design](design/canvas-cards.md): boards, resize, wheel
  interception, cross-host selection scopes and Selection/Events outputs.

## What's in main now
| Area | What works |
|---|---|
| Cards | Metric, Bar, Table (scroll, sortable columns), Line, Text, Legend (categorical or gradient) |
| Themes | Auto, Paper, Night, Smoke and Blueprint; new components default to Auto, missing legacy saved themes to Paper; optional Overlay Theme input or saved, undoable menu; capture/PDF follow the resolved theme |
| Linking | Link component; hover/click a card ↔ the member lights up in the model (both directions); selection shared across cards; Clear |
| Layout | Six slots; drag cards by their title; saved in the `.gh`; undo; Reset layout; Slot input locks a card |
| Feedback | Tooltips after 250 ms; Overlay outputs Selection and Events |
| Output | `ConduitPlusCapture` 1×/2×/4× PNG; experimental `ConduitPlusPdf` (vector cards) |
| Examples | Structural dashboard, room area check and massing/zoning with keyed demo geometry |

## Next up (recommended order)
1. **Use it on a real project.** Build a dashboard from real analysis output (e.g. Karamba) and note what's awkward. That feedback should drive the next items more than this list does.
2. **Small fixes the testing will probably ask for** (architectural findings: [ux-notes.md](ux-notes.md); Decimals, row reveal and wheel-hover refresh are implemented)
   - Bar: optional "worst first" sort and a "show top N" limit for long member lists.
3. **Group keys.** A Metric's "governing member" keys and legend swatches that select a whole group (e.g. "fails" selects every failing member). Needs group hover/select semantics in `BrushState`.
4. **Pins.** Labels anchored to 3D points (e.g. a utilisation tag at each member's midpoint), drawn by the same raster path and clickable through Link keys.
5. **Release 0.2.0.** Run `./tools/Pack-Yak.ps1 -Version 0.2.0` (bumps, commits, builds, tests, packages) and test the exact `.yak` locally. Follow [the release workflow](releasing.md), including its checksum/validation record. (Never run `tools/Publish-Yak.ps1 -Server Public` without the owner's go-ahead.)

## Later (v1 interactive, from PLAN.md)
- **Inputs.** Viewport controls bound to GH sliders/toggles (commit on release, one undo step).
- **Inspector.** A docked panel for card settings and accessible controls.
- **More cards.** Histogram, Scatter, Data quality.
- **Peek and Recipes.** One-click dashboards.
- **Multiple viewports.** Today an Overlay follows the active view only.
- **Gradient-aware Legend.** Reads a GH Gradient wire.
- **Card resize.**
- **Space between owners.** Two Overlay components currently overlap in the same slot.
- **Export.** PDF of the whole view (model + cards) and a multi-page PDF report (see IDEAS.md).

## Still unverified / known trade-offs
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
  3. adds the tooltip and drag ghost when present.
- **Capture and PDF.** Captures re-paint the cards at the capture scale inside the same callback. PDF export runs the same `CardPainter` through `PdfCanvas`, so the output is vectors.
- **Cost** (200% DPI, p95): card update ≈ 3–4 ms, hover ≈ 6–7 ms, model pick < 1 ms with 2,000 members.
