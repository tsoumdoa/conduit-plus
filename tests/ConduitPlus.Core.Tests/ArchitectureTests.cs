using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void RoomsTileTheFootprintAndReportKnownMinima()
    {
        var r = DemoRooms.Create();
        Assert.Equal(17, r.Rooms.Length);
        Assert.Equal(Enumerable.Range(1, 17).Select(i => $"R{i:00}"), r.Rooms.Select(room => room.Key));
        Assert.Equal(320, r.GrossArea); Assert.Equal(40, r.CirculationArea); Assert.Equal(280, r.NetArea);
        Assert.Equal(87.5, r.NetToGrossPercent); Assert.Equal(12.5, r.CirculationPercent);
        Assert.Equal(8, r.Failing);
        Assert.Equal(12, r.Rooms[0].Area); Assert.Equal(16, r.Rooms[0].Required); Assert.Equal(-4, r.Rooms[0].Delta); Assert.False(r.Rooms[0].Pass);
        Assert.True(r.Rooms[4].Pass); // Study exactly at its 12 m² minimum.
        foreach (var a in r.Rooms)
        {
            Assert.InRange(a.X + a.Width, 0, 20); Assert.InRange(a.Y + a.Depth, 0, 16);
            foreach (var b in r.Rooms.Where(b => b.Key != a.Key))
                Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Depth <= b.Y || b.Y + b.Depth <= a.Y);
        }
    }
    [Fact]
    public void ScalingRoomsChangesAreasButPreservesKeysAndRequirements()
    {
        var r = DemoRooms.Create(); var twice = DemoRooms.Create(2);
        Assert.Equal(r.Rooms.Select(room => room.Key), twice.Rooms.Select(room => room.Key));
        Assert.Equal(r.Rooms.Select(room => room.Required), twice.Rooms.Select(room => room.Required));
        Assert.Equal(r.Rooms.Select(room => room.Area * 4), twice.Rooms.Select(room => room.Area));
        Assert.Equal(0, twice.Failing); Assert.Equal(1280, twice.GrossArea);
    }
    [Fact]
    public void MassingUsesFootprintTimesFloorsAndKnownLimits()
    {
        var r = DemoMassing.Create();
        Assert.Equal(6, r.Blocks.Length); Assert.Equal(8000, r.SiteArea); Assert.Equal(16000, r.AllowedGfa);
        Assert.Equal(14976, r.TotalGfa); Assert.Equal(1.872, r.Far, 10); Assert.Equal(2, r.HeightFailures); Assert.False(r.Pass);
        Assert.Equal(new double[] { 6156, 7680, 1140 }, r.GfaByUse); Assert.Equal(r.TotalGfa, r.GfaByUse.Sum());
        Assert.Equal(26.4, r.Blocks[0].Height, 10); Assert.True(r.Blocks[0].Pass); Assert.False(r.Blocks[1].Pass);
        var extra = DemoMassing.Create(1);
        Assert.Equal(r.TotalGfa + r.Blocks.Sum(b => b.Footprint), extra.TotalGfa);
        Assert.True(extra.TotalGfa > extra.AllowedGfa);
        Assert.Equal(r.Blocks.Select(b => b.Key), extra.Blocks.Select(b => b.Key));
        Assert.All(r.Blocks, b => { Assert.InRange(b.X + b.Width, 0, r.SiteWidth); Assert.InRange(b.Y + b.Depth, 0, r.SiteDepth); });
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(.49)] [InlineData(2.01)]
    public void InvalidRoomScaleRejected(double scale) => Assert.Throws<ArgumentOutOfRangeException>(() => DemoRooms.Create(scale));
    [Theory]
    [InlineData(-2)] [InlineData(6)]
    public void InvalidFloorChangeRejected(int extra) => Assert.Throws<ArgumentOutOfRangeException>(() => DemoMassing.Create(extra));
}
