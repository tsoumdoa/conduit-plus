# Conduit+

Conduit+ displays result cards over Rhino viewports and on the Grasshopper canvas: metrics,
signed utilisation bars, scrollable tables, line charts, notes and legends. A shared C# painter
draws each dirty card into a GDI+ bitmap; a DisplayConduit composites the cached
cards. Clean cards reuse their textures. Updates, hover and scrolling run
synchronously on Rhino's UI thread.

Licensed under MIT. Features include Auto, Paper, Night, Smoke and Blueprint themes,
deterministic viewport slots in input order and keyed geometry highlighting.
A docked Inspector is future work.

## Requirements

- Windows and Rhino **8.20+**, running **.NET 8** (use Rhino's `SetDotNetRuntime`
  command if necessary). Grasshopper ships with Rhino.
- Development: **.NET SDK 9 or 10**, the .NET 8 Windows Desktop runtime and
  **PowerShell 7** for release packaging.
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
./tools/Pack-Yak.ps1                  # package the current version
./tools/Pack-Yak.ps1 -Version 0.2.0   # or bump, commit "Release 0.2.0", then package
```

This runs the Release build and tests, stages the three plugin assemblies,
manifest, licence, install notes and icon, then verifies Yak's Windows archive.
`Directory.Build.props` is the single version source. The candidate, SHA-256 and
validation record are written to `artifacts/yak/`. Use `-AllowDirty` for local
uncommitted trials or `-StageOnly` to stage without Rhino/Yak. The default
requires a clean working tree. See [the release workflow](docs/releasing.md).
It never publishes a package; `tools/Publish-Yak.ps1` does that after testing. Install the resulting `.yak` by dropping it onto Rhino,
then restart Rhino and open Grasshopper. When a release is published, it can
also be installed through Rhino's `PackageManager` using `conduit-plus`; tick
**Include pre-releases** to see beta versions. Yak is the only supported install
path, including during development.

## Examples

Open [examples/structural-dashboard.gh](examples/structural-dashboard.gh) in
Grasshopper with Conduit+ loaded. Change the **Span** and **Load** sliders, click
a utilisation bar or truss member, and sort or scroll the member table. Use a large
perspective viewport. The example uses four bays to fit nine cards at 200% DPI.

**Demo Truss** (Examples) defaults to eight bays and generates deterministic,
illustrative results with fictitious capacities, stiffness and member areas.
These numbers are **not structural analysis or design verification**.

Two architectural definitions use metre-based schematic geometry and deliberately
include failures. Their limits are fictitious, not regulatory checks:

- [Room area check](examples/room-areas.gh): **Demo Rooms** generates 16 rooms and
  a circulation hall, colored by type. At scale 1: schematic GIA **320 m²**,
  net/gross **87.5%**, circulation **12.5%**, **8 failures**. GIA includes the hall
  and omits wall thickness; net excludes the hall. Change **Plan scale**: geometry
  scales in XY while type minima remain fixed. The signed bar shows area minus
  minimum in m², with deficits in Bad red. The table includes actual and required
  areas, delta and status; the legend identifies types.
- [Massing / zoning](examples/massing.gh): **Demo Massing** generates six blocks
  colored by Residential, Office and Retail use, plus a 100 × 80 m site boundary.
  Default whole-floorplate GFA is **14,976 m²** against **16,000 m²** allowed,
  FAR **1.872** against **2.0**, and two blocks exceed the **30 m** height limit.
  Change **Extra floors**; the height bar shows height/limit ratios (1.0 is the
  limit), with raw heights, limits, floor counts and GFA in the sortable table.
  A second bar totals GFA by use.

Use a metre-unit Rhino document. Open one definition at a time in a large
viewport (Top for rooms, Perspective for massing), then Zoom Extents. Click inside a room face or a block roof/side:
its keyed row/bar selects, and the table reveals an off-screen selected row even
when sorted. Click a row/bar to highlight the matching geometry with translucent
faces and x-ray wires; click again or **Clear** to deselect. Wheel-scroll the room
table and select **Hall / R17** from the model or bar to test reveal. Select **Bedroom** in the room-type legend
or **Residential** in the GFA-by-use
bar/legend to select every member of that category. The **Rooms below minimum**
and **Blocks above 30 m** Metric bodies select their current failures. Membership
updates with the geometry; the displayed Metric value remains supplied data.

Regenerate and reopen/solve all definitions without installing the plugin:

```powershell
./tools/examples/Build-Examples.ps1
```

Use `-Example Rooms`, `-Example Massing` or `-Example Structural` to regenerate
just one definition; `-VerifyOnly` reopens the existing file. Add `-Exports -Safe`
for 1× PNG region, layout and architectural selected-face checks (Safe skips
PDF); `-Exports` also attempts the experimental PDF. For group-state PNGs, use
`-Example Rooms -Exports -Safe -State GroupSelected -Scale 1`; substitute
`Massing`, `GroupPartial` or `GroupHover` as needed.
Run each state separately: this RhinoCore host can fail natively on a second
full-scene capture. The checks also exercise real GH empty-branch validation,
private Canvas group selection, one event per action and dynamic membership.
Compact fixtures remain structural-only. See [UX findings](docs/ux-notes.md)
for fixed and open issues.

Rhino 8 is required. Each definition runs in a fresh RhinoCore host. The tool
loads this checkout's assemblies directly and does
not install or update any Grasshopper libraries. Export checks, commands and
recorded limitations: [capture/PDF verification](docs/verification/capture-pdf.md).

## Card order and position

Cards appear in the order they arrive in Overlay's Cards input, and each card's Slot input picks its side.
When several wires feed Cards, Grasshopper orders them by when they were connected, so a Merge component in
front of Overlay makes the order explicit. Cards can't be dragged in the viewport.

## Grasshopper components

All components are in the **Conduit+** category. Connect one or more card outputs
to **Overlay**, or one card output to **Canvas Card**.

| Component | Inputs and behavior |
|---|---|
| Metric | Value, title, unit, optional slot and appended Decimals (0–12); unwired precision preserves text, nonnumeric text is unchanged, missing values display an em dash. Appended optional Key targets a member; Members (exactly one text-tree branch) turns it into a group. |
| Bar | Values, optional labels and stable keys, title, threshold and slot; signed values and missing values are supported. Appended optional Colors override per-bar status; zero threshold supports deltas. Appended Members supplies one branch per entry and requires explicit Keys. |
| Line | Ordered values (NaN leaves a gap), optional labels (1…n) and keys (default labels), title, optional threshold and slot. A positive wired threshold shows a band. |
| Text | Optional title, text with explicit newlines and word wrapping, width (default 280) and slot; capped at 30 lines with an ellipsis. |
| Legend | Colors and labels for categorical swatches; wire both Min and Max for a gradient with nice ticks and optional unit. Optional categorical keys enable brushing and selection; appended Members defines one group per entry (requires explicit Keys). Gradient mode rejects Members. |
| Table | Headers, one data-tree branch per row, optional row keys, title, slot and visible-row count (default 8) and appended Width (default 380 CSS px). |
| Link | Geometry tree and required Keys list: one key per branch (preferred), otherwise per flattened item. Repeated keys combine geometry. |
| Scope | Optional Name is a display label only; wire its typed token to Canvas Card and Overlay to share selection and hover within this GH document. |
| Overlay | Cards, optional Links, Theme, Scope and View inputs; Selection and Events outputs. Auto theme by default for new components. **View** defaults to Active viewport, which follows the bound document's active view on data solves while interaction solves stay in the bound view. Right-click View to pick an open standard viewport, or wire its name (trimmed, case-insensitive). The Overlay stays on that viewport if Rhino renames it, for example after `_SetView`; the name is looked up again when the file is reopened. A name that isn't open draws nothing and warns, then reconnects when that viewport appears. |
| Canvas Card | Exactly one Card across the whole input tree; draws the measured card inside its canvas body, with local sort, scrollbar, selection/Clear and GH hover tooltips. Appended optional Scope input and Selection output. Right-click → Theme. |
| Demo Rooms | Examples: Plan scale → 17 keyed planar room faces, names/types, areas/minima/deltas, schedule, GIA/net/circulation metrics and status/type colors. |
| Demo Massing | Examples: Extra floors → six keyed breps, site boundary, floors/heights/limits/GFA/uses, schedule, zoning summaries and use colors. |
| Demo Truss | Examples: Span, Bays, Depth and Load → keyed members, illustrative utilisation/deflection/mass, table rows and summaries. |

Card components are under **Cards**; Link, Scope, Overlay and Canvas Card are under **Display**.
Choose **Overlay → Theme → Auto / Paper / Night / Smoke / Blueprint** in the right-click menu.
New Overlay and Canvas Card components default to **Auto**: Paper for Rhino's light
UI, Night for its dark UI. Auto follows Rhino settings changes without a solve.
Existing saved choices are retained; older files with no saved theme stay Paper.
The selection (including Auto) is saved in the `.gh` file and supports Undo/Redo. A **Theme** input
value, wired or picked from its right-click list, overrides the menu (case-insensitive); unknown names
warn and use Paper. Pick **Use component menu** on the input to hand control back to the menu.
PNG captures and vector PDF exports use each Overlay's active theme. Paper and Night
are opaque light/dark counterparts with the same geometry and no border or shadow.
Smoke has a translucent dark surface and rounded corners; Blueprint has a translucent navy surface,
square corners and a thin blue border.

Component and plugin GUIDs are stable. Legend colors are stored as `#RRGGBB`
strings; Grasshopper color inputs are converted automatically (alpha is ignored).

