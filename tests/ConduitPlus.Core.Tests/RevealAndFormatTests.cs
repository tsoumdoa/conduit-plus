using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class RevealAndFormatTests
{
    private static CardSpec Table(int offset = 0) => new() { Id = "rooms", Kind = CardKind.Table, Headers = ["Area"], MaxRows = 3, ScrollOffset = offset,
        Rows = Enumerable.Range(0, 10).Select(i => new TableRow($"R{i}", [(10 - i).ToString(System.Globalization.CultureInfo.InvariantCulture)])).ToArray() };
    [Fact]
    public void RevealMovesMinimallyAndIgnoresUnknownKeys()
    {
        var c = Table(3);
        Assert.Equal(3, TableScroll.Reveal(c, "R3")); Assert.Equal(3, TableScroll.Reveal(c, "R5"));
        Assert.Equal(2, TableScroll.Reveal(c, "R2")); Assert.Equal(4, TableScroll.Reveal(c, "R6"));
        Assert.Equal(7, TableScroll.Reveal(c, "R9")); Assert.Equal(0, TableScroll.Reveal(c, "R0"));
        Assert.Equal(3, TableScroll.Reveal(c, "removed"));
        Assert.Equal(0, TableScroll.Reveal(c with { Rows = [] }, "R0"));
        Assert.Equal(0, TableScroll.Reveal(c with { MaxRows = 20 }, "R9"));
        Assert.Equal(0, TableScroll.Reveal(c with { Kind = CardKind.Metric, Rows = [] }, "R0"));
    }
    [Fact]
    public void RevealUsesStableSortedOrderAndClampsStaleOffsets()
    {
        var c = Table(0) with { SortColumn = 0 };
        Assert.Equal(7, TableScroll.Reveal(c, "R0")); Assert.Equal(0, TableScroll.Reveal(c, "R9"));
        Assert.Equal(7, TableScroll.Reveal(c with { SortDescending = true }, "R9"));
        Assert.Equal(7, TableScroll.Reveal(c with { ScrollOffset = 999 }, "missing"));
        Assert.Equal(0, TableScroll.Reveal(c with { MaxRows = 1 }, "R9"));
    }
    [Theory]
    [InlineData("123.4567", 2, "123.46")] [InlineData("0.5", 0, "0")] [InlineData("1.2", 3, "1.200")]
    [InlineData("1,234.56", 1, "1234.6")] [InlineData("1.25E3", 0, "1250")] [InlineData("12 / 17", 2, "12 / 17")]
    [InlineData(null, 2, null)] [InlineData("NaN", 2, "NaN")]
    public void NumericPrecisionPreservesMissingAndNonnumericLabels(string? value, int decimals, string? expected) => Assert.Equal(expected, MetricFormatting.Format(value, decimals));
    [Fact]
    public void UnwiredPrecisionPreservesTextAndInvalidPrecisionIsAnError()
    {
        Assert.Equal("1.234567", MetricFormatting.Format("1.234567"));
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricFormatting.Format("1", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricFormatting.Format("1", 13));
    }
    [Fact]
    public void ExplicitBarColorsAcceptNegativeFailureAndZeroThreshold()
    {
        var bar = new CardSpec { Id = "delta", Kind = CardKind.Bar, Values = [-4, 0], Labels = ["Small", "Exact"], Keys = ["R1", "R2"], Colors = [Theme.Paper.Bad, Theme.Paper.Good], Threshold = 0 };
        bar.Validate();
        Assert.Throws<ArgumentException>(() => (bar with { Colors = ["red", "blue"] }).Validate());
        Assert.Throws<ArgumentException>(() => (bar with { Colors = [Theme.Paper.Bad] }).Validate());
    }
}
