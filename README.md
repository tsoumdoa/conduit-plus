# Conduit+

Conduit+ displays result cards over Rhino viewports from Grasshopper: metrics,
signed utilisation bars, scrollable tables, line charts, notes and legends. A shared C# painter
draws each dirty card into a GDI+ bitmap; a DisplayConduit composites the cached
cards. Clean cards reuse their textures. Updates, hover and scrolling run
synchronously on Rhino's UI thread.

Licensed under MIT. Features include Paper, Smoke and Blueprint overlay themes,
deterministic viewport slots, keyed geometry highlighting, saved layout editing
and undo. A docked Inspector is future work.

## Requirements

- Windows and Rhino **8.20+**, running **.NET 8** (use Rhino's `SetDotNetRuntime`
  command if necessary). Grasshopper ships with Rhino.
- Development: **.NET SDK 9 or 10** and the .NET 8 Windows Desktop runtime.
  No JavaScript toolchain is needed.
- Rhino is needed for interactive use, the RhinoCore benchmark and Yak packaging.
  Building and unit testing use public RhinoCommon/Grasshopper NuGet packages
  and do **not** require Rhino to be installed.

## Build and test

From the repository root in PowerShell:

```powershell
dotnet restore ConduitPlus.sln
dotnet build ConduitPlus.sln -c Release --no-restore
dotnet test ConduitPlus.sln -c Release --no-build
```

Warnings are errors through `Directory.Build.props`. There is no cloud CI. A
pre-commit hook runs the build and tests locally whenever a commit touches code
(`.cs`, `.csproj`, `.props`, `.targets`, `.sln`); docs-only commits skip it.
Enable it once per clone:

```powershell
git config core.hooksPath .githooks
```

To force a run on a commit with no code changes, set `CONDUITPLUS_HOOK_FORCE=1`.
The host API version is pinned centrally to Rhino 8.20; NuGet labels that API
build `8.20.25147.11001-rc`.

## Install

Build a local Yak package with an installed Rhino 8:

```powershell
./tools/Pack-Yak.ps1
```

This stages the three plugin assemblies, manifest, licence and available PNG
icon in `artifacts/yak/`, then runs Yak's Windows package build. It never
publishes a package. Install the resulting `.yak` by dropping it onto Rhino,
then restart Rhino and open Grasshopper. When a release is published, it can
also be installed through Rhino's `PackageManager` using `conduit-plus`.

For a developer installation, **close Rhino**, build, then run:

```powershell
./tools/Install-Dev.ps1 -Configuration Release
```

The script copies `ConduitPlus.Rhino.rhp`, `ConduitPlus.Core.dll` and
`ConduitPlus.Grasshopper.gha` into
`%APPDATA%\Grasshopper\Libraries\ConduitPlus`, removes the previous developer
installation folder if present and reports its removal. It skips installation
while Rhino is running. Load the `.rhp` through `PlugInManager` → Install once,
then start Grasshopper. Close Rhino before rebuilding or reinstalling.

## Example

Open [examples/structural-dashboard.gh](examples/structural-dashboard.gh) in
Grasshopper with Conduit+ loaded. Change the **Span** and **Load** sliders, click
a utilisation bar or a truss member, sort/scroll the member table, and drag cards
by their titles. Use a large perspective viewport; the saved manual layout keeps
all cards draggable. The example uses four bays to fit its nine cards at 200% DPI.

**Demo Truss** (Examples) defaults to eight bays and generates deterministic,
illustrative results with fictitious capacities, stiffness and member areas.
These numbers are **not structural analysis or design verification**.

Regenerate and reopen/solve the definition without installing the plugin:

```powershell
./tools/examples/Build-Examples.ps1
```

Rhino 8 is required. The tool loads this checkout's assemblies directly and does
not install or update any Grasshopper libraries. Export checks, commands and
recorded limitations: [capture/PDF verification](docs/verification/capture-pdf.md).

## Edit layout

