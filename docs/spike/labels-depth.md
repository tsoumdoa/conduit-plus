# Label depth spike

The implementation targets the pinned RhinoCommon 8.20 API. It records a fresh
ZBufferCapture for each measured foreground redraw because arbitrary preview
revisions are unavailable. It uses the active mode, samples each occupied
viewport pixel once, and guards its own foreground callback and Conduit+ HUD
during the nested capture. The reference oracle intersects the sampled pixel ray
with the known plane and skips a narrow band around edges. Ordinary hidden dots
are omitted and every ninth anchor represents a selected member at 45% alpha.
The transparent-dot appearance still needs interactive verification.

## Results

On Windows, RhinoCore 8.36.26296.17001 initialized in hidden safe mode and created
a floating viewport (1874 × 1729 reported pixels on the first run). The first
shaded, perspective, 500-anchor case failed on its first warm-up frame. The
paired synchronous Redraw calls did not deliver a matching foreground callback,
so no capture call or timing sample was reached through this path. The wrapper
stopped only its own child after RhinoCore shutdown stalled. This result concerns
the hidden host's redraw path. It does not establish whether an offscreen capture
or an interactive Rhino viewport can read depth.

The Windows and Mac-targeted assemblies compile against 8.20. Mac runtime was
unavailable. The code assumes top-origin viewport pixels, an invalid world point
for background and 24-bit normalized depth for its bias estimate. The plane
comparisons can expose failures in those assumptions, but no live result has
verified them. Early and late diagnostics model two conduit orders. Actual GH
standard preview and Custom Preview depth are unverified.

| Gate | Required | Result |
| --- | --- | --- |
| Baseline-subtracted p95 | ≤2 ms at 500, ≤5 ms at 5,000 | Unmeasured |
| Baseline-subtracted p99 | ≤4 ms at 500, ≤8 ms at 5,000 | Unmeasured |
| Recurring stalls | No recurring ≥16 ms spike | Unmeasured |
| Interior false hits and leaks | Zero | Unmeasured |
| Standard and Custom Preview coverage on both platforms | Both contribute where they write depth | Unverified |

The feasibility gate remains open. Validate ZBufferCapture in interactive Rhino
first. If real GH preview depth is absent or the timing gates fail, test a
batched platform-specific sampler against completed scene depth before choosing
a label renderer.

## Run on Windows or Mac

From the repository root on Windows, the bounded hidden-host reproduction is:

```powershell
./tools/bench/Run-Bench.ps1 -Mode LabelSpike -Configuration Debug
```

For an interactive run, use a disposable empty Rhino model and a maximized
model viewport. Install a local Yak containing this spike through the normal
project workflow once the reviewing thread prepares the test packages. Build
the Windows package on Windows and the Mac package with Pack-Yak.ps1
-Platform mac. Drop the platform-specific Yak onto Rhino and restart Rhino
before running the command. This worktree contains source only.

Windows build:

```powershell
dotnet build src/ConduitPlus.Rhino/ConduitPlus.Rhino.csproj -c Debug
```

Use the RHP in src/ConduitPlus.Rhino/bin/Debug/net8.0-windows/.

Mac build with the .NET 8 SDK:

```sh
dotnet build src/ConduitPlus.Rhino/ConduitPlus.Rhino.csproj -c Debug -p:ConduitPlusPlatform=mac -p:EnableWindowsTargeting=true
```

Use the RHP in artifacts/mac/bin/ConduitPlus.Rhino/Debug/net8.0/. Run
`_ConduitPlusLabelSpike` in Rhino 8 on either platform. The command runs the
same orbit/zoom and stationary-camera blocker shifts in each case, with 20
discarded warm-up frames followed by 600 recorded frames. It saves JSON after
each case. The report contains raw samples, per-stage p50/p95/p99/max, paired
redraw deltas, counts of ≥16 ms deltas, and interior mismatch totals. Redraw
wall time includes blocking readback but does not independently fence final GPU
presentation. The report covers placeholder dots, with no text atlas or hit
routing. Run again with a 4K viewport where available to exercise readback at
that size.

Repeat on native 1× and 2× displays where available, moving Rhino onto the
monitor before starting each run. Check the report's viewport dimensions and
detected DPI, and save each report separately. Set CONDUITPLUS_RESULTS in the
environment inherited by Rhino to an absolute directory such as
repo/artifacts/labels-depth-2x. Without an override the command writes to
Documents/ConduitPlus/artifacts/labels-depth/labels-depth.json. The optional
CONDUITPLUS_SPIKE_DISPLAY tag is recorded alongside detected DPI.

For real GH checks, run the command and set its **Source** option to
GHStandard or GHCustom (the hidden-host bench still reads CONDUITPLUS_SPIKE_GH),
once per source. Each source writes to its own results subfolder. This tag switches to
the external-plane oracle and identifies the report. In Grasshopper build a
single quad mesh with corners (-6,-4,0), (2,-4,0), (2,5,0), (-6,5,0), in that
order, without baking it. For Standard, enable preview only on the mesh output.
For Custom, disable that output preview and wire it into an opaque Custom
Preview, which should be the only preview contributor. Keep Grasshopper open
with the definition active and other previews disabled. Start with Shaded
results. The wireframe oracle expects wire-only coverage outside its excluded
edge band, so a Custom Preview that writes solid depth in wireframe requires a
separate interpretation of those results.
