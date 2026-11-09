using ConduitPlus.Core;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace ConduitPlus.Rhino;

public sealed partial class Link
{
    private sealed record PickProxy(BoundingBox Bounds, Point3d[] Points, Line[] Lines, NurbsCurve[] Curves, Mesh[] Meshes);
    private sealed record PickGroup(string Key, Point3d[] Corners, PickProxy[] Proxies);
    private readonly PickGroup[] _picks;
    private static PickProxy PreparePick(GeometryBase geometry, Wires wires, List<GeometryBase> owned)
    {
        var lines = new List<Line>(wires.Lines);
        var curves = new List<NurbsCurve>();
        foreach (var curve in wires.Curves)
        {
            if (curve.TryGetPolyline(out var polyline))
                for (var i = 1; i < polyline.Count; i++) lines.Add(new(polyline[i - 1], polyline[i]));
            else
            {
                var nurbs = curve.ToNurbsCurve() ?? throw new ArgumentException("Cannot create NURBS pick proxy.");
                owned.Add(nurbs); curves.Add(nurbs);
            }
        }
        Mesh[] meshes = [];
        switch (geometry)
        {
            case Mesh m: meshes = [m]; break; // already privately owned by the Link
            case Brep b: meshes = MeshBrep(b); break;
            case Extrusion e:
                using (var brep = e.ToBrep()) meshes = MeshBrep(brep);
                break;
            case Surface s:
                using (var brep = s.ToBrep()) meshes = MeshBrep(brep);
                break;
        }
        return new(geometry.GetBoundingBox(true), wires.Points, lines.ToArray(), curves.ToArray(), meshes);

        Mesh[] MeshBrep(Brep? brep)
        {
            if (brep is null) throw new ArgumentException("Cannot create brep pick proxy.");
            using var parameters = MeshingParameters.FastRenderMesh;
            var result = Mesh.CreateFromBrep(brep, parameters) ?? throw new ArgumentException("Cannot mesh Link geometry.");
            owned.AddRange(result);
            return result;
        }
    }
    internal void Pick(PickContext context, Transform screen, Point2 cursor, Guid owner, ref PickCandidate? best)
    {
        var winner = best; var currentKey = "";
        foreach (var group in _picks)
        {
            if (!NearBox(group.Corners, screen, cursor)) continue;
            currentKey = group.Key;
            foreach (var proxy in group.Proxies)
            {
                foreach (var point in proxy.Points)
                    if (context.PickFrustumTest(point, out var depth, out _)) Consider(false, Distance(point), depth);
                foreach (var line in proxy.Lines)
                    if (context.PickFrustumTest(line, out var t, out var depth, out _))
                    {
                        // Segment distance is in pixels. Use the clipped hit when an endpoint is behind the camera.
                        var distance = InFront(line.From) && InFront(line.To)
                            ? PickMath.PointSegmentDistance(cursor, Project(line.From), Project(line.To)) : Distance(line.PointAt(t));
                        Consider(false, distance, depth);
                    }
                foreach (var curve in proxy.Curves)
                    if (context.PickFrustumTest(curve, out var t, out var depth, out _)) Consider(false, Distance(curve.PointAt(t)), depth);
                foreach (var mesh in proxy.Meshes)
                    if (context.PickFrustumTest(mesh, PickContext.MeshPickStyle.ShadedModePicking, out var point, out var depth, out _, out _, out _))
                        Consider(true, Distance(point), depth);
            }
        }
        GC.KeepAlive(_geometry);
        best = winner;

        Point2 Project(Point3d p) { p.Transform(screen); return new(p.X, p.Y); }
        double Distance(Point3d p) { var projected = Project(p); return PickMath.PointSegmentDistance(cursor, projected, projected); }
        bool InFront(Point3d p) => screen.M30 * p.X + screen.M31 * p.Y + screen.M32 * p.Z + screen.M33 > 0;
        void Consider(bool mesh, double distance, double depth)
        {
            var candidate = new PickCandidate(owner, currentKey, mesh, distance, depth);
            if (PickMath.Prefer(candidate, winner)) winner = candidate;
        }
    }
    private static bool NearBox(Point3d[] corners, Transform screen, Point2 cursor)
    {
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        foreach (var corner in corners)
        {
            // A box crossing the camera plane has an unbounded projection: leave rejection to PickContext.
            if (screen.M30 * corner.X + screen.M31 * corner.Y + screen.M32 * corner.Z + screen.M33 <= 0) return true;
            var p = corner; p.Transform(screen);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return cursor.X >= minX - PickMath.Tolerance && cursor.X <= maxX + PickMath.Tolerance &&
            cursor.Y >= minY - PickMath.Tolerance && cursor.Y <= maxY + PickMath.Tolerance;
    }
}
