namespace ConduitPlus.Core;

public readonly record struct OcclusionPoint(double X, double Y, double Z)
{
    public static OcclusionPoint operator +(OcclusionPoint a, OcclusionPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static OcclusionPoint operator -(OcclusionPoint a, OcclusionPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static OcclusionPoint operator *(OcclusionPoint a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    internal double Axis(int axis) => axis == 0 ? X : axis == 1 ? Y : Z;
    internal double Dot(OcclusionPoint b) => X * b.X + Y * b.Y + Z * b.Z;
    internal OcclusionPoint Cross(OcclusionPoint b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    internal double Magnitude => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
}

public readonly record struct OcclusionTriangle(OcclusionPoint A, OcclusionPoint B, OcclusionPoint C);

public readonly record struct OcclusionSegment(OcclusionPoint Near, OcclusionPoint Anchor, double WorldUnitsPerPixel)
{
    public bool IsOccluded(OccluderIndex index)
    {
        var offset = Anchor - Near;
        var distance = Math.Sqrt(offset.Dot(offset));
        var bias = Math.Max(WorldUnitsPerPixel * 0.25,
            Math.Max(Near.Magnitude, Anchor.Magnitude) * (16 * 2.220446049250313e-16));
        return double.IsFinite(distance) && distance > bias &&
            index.AnyHit(Near, offset * (1 / distance), distance - bias);
    }
}

public sealed class OccluderIndex
{
    private readonly Node? _root;
    private OccluderIndex(Node? root) => _root = root;

    public static OccluderIndex Build(IEnumerable<OcclusionTriangle> triangles)
    {
        var leaves = triangles.Where(t => double.IsFinite(t.A.Magnitude + t.B.Magnitude + t.C.Magnitude)
                && (t.B - t.A).Cross(t.C - t.A).Magnitude > 0)
            .Select(t => new Node(Bounds.Around(t), t, null, null)).ToArray();
        return new(Build(leaves, 0, leaves.Length));
    }

    public static OccluderIndex Combine(IEnumerable<OccluderIndex> sources)
    {
        var roots = sources.Select(s => s._root).OfType<Node>().ToArray();
        return new(Build(roots, 0, roots.Length));
    }

    public bool AnyHit(OcclusionPoint origin, OcclusionPoint unitDirection, double limit) =>
        Trace(origin, unitDirection, limit, true) < limit;

    public double? ClosestHit(OcclusionPoint origin, OcclusionPoint unitDirection, double limit)
    {
        var result = Trace(origin, unitDirection, limit, false);
        return result < limit ? result : null;
    }

    private double Trace(OcclusionPoint origin, OcclusionPoint direction, double limit, bool any)
    {
        if (_root is null || !(limit > 0)) return limit;
        var ray = new Ray(origin, direction, new(1 / direction.X, 1 / direction.Y, 1 / direction.Z));
        return _root.Box.Entry(ray, limit) < limit ? Visit(_root, ray, limit, any) : limit;
    }

    private static double Visit(Node node, in Ray ray, double limit, bool any)
    {
        if (node.Left is not { } left)
        {
            var triangle = node.Triangle;
            var ab = triangle.B - triangle.A;
            var ac = triangle.C - triangle.A;
            var cross = ray.Direction.Cross(ac);
            var determinant = ab.Dot(cross);
            if (determinant == 0) return limit;
            var offset = ray.Origin - triangle.A;
            var u = offset.Dot(cross) / determinant;
            if (u < 0 || u > 1) return limit;
            var q = offset.Cross(ab);
            var v = ray.Direction.Dot(q) / determinant;
            if (v < 0 || u + v > 1) return limit;
            var t = ac.Dot(q) / determinant;
            return t >= 0 && t < limit ? t : limit;
        }

        var right = node.Right!;
        var first = left.Box.Entry(ray, limit);
        var second = right.Box.Entry(ray, limit);
        if (second < first)
        {
            (left, right) = (right, left);
            (first, second) = (second, first);
        }
        var hit = first < limit ? Visit(left, ray, limit, any) : limit;
        if (any && hit < limit) return hit;
        return second < hit ? Visit(right, ray, hit, any) : hit;
    }

    private static Node? Build(Node[] nodes, int start, int count)
    {
        if (count == 0) return null;
        if (count == 1) return nodes[start];
        var box = nodes[start].Box;
        for (var i = start + 1; i < start + count; i++) box = box.Union(nodes[i].Box);
        var extent = box.Max - box.Min;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(nodes, start, count, AxisOrder[axis]);
        var half = count / 2;
        return new(box, default, Build(nodes, start, half), Build(nodes, start + half, count - half));
    }

    private static readonly IComparer<Node>[] AxisOrder = Enumerable.Range(0, 3).Select(axis =>
        Comparer<Node>.Create((a, b) => (a.Box.Min.Axis(axis) + a.Box.Max.Axis(axis))
            .CompareTo(b.Box.Min.Axis(axis) + b.Box.Max.Axis(axis)))).ToArray();

    private sealed record Node(Bounds Box, OcclusionTriangle Triangle, Node? Left, Node? Right);
    private readonly record struct Ray(OcclusionPoint Origin, OcclusionPoint Direction, OcclusionPoint Inverse);

    private readonly record struct Bounds(OcclusionPoint Min, OcclusionPoint Max)
    {
        public static Bounds Around(OcclusionTriangle t) => new(Minimum(Minimum(t.A, t.B), t.C), Maximum(Maximum(t.A, t.B), t.C));
        public Bounds Union(Bounds b) => new(Minimum(Min, b.Min), Maximum(Max, b.Max));
        private static OcclusionPoint Minimum(OcclusionPoint a, OcclusionPoint b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        private static OcclusionPoint Maximum(OcclusionPoint a, OcclusionPoint b) =>
            new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        public double Entry(in Ray ray, double limit)
        {
            var near = 0d;
            var far = limit;
            return Clip(Min.X - ray.Origin.X, Max.X - ray.Origin.X, ray.Inverse.X, ref near, ref far)
                && Clip(Min.Y - ray.Origin.Y, Max.Y - ray.Origin.Y, ray.Inverse.Y, ref near, ref far)
                && Clip(Min.Z - ray.Origin.Z, Max.Z - ray.Origin.Z, ray.Inverse.Z, ref near, ref far)
                ? near : limit;
        }

        private static bool Clip(double lo, double hi, double inverse, ref double near, ref double far)
        {
            if (double.IsInfinity(inverse)) return lo <= 0 && hi >= 0;
            var a = lo * inverse;
            var b = hi * inverse;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            return near <= far;
        }
    }
}

