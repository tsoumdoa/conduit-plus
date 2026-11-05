<img src="assets/icon.svg" width="64" height="64" alt="">

# conduit-plus

Created in [T3 Code](https://t3.codes).

Windows feasibility spike for a structural-results HUD in Rhino 8 and Grasshopper.
Native, Raster (GDI+) and Web renderers consume the same card spec, paper theme,
revision store and slot layout. Raster and Native share the C# card painter.
See [the plan](docs/PLAN.md) and [measured results](docs/spike/results.md).

## Building & running the spike

Prerequisites: Windows, Rhino 8.20+, .NET 8 Desktop Runtime, .NET SDK 9/10,
Node 22+, npm, and the installed Edge WebView2 runtime. Rhino 8's bundled
WebView2 Core assembly must be **1.0.1938.49**. NuGet references RhinoCommon
and Grasshopper **8.21.25188.17001** without shipping host assemblies.
All plug-in projects target `net8.0-windows`; Core targets `net8.0`.

```powershell
Set-Location src/ConduitPlus.Web
npm ci
npm run build
Set-Location ../..
dotnet build ConduitPlus.sln
dotnet test ConduitPlus.sln --no-build
```

The HTML is embedded in the `.rhp` at build time. Run `npm run build` and
rebuild the solution after changing TypeScript. No runtime server, CDN,
downloaded font or network request is needed. The WebView uses only Core APIs,
an HWND and Rhino's bundled native loader; there is no WebView WinForms/WPF control.

To run directly from the checkout, start Rhino 8 and enter:

```text
_-RunPythonScript "<path-to-repo>\tools\run-spike.py"
```

That script loads/registers the Debug `.rhp` and runs the full benchmark. It
does not exit Rhino unless `CONDUITPLUS_EXIT=1` is set in its launch environment.
For an isolated automatic run from PowerShell:

```powershell
$env:CONDUITPLUS_EXIT = '1'
$env:CONDUITPLUS_LARGE_VIEW = '1' # optional 1900×1800 floating test viewport
Start-Process 'C:\Program Files\Rhino 8\System\Rhino.exe' -WindowStyle Hidden -ArgumentList '/netcore-8 /nosplash /notemplate /runscript="_-RunPythonScript <path-to-repo>\tools\run-spike.py"'
```

`/netcore-8` explicitly selects .NET 8. This machine's Rhino 8.36 defaults to
.NET 10; the target remains .NET 8. See McNeel's
[runtime selection documentation](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/).
Hidden Rhino may suppress viewport redraws: the benchmark detects that and
uses real ViewCapture draw callbacks for native paint timing, labeling redraw
overhead as **not measured**. Run in a visible, maximized viewport to measure
redraw overhead. CPU Redraw + message-pump timings are not orbit/GPU FPS.

For Grasshopper, close Rhino, run `./tools/Install-Dev.ps1`, then load the printed
`.rhp` path with Rhino's PlugInManager → Install before starting Grasshopper.
The installer copies only our three assemblies into
`%APPDATA%/Grasshopper/Libraries/ConduitPlusSpike`. It never copies RhinoCommon,
Grasshopper or WebView2 assemblies. Remove that folder to uninstall the GH components.
Under **Conduit+**, connect Metric/Bar/Table outputs to Overlay's Cards list;
Renderer accepts `Native` (default), `Raster` or `Web`. Bar Keys and Table Keys are unique
within each card; shared keys across cards refer to the same member. Selection
outputs clicked keys; Events keeps the last 32 click events as JSON. Table Rows
uses one branch per row. NaN in Bar Values is treated as a missing value.

Commands:

- `ConduitPlusDemo`, choose `Renderer=Native`, `Raster` or `Web`, then Enter:
  seven demo cards, including a Line chart of utilisation over load cases.
- `ConduitPlusBench`: 60 updates and 60 single-card hover changes per renderer,
  120 redraw calls, 10-card variant, direct and prepared captures at 1×/2×/4×.
- `ConduitPlusCapture 2`: capture the current overlay as transparent PNG. Web
  pre-renders at target scale; Raster synchronously regenerates textures in the
  draw callback and restores screen-scale textures on the next screen draw.
- `ConduitPlusPdf`: experimental vector export of the active view and cards.
  Compiles, but safe-mode RhinoCore stalled loading Rhino's PDF writer; a normal
  Rhino export and visual inspection are still required. No sample PDF is claimed.

Results go to `docs/spike` when running from the checkout; installed copies
default to `Documents/ConduitPlus/spike`. Set `CONDUITPLUS_RESULTS` to an absolute
directory to override. PNGs are in `captures/`, raw timing samples are JSON,
and the command's report is `results-rhino.md`.

The Rhino-free WebView2 probe runs with:

```powershell
dotnet run --project tools/ConduitPlus.WebProbe -- docs/spike
```

It exercises off-screen, hidden and minimized HWNDs, 1×/1.5×/2×/4× device scales,
50 measured full-batch updates and 50 single compact-bar hover changes, alpha
readback, and browser kill/reinitialization/resync. It uses Rhino's exact Core
assembly but never loads RhinoCommon. Its latency excludes DisplayBitmap/GPU upload.
Tests write GDI snapshot PNGs under the test output's `artifacts/` folder and
check Raster alpha at 1×/2×/4×, clipping and pure Line tick generation.

An additional real-Rhino in-process harness can reproduce timing measurements:

```powershell
dotnet run --project tools/ConduitPlus.RhinoProbe --no-build
```

It hosts Rhino 8 under .NET 8 with a hidden 1900×1800 floating view and
`/safemode` to avoid third-party startup plug-ins. It invokes the same benchmark
code against Rhino's real DisplayPipeline. This is **not** a normal interactive
GPU/orbit benchmark. It skips repeated captures because safe-mode ViewCapture
failed and left subsequent captures blank in this environment. Experimental
single captures can be attempted in a fresh process with
`-- --capture Raster 1 direct` (or `Native`/`Web`, scales `2`/`4`, `direct`/`prepared`).
This pass's large 2×/4× targets did not invoke overlay callbacks. Smaller 2×
targets and a single compact metric at 4× worked; full-scene high-resolution
capture still needs normal Rhino verification. Safe-mode RhinoCore shutdown
can stall after the workload and overlay/view cleanup have completed.

For bounded probe processes that handle that shutdown stall and keep this pass's
evidence separate from the lead's earlier results:

```powershell
./tools/Run-RhinoProbe.ps1                         # three-way timings + 2x raster micro-benchmark
./tools/Run-RhinoProbe.ps1 -Mode Line              # dirty-cache checks + Raster/Web Line snapshots
./tools/Run-RhinoProbe.ps1 -Mode Capture -Renderer Raster -Scale 1
./tools/Run-RhinoProbe.ps1 -Mode Capture -Renderer Raster -Scale 2 -Small
./tools/Run-RhinoProbe.ps1 -Mode Capture -Renderer Raster -Scale 4 -Compact
./tools/Run-RhinoProbe.ps1 -Mode Pdf -TimeoutSeconds 20  # currently stalls loading the PDF writer
./tools/Measure-LineLoc.ps1
```

The wrapper defaults to `docs/spike/raster-pass`; use `-Output` to change it.
`-Small` uses a 1000×900 floating window (six cards can overflow). `-Compact`
uses a 500×400 window and one 180×92 metric. These do not change the timing
fixture. Its raw `--line` / `--pdf` modes are also available via `dotnet run`.

## Spike scope

Metric, signed Bar, Table, Line (demo/Core spec), deterministic slots (`wired > manual > auto`), keyed
hover/click selection, per-view lifecycle and captures are implemented. Slot
overflow is currently allowed; maximize the viewport or use the large test view.
No drag editor, Inspector, geometry Link/highlight, persistence, undo, Yak package
or cross-platform renderer is included. Text uses Segoe UI system fonts. Native
cell text truncates with an ellipsis; web cell text clips. Raster uses real GDI+
rounded paths, clipping, antialiasing, ellipsis and polygon fills. Native uses
horizontal strips for rounded corners and a degraded outline for Line area
fills. The paper theme is fixed. Raster scale changes regenerate synchronously;
mixed-monitor Web DPI resync still needs a new card update.

Drawing callbacks never await; Raster can synchronously regenerate at a changed
capture/DPI scale. WebView2 work is serialized
on the UI thread, and bitmap/spec/hit-region revisions publish together.
Document/view closure and plug-in shutdown disable conduits, mouse callbacks
and close WebView2 controllers. A browser failure requests resync from the host
snapshot; recovery in Rhino still needs the interactive gate check.