Connect **Link** to **Overlay → Links**, and give the cards and linked geometry
the same explicit keys. Bar keys default to labels; Table keys default to branch
paths, so they do **not** auto-match. Keys are case-sensitive. Link supports points,
lines, polylines, curves, breps, surfaces, meshes and extrusions; it duplicates
geometry and caches its wires and shared pick/highlight meshes when solved.

Right-click a card's **Slot** input to pick Automatic or a viewport position,
Overlay's **Theme** input to pick a theme, or Metric's **Decimals** input to pick
0–6 (clear its value for automatic formatting; 7–12 can be typed or wired). A
connected wire always takes priority.

Slot codes are `tl`, `tr`, `bl`, `br`, `lc`, `rc`. Set Slot by wire or right-click; otherwise cards use input index % 6. Cards stack in input order within each slot.

## Cards on the Grasshopper canvas

Place **Conduit+ → Display → Canvas Card** and connect the Card output from any
Metric, Bar, Table, Line, Text or Legend. Each display accepts exactly one valid
card across all branches; empty input shows a placeholder and invalid or multiple
cards show an error and clear the previous image. Use separate Canvas Cards to
arrange a dashboard with Grasshopper's normal component layout and groups.

Drag/select the component by its **Canvas Card** title strip, margins or inactive
body areas. Plain left-click on a keyed Metric body, row/bar/sample/swatch replaces
member/group selection; Shift adds, Ctrl removes, and Ctrl+Shift toggles; **Clear** removes it. Table column headers cycle ascending, descending
and original order. Drag the scrollbar thumb or click the track to page; the
**mouse wheel always zooms Grasshopper**. Clicks commit on release within four
screen pixels. Escape or lost capture cancels a scrollbar drag. GH tooltips show
the hovered item's details. Alt clicks, modified clicks outside actionable keyed regions,
and middle-button navigation retain their normal GH behavior.

