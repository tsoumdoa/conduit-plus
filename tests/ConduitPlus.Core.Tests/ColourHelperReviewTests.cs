using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ColourHelperReviewTests
{
    [Fact]
    public void LegendInterpolationAgreesBetweenSamplesAndAtEveryKnot()
    {
        var palettes = Palettes.All.Where(p => p.Kind != PaletteKind.Categorical).Select(p => new[] { p.Name })
            .Concat(Enumerable.Range(2, 31).Select(n => Enumerable.Range(0, n).Select(i => i % 2 == 0 ? "#000000" : "#FFFFFF").ToArray()));
        foreach (var palette in palettes)
        foreach (var (min, max, mid) in new (double, double, double?)[] { (0, 1, null), (-1, 9, null), (0, 1.2, 1), (0, 1, .99), (-3, 9, 1.1) })
        {
            var scale = ColourScale.Create([], palette, min, max, mid);
            var legend = scale.Legend;
            Assert.InRange(legend.Colors.Length, 2, 33);
            var probes = Enumerable.Range(0, 2001).Select(i => i / 2000d)
                .Concat(legend.ColorPositions)
                .Concat(legend.ColorPositions.Zip(legend.ColorPositions.Skip(1)).SelectMany(p => new[] { (p.First + p.Second) / 2, .9 * p.First + .1 * p.Second }));
            foreach (var t in probes)
            {
                var expected = ColorRgba.Parse(scale.ColourAt(min * (1 - t) + max * t));
                var actual = LegendColour(legend, t);
                Assert.True(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1,
                    $"{string.Join(",", palette)} {min}..{max} Mid {mid}, t {t}: {expected} / {actual}");
            }
            // Positioned stops must survive the ordinary Card transport.
            var roundTrip = CardJson.Deserialize<CardSpec>(CardJson.Serialize(legend));
            Assert.Equal(legend.ColorPositions, roundTrip.ColorPositions);
            roundTrip.Validate();
        }
    }

    private static ColorRgba LegendColour(CardSpec legend, double t)
    {
        var positions = legend.ColorPositions;
        var i = 0;
        while (i < positions.Length - 2 && t > positions[i + 1]) i++;
        var u = (t - positions[i]) / (positions[i + 1] - positions[i]);
        var a = ColorRgba.Parse(legend.Colors[i]); var b = ColorRgba.Parse(legend.Colors[i + 1]);
        byte Mix(byte x, byte y) => (byte)Math.Round(x * (1 - u) + y * u, MidpointRounding.AwayFromZero);
        return new(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    [Theory]
    [InlineData(0, 1e308)]
    [InlineData(-1e308, 1e308)]
    [InlineData(-double.MaxValue, double.MaxValue)]
    public void LargeFiniteDomainsHaveFiniteMappingAndExactEndpoints(double min, double max)
    {
        var scale = ColourScale.Create([min, 0, max], min: min, max: max);
        Assert.Equal(scale.Palette.Colours[0], scale.Colours[0]);
        Assert.Equal(scale.Palette.Colours[^1], scale.Colours[^1]);
        Assert.DoesNotContain(Palettes.Missing, scale.Legend.Colors);
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100d;
            var expected = ColorRgba.Parse(scale.ColourAt(min * (1 - t) + max * t));
            var actual = LegendColour(scale.Legend, t);
            Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
            Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
            Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e16)]
    [InlineData(-1e16)]
    [InlineData(double.MaxValue)]
    [InlineData(-double.MaxValue)]
    [InlineData(double.Epsilon)]
    public void ConstantsHaveSingleColourLegendAtTheirValue(double value)
    {
        var scale = ColourScale.Create([value, value]);
        Assert.Equal(value, scale.Min); Assert.Equal(value, scale.Max);
        Assert.All(scale.Legend.Colors, c => Assert.Equal(scale.Colours[0], c));
        Assert.Equal([value], LegendCardPainter.Ticks(scale.Legend));
    }

    [Fact]
    public void AutomaticBoundsIncludeMidAndRespectSingleExplicitBound()
    {
        var scale = ColourScale.Create([.2, .9], ["Status"], mid: 1);
        Assert.True(scale.Min <= .2 && scale.Max > 1);
        Assert.Equal(scale.Palette.ColourAt(.5), scale.ColourAt(1));
        var negative = ColourScale.Create([.2, .9], mid: -1);
        Assert.True(negative.Min < -1 && negative.Max >= .9);
        Assert.True(ColourScale.Create([3, 4], min: 50).Max > 50);
        Assert.True(ColourScale.Create([3, 4], max: -50).Min < -50);
        Assert.Throws<ArgumentException>(() => ColourScale.Create([.2, .9], min: 0, max: 1, mid: 1));
        Assert.Throws<ArgumentException>(() => ColourScale.Create([.2, .9], min: 1, mid: 1));
    }

    [Fact]
    public void GroupNamespacesIsolateComponentsAndRemainStableForReopenAndBarReuse()
    {
        static ColourCategoriesResult Build(string ns, string member, string title = "Legend") =>
            ColourCategories.Create(["Office", null], [member, member + "-blank"], title: title, id: ns, groupNamespace: ns);
        var a = Build("01234567", "R1"); var b = Build("89abcdef", "B1");
        Assert.Equal(["Legend:Office@01234567", "Legend:No data@01234567"], a.GroupKeys);
        Assert.Equal(a.GroupKeys, Build("01234567", "R1").GroupKeys);
        Assert.NotEqual(a.GroupKeys, b.GroupKeys);
        Assert.Equal("Renamed:Office@01234567", Build("01234567", "R1", "Renamed").GroupKeys[0]);
        var bar = new CardSpec { Id = "bar", Kind = CardKind.Bar, Values = [10, 20], Labels = a.Categories,
            Keys = a.GroupKeys, Colors = a.CategoryColours,
            Groups = GroupInputs.Create(a.GroupKeys, 2, true, a.Members.Select((m, i) => new MemberInputBranch($"{{{i}}}", m)).ToArray()) };
        bar.Validate();
        var index = MembershipIndex.Create([new BrushRegistration("overlay", [a.Legend, b.Legend, bar], [])]);
        Assert.Empty(index.Diagnostics); Assert.Equal(4, index.Groups.Count);
        Assert.Equal(["R1"], index.Groups[a.GroupKeys[0]].Members.ToArray());
        Assert.Equal(["B1"], index.Groups[b.GroupKeys[0]].Members.ToArray());
        Assert.Contains("collides", Assert.Throws<ArgumentException>(() => ColourCategories.Create(["Office"], [a.GroupKeys[0]], groupNamespace: "01234567")).Message);
    }

    [Fact]
    public void BlanksDoNotWarnAboutOrderAndMessagesUseCorrectPluralsAndCompactNumbers()
    {
        var blank = ColourCategories.Create(["A", null], order: ["A", "B"]);
        Assert.Empty(blank.Messages); Assert.Equal("No data", blank.Categories[^1]);
        var extra = ColourCategories.Create(["A", "C", null], order: ["A", "B"]);
        Assert.Equal("1 category is not in Order ('C'); added after the listed ones.", Assert.Single(extra.Messages).Text);
        var scale = ColourScale.Create([.02, .27, null], min: .1);
        Assert.Contains(scale.Messages, m => m.Text == "1 missing value shown in grey (#B4B7BD).");
        Assert.Contains(scale.Messages, m => m.Text == "1 of 3 values is outside 0.1–0.3 and uses the end colours.");
        Assert.DoesNotContain(scale.Messages, m => m.Text.Contains("000000000"));
    }

    [Fact]
    public void GradientPositionsRejectMalformedTransportData()
    {
        var card = ColourScale.Create([], ["Status"], 0, 1, .99).Legend;
        foreach (var positions in new double[][] { [0, 1], [0, .5, .5], [0, 1, .9], [0, double.NaN, 1] })
            Assert.Throws<ArgumentException>(() => (card with { ColorPositions = positions }).Validate());
        Assert.Throws<ArgumentException>(() => (ColourCategories.Create(["A"]).Legend with { ColorPositions = [0] }).Validate());
    }

    [Theory]
    [InlineData("Categorical")]
    [InlineData("Categorical reversed")]
    public void ScaleWarnsAndFallsBackFromNamedCategorical(string palette)
    {
        var scale = ColourScale.Create([0, 1], [palette]);
        Assert.Equal("Blue", scale.Palette.Name);
        Assert.Equal("Categorical is for categories; using Blue. Choose a sequential, diverging or Status palette.", Assert.Single(scale.Messages).Text);
        Assert.Equal("Custom", ColourScale.Create([0, 1], ["#000000", "#FFFFFF"]).Palette.Name);
    }
}
