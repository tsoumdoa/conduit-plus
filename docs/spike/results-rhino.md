# Rhino renderer benchmark

Measured 2026-11-05T19:57:44.2926769+08:00. Rhino 8.36.26296.17001; OS Microsoft Windows NT 10.0.26300.0; .NET 10.0.14.

Viewport 589 × 461; DPI scale 2.000.

60 measured updates + 60 hover changes after warmup; 120 Redraw + UI message-pump calls per scenario. No geometry is added to the owner's model.

| Renderer | Single metric update p50 / p95 ms | Single compact bar hover p50 / p95 ms | Redraw ms | Redraw FPS | FPS delta |
|---|---:|---:|---:|---:|---:|
| No overlay | — | — | 0.531 | 1884.372 | — |
| Native | 0.177 / 0.203 | 0.237 / 0.301 | 1.675 | 597.126 | -68.312% |
| Web | 122.583 / 152.893 | 122.934 / 153.814 | 0.635 | 1574.648 | -16.436% |

Native latency = spec/revision preparation + measured layout and immediate-mode paint in DrawForeground. Web latency = spec/revision preparation + HTML layout + sequential CDP card capture + PNG decode + DisplayBitmap creation, before redraw. GPU completion and end-to-end mouse input latency are not measured.

Redraw timings include the UI message pump and measure CPU wall time; these are not measured orbit FPS and cannot establish the ≤5% orbit gate. 10 cards × 4 viewports, mixed-monitor DPI changes, browser crash recovery in Rhino, focus behavior and human visual approval: not measured by this command. Review PNG alpha and text at 1×/2×/4×. Direct web captures intentionally reuse display-scale textures; prepared captures regenerate at target scale.


- Host context: Rhino command.
- Native redraw callbacks: 125 for 125 calls including warmup.
- Native, 10 cards / 1 view: 1.868 ms, 535.216 FPS, delta -71.597%.
- Web redraw callbacks: 125 for 125 calls including warmup.
- Web, 10 cards / 1 view: 0.749 ms, 1334.992 FPS, delta -29.155%.
- Native 1× direct/prepared capture: saved.
- Native 2× direct/prepared capture: saved.
- Native 4× direct/prepared capture: saved.
- Web 1× direct/prepared capture: saved.
- Web 2× direct/prepared capture: saved.
- Web 4× direct/prepared capture: saved.