Unwired Canvas Cards have private, transient selection, sort and scroll state.
State survives data updates (with keys pruned and offsets clamped), resets when
the source card ID changes, and is not saved in the `.gh`. Committed selection
updates the appended Selection output through one coalesced solution; hover,
sorting and scrolling only repaint. Card width/visible rows come from the source;
Slot has no canvas meaning. There is no canvas resize control in this version.

Right-click **Canvas Card → Theme: Auto/Paper/Night/Smoke/Blueprint** to save a theme
choice with Undo/Redo. It uses the same presets and Auto policy as Overlay.
Invalid saved names fall back to Paper with a warning.

Preview off/Hidden do not hide the canvas card. A locked component or disabled
document keeps its last image with a Disabled title and no card interaction.
Zoom initially scales cached pixels, then regenerates visible cards after about
120 ms at the settled target density. The shared UI scheduler paints one card per
tick. Screen caches are bounded to 64 MiB per document, with at most 16 MiB and
4096 pixels per dimension per bitmap; dense/large dashboards reduce raster density
uniformly and evict least-recently-visible frames, so extreme cards can look soft.
Panning normally reuses pixels.

GH image export synchronously renders disposable bitmaps at the export target's
scale, preserving committed selection/sort/scroll and omitting hover. It does not
replace the screen cache or provide vector text. `ConduitPlusCapture` and
`ConduitPlusPdf` remain viewport commands. Pinned API checks and the pending live
Rhino checklist are in [Canvas Card verification](docs/verification/canvas-cards.md).

## Shared selection Scope

Wire the same **Scope** output into **Canvas Card → Scope** and **Overlay → Scope**.
The room-area example includes a canvas room table sharing selection with its
viewport dashboard. Click a canvas row, a viewport row/bar/group or a model face:
all participants immediately project the same member selection. Each display keeps
its own theme, layout, sorting, scrolling and geometry Links. Tooltips stay with
the pointer's host. Unwired displays remain independent, even with identical cards,
keys or Scope names.

Each Scope input requires exactly one live token across its whole tree, from the
same GH document. Invalid, empty wired, multiple or foreign tokens show an error
and disable brush interaction until corrected. **Selection** is the sorted unique
member set for the whole domain, including keys absent from that display. Scoped
Overlay **Events** mirror the same last 32 actions, including Canvas actions with
`sourceSurface: "canvas"` (viewport cards use `"card"`, geometry uses `"model"`).

Selection is transient: tab switching retains it without a solve; closing the GH
document, deleting Scope or removing its last participant releases it. Locked
displays cannot originate actions and reproject the latest state on unlock. A
closed Rhino document suspends its Overlay contribution; only a later normal data
solve can bind that Overlay again. Other scope participants survive.