Drag a card title (or the small edge grip shown when hovering an untitled card)
to one of the six viewport slots. The highlighted zone and insertion line show
where it will land. Wired slots show a lock; change those in Grasshopper.
Layout is saved with the Overlay component in the `.gh` file and supports Undo/Redo
without a solve. Right-click Overlay → **Reset layout** to clear all saved positions
and ordering. Escape on mouse move or leaving the initiating viewport cancels a drag.

## Grasshopper components

All components are in the **Conduit+** category. Connect one or more card outputs
to **Overlay**.

| Component | Inputs and behavior |
|---|---|
| Metric | Formatted value, title, unit and optional slot; missing values display an em dash. |
| Bar | Values, optional labels and stable keys, title, threshold and slot; signed values and missing values are supported. |
| Line | Ordered values (NaN leaves a gap), optional labels (1…n) and keys (default labels), title, optional threshold and slot. A positive wired threshold shows a band. |
| Text | Optional title, text with explicit newlines and word wrapping, width (default 280) and slot; capped at 30 lines with an ellipsis. |
| Legend | Colors and labels for categorical swatches; wire both Min and Max for a gradient with nice ticks and optional unit. Optional categorical keys enable brushing and selection; title and slot are supported. |
| Table | Headers, one data-tree branch per row, optional row keys, title, slot and visible-row count (default 8). |
| Link | Geometry tree and required Keys list: one key per branch (preferred), otherwise per flattened item. Repeated keys combine geometry. |
| Overlay | Cards, optional Links and Theme inputs; Selection and Events outputs. Themes: Paper (default), Smoke and Blueprint. Follows the bound document's active view on data solves; interaction solves stay in the bound view. |
| Demo Truss | Examples: Span, Bays, Depth and Load → keyed members, illustrative utilisation/deflection/mass, table rows and summaries. |

Card components are under **Cards**; Link and Overlay are under **Display**.
Choose **Overlay → Theme → Paper / Smoke / Blueprint** in the right-click menu.
The choice is saved in the `.gh` file and supports Undo/Redo. A wired **Theme**
text input overrides the menu (case-insensitive); unknown names warn and use Paper.
PNG captures and vector PDF exports use each Overlay's active theme. Smoke has a
translucent dark surface and soft shadow; Blueprint has a translucent navy surface,
square corners and a thin blue border. Paper keeps its existing opaque appearance.

Component and plugin GUIDs are stable. Legend colors are stored as `#RRGGBB`
strings; Grasshopper color inputs are converted automatically (alpha is ignored).

Connect **Link** to **Overlay → Links**, and give the cards and linked geometry
the same explicit keys. Bar keys default to labels; Table keys default to branch
paths, so they do **not** auto-match. Keys are case-sensitive. Link supports points,
lines, polylines, curves, breps, surfaces, meshes and extrusions; it duplicates
geometry and caches its wires and pick proxies when solved.

Slots are `tl`, `tr`, `bl`, `br`, `lc`, `rc`. A wired slot takes precedence over
a stored manual slot, followed by automatic placement. Cards stack within slots;
small viewports can overflow. Different Overlay components have separate owner
sessions and do not currently reserve space around one another.

## Commands

| Command | Behavior |
|---|---|
| `ConduitPlusDemo` | Shows metrics, bars, a table, a line chart, legends and a design note, with linked frame members M01–M20 visible on hover/selection. |
| `ConduitPlusCapture` | Saves a transparent PNG at 1×, 2× or 4×, re-rendering all visible sessions at the target scale inside the drawing callback. |
| `ConduitPlusPdf` | **Experimental** vector PDF export; searchable card text, page size and coordinate alignment checked in RhinoCore 8.36. See [verification and limitations](docs/verification/capture-pdf.md). |
| `ConduitPlusBench` | Measures Raster metric-update, compact-bar hover and model-pick latency, checks cache lifecycle and runs per-card raster benchmarks. |

Command output defaults to `Documents\ConduitPlus` (captures in `captures/`).
Set `CONDUITPLUS_RESULTS` to override it. Benchmark results are not written to
the repository by default.

## Scrolling and interaction

