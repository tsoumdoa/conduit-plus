# Capture inventory

`probe-Hidden-{1.0|1.5|2.0|4.0}x-{card}.png` are real WebView2/CDP captures
from the hidden host (Off-screen and Minimized produced matching results and were
pruned from the repo; their timings remain in `samples-probe-*.json`). All 72 original images had the requested device-scale
dimensions. Metric corner alpha is 0 and interior alpha is 255 at every tested
host/scale combination. These establish the web surface and transparency,
not Rhino compositing or capture parity.

Kept Rhino captures (real Rhino 8.36 / .NET 8 / safe-mode RhinoCore, 200% DPI):

- `native-1x-prepared.png`: full-view native ViewCapture, transparent background.
- `raster-1x-direct.png`: full-view GDI+ raster ViewCapture.
- `raster-line-2x.png` / `web-line-2x.png`: the Line card from both renderers, for visual parity.

The other native and web 1×/2×/4× capture PNGs were pruned to keep the repo small. Their
dimensions, alpha checks and pass/fail status are recorded in `../raster-pass/capture-checks.json`.
2× passed on a smaller view and 4× on a compact single-metric fixture; larger targets got no
capture callback in the safe-mode harness, so **full-scene high-resolution capture is still
unverified** and needs a normal interactive Rhino session. The capture command refuses to save
an image if no overlay draw callback occurs.
