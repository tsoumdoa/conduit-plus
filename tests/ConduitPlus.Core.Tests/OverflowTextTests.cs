using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class OverflowTextTests
{
    [Fact]
    public void NothingHiddenClearsTheWarning() => Assert.Null(OverflowText.For(0, 9, "Perspective", 640, 400));

    [Fact]
    public void WarningNamesCountsViewAndSize() =>
        Assert.Equal("3 of 9 cards hidden in Perspective (640 × 400). Enlarge the viewport or remove cards.", OverflowText.For(3, 9, "Perspective", 640, 400));

    [Fact]
    public void SingleCardIsSingular() =>
        Assert.Equal("1 of 1 card hidden in Top (300 × 200). Enlarge the viewport or remove cards.", OverflowText.For(1, 1, "Top", 300, 200));
}
