# Conduit+ renderer feasibility spike — 2026-11-05

## Recommendation

Use **native drawing for the structural-results pilot**. Keep the shared card
spec and the web renderer as a measured experimental path. In the same real
Rhino 8.36 / .NET 8 harness, native card updates and hover painting were about
1 ms p95; web render/capture/decode/DisplayBitmap preparation was about 100 ms.
The current web implementation misses the ≤50 ms update and ≤33 ms hover
gates. This does not establish a limit on all possible web implementations:
atlas capture/cropping and native hover feedback are possible later experiments.

Neither renderer has passed the complete shipping gate. High-resolution
ViewCapture and normal interactive orbit performance still need owner testing.

## Real Rhino measurements

Measured at **19:18 HKT on 2026-11-05**, Rhino **8.36.26296.17001**, .NET
**8.0.33**, Windows **10.0.26300.0** (Windows 11), **200% DPI**. The harness
hosts the installed Rhino engine in-process with a hidden 1874×1729 floating
viewport and `/safemode`; third-party startup plug-ins are disabled. These are
real DisplayPipeline draw callbacks, not GDI substitutes. No geometry was added.
The normal Rhino.exe startup path loaded the plug-in successfully once, but
later launches stalled before the startup script. Desktop UI automation failed
twice with `GetCursorPos failed: Access is denied. (0x80070005)`.

Scenario: 4 metrics, 20-member utilisation bar, 12×4 table. Each latency series
has **60 samples after 5 warmups**. Metric latency changes one metric while the
six-card overlay remains present. Hover uses one **240×180 CSS-pixel** bar with
six keyed members. Percentiles use the nearest-rank method.

| Renderer | Metric update p50 / p95 ms | Hover p50 / p95 ms | Redraw + pump ms | Effective CPU redraw rate | Delta vs baseline |
|---|---:|---:|---:|---:|---:|
| No overlay | — | — | 11.744 | 85.147/s | — |
| Native | 0.667 / **1.009** | 0.771 / **1.217** | 15.767 | 63.422/s | −25.515% |
| Web | 83.596 / **99.076** | 88.145 / **109.566** | 16.794 | 59.545/s | −30.068% |

Native update latency includes spec validation/revision preparation, layout and
the changed card's actual immediate-mode paint. Native hover includes the whole
single-card paint. Web includes validation/revisions, JS layout, CDP PNG capture,
PNG decode and DisplayBitmap construction. GPU upload completion and physical
mouse-event-to-screen latency are **not measured**.

Each renderer produced **125 actual overlay callbacks for 125 redraw calls**
(120 timed plus 5 warmups). Ten cards / one view measured native 18.356 ms
(54.477/s) and web 11.678 ms (85.633/s). These redraw timings include the UI
message pump, display synchronization and scheduling noise. An earlier run
put web faster than the no-overlay baseline; therefore **do not interpret these
rates as orbit FPS or as reliable proof of relative frame overhead**. The ≤5%
orbit gate is **not measured**, including the 10-card × 4-view scenario.

Raw samples and the command-generated report: `samples-native.json`,
`samples-web.json`, [results-rhino.md](results-rhino.md).

## Rhino-free WebView2 measurements

Core API **1.0.1938.49**, installed runtime **156.0.4314.58**, .NET **8.0.33**.
Each entry has 50 measured updates after 5 warmups. Batch latency deliberately
recaptures all six cards; it differs from the single-dirty-card Rhino benchmark.
Hover recaptures just the 240×180 card. This probe excludes Rhino and GPU upload.

| Host | Scale | Six-card batch p50 / p95 ms | Single hover p50 / p95 ms |
|---|---:|---:|---:|
| Off-screen | 1× | 388.03 / 423.06 | 92.89 / 95.44 |
| Off-screen | 1.5× | 402.54 / 425.03 | 93.05 / 94.57 |
| Off-screen | 2× | 420.98 / 464.39 | 93.88 / 109.24 |
| Off-screen | 4× | 498.47 / 530.05 | 107.66 / 110.64 |
| Hidden | 1× | 401.85 / 422.32 | 93.72 / 95.18 |
| Hidden | 1.5× | 408.79 / 440.87 | 93.39 / 108.99 |
| Hidden | 2× | 421.81 / 471.08 | 93.02 / 108.64 |
| Hidden | 4× | 501.53 / 528.16 | 93.95 / 124.98 |
| Minimized | 1× | 400.37 / 422.15 | 93.03 / 94.79 |
| Minimized | 1.5× | 420.39 / 438.88 | 93.64 / 95.41 |
| Minimized | 2× | 421.01 / 453.74 | 93.81 / 124.91 |
| Minimized | 4× | 512.57 / 534.11 | 94.60 / 110.66 |

