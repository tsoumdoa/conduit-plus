namespace ConduitPlus.Core;

public readonly record struct PickCandidate(Guid Owner, string Key, bool IsMesh, double Distance, double Depth);

/// <summary>Screen distances are pixels; Rhino pick depth increases towards the camera.</summary>
public static class PickMath
{
    public const double Tolerance = 6;
    public static double PointSegmentDistance(Point2 point, Point2 start, Point2 end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0, 1);
        dx = point.X - start.X - t * dx; dy = point.Y - start.Y - t * dy;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    public static bool Prefer(PickCandidate candidate, PickCandidate? current)
    {
        if (!double.IsFinite(candidate.Distance) || !double.IsFinite(candidate.Depth) || candidate.Distance < 0 || candidate.Distance > Tolerance) return false;
        return current is not { } previous || Compare(candidate, previous) < 0;
    }
    public static int Compare(PickCandidate a, PickCandidate b)
    {
        var order = a.IsMesh.CompareTo(b.IsMesh); // any wire/point within tolerance wins over a face
        if (order == 0) order = a.IsMesh ? b.Depth.CompareTo(a.Depth) : a.Distance.CompareTo(b.Distance);
        if (order == 0) order = a.Owner.CompareTo(b.Owner);
        return order != 0 ? order : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
}
