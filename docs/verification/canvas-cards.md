# Canvas Card PR1 verification

The implementation follows [the approved design](../design/canvas-cards.md).
This implementation worktree is not installed; the reviewing agent installs the
combined integration checkout after review.

## Pinned API inspection

Inspected the NuGet `Grasshopper/8.20.25147.11001-rc/lib/net7.0/Grasshopper.dll`
with local ILSpy, alongside its XML documentation. No online Rhino 9 API was used.

- `GH_Canvas.GenerateHiResImageTile` temporarily sets `DrawingMode` to
  `GH_CanvasMode.Export`, replaces the viewport, calls `ComputeProjection` and
  `ApplyProjection` on its bitmap Graphics, then renders the middle ground.
  The attributes use a synchronous temporary raster in every non-Control mode,
  bypassing their screen visibility/debounce/cache path.
- `GH_Viewport.ApplyProjection` installs the canvas projection in Graphics.
  World-to-device basis conversion therefore supplies the effective raster scale;
  multiplying it again by `Graphics.DpiX` or control DPI would double-count density
  where the transform already includes it. The source-linked GDI tests include
  a target with 192 DPI metadata and a pixel-space transform.
- `GH_Attributes<T>` exposes virtual mouse down/move/up and key down/up responses.
  A `Capture` response installs the component as `GH_Canvas.ActiveObject`; a
  `Release` response clears it. The implementation also owns native mouse capture
  for scrollbar drags and cancels on capture loss/Escape/data or layout changes.
- `SetupTooltip` receives a canvas point, whereas `GH_TooltipDisplayEventArgs.Region`
  is in control coordinates. Hit details are supplied through that infrastructure.
  `GH_Tooltip.Clear` hides and releases the active tooltip; no custom tooltip window
  or Rhino tooltip texture is created.
- `LayoutInputParams`, `LayoutBounds`, the Wires channel, GH capsule palettes/grips,
  and the normal base Overlay channel retain GH wiring and component chrome.
- The `GH_Canvas.Document` setter emits Unloaded/Loaded on tab switches and calls
  `NewSolution(expireAllObjects: false)` on return. Unload therefore releases all
  images/subscriptions/queue entries but preserves the managed snapshot and local
  state; returning can redraw without another solve. Close/delete clear that state.

All used GH symbols compile against the pinned package. These inspections verify
API presence and the export dispatch path, not the live interaction/display result.

## Automated checks

### Theme API and Auto mode

Inspected `RhinoCommon/8.20.25147.11001-rc/lib/net7.0/RhinoCommon.dll`
using `ilspycmd -t Rhino.Runtime.HostUtils` and `ilspycmd -t Rhino.RhinoApp`
(each followed by that DLL path), alongside its shipped XML documentation.
The pinned reference assembly exposes `public static bool RunningInDarkMode`
and `public static event EventHandler AppSettingsChanged`. The latter is the
general application-settings event; there is no dedicated public appearance event
on those types. This verifies API availability in 8.20, not a live GUI switch.
`AppearanceSettings.UsingDefaultDarkModeColors()` instead tests the default color
scheme, so Auto uses `HostUtils.RunningInDarkMode` in the Rhino host adapter.

Core resolves selection + mode to a palette without Rhino types. Overlay sessions
retain the selection separately from the resolved palette. Settings callbacks
marshal to the UI thread, re-resolve Auto and repaint sessions/Canvas Cards without
solving or modifying the saved selection. Subscriptions are removed on manager
shutdown and Canvas Card removal/unload. Overlay updates and Canvas Card repaint
also re-resolve, so a later publish/paint can recover a missed notification. Locked
or disabled Canvas Cards retain their existing last-frame behavior until enabled.
Both component read paths override the new Auto default with Paper when the saved
theme item is absent; Core tests cover this decision, explicit choices and Auto.

