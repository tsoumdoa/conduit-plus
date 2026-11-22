using ConduitPlus.Core;
using ConduitPlus.Platform;
using Rhino;
using Rhino.Commands;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public static partial class OverlayManager
{
    private sealed record MacScrollTarget(OverlaySession? Session, string? CardId, System.Drawing.Point Client);

    /// <summary>
    /// macOS counterpart of <see cref="WheelHook"/>: an AppKit local event monitor on the main thread, scoped to
    /// this app's windows. It sees scroll-wheel/trackpad events before any view does; over a scrollable card it
    /// scrolls and consumes the event (returns nil) so Rhino doesn't zoom or orbit. It also observes pointer moves,
    /// exits, releases and Escape, which RhinoCommon's MouseCallback doesn't report. Everything else passes through.
    /// Trackpad gestures (including momentum) are latched to the owner decided when the gesture began.
    /// </summary>
    internal static class MacInput
    {
        private static ObjC.Block? _block;
        private static IntPtr _monitor;
        private static readonly ScrollGestureLatch<MacScrollTarget> Latch = new();
        private static UiTimer? _watch;
        internal static bool Installed => _monitor != IntPtr.Zero;

        public static void Install()
        {
            if (_monitor != IntPtr.Zero) return;
            try
            {
                _block ??= new ObjC.Block(OnEvent);
                var mask = AppKit.Mask(AppKit.ScrollWheel, AppKit.MouseMoved, AppKit.LeftMouseDragged, AppKit.RightMouseDragged, AppKit.OtherMouseDragged,
                    AppKit.MouseExited, AppKit.LeftMouseUp, AppKit.KeyDown, AppKit.KeyUp);
                var monitor = ObjC.Send(ObjC.Class("NSEvent"), ObjC.Sel("addLocalMonitorForEventsMatchingMask:handler:"), mask, _block.Handle);
                if (monitor == IntPtr.Zero)
                {
                    MacDiagnostics.MonitorStatus = "failed (nil monitor)";
                    RhinoApp.WriteLine("Conduit+: scroll-wheel/trackpad scrolling over cards unavailable (event monitor failed).");
                    return;
                }
                _monitor = ObjC.Send(monitor, ObjC.Sel("retain"));
                MacDiagnostics.MonitorStatus = "installed";
            }
            catch (Exception e)
            {
                MacDiagnostics.MonitorStatus = "failed: " + e.Message;
                MacDiagnostics.Fault("event monitor install", e);
            }
        }

        public static void Uninstall()
        {
            _watch?.Stop();
            Latch.Reset(); MacEscape.Down = false;
            if (_monitor == IntPtr.Zero) return;
            try
            {
                ObjC.Send(ObjC.Class("NSEvent"), ObjC.Sel("removeMonitor:"), _monitor);
                ObjC.Send(_monitor, ObjC.Sel("release"));
            }
            catch (Exception e) { MacDiagnostics.Fault("event monitor remove", e); }
            _monitor = IntPtr.Zero;
            MacDiagnostics.MonitorStatus = "removed";
        }

        /// <summary>
        /// MouseCallback has no leave event and AppKit sends no move once the pointer leaves the app. While a pointer is
        /// tracked, poll its window at 10 Hz, like the Windows hook's WM_MOUSELEAVE observation.
        /// </summary>
        internal static void WatchPointer()
        {
            if (_watch is null)
            {
                _watch = new UiTimer(100);
                _watch.Tick += (_, _) =>
                {
                    try
                    {
                        if (!_started || !Mouse.HasPointer) { _watch.Stop(); return; }
                        ObserveInteractionPointer();
                    }
                    catch (Exception e) { MacDiagnostics.Fault("pointer watch", e); }
                };
            }
            _watch.Start();
        }

        private static IntPtr OnEvent(IntPtr block, IntPtr e)
        {
            try
            {
                MacDiagnostics.MonitorEvents++;
                var type = AppKit.EventType(e);
                if (type is AppKit.KeyDown or AppKit.KeyUp)
                {
                    if (AppKit.KeyCode(e) == AppKit.EscapeKeyCode) MacEscape.Down = type == AppKit.KeyDown;
                    if (_started && !Busy && Sessions.Count > 0 && MacEscape.Down) Mouse.CheckScrollGesture(MacView.CurrentScreen());
                    return e;
                }
                if (type == AppKit.ScrollWheel)
                {
                    if (!_started || Sessions.Count == 0) { Latch.Reset(); return e; }
                    return Scroll(e, Busy) ? IntPtr.Zero : e;
                }
                if (!_started || Busy || Sessions.Count == 0) return e;
                MacDiagnostics.MoveEvents++;
                var screen = MacView.CurrentScreen();
                Mouse.CheckScrollGesture(screen);
                Mouse.ObservePointerWindow(screen);
            }
            catch (Exception ex) { MacDiagnostics.Fault("event monitor", ex); }
            return e;
        }

        /// <param name="busy">A command/capture is preparing cards: new gestures go to Rhino, a claimed one is finished silently.</param>
        private static bool Scroll(IntPtr e, bool busy)
        {
            MacDiagnostics.ScrollEvents++;
            var screen = MacView.CurrentScreen();
            if (!busy) Mouse.CheckScrollGesture(screen);
            var phase = (ScrollPhase)AppKit.Phase(e); var momentum = (ScrollPhase)AppKit.MomentumPhase(e);
            var dy = AppKit.ScrollingDeltaY(e); var precise = AppKit.HasPreciseScrollingDeltas(e);
            var suspended = busy || Command.InCommand() || Mouse.Interacting;
            var classic = phase == ScrollPhase.None && momentum == ScrollPhase.None;
            // Classic wheel events stand alone, like WM_MOUSEWHEEL. Horizontal-only ones (tilt / Shift-wheel)
            // pass to Rhino, as WM_MOUSEHWHEEL does on Windows.
            if (classic && (suspended || dy == 0)) { Latch.Reset(); return false; }
            var window = AppKit.EventWindow(e); var location = AppKit.EventLocationInWindow(e);
            // While suspended, a gesture already claimed by a card keeps being consumed (without scrolling) until it
            // ends, so its tail never reaches Rhino as orbit/pan/zoom; any new gesture belongs to Rhino.
            var target = suspended
                ? Latch.Decide(phase, momentum, () => null)
                : Latch.Decide(phase, momentum, () => Hit(window, location, screen));
            MacDiagnostics.LastScroll = $"dy={dy:0.###} precise={precise} phase={phase} momentum={momentum} suspended={suspended} target={(target is null ? "Rhino" : target.CardId ?? "hidden-list")}";
            if (target is null) return false;
            if (!suspended && target.Session is { } session && target.CardId is { } card && !session.IsDisposed && session.Visible && session.Card(card) is not null)
            {
                var units = MacScroll.Units(dy, precise, session.Theme.RowHeight);
                if (units != 0) Mouse.QueueWheel(session, card, target.Client, units);
            }
            MacDiagnostics.ScrollSwallowed++;
            return true;
        }

        private static MacScrollTarget? Hit(IntPtr window, ObjC.CGPoint location, System.Drawing.Point screen)
        {
            if (window == IntPtr.Zero) return null;
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!s.Visible) continue;
                var ns = MacView.NsView(s.View);
                if (ns == IntPtr.Zero || AppKit.Window(ns) != window || !PointerInView(s.View, screen)) continue;
                if (MacView.FromWindow(s.View, ns, location) is not { } native) continue;
                var p = MacView.Calibration(s.View).Apply(native);
                var client = new System.Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
                // The pointer doesn't move while scrolling: prefer Rhino's own last point when it agrees.
                if (Mouse.PointerIn(s.View) is { } last && Math.Abs(last.X - client.X) <= 6 && Math.Abs(last.Y - client.Y) <= 6) client = last;
                if (HiddenHit(s.View, client.X, client.Y)) return new(null, null, client);
                if (s.Hit(client.X, client.Y) is not { } hit || s.Card(hit.CardId) is not { } card || !TableScroll.CanScroll(card)) continue;
                return new(s, card.Id, client);
            }
            return null;
        }
    }
}
