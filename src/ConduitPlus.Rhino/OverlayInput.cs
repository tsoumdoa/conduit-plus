using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.UI;
using System.Runtime.InteropServices;

namespace ConduitPlus.Rhino;

public static partial class OverlayManager
{
    private sealed class OverlayMouse : MouseCallback
    {
        private bool _pressed;
        // Active scrollbar-thumb drag: session, card, and where on the thumb it was grabbed (card-local CSS px).
        private (OverlaySession Session, string CardId, double Grab)? _drag;
        private static OverlaySession[] InView(RhinoView view) => Sessions.Values.Where(s => s.View.MainViewport.Id == view.MainViewport.Id).ToArray();
        protected override void OnMouseMove(MouseCallbackEventArgs e)
        {
            if (Busy) return;
            if (_drag is { } drag)
            {
                e.Cancel = true;
                var local = drag.Session.Local(drag.CardId, e.ViewportPoint.X, e.ViewportPoint.Y);
                if (local is { } p && drag.Session.Card(drag.CardId) is { } card)
                    drag.Session.ScrollTo(drag.CardId, TableScroll.OffsetForThumbTop(card, drag.Session.Theme, p.Y - drag.Grab));
                return;
            }
            foreach (var s in InView(e.View))
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                s.SetHover(hit?.CardId, hit?.Key);
            }
        }
        protected override void OnMouseDown(MouseCallbackEventArgs e)
        {
            if (Busy || e.Button != System.Windows.Forms.MouseButtons.Left) return;
            foreach (var s in InView(e.View))
            {
                var hit = s.Hit(e.ViewportPoint.X, e.ViewportPoint.Y);
                if (hit is null) continue;
                e.Cancel = true; _pressed = true;
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
        }
        protected override void OnMouseUp(MouseCallbackEventArgs e)
        { _drag = null; if (_pressed) { e.Cancel = true; _pressed = false; } }
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
