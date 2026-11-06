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
        // Active scrollbar-thumb drag: session, card, and where on the thumb it was grabbed (card-local CSS px).
        private (OverlaySession Session, string CardId, double Grab)? _drag;
        public OverlayMouse() => _hoverTimer.Tick += (_, _) => FlushHover();
        public void Reset() { _hoverTimer.Stop(); _pending = null; _drag = null; _pressed = false; }
        public void Forget(OverlaySession session)
        {
            _hoverTimer.Stop(); _pending = null;
            if (_drag is { } drag && drag.Session == session) _drag = null;
        }
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.Visible &&
            s.Document.RuntimeSerialNumber == view.Document.RuntimeSerialNumber && s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        private void FlushHover()
        {
            _hoverTimer.Stop();
            var pending = _pending; _pending = null;
            if (pending is not { } p) return;
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
            if (Busy) { _hoverTimer.Stop(); _pending = null; return; }
            if (_drag is { } drag)
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
            if (Busy || e.Button != System.Windows.Forms.MouseButtons.Left) return;
            foreach (var s in InView(e.View))
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
                        if (p.Y >= bar.Thumb.Y && p.Y <= bar.Thumb.Y + bar.Thumb.Height) _drag = (s, card.Id, p.Y - bar.Thumb.Y);
                        else s.ScrollBy(card.Id, p.Y < bar.Thumb.Y ? -TableScroll.Visible(card) : TableScroll.Visible(card)); // page
                    }
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
            _drag = null; if (_pressed) { e.Cancel = true; _pressed = false; }
            Enabled = Sessions.Count > 0;
        }
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