All three host modes rendered successfully at all four scales. A metric's
corner alpha was **0**, interior **255**, with exact expected dimensions
(280×92, 420×138, 560×184, 1120×368). System-font text and rounded alpha edges
were inspected in representative images. Host background-throttling flags were
disabled for every mode; the controller remained visible even when its HWND
was hidden. The off-screen tool window uses NOACTIVATE. Focus-stealing behavior
in a normal user session is **not measured**.

After killing each host's **own** WebView2 browser process, reinitialization and
six-card resync succeeded: off-screen **689.93 ms**, hidden **653.29 ms**,
minimized **659.00 ms**. Crash recovery **inside Rhino** is **not measured**.
The same renderer subscribes to ProcessFailed and requests a host-snapshot
resync. See [results-webprobe.md](results-webprobe.md) and `samples-probe-*.json`.

## Capture findings

| Path | 1× | 2× | 4× |
|---|---|---|---|
| WebView2 per-card PNG | Valid alpha and scaled text | Valid alpha and scaled text | Valid alpha and scaled text |
| Native Rhino ViewCapture | **Valid**, direct and prepared | Failed/stalled in safe-mode harness | Not verified after pipeline failure |
| Web Rhino ViewCapture, direct | First run drew cards, clipped | First run drew cards, clipped | First run drew cards, clipped |
| Web Rhino ViewCapture, prepared | Capture gate not verified | Capture gate not verified | Capture gate not verified |

The valid native 1× output is 1874×1729 at desktop DPI 2.0, with corner alpha 0
and card alpha 255. [native-1x-prepared.png](captures/native-1x-prepared.png)
was generated from the final sample-data revision and visually inspected.

Repeated ViewCapture in the in-process safe-mode environment threw a native
`External component has thrown an exception`, then stopped invoking the draw
callback. A fresh 2× attempt also stalled inside `CaptureToBitmap`; only our own
test process was stopped. Blank PNGs from earlier experiments were discarded. The command now rejects captures with no overlay callback.
The first Rhino.exe web captures exposed a source-versus-target viewport-size
bug, now fixed in source; those clipped images were discarded and are
**not passing capture results**. A fresh native 1× prepared capture succeeded.
Safe-mode RhinoCore shutdown can also stall after our overlay and floating view
have been disposed. These host/driver limitations do not establish normal Rhino
capture behavior. No 2×/4× shipping claim is made.

## Build, installation and remaining manual checks

- `npm run build`: passed (TypeScript strict check + esbuild, embedded single HTML).
- `dotnet build ConduitPlus.sln`: passed with **0 warnings / 0 errors**.
- `dotnet test ConduitPlus.sln`: **5 passed**; native GDI PNG snapshots at 1×/1.5×/2×.
- Developer files installed to `%APPDATA%/Grasshopper/Libraries/ConduitPlusSpike`.
  The `.rhp` was also registered/loaded from the checkout by the first Rhino run.
  Rhino/Grasshopper/WebView2 assemblies are not copied into plug-in output.

To finish the unmeasured gate checks:

1. Start a **normal visible Rhino 8** session, optionally `/netcore-8`, and load
   the printed developer `.rhp` with PlugInManager → Install. Maximize the active
   viewport (small views intentionally allow card overflow).
2. Run `ConduitPlusDemo`, choose Native, then run `ConduitPlusCapture` at 1, 2
   and 4. Repeat with Web. Inspect alpha, text and placement in saved PNGs.
3. Run `ConduitPlusBench` in that normal session. It runs both renderers and
   records samples/captures. Set `CONDUITPLUS_RESULTS` before launch if results
   should return to this checkout from an installed copy. CPU redraw rate is
   still not orbit FPS; perform the 10-card × 4-view orbit comparison separately.
4. In Grasshopper, wire Metric/Bar/Table to Overlay; test both Renderer inputs,
   clicked Selection/Events, document/view close, plug-in shutdown and monitor
   DPI transitions. **Interactive GH outputs are built but not manually tested.**
5. Test browser failure recovery and focus in Rhino. Browser PID is available
   in the renderer's diagnostic state; the standalone probe already exercises
   automatic kill/re-init without touching unrelated browser processes.

No Inspector, drag editor, Link geometry highlight, persistence/undo, Yak package,
or cross-platform support is included. Theme is fixed paper; cells use clipped
web text versus native ellipsis; native rounded corners use 2D strips. Mixed-DPI
web resampling needs the next card update. All code and measurements remain
**uncommitted** for lead review.

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