- Hover a bar/table row or linked viewport geometry to brush every card with the
  same key and draw linked geometry in a thin
  accent wire. Click toggles selection, drawing thicker x-ray wires and point
  markers, also in captures. **Clear** in a keyed card's header clears that
  owner's selection. Linked model clicks emit a `click` event with card id `model`.
  Cards take priority; other model clicks pass through to Rhino. Model picking
  pauses during Rhino commands and uses a six-pixel tolerance with hover near 30 Hz.
- Overlay outputs selected keys and the last 32 click/clear events as JSON.
  Selection and hover belong to the document/owner and survive following another
  view; removed keys are pruned against all card and Link keys. Selection is
  transient (no undo or `.gh` persistence). Hover never triggers a GH solve.
- Over a scrollable table, the mouse wheel moves three rows per notch and is
  consumed before Rhino can zoom. Elsewhere, Rhino receives the wheel normally.
- Drag the scrollbar thumb or click its track to page. Scroll and hover state
  survive incoming Grasshopper solves; table offsets clamp as data changes.
- Click a table column header to cycle ascending, descending and original order.
  Sorting is stable and numeric-aware; empty cells stay last. The triangle marks
  the sorted column. Sort and scroll survive solves; selection follows row keys.
- Hold hover over a keyed bar, line sample, table row, legend swatch or linked
  geometry for 250 ms to see its details near the cursor. Tooltips follow DPI,
  stay inside the viewport and reuse one texture while moving. They hide during
  drags and Rhino commands and are omitted from captures.
- Each document/view/owner session owns its cache. Following another view,
  closing a view/document, removing an Overlay or shutting down the plugin
  releases its conduit and textures.
- DPI changes and capture scale changes regenerate textures synchronously. A
  failed capture-scale render retains existing textures; the next screen draw
  restores screen resolution. Full-scene 2×/4× captures and mixed-monitor
  behavior still need normal Rhino validation.

## RhinoCore benchmark

```powershell
./tools/bench/Run-Bench.ps1
./tools/bench/Run-Bench.ps1 -Mode Capture -Scale 2
./tools/bench/Run-Bench.ps1 -Mode Capture -Scale 4 -Compact
```

The wrapper builds Release, launches a hidden RhinoCore safe-mode host and writes
JSON samples, a text report and logs under `%TEMP%\ConduitPlus\bench-<timestamp>`.
Use `-Output` to change the destination. Update and hover latency include revision
work, GDI+ paint and display-texture creation; they exclude redraw, GPU completion
and physical mouse latency. The per-card benchmark measures allocation and paint
at 2×. These measurements do not establish interactive orbit performance.

Model picking uses cached proxies for 2,000 line members and 200 brep boxes over
a 40×30 cursor grid (three passes); `samples-pick.json` records p50/p95, hit counts
and whether the ≤ 5 ms p95 target was met. Its timing includes pick-context setup,
clipping and bbox filtering, and excludes Link construction and card rendering.

The wrapper bounds workload duration. If safe-mode RhinoCore shutdown stalls
after successful workload and overlay/view cleanup, it stops only its own child
process and reports that separately. `-Mode Pdf` is available for experimental
export checks and may time out in this host.

## Layout

- `src/ConduitPlus.Core`: card specs, store/revisions, layout, scrolling, ticks,
  `ICanvas2D` and `CardPainter`.
- `src/ConduitPlus.Rhino`: plugin, owner sessions, input/wheel hook, GDI canvas,
  Raster cache, capture/PDF adapters and commands.
- `src/ConduitPlus.Grasshopper`: card and Overlay components.
- `tests/ConduitPlus.Core.Tests`: pure Core and Windows GDI alpha/clip/painter tests.
- `tools/bench`: RhinoCore host and bounded benchmark entry point.
- `packaging`: Yak manifest; `tools/Pack-Yak.ps1` builds the local package.

The renderer feasibility history remains in `docs/spike/`; removed renderer code
is recoverable at tag `spike-renderer-feasibility`. `docs/PLAN.md` and
`docs/IDEAS.md` retain the original planning context.