User selection/Clear schedules one coalesced output solution. Data reconciliation
can request one output refresh; a further feedback prune warns and awaits an
external solve. Direct recursive GH wires are rejected by Grasshopper; the host
verification uses a harness-only snapshot parameter to break evaluation recursion
and exercise this refresh bound. See [scope verification](docs/verification/selection-scope.md)
for lifecycle evidence, feedback details and 1× PNGs.

```powershell
./tools/examples/Build-Examples.ps1 -Example Rooms -VerifyOnly -Safe -Exports -State Scope -Output artifacts/screenshots-scope
```

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

## Group members

Bar and categorical Legend accept an optional **Members** text tree. Branch i,
in GH path order, contains the member keys for entry i. Supply exactly one branch
per entry, preserving empty branches, and explicit **Keys**; an unwired Members
input retains individual selection. A wired empty tree with entries is an error.
Metric accepts an optional **Key** and **Members** with exactly one branch:
Key alone selects a member; Key plus Members selects a group. Members without
Key is an error. Titles and Clear retain priority over Metric body clicks.

Keys are ordinal and case-sensitive. Group keys must not also be individual or
member keys; nesting is unsupported. Repeated group definitions must have equal
member sets. Conflicts warn on the display and disable affected group targets.
An empty group stays inert and reports “0 members.”

Click a partial group to select all its members; click a full group to remove
all its members. Other selected members remain selected. Selection outputs are
sorted unique member keys, never group tokens. Partial groups show an accent
dash; full groups and selected members keep the strong treatment. Hover outlines
effective members and related groups; only the direct target gets a light fill.
Category colors stay visible. Member tooltips list containing labels (five plus
a remainder); group tooltips report selected/hovered member counts. PNG, GH image
and PDF card exports omit hover and retain selection. Displays stay independent
unless explicitly connected to the same Scope.

## Scrolling and interaction

- Hover a bar/table row or linked viewport geometry to brush every card with the
  same key and draw linked geometry in a thin
  accent wire with translucent faces for surface geometry. Plain click replaces selection;
  Shift adds, Ctrl removes, and Ctrl+Shift toggles. Selected geometry draws stronger
  translucent faces and thicker x-ray wires, also in captures. Only real point geometry
  draws point dots. Alt does not change viewport selection mode. Plain empty viewport
  clicks clear selection on release within four pixels; drags and Shift/Ctrl clicks retain it. **Clear** in a keyed card's header clears that
  owner's selection, or the whole domain when scoped. Linked model clicks emit a `click` event with card id `model`.
  Cards take priority; other model clicks pass through to Rhino. Model picking
  pauses during Rhino commands and uses a six-pixel tolerance with hover near 30 Hz.
- Overlay outputs selected member keys and the last 32 click/clear events as JSON.
  Events retain owner/cardId/key/kind and add targetKind, affectedKeys, selectedKeys
  and sourceSurface. One group action emits one event.
  Unwired selection and hover belong to the document/owner; scoped brush state
  belongs to the explicit GH-document Scope. Both survive following another view; removed keys are pruned against individual card/Link keys and valid group
  members. Selection is
  transient (no undo or `.gh` persistence). Hover never triggers a GH solve.
- Over a scrollable table, the mouse wheel moves three rows per notch and is
  consumed before Rhino can zoom. Elsewhere, Rhino receives the wheel normally.
- Drag the scrollbar thumb or click its track to page. Scroll and hover state
  survive incoming Grasshopper solves; table offsets clamp as data changes.
- Click a table column header to cycle ascending, descending and original order.
  A group action reveals only the first newly selected row in current display order.
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
  failed screen-scale render retains existing textures; the next screen draw
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
- `src/ConduitPlus.Display`: shared DLL with owner sessions, input/wheel hook,
  GDI canvas, raster cache and PDF adapters. Starts on first use and stops when Rhino closes.
- `src/ConduitPlus.Rhino`: load-on-demand RHP with plugin identity and commands.
  Both the RHP and GHA reference Display and Core; the GHA never references the RHP.
  Build outputs copy Display.dll and Core.dll alongside each host; Yak includes each DLL once.
- `src/ConduitPlus.Grasshopper`: card and Overlay components.
- `tests/ConduitPlus.Core.Tests`: pure Core and Windows GDI alpha/clip/painter tests.
- `tools/bench`: RhinoCore host and bounded benchmark entry point.
- `packaging`: Yak manifest; `tools/Pack-Yak.ps1` bumps and builds the local package, `tools/Publish-Yak.ps1` publishes it.

The renderer feasibility history remains in `docs/spike/`; removed renderer code
is recoverable at tag `spike-renderer-feasibility`. `docs/PLAN.md` and
`docs/IDEAS.md` retain the original planning context.
See [how charts are drawn](docs/design/rendering.md) for diagrams of the current
raster pipeline, the former HTML/JS path and maintainability options.
