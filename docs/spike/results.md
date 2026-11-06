> **Historical record:** After this decision, the Web and Native renderer code was removed. It remains recoverable at tag `spike-renderer-feasibility`.

# Renderer feasibility spike results

Updated **2026-11-05**, Windows / Rhino 8. Three renderers compared: Native
(DisplayPipeline 2D), Raster (GDI+ → bitmap) and Web (WebView2 → bitmap).

## Recommendation

Continue with **Raster as the default rich-card candidate**, keeping Native for
very frequent, simple metrics and Web for content that needs browser layout.
Raster metric/hover p95 is **3.047 / 5.655 ms**, versus Native **1.180 / 1.588 ms**
and Web **154.785 / 153.801 ms** in the same harness run. This supports low-latency
rich drawing, but does not establish native-equivalent latency or the orbit gate.
The new Line card needs comparable amounts of C# and SVG code; GDI+ makes the
filled chart practical without adding a graphics dependency. Its absence of
SkiaSharp did not block this chart; shaping, complex layout and cross-platform
rendering remain outside this Windows spike.

## Implementation

- `RendererKind.Raster`, `Rendering/RasterCardRenderer.cs`, `RasterDrawing.cs` and
  promoted `Rendering/GdiCanvas.cs`: synchronous per-card rendering into
  `Format32bppPArgb`, true rounded GraphicsPath fills, antialiasing, nested
  SetClip/Restore, single-line ellipsis and filled polygons. Text explicitly uses
  **AntiAliasGridFit**, never ClearType. Fonts/brushes are local to one disposable
  canvas; no GDI+ object is shared across threads.
- One bitmap + DisplayBitmap pair per live card/current revision/scale. A cache
  generation publishes pixels, hit regions, spec and revision together, then
  disposes replaced/removed pairs. RhinoProbe verifies initial 7 renders, one
  more for hover, one more for selection, none for a clean update, 7 for a scale
  change, and eviction down to one live card.
- Demo and GH Overlay accept **Native|Raster|Web**. Demo adds a seventh, Line card;
  the timing fixture stays the original six cards. Capture, Bench and RhinoProbe
  support Raster. Owner-keyed sessions, scoped UI contexts, the stable WebView2
  profile and the lead's WebSurface location are retained.
- Line cards show utilisation over load cases, 1/2/5 decade ticks, axes, a
  threshold band/line, filled areas, missing-value gaps, sample hit regions and
  hover/selection markers. Raster and Web have area fills; Native deliberately
  degrades polygon fills to outlines. The shared NativeCardPainter remains the
  C# painter. GH has no separate Line component yet; the demo/Core spec exposes it.

## Real Rhino measurements (three renderers)

Measured **20:25:55 HKT**, Rhino **8.36.26296.17001**, .NET **8.0.33**, Windows
**10.0.26300.0**, **200% DPI**. RhinoProbe uses the installed Rhino engine,
`/safemode`, a hidden **1874 x 1729** floating viewport and actual DisplayPipeline
callbacks. No geometry was added. **60 samples after 5 warmups**, nearest-rank
percentiles. Metric updates change one card among four metrics, a 20-member bar
and a 12 x 4 table. Hover changes one keyed row of a **240 x 180** compact bar.

| Renderer | Metric update p50 / p95 ms | Hover p50 / p95 ms | Redraw + pump ms | CPU redraw rate | Delta vs baseline |
|---|---:|---:|---:|---:|---:|
| No overlay | - | - | 13.178 | 75.886/s | - |
| Native | 0.911 / **1.180** | 1.084 / **1.588** | 20.191 | 49.526/s | -34.736% |
| Raster | 2.868 / **3.047** | 4.263 / **5.655** | 12.514 | 79.912/s | +5.306% |
| Web | 128.817 / **154.785** | 129.326 / **153.801** | 19.431 | 51.464/s | -32.182% |

Native includes revision/spec work, layout and the affected card's immediate
paint. Raster includes revision/spec work, GDI+ paint and DisplayBitmap creation;
Web includes revision/spec work, HTML layout, CDP screenshot, decode and
DisplayBitmap creation. Bitmap paths stop before redraw; GPU completion and
physical mouse-event-to-screen latency are **not measured**.

All three produced **125 callbacks for 125 redraw calls**. Ten cards / one view:
Native 21.532 ms, Raster 11.466 ms, Web 11.968 ms. Redraw includes message pumping,
synchronization and scheduling noise. Raster beating the no-overlay baseline
again shows why these rates **are not orbit FPS or a reliable overhead ranking**.
The <=5% orbit gate, especially 10 cards x 4 views, is still unmeasured.
Web's slower absolute times than the original run do not establish a regression;
this run still shows the same large screenshot floor and scheduler variability.

Raw samples and report are under [raster-pass/](raster-pass/results-rhino.md):
`samples-native.json`, `samples-raster.json`, `samples-web.json`. Earlier lead-run
results in the root spike directory were not overwritten.

## Raster micro-benchmark

