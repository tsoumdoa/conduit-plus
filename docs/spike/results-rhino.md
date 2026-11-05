# Rhino renderer benchmark

Measured 2026-11-05T19:18:23.1505352+08:00. Rhino 8.36.26296.17001; OS Microsoft Windows NT 10.0.26300.0; .NET 8.0.33.

Viewport 1874 × 1729; DPI scale 2.000.

60 measured updates + 60 hover changes after warmup; 120 Redraw + UI message-pump calls per scenario. No geometry is added to the owner's model.

| Renderer | Single metric update p50 / p95 ms | Single compact bar hover p50 / p95 ms | Redraw ms | Redraw FPS | FPS delta |
|---|---:|---:|---:|---:|---:|
| No overlay | — | — | 11.744 | 85.147 | — |
| Native | 0.667 / 1.009 | 0.771 / 1.217 | 15.767 | 63.422 | -25.515% |
| Web | 83.596 / 99.076 | 88.145 / 109.566 | 16.794 | 59.545 | -30.068% |

Native latency = spec/revision preparation + measured layout and immediate-mode paint in DrawForeground. Web latency = spec/revision preparation + HTML layout + sequential CDP card capture + PNG decode + DisplayBitmap creation, before redraw. GPU completion and end-to-end mouse input latency are not measured.

Redraw timings include the UI message pump and measure CPU wall time; these are not measured orbit FPS and cannot establish the ≤5% orbit gate. 10 cards × 4 viewports, mixed-monitor DPI changes, browser crash recovery in Rhino, focus behavior and human visual approval: not measured by this command. Review PNG alpha and text at 1×/2×/4×. Direct web captures intentionally reuse display-scale textures; prepared captures regenerate at target scale.


- Host context: RhinoCore Hidden /safemode, forced ViewCapture callbacks; third-party startup plug-ins disabled.
- Native redraw callbacks: 125 for 125 calls including warmup.
- Native, 10 cards / 1 view: 18.356 ms, 54.477 FPS, delta -36.021%.
- Web redraw callbacks: 125 for 125 calls including warmup.
- Web, 10 cards / 1 view: 11.678 ms, 85.633 FPS, delta 0.571%.
- Captures skipped by this harness run; use ConduitPlusCapture in normal Rhino to test the capture gate.
