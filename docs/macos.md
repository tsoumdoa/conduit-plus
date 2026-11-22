# macOS support (Rhino 8.20+ for Mac, Grasshopper 1)

Status: **implemented, built and unit-tested on Windows; not yet run on a Mac.**
Everything below marked *unverified* needs the on-Mac checklist at the end. The
Windows package (`rh8_20-win`) is built exactly as before; macOS ships as a
separate Yak distribution, `rh8_20-mac`.

## Approach

Conduit+ keeps its bitmap renderer on macOS. RhinoCommon's own NuGet props state
that "Rhino includes a Windows Forms and System Drawing implementation for Mac",
and enable `UseWindowsForms` for a plain `net8.0` target. So the shared C#
painter, the GDI+-style rasters and the DisplayConduit compositing are unchanged.
A WKWebView renderer (docs/PLAN.md) is not needed for parity.

What differs per platform is isolated in `src/ConduitPlus.Display/Platform/`.
On Windows each member there makes the same call the code made before. On macOS
it talks to AppKit directly through `libobjc`, with no Xamarin or Microsoft.macOS
dependency:

| Concern | Windows (unchanged) | macOS |
|---|---|---|
| Wheel over cards | `WH_GETMESSAGE` thread hook, swallows `WM_MOUSEWHEEL` | `NSEvent` local monitor (app windows only, main thread); returns nil for card-owned scroll events |
| Trackpad | n/a | Gesture latched at `Began`/`MayBegin`, including momentum, to the card or to Rhino (`ScrollGestureLatch`) |
| Scroll units | 120 per notch → 3 rows | `scrollingDeltaY`: wheel line → 1 row; precise (trackpad) points → rows by theme row height |
| Pointer leave / topmost window | `WindowFromPoint` + `IsChild`, hook sees `WM_MOUSELEAVE` | `windowNumberAtPoint` + `hitTest`/`isDescendantOf`, monitor moves/exits + 10 Hz watchdog while hovering |
| Pointer position | `Control.MousePosition`, `RhinoView.ScreenToClient` | `NSEvent.mouseLocation` → window → NSView, scaled to `MainViewport.Size`, corrected by the offset learnt from MouseCallback points (`PointerCalibration`) |
| Buttons / Escape | `GetAsyncKeyState` | `NSEvent.pressedMouseButtons`; Escape from the monitor's keyDown/keyUp |
| Modifiers | Rhino `CtrlKeyDown`, `Control.ModifierKeys` | `NSEvent.modifierFlags`: **Command** toggles; **Control-click is the secondary click** and never selects |
| Viewport scale | `GetDpiForWindow / 96` | viewport pixels ÷ NSView points (Retina factor if Rhino works in backing pixels), quarter-snapped, `[1, 4]` |
| Timers | `System.Windows.Forms.Timer` | Eto `UITimer` (NSTimer on the main run loop) |
| Clipboard | `DataObject`: PNG + opaque CF_DIB | `NSPasteboard`: `public.png` (transparent) + `public.tiff` (opaque, converted by NSImage) |
| Card font | Segoe UI | Helvetica Neue (then Helvetica, Arial), same family for measuring and painting |
| GH canvas density | Graphics transform | × window backing factor unless the transform already carries it (`CanvasScale.Ratio`) |
| Pinned canvas cards | layout × `DeviceDpi/96`, `DrawImageUnscaled` | layout in points (1), raster × backing, drawn into the point rectangle |
| Output folder | Documents\ConduitPlus | ~/Documents/ConduitPlus (`CONDUITPLUS_RESULTS` overrides both) |

Platform-neutral policy (modifier mapping, scroll units, gesture latching, font
fallback, scale snapping, calibration, canvas density) lives in
`src/ConduitPlus.Core/HostPlatform.cs` and is unit-tested on Windows
(`tests/ConduitPlus.Core.Tests/MacPlatformTests.cs`).

## Build and package

```powershell
dotnet build src/ConduitPlus.Grasshopper/ConduitPlus.Grasshopper.csproj -c Release -p:ConduitPlusPlatform=mac
dotnet build src/ConduitPlus.Rhino/ConduitPlus.Rhino.csproj -c Release -p:ConduitPlusPlatform=mac
./tools/Pack-Yak.ps1 -Platform mac            # or -Platform mac -AllowDirty for a local trial
```

