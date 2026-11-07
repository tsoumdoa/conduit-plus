using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class PickingTests
{
    [Theory]
    [InlineData(5, 3, 3)]
    [InlineData(-4, 3, 5)]
    [InlineData(14, 3, 5)]
    [InlineData(5, 0, 0)]
    public void SegmentDistanceClampsToEndpoints(double x, double y, double expected) =>
        Assert.Equal(expected, PickMath.PointSegmentDistance(new(x, y), new(0, 0), new(10, 0)), 10);
    [Fact]
    public void SegmentDistanceHandlesPointsAndVerticalSegments()
    {
        Assert.Equal(5, PickMath.PointSegmentDistance(new(4, 6), new(1, 2), new(1, 2)));
        Assert.Equal(3, PickMath.PointSegmentDistance(new(4, 6), new(1, 2), new(1, 10)));
    }
    [Fact]
    public void WiresUseDistanceAndTakePriorityOverNearerFaces()
    {
        var face = new PickCandidate(Guid.Empty, "face", true, 0, 100);
        var wire = new PickCandidate(Guid.Empty, "wire", false, 6, -100);
        Assert.True(PickMath.Prefer(wire, face)); Assert.False(PickMath.Prefer(face, wire));
        Assert.True(PickMath.Prefer(wire with { Distance = 2, Depth = -200 }, wire));
        Assert.False(PickMath.Prefer(wire with { Distance = 7 }, face));
    }
    [Fact]
    public void FacesUseLargerDepthAndIgnoreDistanceForOrdering()
    {
        var far = new PickCandidate(Guid.Empty, "far", true, 0, -10);
        Assert.True(PickMath.Prefer(far with { Key = "near", Distance = 5, Depth = 10 }, far));
        Assert.False(PickMath.Prefer(far with { Depth = -20 }, far));
    }
    [Fact]
    public void EqualHitsUseOwnerThenOrdinalKeyRegardlessOfEnumerationOrder()
    {
        var first = new PickCandidate(Guid.Parse("00000001-0000-0000-0000-000000000000"), "a", false, 2, 0);
        var hits = new[] { first, first with { Key = "A", Depth = -1 }, first with { Owner = Guid.Parse("00000002-0000-0000-0000-000000000000"), Key = "0" } };
        foreach (var order in new[] { hits, hits.AsEnumerable().Reverse().ToArray(), new[] { hits[1], hits[2], hits[0] } })
        {
            PickCandidate? best = null;
            foreach (var hit in order) if (PickMath.Prefer(hit, best)) best = hit;
            Assert.Equal(hits[1], best);
        }
        var mesh = first with { IsMesh = true };
        Assert.True(PickMath.Prefer(mesh with { Key = "A" }, mesh));
        Assert.False(PickMath.Prefer(first, first));
    }
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(6.001, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.NegativeInfinity)]
    public void InvalidHitsCannotWin(double distance, double depth) =>
        Assert.False(PickMath.Prefer(new(Guid.Empty, "key", false, distance, depth), null));
}
