# Capture inventory

`probe-Hidden-{1.0|1.5|2.0|4.0}x-{card}.png` are real WebView2/CDP captures
from the hidden host (Off-screen and Minimized produced matching results and were
pruned from the repo; their timings remain in `samples-probe-*.json`). All 72 original images had the requested device-scale
dimensions. Metric corner alpha is 0 and interior alpha is 255 at every tested
host/scale combination. These establish the web surface and transparency,
not Rhino compositing or capture parity.

`native-1x-direct.png` and `native-1x-prepared.png` are real Rhino 8.36 / .NET 8 / safe-mode ViewCaptures at
200% desktop DPI, 1874×1729 output. It contains all six cards. Corner alpha is
0 and card interior alpha is 255. The prepared PNG and its JSON metadata use
the final sample data. Sample data in older captures precedes the
demo's passing-member count correction (17 of 20 pass).

Rejected experiments were removed from the repo before review:
blank native 2×/4× images (after a native ViewCapture exception in the safe-mode
harness) and clipped web captures from the first run (a capture-layout bug, since
fixed). A fresh 2× capture in the safe-mode harness also stalled in native
`CaptureToBitmap`, so high-resolution capture remains **unverified**. The capture
command now refuses to save an image if no overlay draw callback occurs.
