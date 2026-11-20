using ConduitPlus.Core;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public static partial class OverlayManager
{
    // All callers and the timer run on Rhino's UI thread. Snapshot queues before calling host code:
    // redraw/domain callbacks may reenter, dispose a session, or stop the manager altogether.
    private static class InteractionPump
    {
        private static readonly System.Windows.Forms.Timer Timer = new() { Interval = InteractionTiming.FrameMilliseconds };
        private static readonly InteractionFrameQueue<OverlaySession> Updates = new();
        private static readonly InteractionFrameQueue<uint> Redraws = new();
        private static bool _flushing;
        static InteractionPump() => Timer.Tick += (_, _) => Flush();

        internal static void Wake() { if (_started && !_stopping && !_flushing && !Timer.Enabled) Timer.Start(); }
        internal static void Update(OverlaySession session)
        {
            if (!_started || _stopping || session.IsDisposed) return;
            Updates.Request(session); Wake();
        }
        internal static void Redraw(OverlaySession session)
        {
            if (!_started || _stopping || session.IsDisposed) return;
            Redraws.Request(session.View.RuntimeSerialNumber); Wake();
        }
        internal static void Updated(OverlaySession session) => Updates.Remove(session);
        internal static bool Pending(OverlaySession session) => Updates.Contains(session);
        internal static void Forget(OverlaySession session) => Updates.Remove(session);
        internal static void Reset() { Timer.Stop(); Updates.Clear(); Redraws.Clear(); }

        internal static void Flush()
        {
            if (_flushing) return;
            Timer.Stop();
            if (!_started || _stopping) return;
            _flushing = true;
            try
            {
                // Commands/captures can pump Windows messages. Leave their synchronous raster state alone.
                if (Busy) { Mouse.CancelPending(); return; }
                Mouse.ApplyPending(Environment.TickCount64);
                foreach (var session in Updates.Take())
                {
                    if (!_started || _stopping) return;
                    if (session.IsDisposed || session.Document.IsClosing) continue;
                    if (Busy) { Updates.Request(session); continue; }
                    session.ApplyInteraction();
                    Redraw(session);
                }
                foreach (var serial in Redraws.Take())
                {
                    if (!_started || _stopping) return;
                    if (Busy) { Redraws.Request(serial); continue; }
                    try
                    {
                        if (RhinoView.FromRuntimeSerialNumber(serial) is { } view && !view.Document.IsClosing) view.Redraw();
                    }
                    catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ interaction redraw: {ex.Message}"); }
                }
            }
            catch (Exception ex) { RhinoApp.WriteLine($"Conduit+ interaction frame: {ex.Message}"); }
            finally
            {
                _flushing = false;
                if (Updates.Pending || Redraws.Pending || Mouse.Pending) Wake();
            }
        }
    }

    internal static void RequestInteraction(OverlaySession session) => InteractionPump.Update(session);
    internal static void RequestInteractionRedraw(OverlaySession session) => InteractionPump.Redraw(session);
    internal static void InteractionUpdated(OverlaySession session) => InteractionPump.Updated(session);
    internal static bool HasPendingInteraction(OverlaySession session) => InteractionPump.Pending(session);
    internal static void ForgetInteraction(OverlaySession session) => InteractionPump.Forget(session);
}
