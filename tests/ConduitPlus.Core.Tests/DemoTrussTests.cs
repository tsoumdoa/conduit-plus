using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class DemoTrussTests
{
    [Fact]
    public void DefaultFixtureHasStableTopologyAndIllustrativeOverstressedChords()
    {
        var r = DemoTruss.Create();
        Assert.Equal(33, r.Members.Length); Assert.Equal(9, r.Deflection.Length);
        Assert.Equal(Enumerable.Range(1, 33).Select(i => $"M{i:00}"), r.Members.Select(m => m.Key));
        Assert.Equal(4, r.Members.Count(m => m.Utilisation > 1)); Assert.Equal(29, r.Passing);
        Assert.All(r.Members, m => Assert.True(m.Length > 0 && double.IsFinite(m.Utilisation)));
        Assert.Equal(0, r.Deflection[0]); Assert.Equal(0, r.Deflection[^1]);
        Assert.Equal(21.6, r.MaxDeflection, 8); Assert.Equal(2.7031695879, r.Mass, 9);
        Assert.Equal(CardJson.Serialize(r), CardJson.Serialize(DemoTruss.Create()));
    }
    [Fact]
    public void LoadScalesResultsAndDepthReducesDeflectionWithoutChangingKeys()
    {
        var r = DemoTruss.Create(); var twice = DemoTruss.Create(load: 24); var deep = DemoTruss.Create(depth: 6);
        Assert.Equal(r.Members.Select(m => m.Utilisation * 2), twice.Members.Select(m => m.Utilisation));
        Assert.Equal(r.Deflection.Select(d => d * 2), twice.Deflection); Assert.Equal(r.Mass, twice.Mass);
        Assert.Equal(r.Deflection.Select(d => d / 4), deep.Deflection);
        Assert.Equal(r.Members.Select(m => m.Key), deep.Members.Select(m => m.Key));
        Assert.All(DemoTruss.Create(load: 0).Members, m => Assert.Equal(0, m.Utilisation));
    }
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(40)]
    public void BayCountsPreserveSymmetryAndPanelPointGeometry(int bays)
    {
        var r = DemoTruss.Create(bays: bays);
        Assert.Equal(4 * bays + 1, r.Members.Length); Assert.Equal(bays + 1, r.Positions.Length);
        for (var i = 0; i <= bays; i++) Assert.Equal(r.Deflection[i], r.Deflection[bays - i], 9);
        Assert.Equal(0, r.Positions[0]); Assert.Equal(24, r.Positions[^1]);
    }
    [Theory]
    [InlineData(0, 8, 3, 12)] [InlineData(double.NaN, 8, 3, 12)] [InlineData(24, 1, 3, 12)]
    [InlineData(24, 41, 3, 12)] [InlineData(24, 8, -1, 12)] [InlineData(24, 8, 3, -1)]
    [InlineData(24, 8, 3, double.PositiveInfinity)]
    public void InvalidInputsFailBeforeProducingData(double span, int bays, double depth, double load) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoTruss.Create(span, bays, depth, load));
}