The Mac flavour targets plain `net8.0` with a compile-time
`Microsoft.WindowsDesktop.App.WindowsForms` framework reference. Its bin/obj
live under `artifacts/mac/` so the two flavours never share intermediates.
`Pack-Yak.ps1 -Platform mac` still runs the Windows solution build and tests
first, then stages `artifacts/yak/stage-mac` and writes
`artifacts/yak/conduit-plus-VERSION-rh8_20-mac.yak` (plus `.sha256`/`.json`).
`Test-YakPackage.ps1 -Platform mac` checks the inventory, the manifest
(`platform: mac`) and that no host assembly is a `net8.0-windows` build. The Mac
package carries `packaging/README-mac.md` as its README. `Publish-Yak.ps1`
still publishes Windows candidates only, so a Mac build can't be pushed by accident.

## Parity

| Feature | Windows | macOS | Mac status |
|---|---|---|---|
| Yak install, RHP + GHA load | ✓ | `rh8_20-mac` package | unverified |
| Viewport cards (all kinds), themes, Auto theme | ✓ | same painter via Rhino's System.Drawing | unverified (renders on Windows; self-test renders every kind) |
| Retina sharpness | per-monitor DPI | viewport/NSView ratio | unverified, see risk 1 |
| Layout, slots, compact forms, hidden-cards chip/list | ✓ | same Core layout | Core logic unit-tested; on-Mac look unverified |
| Click select / Shift add / toggle | Ctrl | Command | unverified |
| Control-click | n/a | secondary click, passed to Rhino | unverified |
| Hover, dwell, model hover, tooltips | ✓ | MouseCallback + AppKit pointer, Eto timers | unverified |
| Wheel scroll over tables/bars, no viewport zoom | ✓ | local event monitor | unverified, see risk 2 |
| Trackpad scroll incl. momentum, no orbit/pan leak | n/a | gesture latch | unverified |
| Scrollbar drag, page click, sort headers, Escape cancel | ✓ | ✓ | unverified |
| Pointer leaving the view or app clears hover | ✓ | window hit test + watchdog | unverified |
| Linked geometry highlight / selection | ✓ | Rhino display pipeline (neutral) | unverified |
| Canvas Card (inline GH) incl. drag/scroll/select | ✓ | GH Mac + density ratio | unverified, see risk 3 |
| Canvas Overlay (pinned GH widgets) | ✓ | layout in points | unverified, see risk 3 |
| GH canvas wheel | always zooms GH | same | n/a (unchanged design) |
| Copy card as image | PNG + DIB | PNG + TIFF | unverified |
| ConduitPlusCapture PNG 1/2/4× | ✓ | RhinoCommon ViewCapture | unverified |
| ConduitPlusPdf (experimental) | ✓ | RhinoCommon FilePdf, Helvetica Neue | unverified |
| ConduitPlusBench (in-Rhino) | ✓ | same command | unverified |
| RhinoCore bench harness (tools/bench) | ✓ | Windows-only tooling | not ported (developer tool) |
| Component icons | ✓ | System.Drawing | unverified |

## Risks and what to do if they show up

1. **Viewport units.** If `ConduitPlusPlatformCheck` reports "Rhino viewport is
   in points", cards render at 1× and look soft on Retina. Fix: draw the
   `Unit × backing` raster with `DisplayPipeline.DrawSprite(bitmap, center, w, h)`
   at point size instead of `DrawBitmap`. That's a small, Mac-only change in
   `OverlaySession.Draw`. Confirm first with `CONDUITPLUS_MAC_VIEW_SCALE=2`
   (cards should double in size if the viewport is in points).
