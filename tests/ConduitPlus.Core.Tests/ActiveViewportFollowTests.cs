using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class ActiveViewportFollowTests
{
    private static readonly Guid Top = Guid.NewGuid(), Front = Guid.NewGuid(), Page = Guid.NewGuid();

    [Fact]
    public void MovesToAnotherStandardViewInBoundDocument() =>
        Assert.Equal(Front, ActiveViewportFollow.Target(1, Top, 1, Front, [Top, Front]));

    [Fact]
    public void DoesNotMoveToTheSameView() =>
        Assert.Null(ActiveViewportFollow.Target(1, Top, 1, Top, [Top, Front]));

    [Theory]
    [InlineData(2u)]
    [InlineData(null)]
    public void DoesNotFollowAnotherOrMissingDocument(uint? activeDocument) =>
        Assert.Null(ActiveViewportFollow.Target(1, Top, activeDocument, Front, [Top, Front]));

    [Fact]
    public void DoesNotFollowLayoutsOrDetailViewports() =>
        Assert.Null(ActiveViewportFollow.Target(1, Top, 1, Page, [Top, Front]));

    [Fact]
    public void DoesNotFollowAMissingActiveView() =>
        Assert.Null(ActiveViewportFollow.Target(1, Top, 1, null, [Top, Front]));

    [Fact]
    public void CanRecoverAfterTheBoundViewCloses() =>
        Assert.Equal(Front, ActiveViewportFollow.Target(1, null, 1, Front, [Front]));

    [Fact]
    public void NoModelViewsMeansNoMove() =>
        Assert.Null(ActiveViewportFollow.Target(1, Top, 1, Front, []));
}
