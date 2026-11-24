using System.Drawing;
using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ColourScaleRangeTests
{
    [Fact]
    public void AutoKeepsExistingRoundedBoundsAndColours()
    {
        var result = ColourScale.Create([1, 9, null]);
        Assert.Equal(0, result.Min);
        Assert.Equal(10, result.Max);
        Assert.Equal(5, result.Mid);
        Assert.Equal(["#B4D8F9", "#1F559B", Palettes.Missing], result.Colours);
        Assert.Equal(["#CFE8FE", "#8CC0F2", "#5196DF", "#296CBA", "#184687"], result.Legend.Colors);
        Assert.Equal([0d, .25, .5, .75, 1], result.Legend.ColorPositions);
    }

    [Fact]
    public void ExactDataUsesFiniteBoundsAndPadsAroundControls()
    {
        var exact = ColourScale.Create([1, 9, double.PositiveInfinity], rangeMode: ColourRangeMode.ExactData);
        Assert.Equal((1d, 9d), (exact.Min, exact.Max));

        var midpoint = ColourScale.Create([1, 9], mid: 11, rangeMode: ColourRangeMode.ExactData);
        Assert.Equal((1d, 11.1), (midpoint.Min, midpoint.Max));

        var wired = ColourScale.Create([1, 9], min: 11, rangeMode: ColourRangeMode.ExactData);
        Assert.Equal((11d, 11.1), (wired.Min, wired.Max));

        var zeroSpan = ColourScale.Create([11], max: 11, rangeMode: ColourRangeMode.ExactData);
        Assert.Equal((Math.BitDecrement(11), 11d), (zeroSpan.Min, zeroSpan.Max));
    }

    [Theory]
    [InlineData(1, .3, 29.7)]
    [InlineData(2, .6, 29.4)]
    [InlineData(5, 1.5, 28.5)]
    public void ClipUsesTypeSevenQuantilesOfFiniteValues(int percent, double min, double max)
    {
        var result = ColourScale.Create([30, null, 0, double.NaN, 10, double.NegativeInfinity, 20],
            rangeMode: ColourRangeMode.ClipOutliers, clipPercent: percent);
        Assert.Equal(min, result.Min, 12);
        Assert.Equal(max, result.Max, 12);
    }

    [Fact]
    public void CollapsedClipShowsConstantSwatchAndCountsOutliers()
    {
        var values = new double?[100];
        Array.Fill(values, 0d);
        values[^1] = 200;
        var result = ColourScale.Create(values, above: "#FF0000", rangeMode: ColourRangeMode.ClipOutliers);
        Assert.Equal((0d, 0d), (result.Min, result.Max));
        Assert.All(result.Legend.Colors!, colour => Assert.Equal("#5196DF", colour));
        Assert.Equal("#FF0000", result.Colours[^1]);
        Assert.Contains(result.Messages, message => message.Text.StartsWith("1 of 100"));
        Assert.Equal("#184687", ColourScale.Create(values, rangeMode: ColourRangeMode.ClipOutliers).Colours[^1]);
    }

    [Theory]
    [InlineData("Viridis")]
    [InlineData("Magma")]
    [InlineData("Cividis")]
    [InlineData("Greys")]
    [InlineData("Greens")]
    [InlineData("Red–Yellow–Green")]
    [InlineData("Brown–Teal")]
    [InlineData("Orange–Purple")]
    [InlineData("Spectral")]
    [InlineData("Daylight")]
    [InlineData("Solar")]
    [InlineData("Comfort")]
    [InlineData("Wind")]
    public void NewPalettesResolveInBothDirections(string name)
    {
        var messages = new List<ColourMessage>();
        var palette = Palettes.Resolve([name.ToLowerInvariant()], "Blue", messages);
        var reversed = Palettes.Resolve([name + " reversed"], "Blue", messages);
        Assert.Empty(messages);
        Assert.Equal(name, palette.Name);
        Assert.Equal(name + " reversed", reversed.Name);
        Assert.Equal(5, palette.Colours.Length);
        Assert.Equal(palette.Colours.Reverse(), reversed.Colours.AsEnumerable());
    }

    [Fact]
    public void OnlyBlueAndTealOverlapKnownColourNames()
    {
        var collisions = Palettes.All.SelectMany(p => new[] { p.Name, p.Name + " reversed" })
            .Where(name => Color.FromName(name).IsKnownColor);
        Assert.Equal(["Blue", "Teal"], collisions);
    }

    [Fact]
    public void PaletteInputDistinguishesGreenFromGreens()
    {
        Assert.Equal(["#008000"], Palettes.NormalizeInput(["Green"]));
        var custom = Palettes.Resolve(Palettes.NormalizeInput(["#FFFFFF", "Green"]), "Blue", []);
        Assert.Equal("Custom", custom.Name);
        Assert.Equal(["#FFFFFF", "#008000"], custom.Colours.AsEnumerable());
        Assert.Equal("Greens", Palettes.Resolve(Palettes.NormalizeInput(["Greens"]), "Blue", []).Name);
    }
}
