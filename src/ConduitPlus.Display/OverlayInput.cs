using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.Commands;
using Rhino.UI;
using System.Runtime.InteropServices;
using ConduitPlus.Platform;

namespace ConduitPlus.Rhino;

public static partial class OverlayManager
{
    private sealed class OverlayMouse : MouseCallback
    {
        private bool _pressed;
        private bool _buttonDown;
        private (RhinoView View, System.Drawing.Point Point)? _emptyPress;
        public bool Pressed => _pressed;
        public bool Interacting => _pressed || _buttonDown || _gesture is not null;
        internal bool HoverUnsettled => _modelDwell.Pending || _scrollRest.Pending;
        private readonly HoverDwell _modelDwell = new();
        private readonly ScrollHoverRest _scrollRest = new();
        // macOS deltas are finer (trackpad points): one accumulator step per row instead of three rows per notch.
        private readonly WheelAccumulator<(OverlaySession Session, string Card)> _wheel = HostInput.IsMac ? new(MacScroll.UnitsPerRow, 1) : new();
        private (RhinoView View, System.Drawing.Point Point)? _pointer;
        private bool _movePending, _clearScrollHover, _exitPending;
        // Accepted wheel notches outlive hover cancellation (e.g. a Busy flush), so they keep the pump awake too.
        internal bool Pending => _exitPending || _movePending || _modelDwell.Pending || _scrollRest.Pending || _wheel.HasWhole;
        private abstract record Gesture(OverlaySession Session);
        private sealed record ScrollDrag(OverlaySession Session, string CardId, double Grab) : Gesture(Session);
        private Gesture? _gesture; // null = idle; swallowed-up bookkeeping is independent of cancellation
        internal void CheckScrollGesture(System.Drawing.Point screen)
        {
            if (_gesture is { } gesture && (!PointerInView(gesture.Session.View, screen) ||
                Command.InCommand() || !HostInput.LeftButtonDown || HostInput.EscapeDown)) Cancel();
        }
        public void Reset() { CancelPending(); _wheel.Clear(); Cancel(); _pressed = false; _buttonDown = false; }
        internal void CancelPending(bool closeLabels = true)
        {
            if (closeLabels) CloseLabelHover();
            _modelDwell.Cancel(); _scrollRest.Cancel(); // accepted wheel input survives: its message was already swallowed
            _pointer = null; _movePending = false; _clearScrollHover = false; _exitPending = false;
        }
        private OverlaySession? _labelHoverOwner;
        internal void CloseLabelHover()
        {
            var owner = _labelHoverOwner;
            _labelHoverOwner = null;
            if (owner is { IsDisposed: false }) SetLabelHover(owner, null);
        }
        private void Cancel()
        {
            _emptyPress = null;
            var previous = _gesture; _gesture = null;
            if (previous is not null) CancelBrushHover(previous.Session);
        }
        public void Forget(OverlaySession session)
        {
            if (_labelHoverOwner == session) CloseLabelHover();
            if (_pointer is { } pointer && SameView(pointer.View, session.View)) { _modelDwell.Cancel(); _scrollRest.Cancel(); _pointer = null; _movePending = false; }
            if (_emptyPress is { } press && SameView(press.View, session.View)) _emptyPress = null;
            if (_gesture?.Session == session) Cancel();
        }
        private bool Valid(MouseCallbackEventArgs e)
        {
            if (_gesture is not { } gesture) return true;
            var s = gesture.Session; var size = s.ViewSize;
            var valid = !Busy && !Command.InCommand() && s.Visible &&
                s.Document.RuntimeSerialNumber == e.View.Document.RuntimeSerialNumber && s.View.MainViewport.Id == e.View.MainViewport.Id &&
                new Rect2(0, 0, size.Width, size.Height).Contains(e.ViewportPoint.X, e.ViewportPoint.Y);
            if (!valid) Cancel();
            return valid;
        }
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.Visible &&
            s.Document.RuntimeSerialNumber == view.Document.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        private static bool SameView(RhinoView a, RhinoView b) => a.RuntimeSerialNumber == b.RuntimeSerialNumber;
        private static void ClearPointerHover()
        {
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!_started) return;
                if (s.IsDisposed) continue;
                s.Cursor(default, false); s.ClearCardHover(); ClearHover(s, false); ClearHover(s, true);
            }
        }
        internal void RecheckLabelPointer(RhinoView view)
        {
            if (_labelHoverOwner is null || _pointer is not { } pointer || !SameView(view, pointer.View)) return;
            _movePending = true;
            InteractionPump.Wake();
        }

        internal bool HasPointer => _pointer is not null;
        internal System.Drawing.Point? PointerIn(RhinoView view) => _pointer is { } p && SameView(p.View, view) ? p.Point : null;
        internal void ObservePointerWindow(System.Drawing.Point screen)
        {
            if (_pointer is not { } p || PointerInView(p.View, screen)) return;
            CancelPending();
            _exitPending = true;
            InteractionPump.Wake();
        }
        internal void QueueWheel(OverlaySession session, string card, System.Drawing.Point point, int delta)
        {
            _wheel.Add((session, card), delta);
            _pointer = (session.View, point);
            _modelDwell.Cancel();
            if (!_scrollRest.Pending) _clearScrollHover = true;
            _scrollRest.Defer(Environment.TickCount64);
            _movePending = true;
            InteractionPump.Wake();
        }
        internal void ApplyPending(long now, bool syntheticPointer = false)
        {
            if (_exitPending)
            {
                _exitPending = false;
                ClearPointerHover();
            }
            // Accepted deltas apply to surviving cards; only removed/hidden sessions drop theirs.
            _wheel.RemoveWhere(key => key.Session.IsDisposed || !key.Session.Visible);
            var wheel = _wheel.Take();
            foreach (var (target, rows) in wheel)
                if (!target.Session.IsDisposed && target.Session.Visible) target.Session.ScrollBy(target.Card, rows);
            if (_pointer is not { } p) return;
            var screen = HostView.PointerScreen;
            if (!syntheticPointer && !PointerInView(p.View, screen)) { CancelPending(); ClearPointerHover(); return; }
            if (Interacting || Command.InCommand())
            {
                CancelPending();
                ClearPointerHover();
                return;
            }
            // Revalidate the actual cursor, even if Rhino didn't deliver its latest move (e.g. a window opened).
            // macOS: if AppKit can't map the pointer, keep Rhino's own MouseCallback point.
            var actual = syntheticPointer ? p.Point : HostView.ToClient(p.View, screen) ?? p.Point;
            if (actual != p.Point)
            {
                _pointer = p = (p.View, actual); _movePending = true;
                _modelDwell.Move(new(actual.X, actual.Y), now);
            }
            // Scroll offsets are presented after this input phase. Re-hit only in a subsequent frame,
            // even when a blocked UI thread delivers the first wheel frame after the rest deadline.
            var scrollRested = _scrollRest.Take(now, wheel.Length > 0);
            if (!_movePending && !scrollRested && !_modelDwell.Pending) return;
            _movePending = false;
            var sessions = InView(p.View);
            var hidden = HiddenHit(p.View, p.Point.X, p.Point.Y) ||
                sessions.Any(s => s.LabelTooltipBounds is { } bounds && bounds.Contains(p.Point.X, p.Point.Y));
            var hits = sessions.ToDictionary(s => s, s => hidden ? null : s.Hit(p.Point.X, p.Point.Y));
            var overCard = hits.Values.Any(h => h.HasValue);
            var label = overCard || hidden || _scrollRest.Pending || HostInput.EscapeDown
                ? null : OverlaySession.PickLabel(p.View, p.Point, retain: true);
            if (overCard || hidden || label.HasValue) _modelDwell.Cancel();
            if (_labelHoverOwner != label?.Owner) CloseLabelHover();
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!_started) return;
                if (s.IsDisposed) continue;
                s.Cursor(new(p.Point.X, p.Point.Y), sessions.Contains(s));
                if (_scrollRest.Pending)
                {
                    if (_clearScrollHover) { s.ClearCardHover(); ClearHover(s, false); ClearHover(s, true); s.HideTooltip(); }
                    continue;
                }
                if (hits.TryGetValue(s, out var hit) && hit is { } card) s.SetHover(card.CardId, card.Key);
                else { s.ClearCardHover(); ClearHover(s, false); }
                if (overCard || hidden || label.HasValue) ClearHover(s, true);
            }
            _clearScrollHover = false;
            if (_scrollRest.Pending) { _modelDwell.Cancel(); return; }
            if (label is { } labelHit && !labelHit.Owner.IsDisposed)
            {
                _labelHoverOwner = labelHit.Owner;
                SetLabelHover(labelHit.Owner, labelHit);
                return;
            }
            if (overCard || hidden) { _modelDwell.Cancel(); return; }
            if (scrollRested) _modelDwell.Move(new(p.Point.X, p.Point.Y), now);
            if (!_modelDwell.Take(now)) return;
            var picked = sessions.Any(s => s.HasLinks) ? ModelPicking.Pick(p.View, sessions, p.Point) : null;
            foreach (var session in Sessions.Values.ToArray())
            {
                if (!_started) return;
                if (session.IsDisposed) continue;
                if (picked is { } hit && session == hit.Session) Hover(session, hit.Hit.Key, true);
                else ClearHover(session, true);
            }
        }
        protected override void OnMouseMove(MouseCallbackEventArgs e)
        {
            if (HostInput.IsMac) MacView.Observe(e.View, e.ViewportPoint);
            if (_emptyPress is { } press && (!SameView(press.View, e.View) || !CanvasCardGeometry.WithinClick(new(press.Point.X, press.Point.Y), new(e.ViewportPoint.X, e.ViewportPoint.Y)))) _emptyPress = null;
            if (_gesture is not null)
            {
                e.Cancel = true;
                if (!Valid(e)) return;
                // MouseCallback has no capture/key event. Observe release outside or Escape on the next move.
                if (!HostInput.LeftButtonDown || HostInput.EscapeDown) { Cancel(); return; }
            }
            if (Busy) { CancelPending(); return; }
            if (_gesture is ScrollDrag drag)
            {
                e.Cancel = true;
                var local = drag.Session.Local(drag.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                if (local is { } p && drag.Session.Card(drag.CardId) is { } card)
                    drag.Session.ScrollTo(drag.CardId, TableScroll.OffsetForThumbTop(card, drag.Session.Theme, p.Y - drag.Grab));
                // ScrollTo queues one coalesced update; the pump presents the latest thumb position per frame.
                return;
            }
            if (_buttonDown)
            {
                // Native Rhino gestures aren't swallowed, but must not arm a model hover either.
                _buttonDown = HostInput.AnyButtonDown;
                if (_buttonDown) { CancelPending(); return; }
            }
            MoveHover(e.View, e.ViewportPoint);
        }
        internal TimeSpan MoveForChecks(RhinoView view, System.Drawing.Point point)
        {
            MoveHover(view, point);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            ApplyPending(Environment.TickCount64, syntheticPointer: true);
            _pointer = null;
            return System.Diagnostics.Stopwatch.GetElapsedTime(started);
        }

        private void MoveHover(RhinoView view, System.Drawing.Point point)
        {
            if (_pointer is not { } previous || !SameView(previous.View, view)) _modelDwell.Cancel();
            _exitPending = false;
            _pointer = (view, point); _movePending = true;
            _modelDwell.Move(new(point.X, point.Y), Environment.TickCount64);
            InteractionPump.Wake();
            if (HostInput.IsMac) MacInput.WatchPointer();
        }
        private static bool PrimaryButton(MouseCallbackEventArgs e) =>
            HostInput.IsMac ? e.MouseButton == MouseButton.Left : e.Button == System.Windows.Forms.MouseButtons.Left;
        /// <summary>Windows: Rhino's button and Ctrl/Shift. macOS: Command toggles; Control-click is a secondary click.</summary>
        private static PointerIntent Intent(MouseCallbackEventArgs e)
        {
            if (!HostInput.IsMac) return PointerModifiers.Resolve(false, e.Button == System.Windows.Forms.MouseButtons.Left, e.ShiftKeyDown, e.CtrlKeyDown);
            var (shift, command, control) = HostInput.MacModifiers();
            return PointerModifiers.Resolve(true, e.MouseButton == MouseButton.Left, shift || e.ShiftKeyDown, e.CtrlKeyDown, command, control);
        }
        protected override void OnMouseDown(MouseCallbackEventArgs e)
        {
            // A click must hit the rows after all preceding wheel messages, and no deferred hover
            // may run after its selection/sort. Flush presentation again before returning to Rhino.
            InteractionPump.Flush();
            CancelPending(closeLabels: false);
            _buttonDown = true;
            foreach (var s in Sessions.Values.ToArray()) ClearHover(s, true);
            try { MouseDown(e); }
            finally { CloseLabelHover(); InteractionPump.Flush(); }
        }
        private void MouseDown(MouseCallbackEventArgs e)
        {
            if (HiddenHit(e.View,e.ViewportPoint.X,e.ViewportPoint.Y,true)) { e.Cancel=true; _pressed=true; _emptyPress=null; return; }
            _emptyPress = null;
            var tooltipHit = InView(e.View).Any(s => s.LabelTooltipBounds is { } bounds &&
                bounds.Contains(e.ViewportPoint.X, e.ViewportPoint.Y));
            foreach (var s in Sessions.Values.ToArray()) s.HideTooltip();
            if (_gesture is not null) { e.Cancel = true; Cancel(); return; } // a new down also ends a gesture whose outside release was missed
            var intent = Intent(e);
            if (Busy || !intent.Primary)
            {
                CancelPending();
                foreach (var session in InView(e.View)) CancelBrushHover(session);
                return;
            }
            if (tooltipHit) { e.Cancel = true; _pressed = true; return; }
            var mode = BrushSelection.FromModifiers(intent.Shift, intent.Toggle);
            var sessions = InView(e.View);
            foreach (var s in sessions)
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                if (hit is null) continue;
                e.Cancel = true; _pressed = true;
                CancelPending();
                if (TableSort.Column(hit.Value.Key) is { } column)
                {
                    s.Sort(hit.Value.CardId, column);
                    return;
                }
                if (hit.Value.Key == OverlaySession.ScrollbarKey)
                {
                    var card = s.Card(hit.Value.CardId); var local = s.Local(hit.Value.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                    if (card is not null && local is { } p && TableScroll.Bar(card, s.Theme) is { } bar)
                    {
                        if (p.Y >= bar.Thumb.Y && p.Y <= bar.Thumb.Y + bar.Thumb.Height) _gesture = new ScrollDrag(s, card.Id, p.Y - bar.Thumb.Y);
                        else s.ScrollBy(card.Id, p.Y < bar.Thumb.Y ? -TableScroll.Visible(card) : TableScroll.Visible(card)); // page
                    }
                    return;
                }
                if (!string.IsNullOrEmpty(hit.Value.Key))
                {
                    s.Select(hit.Value.CardId, hit.Value.Key, mode);
                }
                return;
            }
            if (TrySelectLabel(e.View, e.ViewportPoint, mode))
            {
                e.Cancel = true; _pressed = true;
                return;
            }
            if (Command.InCommand()) return;
            // Clicks always re-pick; dwell hover may still describe a previous cursor position.
            if (ModelPicking.Pick(e.View, sessions, e.ViewportPoint) is not { } picked)
            {
                if (!intent.Shift && !intent.Toggle) _emptyPress = (e.View, e.ViewportPoint);
                return;
            }
            e.Cancel = true; _pressed = true;
            CancelPending();
            foreach (var s in Sessions.Values.ToArray())
                if (s == picked.Session) Hover(s, picked.Hit.Key, true); else ClearHover(s, true);
            picked.Session.Select("model", picked.Hit.Key, mode);
        }
        protected override void OnMouseUp(MouseCallbackEventArgs e)
        {
            _buttonDown = false;
            try { MouseUp(e); }
            finally { InteractionPump.Flush(); }
        }
        private void MouseUp(MouseCallbackEventArgs e)
        {
            if (HiddenHit(e.View,e.ViewportPoint.X,e.ViewportPoint.Y)) { e.Cancel=true; _pressed=false; Cancel(); return; }
            // Release ownership follows the button alone: a modifier pressed mid-gesture (macOS Control) must not
            // leave a captured press unfinished.
            if (!PrimaryButton(e)) return;
            var intent = Intent(e);
            var empty = _emptyPress; _emptyPress = null;
            if (empty is { } press && !Busy && !Command.InCommand() && !intent.Shift && !intent.Toggle &&
                SameView(press.View, e.View) && CanvasCardGeometry.WithinClick(new(press.Point.X, press.Point.Y), new(e.ViewportPoint.X, e.ViewportPoint.Y)))
            {
                var sessions = InView(e.View);
                if (!sessions.Any(s => s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y) is not null) &&
                    OverlaySession.PickLabel(e.View, e.ViewportPoint) is not { Anchor.Style.Key: not null } &&
                    ModelPicking.Pick(e.View, sessions, e.ViewportPoint) is null)
                    foreach (var session in sessions) session.Select("model", CardMeasure.ClearKey);
            }
            Cancel(); if (_pressed) { e.Cancel = true; _pressed = false; }
            if (!Busy) MoveHover(e.View, e.ViewportPoint);
            Enabled = Sessions.Count > 0;
        }
    }

    private static bool TrySelectLabel(RhinoView view, System.Drawing.Point point, BrushSelectMode mode)
    {
        if (OverlaySession.PickLabel(view, point) is not { Anchor.Style.Key: { } key } label) return false;
        Select(label.Owner, "label", key, mode, label: true);
        return true;
    }

    internal static void RecheckLabelPointer(RhinoView view) => Mouse.RecheckLabelPointer(view);

    internal static TimeSpan MovePointerForChecks(RhinoView view, System.Drawing.Point point)
    {
        var elapsed = Mouse.MoveForChecks(view, point);
        InteractionPump.Flush();
        return elapsed;
    }

    internal static bool ClickLabelForChecks(RhinoView view, System.Drawing.Point point) =>
        TrySelectLabel(view, point, BrushSelectMode.Replace);

    /// <summary>Which wheel/pointer route is active (diagnostics).</summary>
    internal static string InputRoute => HostInput.IsMac ? $"AppKit local event monitor: {MacDiagnostics.MonitorStatus ?? "not installed"}"
        : WheelHook.Installed ? "Win32 WH_GETMESSAGE hook installed" : "Win32 WH_GETMESSAGE hook not installed";
    internal static void ObserveInteractionPointer() => Mouse.ObservePointerWindow(HostView.PointerScreen);

    // RhinoCommon 8.20 documents Handle as the window this view is bound to. Rectangle tests alone
    // also match editor/other windows floating over the view. Test both routing and the topmost window.
    internal static bool PointerInView(RhinoView view, System.Drawing.Point screen, IntPtr messageWindow = default)
    {
        if (HostInput.IsMac) return MacView.InView(view, screen);
        var handle = view.Handle;
        return handle != IntPtr.Zero && view.ScreenRectangle.Contains(screen) &&
            BelongsToView(handle, WindowFromPoint(screen)) &&
            (messageWindow == IntPtr.Zero || BelongsToView(handle, messageWindow));
    }
    private static bool BelongsToView(IntPtr view, IntPtr window) => window != IntPtr.Zero && (window == view || IsChild(view, window));
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(IntPtr parent, IntPtr window);

    /// <summary>
    /// RhinoCommon's MouseCallback has no wheel event. A thread-local WH_GETMESSAGE hook on Rhino's UI thread sees
    /// WM_MOUSEWHEEL before any window does; over a scrollable table it scrolls and swallows the message so Rhino
    /// doesn't zoom. Everything else passes through untouched.
    /// </summary>
    private static class WheelHook
    {
        private const int WhGetMessage = 3, PmRemove = 1, WmMouseWheel = 0x020A, WmNull = 0;
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        private static readonly HookProc Proc = OnMessage; // rooted for the hook's lifetime
        private static IntPtr _hook;
        internal static bool Installed => _hook != IntPtr.Zero;
        public static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WhGetMessage, Proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero) RhinoApp.WriteLine("Conduit+: mouse-wheel scrolling unavailable (hook failed).");
        }
        public static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero;
        }
        private static IntPtr OnMessage(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && wParam == PmRemove && !Busy && Sessions.Count > 0)
                {
                    var message = Marshal.ReadInt32(lParam, IntPtr.Size);
                    // MouseCallback has no leave event. Observe queued client/non-client moves/leaves
                    // too, including those routed to the floating GH editor on Rhino's UI thread.
                    if (message is 0x0200 or 0x00A0 or 0x02A3 or 0x02A2)
                    {
                        Mouse.CheckScrollGesture(System.Windows.Forms.Control.MousePosition);
                        Mouse.ObservePointerWindow(System.Windows.Forms.Control.MousePosition);
                    }
                    if (message != WmMouseWheel) return CallNextHookEx(_hook, code, wParam, lParam);
                    var msgW = Marshal.ReadIntPtr(lParam, IntPtr.Size * 2).ToInt64();
                    var msgL = Marshal.ReadIntPtr(lParam, IntPtr.Size * 3).ToInt64();
                    var delta = (short)((msgW >> 16) & 0xFFFF);
                    var screen = new System.Drawing.Point((short)(msgL & 0xFFFF), (short)((msgL >> 16) & 0xFFFF));
                    if (TryScroll(screen, delta, Marshal.ReadIntPtr(lParam))) Marshal.WriteInt32(lParam, IntPtr.Size, WmNull); // swallow: no Rhino zoom
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ wheel: {ex.Message}"); }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
        private static bool TryScroll(System.Drawing.Point screen, short delta, IntPtr messageWindow)
        {
            Mouse.CheckScrollGesture(screen);
            if (Command.InCommand() || Mouse.Interacting || delta == 0) return false;
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!s.Visible) continue;
                if (!PointerInView(s.View, screen, messageWindow)) continue;
                var client = s.View.ScreenToClient(screen);
                if (HiddenHit(s.View,client.X,client.Y)) return true;
                if (s.Hit(client.X, client.Y) is not { } hit || s.Card(hit.CardId) is not { } card || !TableScroll.CanScroll(card)) continue;
                Mouse.QueueWheel(s, card.Id, client, delta);
                return true;
            }
            return false;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
