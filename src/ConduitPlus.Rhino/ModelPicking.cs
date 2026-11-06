using ConduitPlus.Core;
using Rhino.ApplicationSettings;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace ConduitPlus.Rhino;

internal static class ModelPicking
{
    // Input gates (commands, Busy and cards) belong to the caller; the bench measures this same picker directly.
    internal static (OverlaySession Session, PickCandidate Hit)? Pick(RhinoView view, OverlaySession[] sessions, System.Drawing.Point cursor)
    {
        var viewport = view.ActiveViewport;
        if (!sessions.Any(s => Eligible(s)) || !viewport.GetFrustumLine(cursor.X, cursor.Y, out var line)) return null;
        using var context = new PickContext { View = view, PickStyle = PickStyle.PointPick, PickLine = line };
        // The point overload uses Rhino's pick radius. Scale clipping X/Y to our fixed six-pixel radius.
        var radius = ModelAidSettings.MousePickboxRadius;
        var transform = radius > 0
            ? Transform.Scale(Plane.WorldXY, radius / PickMath.Tolerance, radius / PickMath.Tolerance, 1) * viewport.GetPickTransform(cursor)
            : viewport.GetPickTransform(new System.Drawing.Rectangle(cursor.X - 6, cursor.Y - 6, 12, 12));
        context.SetPickTransform(transform);
        context.UpdateClippingPlanes();
        var screen = viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
        PickCandidate? best = null; OverlaySession? winner = null;
        foreach (var session in sessions)
        {
            if (!Eligible(session)) continue;
            var previous = best;
            session.Pick(context, screen, new(cursor.X, cursor.Y), ref best);
            if (best != previous) winner = session;
        }
        return winner is not null && best is { } hit ? (winner, hit) : null;

        bool Eligible(OverlaySession s) => s.Visible && s.HasLinks && s.Document.RuntimeSerialNumber == view.Document.RuntimeSerialNumber &&
            s.View.MainViewport.Id == viewport.Id;
    }
}
