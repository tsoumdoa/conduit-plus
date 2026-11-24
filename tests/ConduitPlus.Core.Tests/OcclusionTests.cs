using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class OcclusionTests
{
    private static OcclusionTriangle Face(double z) => new(new(-1, -1, z), new(1, -1, z), new(0, 1, z));
    private static OcclusionSegment Segment(double anchorZ) => new(new(0, 0, 5), new(0, 0, anchorZ), 1);

    [Theory]
    [InlineData(0, false)]
    [InlineData(-0.24, false)]
    [InlineData(-0.25, false)]
    [InlineData(-0.26, true)]
    public void BiasExcludesOnlyTheEndpoint(double z, bool blocked) =>
        Assert.Equal(blocked, Segment(z).IsOccluded(OccluderIndex.Build([Face(0)])));

    [Fact]
    public void ClosedSolidBlocksItsBackAnchor()
    {
        var a = new OcclusionPoint(-1, -1, 1);
        var b = new OcclusionPoint(1, -1, 1);
        var c = new OcclusionPoint(0, 1, 1);
        var d = new OcclusionPoint(0, 0, -1);
        var index = OccluderIndex.Build([new(a, b, c), new(a, b, d), new(b, c, d), new(c, a, d)]);
        Assert.False(Segment(1).IsOccluded(index));
        Assert.True(Segment(-1).IsOccluded(index));
    }

    [Fact]
    public void GrazingTwoSidedRayAndEdgeHits()
    {
        var index = OccluderIndex.Build([Face(0)]);
        Assert.False(new OcclusionSegment(new(-2, -1, 1e-9), new(0, -1, 0), 0.01).IsOccluded(index));
        Assert.True(new OcclusionSegment(new(-2, -1, -1e-9), new(0.5, -1, 1e-9), 0.01).IsOccluded(index));
        Assert.False(index.AnyHit(new(-2, 0, 1), new(1, 0, 0), 10));
        Assert.Equal(1d, index.ClosestHit(new(-1, -1, 1), new(0, 0, -1), 10));
    }

    [Fact]
    public void EmptyAndDegenerateSnapshotsDoNotBlock()
    {
        var a = new OcclusionPoint(1, 1, 1);
        Assert.False(Segment(-1).IsOccluded(OccluderIndex.Build([])));
        Assert.False(Segment(-1).IsOccluded(OccluderIndex.Build([new(a, a, a), new(a, a * 2, a * 3)])));
    }

    [Fact]
    public void ClosestAndAnyHitAgreeWithAnIndependentPlaneOracle()
    {
        var random = new Random(991);
        var faces = Enumerable.Range(0, 600).Select(_ =>
        {
            var offset = new OcclusionPoint(random.NextDouble() * 20, random.NextDouble() * 20, random.NextDouble() * 20);
            return new OcclusionTriangle(offset, offset + new OcclusionPoint(1, 0, 0), offset + new OcclusionPoint(0, 1, 0));
        }).ToArray();
        var index = OccluderIndex.Combine(faces.Chunk(25).Select(OccluderIndex.Build));
        var reversed = OccluderIndex.Build(faces.AsEnumerable().Reverse());
        for (var i = 0; i < 500; i++)
        {
            var origin = new OcclusionPoint(random.NextDouble() * 21, random.NextDouble() * 21, 21);
            var expected = faces.Where(face => origin.X >= face.A.X && origin.Y >= face.A.Y &&
                origin.X - face.A.X + origin.Y - face.A.Y <= 1)
                .Select(face => (double?)(origin.Z - face.A.Z)).Min();
            var ray = new OcclusionPoint(0, 0, -1);
            Assert.Equal(expected, index.ClosestHit(origin, ray, 22));
            Assert.Equal(expected, reversed.ClosestHit(origin, ray, 22));
            Assert.Equal(expected.HasValue, index.AnyHit(origin, ray, 22));
        }
    }

    [Fact]
    public void FrameBudgetPrioritizesActiveAndPreservesRoundRobinProgress()
    {
        var index = OccluderIndex.Build([Face(0)]);
        var schedule = new OcclusionSchedule(4);
        OcclusionSegment?[] segments = [Segment(-1), Segment(-1), Segment(-1), Segment(-1)];
        var ticks = 0;
        schedule.Classify(index, 1, 1, segments, [3], () => ticks++ < 2);
        Assert.True(schedule.Answer(3, 1, 1).HasHit(true));
        Assert.False(schedule.Answer(3, 1, 1).HasHit(false));
        Assert.Equal(0.45, schedule.Answer(3, 1, 1).Opacity(true));
        Assert.False(schedule.Answer(2, 1, 1).Visible);
        for (var frame = 2; frame <= 4; frame++)
        {
            ticks = 0;
            schedule.Classify(index, 1, frame, segments, [], () => ticks++ < 1);
            Assert.True(schedule.Answer(frame - 1, 1, frame).Current);
        }
        Assert.True(schedule.Answer(0, 1, 4).Visible);
        Assert.False(schedule.Answer(0, 1, 4).HasHit(true));
        Assert.False(schedule.Answer(0, 2, 4).Visible);
        schedule.Classify(index, 1, 4, segments, [], () => true);
        for (var i = 0; i < segments.Length; i++) Assert.True(schedule.Answer(i, 1, 4).Current);
    }

    [Fact]
    public void ReservedBackgroundSlotsRevisitEveryCandidateInFourFrames()
    {
        var index = OccluderIndex.Build([]);
        var schedule = new OcclusionSchedule(7);
        var segments = Enumerable.Repeat<OcclusionSegment?>(Segment(-1), 7).ToArray();
        var last = new int[segments.Length];
        for (var frame = 1; frame <= 12; frame++)
        {
            schedule.Classify(index, 1, frame, segments, [0], () => false, minimumBackgroundSlots: 2);
            for (var i = 0; i < segments.Length; i++)
            {
                if (schedule.Answer(i, 1, frame).Current) last[i] = frame;
                if (frame >= 4) Assert.InRange(frame - last[i], 0, 3);
            }
        }
    }
}