**2x absolute CSS scale**, bitmap allocation + GDI+ paint + drawing-object cleanup;
no CardStore, DisplayBitmap, returned-bitmap disposal or GPU. One card at a time, **200 samples / 20 warmups**,
same process/runtime as the three-way benchmark. Raw samples:
[raster-pass/samples-raster-micro.json](raster-pass/samples-raster-micro.json).

| Card | Fixture | p50 ms | p95 ms |
|---|---|---:|---:|
| Metric | 280 x 92 | 1.3833 | 1.9333 |
| Bar | 20 members, 280 x 488 | 9.5228 | 12.6078 |
| Table | 12 x 4, 380 x 334 | 7.2649 | 8.1332 |
| Line | 8 load cases including a gap, 340 x 220 | 5.3113 | 6.0364 |

Full bar/table changes are appreciably more expensive than a metric. This is
still synchronous UI-thread work; dirty-card caching matters for large batches.

## Capture and alpha findings

**Chosen approach: re-render inside DrawForeground.** Raster Update prepares the
screen texture; a draw at a new scale synchronously replaces it before drawing.
No drawing callback awaits or pumps messages. The next screen draw restores the
screen scale. Direct and prepared Raster capture paths therefore use fresh
pixels, unlike Web's intentionally low-resolution direct capture path.

| Capture fixture | Native | Raster |
|---|---|---|
| 1x, six cards, source 1874 x 1729 | Pass, 1874 x 1729 | Pass, 1874 x 1729 |
| 2x, six cards, source 974 x 829 | Pass, 1948 x 1658 | Pass; scale 4 textures, 12 total renders |
| 4x, one 180 x 92 metric, source 474 x 329 | Pass, 1896 x 1316 | Pass; new 1440 x 736 texture at scale 8 |
| 2x / 4x, source 1874 x 1729 | No capture callback | No capture callback |
| 4x, source 974 x 829 | Not retried | No capture callback |

These are separate fresh processes. The smaller six-card fixture overflows its
viewport; it verifies scale regeneration, not full composition. The compact 4x
fixture verifies a single metric only. **Full-scene high-resolution capture is
not a passing gate.** The larger targets returned without DrawForeground; blank
PNGs were refused. This is size-dependent behavior in this safe-mode host, not
a proven driver diagnosis. Normal Rhino validation remains necessary. The lead
already had separate normal-Rhino high-resolution evidence; it was preserved,
not relabeled as this run's measurements.

Every accepted capture reports outside alpha **0**, card-interior alpha **255**.
Unit tests check all four card types at 1x/2x/4x. The raw raster rounded edges were
visually composited over dark, gray and magenta: no colour fringes. Rhino's
transparent PNG capture retains a small gray matte tint in partly transparent
edge RGB, also observed with Web; it is not pristine source alpha compositing.
Scale and bitmap dimensions are recorded in
[raster-pass/capture-checks.json](raster-pass/capture-checks.json).
Only three new representative PNGs are retained:
[raster viewport](captures/raster-1x-direct.png),
[raster Line](captures/raster-line-2x.png), [web Line](captures/web-line-2x.png).
No failed or blank image from this pass was kept.

## Line card code cost

Reproduce with `./tools/Measure-LineLoc.ps1`. Counts are **physical source lines**,
including braces/comments/blanks; existing compact TS style makes these counts
formatting-sensitive. This is not a normalized statement count.

| Renderer | Shared C# lines | Adapter / renderer-specific lines | Total lines |
|---|---:|---:|---:|
| Raster | 77 | 7 | **84** |
| Native, degraded outline fill | 77 | 5 | **82** |
| Web, full SVG fill | 0 | 63 | **63** |

Shared C#: all 73 lines of `LineCardPainter.cs` (ticks, hit regions, painting),
three Line dispatch/measure/hit lines in `Drawing.cs`, and one FillPolygon
interface signature. Raster adds the seven-line GDI+ FillPolygon method. Native
adds the five-line outline fallback. Web counts all 57 lines of `line.ts` and
six integration/type/height lines in `runtime.ts`. Common schema/validation,
Theme/demo data, tests, general GDI setup and cache/command plumbing are excluded
from this card-only comparison. GDI's new reusable polygon primitive is included
rather than hidden as infrastructure. **Web is fewer lines in this implementation**;
the reason to choose Raster is latency and shared C# integration, not a LOC win.

## Vector PDF stretch

