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
    [InlineData("-2.5", 0, "-3")]
    [InlineData("-44.125", 2, "-44.13")]
    [InlineData("-0.0001", 2, "0.00")]
    [InlineData("1e100", 2, null)]
    public void TableAndMetricShareAwayFromZeroRounding(string raw, int decimals, string? expected)
    {
        var metric = MetricFormatting.Format(raw, decimals)!;
        Assert.Equal(metric.Replace('-', '−'), TablePresentation.Format(raw, decimals));
        if (expected is not null) Assert.Equal(expected.Replace('-', '−'), metric);
        else Assert.EndsWith(".00", metric);
    }

    [Theory]
    [InlineData(new double[] { 1.123, 2.123 }, 0)]
    [InlineData(new double[] { 1.123, 1.223 }, 1)]
    [InlineData(new double[] { 1.123, 1.133 }, 2)]
    [InlineData(new double[] { 1.123, 1.124 }, 2)]
    [InlineData(new double[] { 1.123, 1.123 }, 0)]
    public void AutomaticPrecisionPreservesDistinctValuesWhenPossible(double[] values, int decimals)
        => Assert.Equal(decimals, NumericFormatting.AutomaticDecimals(values));
}
