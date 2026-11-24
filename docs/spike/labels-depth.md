# Label depth spike

The depth readback spike was retired after the hidden host returned no usable depth buffer. The [labels design](../design/labels.md#visibility-before-layout) uses CPU ray occlusion. Run the replacement headless harness with `./tools/bench/Run-Bench.ps1 -Mode LabelOcclusion`. Reports are written under `artifacts/`.

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

### Hidden-host follow-up (2026-11-24)

- `view.Redraw()` only invalidates the view. The foreground callback ran only once each redraw was followed by `RhinoApp.Wait()`, as `BenchCommand` already does. That was the cause of the "Redraw did not invoke…" failure in both the hidden host and interactive Rhino.
- Calling `ZBufferCapture.WorldPointAt` from inside `DrawForeground` crashed with a native access violation. The capture and classification now run between frames, before the paint. That matches the design's classify-before-layout order.
- In the hidden RhinoCore host, `ZBufferCapture` yields no buffer: `GrayscaleDib()` throws natively and `WorldPointAt` would read invalid memory.
- The spike now checks the buffer size once per case, before sampling, so a missing capture fails with a managed error instead of crashing Rhino.
- The hidden host yielded no depth measurements.