Added `Rendering/PdfCanvas.cs` and **ConduitPlusPdf**, backed by Rhino's
[FilePdf API](https://developer.rhino3d.com/api/rhinocommon/type/rhino.fileio.filepdf/):
Create, AddPage(ViewCaptureSettings) with RasterMode=false, DrawText, DrawLine,
filled DrawPolyline and a DrawBitmap fallback hook. All current painter
primitives have vector implementations; rounded corners approximate each
quarter circle with 12 straight segments. Clipping is performed in the adapter
(polygons/segments), and text uses measured ellipsis. Unsupported future bitmap
content can use DrawBitmap; that content would remain raster. Rhino's own
shaded/model content can also depend on its export/display behavior.

The command hides all sessions in the active view while adding the base page,
then paints visible sessions' cards through the shared painter and restores
visibility in finally. Proposed mapping: **0.75 PDF points / CSS pixel**;
page resolution **96 x desktop DPI**, page dots equal source physical pixels.
Typography/alignment and point-versus-dot mapping are **not visually verified**.

**No successful PDF sample.** In RhinoCore safe mode, FilePdf.Create stalled
before AddPage (60-second bounded probe). Explicitly loading the installed
`export_PDF.rhp` also stalled (20-second probe). Only our own probe processes
were stopped. The backend/command compile, but exporting and inspecting a
sample in normal Rhino is still required. No raster PDF is presented as vector
proof, and no blank/rejected PDF was saved.

## Verification and next checks

- `npm run build`: passed (strict TypeScript and embedded bundle).
- `dotnet build ConduitPlus.sln`: passed, **0 warnings / 0 errors**.
- `dotnet test ConduitPlus.sln`: **8 passed**. Alpha/corners/interior, clip
  restoration and pure Line ticks added; previous tests retained.
- Rhino cache lifecycle and Web Line layout/hit validation: passed;
  [raster-cache-check.json](raster-pass/raster-cache-check.json).
- No new third-party dependency. No commit, push, PR or developer-install action.
- RhinoCore.Dispose still stalls after overlay/view cleanup. The optional
  `Run-RhinoProbe.ps1` wrapper detects that marker, allows 3 seconds, then stops
  only the process it launched; work timeouts are failures, not successful runs.
- Remaining: normal Rhino PDF/sample inspection, full-scene 2x/4x capture,
  interactive GH/selection/focus, mixed monitors, browser failure recovery in
  Rhino and the 10-card x 4-view orbit gate. Earlier standalone browser-mode,
  scale and crash-recovery findings remain in [results-webprobe.md](results-webprobe.md).

## Lead review notes (Claude)

Fixed before merge:

- The UI `SynchronizationContext` was replaced globally on Rhino's UI thread, which would affect every
  other plug-in, and `Send` ran inline on the caller's thread. It is now scoped to our own async call
  chains (`CaptureUiContext.Enter`, `OverlayManager.RunOnUi`), and `Send` marshals correctly.
- Two Overlay components on one view evicted each other's session on every solve. Sessions are now keyed
  per owner. An overlay that follows the active view now removes its session from the previous view
  instead of leaving stale cards behind.
- The WebView2 profile was a new `%TEMP%` folder per Rhino process and was never deleted. It now uses
  one stable `%LOCALAPPDATA%\ConduitPlus\WebView2`.
- `WebSurface` moved from `tools/Shared` into the plug-in project. The built web bundle (`dist/`) is
  git-ignored. Absolute paths were removed from the README.

After these fixes the harness was re-run in the same environment. Latencies reproduced: native metric
update p95 1.02 ms and hover p95 0.90 ms; web 105.5 ms and 111.2 ms. Redraw rates swung again (web
measured *faster* than the no-overlay baseline), which confirms they are not usable as an orbit metric.
The evidence files above are from the original run.

Observations to carry into the next phase (not fixed here):

- **Per-frame cost matters as much as update latency.** In the harness, both overlays added about 4–5 ms
  to an 11.7 ms CPU redraw. Native re-measures every text run each frame (`Measure2dText`, plus a
  truncation loop) and draws rounded corners as 1-px strips. Caching measured text and layout per
  revision should cut that substantially. This still has to be measured in an interactive session before
  the ≤5% orbit gate can be judged.
- **Web latency has a floor around 90 ms per CDP screenshot,** even outside Rhino (see the probe table).
  Batching cards into one atlas capture helps multi-card updates but not single-card hover. Hover
  feedback would have to be drawn natively on top of the cached bitmap for web to be viable.
- The overlay draws into `RhinoDoc.ActiveDoc.Views.ActiveView` at solve time. Pinning a view (or a
  Views input) is needed before multi-view use.
- Two overlays in the same view do not yet avoid each other's slots.

### Round 2 (Raster renderer)

- Reviewed: per-card cache invalidation (verified by RenderProbe), GDI+ alpha handling
  (premultiplied ARGB, AntiAliasGridFit, no ClearType), nested clip restore, tick generation
  tests, and Raster wiring through Demo, GH, Bench, Capture and probes. Raster and Web Line
  cards are visually near-identical (`captures/raster-line-2x.png` vs `web-line-2x.png`).
- Fixed: a Raster re-render at capture scale runs inside `DrawForeground`. It is now guarded,
  so a GDI+ failure (e.g. out of memory for a very large capture) falls back to the existing
  bitmaps instead of throwing from a display conduit.
- Pruned unreferenced 1×/2×/4× native/web capture PNGs (4.1 MB → 1.3 MB); metadata stays in
  `raster-pass/capture-checks.json`.
- Note: the first screen frame after a capture re-renders every card at screen scale
  synchronously (~30 ms for the demo set at 2× DPI). That's a one-off hitch, acceptable for now.
- `ConduitPlusPdf` is experimental: `FilePdf.Create()` stalled in the safe-mode harness, so it is
  unverified. Try it in a normal Rhino session before building on it.
