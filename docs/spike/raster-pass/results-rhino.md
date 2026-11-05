# Rhino renderer benchmark

Measured 2026-11-05T20:25:55.0881912+08:00. Rhino 8.36.26296.17001; OS Microsoft Windows NT 10.0.26300.0; .NET 8.0.33.

Viewport 1874 × 1729; DPI scale 2.000.

60 measured updates + 60 hover changes after warmup; 120 Redraw + UI message-pump calls per scenario. No geometry is added to the owner's model.

| Renderer | Single metric update p50 / p95 ms | Single compact bar hover p50 / p95 ms | Redraw ms | Redraw FPS | FPS delta |
|---|---:|---:|---:|---:|---:|
| No overlay | — | — | 13.178 | 75.886 | — |
| Native | 0.911 / 1.180 | 1.084 / 1.588 | 20.191 | 49.526 | -34.736% |
| Raster | 2.868 / 3.047 | 4.263 / 5.655 | 12.514 | 79.912 | 5.306% |
| Web | 128.817 / 154.785 | 129.326 / 153.801 | 19.431 | 51.464 | -32.182% |

Native latency = spec/revision preparation + measured layout and immediate-mode paint in DrawForeground. Raster latency = spec/revision preparation + GDI+ render + DisplayBitmap creation, before redraw. Web latency = spec/revision preparation + HTML layout + sequential CDP card capture + PNG decode + DisplayBitmap creation, before redraw. GPU completion and end-to-end mouse input latency are not measured.

Redraw timings include the UI message pump and measure CPU wall time; these are not measured orbit FPS and cannot establish the ≤5% orbit gate. 10 cards × 4 viewports, mixed-monitor DPI changes, browser crash recovery in Rhino, focus behavior and human visual approval: not measured by this command. Review PNG alpha and text at 1×/2×/4×. Direct web captures intentionally reuse display-scale textures; prepared captures regenerate at target scale.


- Host context: RhinoCore Hidden /safemode; ViewCapture fallback if redraw is suppressed; third-party startup plug-ins disabled.
- Native redraw callbacks: 125 for 125 calls including warmup.
- Native, 10 cards / 1 view: 21.532 ms, 46.443 FPS, delta -38.800%.
- Raster redraw callbacks: 125 for 125 calls including warmup.
- Raster, 10 cards / 1 view: 11.466 ms, 87.218 FPS, delta 14.932%.
- Web redraw callbacks: 125 for 125 calls including warmup.
- Web, 10 cards / 1 view: 11.968 ms, 83.557 FPS, delta 10.108%.
- Captures skipped by this harness run; use ConduitPlusCapture in normal Rhino to test the capture gate.
