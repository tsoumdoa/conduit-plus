using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ColourScaleOutOfRangeTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("#abcdef", null)]
    [InlineData(null, "#FEDCBA")]
    [InlineData("#abcdef", "#FEDCBA")]
    public void OptionalColoursOnlyOverrideStrictlyOutsideFiniteValues(string? below, string? above)
    {
        double?[] values = [-1, 0, .5, 1, 2, null, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        var scale = ColourScale.Create(values, ["Blue-Red reversed"], min: 0, max: 1, below: below, above: above);
        var original = ColourScale.Create(values, ["Blue-Red reversed"], min: 0, max: 1);
        Assert.Equal(below ?? original.Colours[0], scale.Colours[0]);
        Assert.Equal(above ?? original.Colours[4], scale.Colours[4]);
        Assert.Equal(original.Colours[1..4], scale.Colours[1..4]);
        Assert.Equal(scale.Palette.Colours[0], scale.ColourAt(0));
        Assert.Equal(scale.Palette.Colours[^1], scale.ColourAt(1));
        Assert.Equal(below ?? original.ColourAt(-double.Epsilon), scale.ColourAt(-double.Epsilon));
        Assert.Equal(above ?? original.ColourAt(Math.BitIncrement(1d)), scale.ColourAt(Math.BitIncrement(1d)));
        Assert.All(scale.Colours[5..], c => Assert.Equal(Palettes.Missing, c));
        Assert.All(values, v => Assert.Equal(scale.Colours[Array.IndexOf(values, v)], scale.ColourAt(v)));
        Assert.Equal(below, scale.Below); Assert.Equal(above, scale.Above);
        Assert.Equal(below, scale.Legend.BelowColor); Assert.Equal(above, scale.Legend.AboveColor);
        Assert.Equal(original.Legend.Colors, scale.Legend.Colors);
        Assert.Equal(original.Legend.ColorPositions, scale.Legend.ColorPositions);
    }

    [Fact]
    public void ConstantRangeKeepsItsMiddleColourAndCanExtendOnEitherSide()
    {
        var scale = ColourScale.Create([5, 5], below: "#123456", above: "#654321");
        Assert.Equal(scale.Palette.ColourAt(.5), scale.ColourAt(5));
        Assert.Equal("#123456", scale.ColourAt(4));
        Assert.Equal("#654321", scale.ColourAt(6));
        var original = ColourScale.Create([5, 5]);
        Assert.Equal(original.ColourAt(5), original.ColourAt(4));
        Assert.Equal(original.ColourAt(5), original.ColourAt(6));
        scale.Legend.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("1234567")]
    [InlineData("#12GG56")]
    [InlineData("#12345678")]
    [InlineData(" #123456")]
    public void InvalidOptionalColoursIdentifyTheSideAndRequiredFormat(string colour)
    {
        Assert.Equal("Below colour must be a #RRGGBB string.", Assert.Throws<ArgumentException>(() =>
            ColourScale.Create([], min: 0, max: 1, below: colour)).Message);
        Assert.Equal("Above colour must be a #RRGGBB string.", Assert.Throws<ArgumentException>(() =>
            ColourScale.Create([], min: 0, max: 1, above: colour)).Message);
        var legend = ColourScale.Create([], min: 0, max: 1).Legend;
        Assert.Equal("BelowColor must be a #RRGGBB string.", Assert.Throws<ArgumentException>(() =>
            (legend with { BelowColor = colour }).Validate()).Message);
        Assert.Equal("AboveColor must be a #RRGGBB string.", Assert.Throws<ArgumentException>(() =>
            (legend with { AboveColor = colour }).Validate()).Message);
    }

    [Theory]
    [InlineData("#123456", "#654321", "2 below Min use the Below colour; 1 above Max uses the Above colour")]
    [InlineData("#123456", null, "2 below Min use the Below colour; 1 above Max uses the last colour")]
    [InlineData(null, "#654321", "2 below Min use the first colour; 1 above Max uses the Above colour")]
    public void RemarksDescribeEachSideAndMissingValuesStaySeparate(string? below, string? above, string detail)
    {
        var scale = ColourScale.Create([-2, -1, 0, 1, 2, null], min: 0, max: 1, below: below, above: above);
        Assert.Contains(scale.Messages, m => m.Level == ColourMessageLevel.Remark && m.Text == $"3 of 6 values are outside 0–1: {detail}.");
        Assert.Contains(scale.Messages, m => m.Text == $"1 missing value shown in grey ({Palettes.Missing}).");
    }

    [Theory]
    [InlineData(-1, "1 below Min uses the Below colour")]
    [InlineData(2, "1 above Max uses the Above colour")]
    public void AllOutsideStillWarnsAndDoesNotMentionAnUnusedSide(double value, string detail)
    {
        var scale = ColourScale.Create([value], min: 0, max: 1, below: "#123456", above: "#654321");
        var message = Assert.Single(scale.Messages);
        Assert.Equal(ColourMessageLevel.Warning, message.Level);
        Assert.Equal($"1 of 1 value is outside 0–1: {detail}.", message.Text);
        Assert.Equal(ColourMessageLevel.Warning, Assert.Single(ColourScale.Create([-1, 2], min: 0, max: 1,
            below: "#123456", above: "#654321").Messages).Level);
        Assert.Empty(ColourScale.Create([0, 1], min: 0, max: 1, below: "#123456", above: "#654321").Messages);
    }

    [Fact]
    public void ExtendColoursRequireAGradientLegend()
    {
        var gradient = ColourScale.Create([], min: 0, max: 1).Legend;
        foreach (var kind in Enum.GetValues<CardKind>().Where(k => k != CardKind.Legend))
            Assert.Throws<ArgumentException>(() => (gradient with { Kind = kind, BelowColor = "#123456" }).Validate());
        var categorical = ColourCategories.Create(["A"]).Legend;
        Assert.Throws<ArgumentException>(() => (categorical with { BelowColor = "#123456" }).Validate());
        Assert.Throws<ArgumentException>(() => (categorical with { AboveColor = "#123456" }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Max = null, AboveColor = "#123456" }).Validate());
        (gradient with { BelowColor = "#abcdef", AboveColor = "#FEDCBA" }).Validate();
    }

    [Fact]
    public void EitherCapChangingOrBeingRemovedInvalidatesTheStoredCard()
    {
        var card = ColourScale.Create([], min: 0, max: 1).Legend;
        var store = new CardStore();
        var previousRev = Assert.Single(store.Update([card])).Rev;
        foreach (var next in new[] { card with { BelowColor = "#123456" }, card with { BelowColor = "#FFFFFF" },
            card with { AboveColor = "#654321" }, card with { AboveColor = "#000000" }, card })
        {
            var stored = Assert.Single(store.Update([next]));
            Assert.True(stored.Rev > previousRev);
            Assert.Equal(next.BelowColor, stored.BelowColor); Assert.Equal(next.AboveColor, stored.AboveColor);
            Assert.Empty(store.Update([next]));
            previousRev = stored.Rev;
        }
    }
}