Pending live checks: toggle Rhino light/dark with Auto on both displays (including
a wired mixed-case `aUtO` Overlay input); confirm no GH solve, correct hover,
selection/threshold colors and tooltips, and unchanged fixed palettes. Undo/redo
theme choices, save/reopen Auto in the opposite mode, and open an older `.gh`
without a theme item to confirm Paper. Check capture/PDF and GH image export.

### Core and raster checks

Run `dotnet build ConduitPlus.sln -c Debug` and
`dotnet test ConduitPlus.sln -c Debug` from this checkout. Core tests load no Rhino
or GH assemblies; GDI checks source-link the existing raster adapter.

Tests cover canvas/local round trips, pan-invariant zoom/DPI device bases, the
four-pixel click threshold, actual ceiled raster allocations, uniform density
reduction, per-document sharing with replacement headroom, LRU protection of the
current frame, revisions/theme/size/density invalidation, independent owners,
source replacement, keyed selection, immutable input snapshots, pruning and
sort/scroll clamping. All six card kinds are rendered through the bounded plan.

## Pending live Rhino 8.20 checks

1. Connect each of Metric/Bar/Table/Line/Text/Legend to a separate Canvas Card.
   Verify measured bodies, normal wires/input grips, title dragging, grouping and
   GH selection. Empty the input, feed an invalid item, and feed two cards in
   separate branches: the previous image must disappear and only the latter two
   cases should report an error.
2. On a keyed card, click/release to select and Clear. Drag more than four screen
   pixels then return and release: no selection should commit. On keyed regions, plain
   click replaces, Shift adds, Ctrl removes, Ctrl+Shift toggles; repeated Replace/Add/Remove
   no-ops must not solve. Alt clicks and modified clicks on titles, inactive bodies,
   sort headers and scrollbars remain GH gestures, as do middle pan and wheel zoom. Two displays of the same source and an Overlay must stay independent.
3. On a long table, cycle sort, page by clicking the track, and drag its thumb.
   Escape, focus/capture loss, incoming data and document switching must cancel a
   drag and restore its original offset. Check hover details again after sort/page.
   Update values/reorder keyed rows, remove selected keys, shrink rows/columns and
   replace the source component; confirm preservation, clamping and reset behavior.
   Confirm interaction does not increment GH's solution count.
4. Toggle Hidden/Preview off; the canvas image remains. Lock/disable it; the last
   image remains with Disabled chrome and no interaction. Pause the solver and
   verify the last result remains. Choose themes, Undo/Redo, save/reopen the `.gh`:
   only the theme and GH pivot persist, not selection/sort/scroll. All five theme
   choices use the shared presets; test Auto using the light/dark checklist above.
5. Pan and continuously zoom, then stop: existing pixels scale during zoom and
   text becomes crisp after settling and queued repaints. Move Rhino between mixed
   DPI monitors and repeat. Test many visible cards and exceptionally tall Bar or
   Legend cards; density may reduce, but memory/bitmap dimensions must stay bounded.
6. Export GH images at several zooms and of offscreen regions while a card is
   hovered. Export must include current committed selection/sort/scroll, omit hover
   and match the export scale without modifying the screen cache. During an active
   scrollbar drag export uses its original, committed offset. Compare a normal
   canvas screenshot too. No vector-text claim is made.
7. Observe process GDI handles/private bytes while repeatedly zooming, replacing
   input, deleting/undoing components and closing/unloading documents. Retained
   bitmap memory must plateau; closing documents must release their frames and
   registrations. Interactive runtime and handle-plateau verification is pending.

## Scope and integration

The theme resolver uses the shared Core presets, with Rhino mode detection in host
code. Cache pressure shares raster capacity across visible cards
with room for one atomic replacement, then evicts least-recently-visible frames
as needed. The existing GDI measurer now caches managed measurement values rather
than retaining an undisposable thread-static Graphics/bitmap/font cache.

No boards, resizing, wheel interception, cross-host linking or outputs are added.
Existing component/plugin GUIDs and input orders are unchanged.
