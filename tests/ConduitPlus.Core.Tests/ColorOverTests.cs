using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ColorOverTests
{
    [Fact]
    public void OverCompositesOntoOpaqueSurfaceWithoutAlpha()
    {
        var tint = ColorRgba.Parse("#BE3238").Over(ColorRgba.Parse("#1A1D23"), 0x18);
        Assert.Equal(255, tint.A);
        // 0x18/255 of red over Night surface.
        Assert.Equal(new ColorRgba(41, 31, 37), tint);
    }

    [Fact]
    public void OverKeepsTranslucentSurfaceAlpha()
    {
        var tint = ColorRgba.Parse("#FFFFFF").Over(ColorRgba.Parse("#12151CCC"), 0x80);
        Assert.Equal(0xCC, tint.A);
        Assert.Equal(137, tint.R);
    }
}