2. **Event monitor.** If the report shows the monitor not installed, or scroll
   events counted but `target=Rhino` over a table, wheel scrolling over cards
   falls back to Rhino zooming (the same state as a failed Windows hook).
   Pointer-mapping errors (the self-test's sampling section) are the usual
   cause. Send `report.txt`.
3. **Grasshopper canvas density.** Pinned and inline canvas cards assume GH Mac
   control coordinates are points. If they are the wrong size, set
   `CONDUITPLUS_MAC_CANVAS_RATIO=1` (or 2) to find the right factor, then change
   `WidgetService.Dpi` / `CanvasScale.Ratio` to match.
4. **WinForms shim gaps.** Context menus (`ContextMenuStrip`) and GH canvas
   events come from Rhino's Mac implementation. The viewport adapter doesn't
   depend on them (it uses AppKit/Eto); the GH adapter does, by necessity.

Diagnostics-only environment variables (set before launching Rhino:
`launchctl setenv NAME value`, then quit and reopen Rhino):

- `CONDUITPLUS_MAC_VIEW_SCALE`: force the viewport card scale (e.g. `1`, `2`).
- `CONDUITPLUS_MAC_CANVAS_RATIO`: force the GH canvas raster density.
- `CONDUITPLUS_RESULTS`: output folder for captures and self-test reports.

## On-Mac test checklist

Use a Retina Mac with Rhino 8.20+ (`SetDotNetRuntime` = .NET 8), a mouse with a
wheel **and** a trackpad. Ideally also an external non-Retina display.

**A. Install and self-test (10 min)**
1. Drop `conduit-plus-VERSION-rh8_20-mac.yak` onto Rhino, restart Rhino.
   ✓ No load errors; `ConduitPlusDemo` and `ConduitPlusPlatformCheck` autocomplete.
2. Open a new metre-unit file, maximise the Perspective view, run
   `ConduitPlusPlatformCheck` (defaults: Clipboard, Exports, PointerSampling, DemoCards).
   When prompted, move the pointer over the whole viewport (corners too), then press Enter.
3. Open `~/Documents/ConduitPlus/platform-check/<time>/report.txt`.
   ✓ No FAIL lines. Note *view handle class*, *viewport/bounds ratio*,
   *backingScaleFactor*, *DisplayPipeline.DpiScale*, *pointer mapping* error,
   *System.Drawing face*. Open `cards/*.png` and check every card kind and theme looks right.
4. Paste into Keynote or Preview (File → New from Clipboard). ✓ Sharp 2× card;
   Keynote keeps transparency.

**B. Viewport cards (15 min)** with the demo cards from step A
5. ✓ Cards are sharp (not blurry) and the same size as on Windows at the same
   logical size. Text isn't clipped; the axes icon isn't covered.
6. Hover bar rows and table rows. ✓ Hover highlight follows within a frame;
   tooltip appears after about ¼ s; the linked truss member highlights.
7. Move the pointer off the viewport, onto another viewport, onto the
   Grasshopper window and out of Rhino entirely. ✓ Hover and tooltip clear each time.
8. Click a bar row. ✓ Row and geometry select. **Shift-click** adds;
   **Command-click** toggles; click empty viewport space clears.
   **Control-click** on a card opens Rhino's context menu and does not select.
9. Click a truss member in the model. ✓ Its row selects and the table scrolls to it.

**C. Scrolling (15 min)** over the "Structural results" table and the bar card
10. Mouse wheel over the table. ✓ Table scrolls about one row per notch;
    the viewport does **not** zoom. Off the card, the wheel zooms as normal.
11. Trackpad two-finger swipe over the table, slow and fast (with momentum).
    ✓ Table scrolls smoothly and stops at its ends; the view never orbits/pans
    during or after the swipe, even when momentum continues past the end.
12. Start a swipe off the card and drift onto it. ✓ The view keeps navigating;
    the table doesn't take over mid-gesture.
13. Drag the table scrollbar thumb; press Escape mid-drag. ✓ Drag scrolls;
    Escape cancels. Click above/below the thumb pages.
14. Click a table header. ✓ Sort cycles.
15. Run `ConduitPlusPlatformCheck` again (PointerSampling off is fine).
    ✓ *monitor events* and *scroll consumed* counters are non-zero; the last scroll line names a card.

**D. Grasshopper (15 min)**
16. Open `examples/showcase.gh`. ✓ Components load with icons; viewport
    dashboard appears.
17. Canvas Cards: ✓ sharp at 100%/200% canvas zoom; drag-scroll the table, click
    rows (Shift/Command), hover tooltips. Mouse wheel/trackpad over a canvas card
    zooms the GH canvas (same as Windows).
18. Canvas Overlay pinned cards: ✓ correct size and position, not overlapping
    the GH navigation widgets; clicks select; right-click (or Control-click) →
    **Copy card as image** works.
19. Change sliders. ✓ Cards update without flicker; scroll positions persist.

**E. Exports (5 min)**
20. `ConduitPlusCapture` at 1, 2 and 4. ✓ PNGs in ~/Documents/ConduitPlus/captures
    have sharp cards, no hover state.
21. `ConduitPlusPdf`. ✓ PDF opens; cards are vector text in Helvetica Neue and line up with the viewport.

**F. Displays and appearance (optional)**
22. Drag Rhino between Retina and non-Retina displays. ✓ Cards re-render at the
    new density within a moment, same logical size.
23. Switch macOS to Dark mode with theme Auto. ✓ Cards follow Rhino's theme.

Send `report.txt` from steps 3 and 15, plus screenshots of anything that differs.
