using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.Commands;
using Rhino.UI;
using System.Runtime.InteropServices;

namespace ConduitPlus.Rhino;

public static partial class OverlayManager
{
    private sealed class OverlayMouse : MouseCallback
    {
        private bool _pressed;
        public bool Pressed => _pressed;
        private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 34 };
        private (RhinoView View, System.Drawing.Point Point)? _pending;
        private long _lastPick;
        private abstract record Gesture(OverlaySession Session);
        private sealed record PendingTitle(OverlaySession Session, string CardId, Point2 Press, Size2 Size, double Dpi, bool Draggable) : Gesture(Session);
        private sealed record CardDrag(OverlaySession Session) : Gesture(Session);
        private sealed record ScrollDrag(OverlaySession Session, string CardId, double Grab) : Gesture(Session);
        private Gesture? _gesture; // null = idle; swallowed-up bookkeeping is independent of cancellation
        internal bool BlockWheel(System.Drawing.Point screen)
        {
            var block = _gesture is CardDrag;
            if (_gesture is { } gesture && (!gesture.Session.View.ScreenRectangle.Contains(screen) || !gesture.Session.LayoutEnvironmentValid() ||
                Command.InCommand() || (GetAsyncKeyState(0x01) & 0x8000) == 0 || (GetAsyncKeyState(0x1B) & 0x8000) != 0)) Cancel();
            return block;
        }
        public OverlayMouse() => _hoverTimer.Tick += (_, _) => FlushHover();
        public void Reset() { _hoverTimer.Stop(); _pending = null; Cancel(); _pressed = false; }
        private void Cancel()
        {
            var previous = _gesture; _gesture = null;
            previous?.Session.EndLayoutDrag();
        }
        public void Forget(OverlaySession session)
        {
            _hoverTimer.Stop(); _pending = null;
            if (_gesture?.Session == session) Cancel();
            session.TitleHover(null);
        }
        private bool Valid(MouseCallbackEventArgs e)
        {
            if (_gesture is not { } gesture) return true;
            var s = gesture.Session; var size = s.ViewSize;
            var valid = !Busy && !Command.InCommand() && s.Visible &&
                s.Document.RuntimeSerialNumber == e.View.Document.RuntimeSerialNumber && s.View.MainViewport.Id == e.View.MainViewport.Id &&
                new Rect2(0, 0, size.Width, size.Height).Contains(e.ViewportPoint.X, e.ViewportPoint.Y) && s.LayoutEnvironmentValid();
            if (gesture is PendingTitle p) valid &= p.Size == size && p.Dpi == UiContext.Dpi(s.View);
            if (!valid) Cancel();
            return valid;
        }
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.Visible &&
            s.Document.RuntimeSerialNumber == view.Document.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        private void FlushHover()
        {
            _hoverTimer.Stop();
            var pending = _pending; _pending = null;
            if (_gesture is not null || _pressed || pending is not { } p) return;
            _lastPick = System.Diagnostics.Stopwatch.GetTimestamp();
            var sessions = InView(p.View);
            var hit = !Busy && !Command.InCommand() && !sessions.Any(s => s.Hit(p.Point.X, p.Point.Y) is not null)
                ? ModelPicking.Pick(p.View, sessions, p.Point) : null;
            foreach (var session in Sessions.Values.ToArray())
            {
                if (hit is { } picked && session == picked.Session) Hover(session, picked.Hit.Key, true);
                else ClearHover(session, true);
            }
        }
        protected override void OnMouseMove(MouseCallbackEventArgs e)
        {
            if (_gesture is not null)
            {
                e.Cancel = true;
                if (!Valid(e)) return;
                // MouseCallback has no capture/key event. Observe release outside or Escape on the next move.
                if ((GetAsyncKeyState(0x01) & 0x8000) == 0 || (GetAsyncKeyState(0x1B) & 0x8000) != 0) { Cancel(); return; }
            }
            if (Busy) { _hoverTimer.Stop(); _pending = null; return; }
            if (_gesture is PendingTitle title)
            {
                var point = new Point2(e.ViewportPoint.X, e.ViewportPoint.Y);
                if (title.Draggable && Math.Sqrt(Math.Pow(point.X - title.Press.X, 2) + Math.Pow(point.Y - title.Press.Y, 2)) > 4 * title.Dpi)
                {
                    try
                    {
                        if (title.Session.BeginLayoutDrag(title.CardId, title.Press))
                        { _gesture = new CardDrag(title.Session); title.Session.MoveLayoutDrag(point); }
                    }
                    catch (Exception ex) { Cancel(); RhinoApp.WriteLine($"Conduit+ drag: {ex.Message}"); }
                }
                return;
            }
            if (_gesture is CardDrag cardDrag) { cardDrag.Session.MoveLayoutDrag(new(e.ViewportPoint.X, e.ViewportPoint.Y)); return; }
            if (_gesture is ScrollDrag drag)
            {
                e.Cancel = true;
                var local = drag.Session.Local(drag.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                if (local is { } p && drag.Session.Card(drag.CardId) is { } card)
                    drag.Session.ScrollTo(drag.CardId, TableScroll.OffsetForThumbTop(card, drag.Session.Theme, p.Y - drag.Grab));
                return;
            }
            var sessions = InView(e.View);
            var overCard = sessions.Any(s => s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y) is not null);
            foreach (var s in Sessions.Values.ToArray())
            {
                var hit = sessions.Contains(s) ? s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y) : null;
                s.TitleHover(hit?.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                if (hit is { } card) s.SetHover(card.CardId, card.Key);
                else ClearHover(s, false);
            }
            if (overCard || Command.InCommand())
            {
                _hoverTimer.Stop(); _pending = null;
                foreach (var s in Sessions.Values.ToArray()) ClearHover(s, true);
                return;
            }
            _pending = (e.View, e.ViewportPoint);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(_lastPick).TotalMilliseconds >= 34) FlushHover();
            else if (!_hoverTimer.Enabled) _hoverTimer.Start(); // keep the final cursor position even if movement stops
        }
        protected override void OnMouseDown(MouseCallbackEventArgs e)
        {
            if (_gesture is not null) { e.Cancel = true; Cancel(); return; } // a new down also ends a gesture whose outside release was missed
            if (Busy || e.Button != System.Windows.Forms.MouseButtons.Left) return;
            var sessions = InView(e.View);
            var hits = sessions.Select(s => (Session: s, Hit: s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y))).Where(h => h.Hit.HasValue).ToArray();
            foreach (var s in sessions)
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                if (hit is null) continue;
                e.Cancel = true; _pressed = true;
                _hoverTimer.Stop(); _pending = null;
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
                if (System.Windows.Forms.Control.ModifierKeys == System.Windows.Forms.Keys.None && !Command.InCommand() && s.TitleAt(hit.Value.CardId, e.ViewportPoint.X, e.ViewportPoint.Y))
                {
                    var locked = s.Card(hit.Value.CardId)!.WiredSlot.HasValue;
                    _gesture = new PendingTitle(s, hit.Value.CardId, new(e.ViewportPoint.X, e.ViewportPoint.Y), s.ViewSize, UiContext.Dpi(s.View), !locked && hits.Length == 1);
                    if (hits.Length > 1) return; // ambiguous owner; retain existing data/scroll behavior outside title gestures
                    if (locked) RhinoApp.WriteLine("Conduit+: slot is set in Grasshopper");
                    return;
                }
                if (!string.IsNullOrEmpty(hit.Value.Key))
                {
                    s.Select(hit.Value.CardId, hit.Value.Key);
                }
                return;
            }
            if (Command.InCommand()) return;
            // Clicks always re-pick; the throttled hover may still describe a previous cursor position.
            if (ModelPicking.Pick(e.View, InView(e.View), e.ViewportPoint) is not { } picked) return;
            e.Cancel = true; _pressed = true;
            _hoverTimer.Stop(); _pending = null;
            foreach (var s in Sessions.Values.ToArray())
                if (s == picked.Session) Hover(s, picked.Hit.Key, true); else ClearHover(s, true);
            picked.Session.Select("model", picked.Hit.Key);
        }
        protected override void OnMouseUp(MouseCallbackEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
            try
            {
                if (Valid(e) && _gesture is CardDrag drag)
                {
                    drag.Session.MoveLayoutDrag(new(e.ViewportPoint.X, e.ViewportPoint.Y));
                    drag.Session.DropLayoutDrag();
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ drop: {ex.Message}"); }
            finally { Cancel(); if (_pressed) { e.Cancel = true; _pressed = false; } }
            Enabled = Sessions.Count > 0;
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    }

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
                if (code >= 0 && wParam == PmRemove && !Busy && Sessions.Count > 0 && Marshal.ReadInt32(lParam, IntPtr.Size) == WmMouseWheel)
                {
                    var msgW = Marshal.ReadIntPtr(lParam, IntPtr.Size * 2).ToInt64();
                    var msgL = Marshal.ReadIntPtr(lParam, IntPtr.Size * 3).ToInt64();
                    var delta = (short)((msgW >> 16) & 0xFFFF);
                    var screen = new System.Drawing.Point((short)(msgL & 0xFFFF), (short)((msgL >> 16) & 0xFFFF));
                    if (TryScroll(screen, delta)) Marshal.WriteInt32(lParam, IntPtr.Size, WmNull); // swallow: no Rhino zoom
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ wheel: {ex.Message}"); }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
        private static bool TryScroll(System.Drawing.Point screen, short delta)
        {
            if (Mouse.BlockWheel(screen)) return true;
            foreach (var s in Sessions.Values.ToArray())
            {
                if (!s.Visible) continue;
                if (!s.View.ScreenRectangle.Contains(screen)) continue;
                var client = s.View.ScreenToClient(screen);
                if (s.Hit(client.X, client.Y) is not { } hit || s.Card(hit.CardId) is not { } card || !TableScroll.CanScroll(card)) continue;
                var notches = Math.Max(1, Math.Abs(delta) / 120);
                s.ScrollBy(card.Id, -Math.Sign(delta) * 3 * notches);
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
