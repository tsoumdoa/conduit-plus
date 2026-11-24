using Rhino.Geometry;

namespace ConduitPlus.Rhino;

public sealed partial class Link
{
    private readonly Lazy<IReadOnlyDictionary<string, AnchorResult>> _labelAnchors;
    internal IReadOnlyDictionary<string, AnchorResult> LabelAnchors => _labelAnchors.Value;
    internal ConduitPlus.Core.LabelSource LabelSource { get; init; } = new(Guid.NewGuid(), "{0}");
    internal readonly record struct AnchorResult(Point3d Point, string Location, string? Diagnostic);

    internal static AnchorResult FindAnchor(GeometryBase geometry, string location)
    {
        var point = Point3d.Unset;
        switch (geometry)
        {
            case global::Rhino.Geometry.Point p:
                point = p.Location;
                break;
            case Curve curve:
                var length = curve.GetLength();
                if (length > 0 && double.IsFinite(length) && curve.NormalizedLengthParameter(0.5, out var t))
                    point = curve.PointAt(t);
                if (!point.IsValid)
                    return Fallback(curve.PointAtStart.IsValid ? curve.PointAtStart : curve.PointAtEnd, location, "curve endpoint");
                break;
            case Mesh mesh:
                using (var area = AreaMassProperties.Compute(mesh))
                {
                    if (area is { Area: > 0 } && area.Centroid.IsValid)
                        point = NearestMeshPoint(mesh, area.Centroid);
                }
                if (!point.IsValid)
                    return Fallback(NearestMeshPoint(mesh, mesh.GetBoundingBox(true).Center), location, "mesh sample");
                break;
            case Brep brep:
                return BrepAnchor(brep, location);
            case Extrusion extrusion:
                using (var converted = extrusion.ToBrep()) return BrepAnchor(converted, location);
            case Surface surface:
                using (var converted = surface.ToBrep()) return BrepAnchor(converted, location);
        }
        return point.IsValid ? new(point, location, null) : Fallback(point, location, "sample");
    }

    private static AnchorResult Fallback(Point3d point, string location, string sample) =>
        new(point, location, point.IsValid ? $"Used a {sample} for degenerate geometry." : "Skipped geometry without a valid anchor.");

    private static AnchorResult BrepAnchor(Brep? brep, string location)
    {
        if (brep is null) return Fallback(Point3d.Unset, location, "surface sample");
        using var area = AreaMassProperties.Compute(brep);
        var centroid = area?.Centroid ?? Point3d.Unset;
        var nearest = Point3d.Unset;
        var fallback = Point3d.Unset;
        var distance = double.PositiveInfinity;
        foreach (var face in brep.Faces)
        {
            using var trimmed = face.DuplicateFace(false);
            if (!fallback.IsValid)
                fallback = trimmed.ClosestPoint(face.PointAt(face.Domain(0).Mid, face.Domain(1).Mid));
            if (area is not { Area: > 0 } || !centroid.IsValid) continue;
            var candidate = trimmed.ClosestPoint(centroid);
            if (!candidate.IsValid) continue;
            var squared = candidate.DistanceToSquared(centroid);
            if (squared >= distance) continue;
            nearest = candidate;
            distance = squared;
        }
        return nearest.IsValid ? new(nearest, location, null) : Fallback(fallback, location, "surface sample");
    }

    private static Point3d NearestMeshPoint(Mesh mesh, Point3d target)
    {
        var nearest = Point3d.Unset;
        var distance = double.PositiveInfinity;
        foreach (var face in mesh.Faces)
        {
            Visit(face.A, face.B, face.C);
            if (face.IsQuad) Visit(face.A, face.C, face.D);
        }
        return nearest;

        void Visit(int a, int b, int c)
        {
            var triangle = new Triangle3d(mesh.Vertices.Point3dAt(a), mesh.Vertices.Point3dAt(b), mesh.Vertices.Point3dAt(c));
            if (!(triangle.Area > 0)) return;
            var barycentric = triangle.BarycentricCoordsAt(target, out _);
            var point = barycentric.X >= 0 && barycentric.Y >= 0 && barycentric.X + barycentric.Y <= 1
                ? triangle.PointAtBarycentricCoords(barycentric)
                : triangle.ClosestPointOnBoundary(target);
            if (!point.IsValid) return;
            var squared = point.DistanceToSquared(target);
            if (squared >= distance) return;
            nearest = point;
            distance = squared;
        }
    }
}
