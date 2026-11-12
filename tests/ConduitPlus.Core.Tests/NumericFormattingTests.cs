using System.Globalization;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class NumericFormattingTests
{
    [Theory]
    [InlineData("0.5", 0, "1")]
    [InlineData("1.5", 0, "2")]
    [InlineData("2.5", 0, "3")]
    [InlineData("3.5", 0, "4")]
    [InlineData("12.5", 0, "13")]
    [InlineData("10.25", 1, "10.3")]
    [InlineData("44.125", 2, "44.13")]
    [InlineData("-0.5", 0, "-1")]
    [InlineData("-1.5", 0, "-2")]
    [InlineData("-2.5", 0, "-3")]
    [InlineData("-3.5", 0, "-4")]
    [InlineData("-12.5", 0, "-13")]
    [InlineData("-10.25", 1, "-10.3")]
    [InlineData("-44.125", 2, "-44.13")]
    [InlineData("1234567890123456", 0, "1234567890123456")]
    [InlineData("9007199254740991", 0, "9007199254740991")]
    [InlineData("1234.123456789012", 12, "1234.123456789012")]
    [InlineData("-1234567890123456", 0, "-1234567890123456")]
    [InlineData("-9007199254740991", 0, "-9007199254740991")]
    [InlineData("-1234.123456789012", 12, "-1234.123456789012")]
    [InlineData("-0.0001", 2, "0.00")]
    [InlineData("-0", 2, "0.00")]
    [InlineData("1e100", 2, null)]
    [InlineData("-1e100", 2, null)]
    public void TableAndMetricShareAwayFromZeroRounding(string raw, int decimals, string? expected)
    {
        var metric = MetricFormatting.Format(raw, decimals)!;
        Assert.Equal(metric, NumericFormatting.Fixed(double.Parse(raw, CultureInfo.InvariantCulture), decimals));
        Assert.Equal(metric.Replace('-', '−'), TablePresentation.Format(raw, decimals));
        if (expected is not null) Assert.Equal(expected, metric);
        else Assert.EndsWith(".00", metric);
    }

    [Theory]
    [InlineData(new double[] { 48.74153519689177, 3.2 }, 2)]
    [InlineData(new double[] { 0.00123456789, 0.5 }, 4)]
    [InlineData(new double[] { -0.00123456789, 0 }, 4)]
    [InlineData(new double[] { 0.0123456789, 0.5 }, 3)]
    [InlineData(new double[] { 0.000123456789, 0.5 }, 5)]
    [InlineData(new double[] { 0.0000123456789, 0.5 }, 6)]
    [InlineData(new double[] { 1e-20, 0 }, 6)]
    [InlineData(new double[] { 0, 0 }, 2)]
    public void AutomaticPrecisionKeepsTwoSignificantDigitsWithinTheSixPlaceCap(double[] values, int decimals)
        => Assert.Equal(decimals, NumericFormatting.AutomaticDecimals(values));

    [Theory]
    [InlineData("9007199254740993", 0, "9007199254740993")]
    [InlineData("1234567890123456.125", 2, "1234567890123456.13")]
    [InlineData("−1234567890123456.125", 2, "−1234567890123456.13")]
    [InlineData("1234567890123456125e-3", 2, "1234567890123456.13")]
    [InlineData("−0.0001", 2, "0.00")]
    public void TableRoundsOriginalSourceDigitsBeforeAnyDoubleConversion(string raw, int decimals, string expected)
        => Assert.Equal(expected, TablePresentation.Format(raw, decimals));

    [Theory]
    [InlineData("72057594037927936", 0, "72057594037927936")]
    [InlineData("-72057594037927936", 0, "-72057594037927936")]
    [InlineData("9007199254740993", 0, "9007199254740993")]
    public void MetricKeepsSourceDigits(string raw, int decimals, string expected)
        => Assert.Equal(expected, MetricFormatting.Format(raw, decimals));

    [Fact]
    public void IntegralDoublesFormatExactly()
        => Assert.Equal("72057594037927936", NumericFormatting.Fixed(72057594037927936d, 0));
}
