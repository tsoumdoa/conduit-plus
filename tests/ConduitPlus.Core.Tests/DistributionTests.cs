using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class DistributionTests
{
    internal static CardSpec PaintFixture() => Card([10, 12, 20, 20, 25]);

    private static CardSpec Card(double?[] values, DistributionMode mode = DistributionMode.Dots, double? width = null)
    {
        var keys = InputKeys.Numbered(values.Length);
        return new() { Id = "areas", Kind = CardKind.Distribution, Values = values, Labels = keys, Keys = keys, Distribution = new(mode, BinWidth: width) };
    }

    [Fact]
    public void BinsAlignAcrossZeroIncludeUpperEdgeAndOmitEmptyBins()
    {
        var card = Card([-4, -3.5, null, 4, 5, 6], DistributionMode.Histogram, 1);
        var layout = Distribution.Geometry(card, Theme.Paper);
        Assert.Equal(5, layout.Count);
        Assert.Equal([-4d, 4, 5], layout.Marks.Select(m => m.Bin!.Min));
        Assert.Equal(["5", "6"], Distribution.Members(card, Theme.Paper, layout.Marks[^1].Hit.Key)!);
        Assert.Equal(6, layout.Max);
    }

    [Fact]
    public void TinyWidthsFallbackAndConstantsAreCentred()
    {
        var invalid = Card([0, 1], width: 1e-30);
        Assert.True(Distribution.Geometry(invalid, Theme.Paper).Fallback);
        var constant = Distribution.Geometry(Card([4, 4], width: 2), Theme.Paper);
        Assert.Equal(3, constant.Min);
        Assert.Equal(5, constant.Max);
        Assert.Equal(constant.Marks[0].Hit.X, constant.Marks[1].Hit.X);
        Assert.True(constant.Marks[0].Hit.Y > constant.Marks[1].Hit.Y);
        Assert.False(Distribution.Geometry(Card([null]), Theme.Paper).Forced);
    }

    [Fact]
    public void CountAndDensityCanForceHistogram()
    {
        Assert.True(Distribution.Geometry(Card(Enumerable.Repeat<double?>(1, 70).ToArray()), Theme.Paper).Forced);
        Assert.True(Distribution.Geometry(Card(Enumerable.Range(0, 401).Select(i => (double?)i).ToArray()), Theme.Paper).Forced);
        var result = Distribution.Geometry(Card(Enumerable.Repeat<double?>(1, 25).ToArray()), Theme.Paper);
        Assert.False(result.Forced);
        Assert.InRange(result.Marks[0].Hit.W, 3, 5);
    }

    [Fact]
    public void LocalChoicesSurviveWiringAndResetOnDefaultChange()
    {
        var a = new CanvasCardState();
        var b = new CanvasCardState();
        var card = Card([1, 2, 3]);
        a.Replace(card); b.Replace(card);
        a.Activate(Distribution.HistogramKey);
        Assert.Equal(DistributionMode.Histogram, Mode(a));
        Assert.Equal(DistributionMode.Dots, Mode(b));
        a.Replace(card with { Distribution = new(Wired: DistributionMode.Dots) });
        Assert.Equal(DistributionMode.Dots, Mode(a));
        Assert.DoesNotContain(CardMeasure.Hits(a.Spec!, Theme.Paper), h => Distribution.Choice(h.Key).HasValue);
        a.Replace(card);
        Assert.Equal(DistributionMode.Histogram, Mode(a));
        b.Activate(Distribution.DotsKey);
        card = card with { Distribution = new(DistributionMode.Histogram) };
        a.Replace(card); b.Replace(card);
        Assert.Null(a.Spec!.DistributionChoice);
        Assert.Null(b.Spec!.DistributionChoice);
        Assert.Equal(DistributionMode.Histogram, Mode(b));

        static DistributionMode Mode(CanvasCardState state) => Distribution.Geometry(state.Spec!, Theme.Paper).Mode;
    }

    [Theory]
    [InlineData(CardModifiers.None, "1,2")]
    [InlineData(CardModifiers.Shift, "1,2,3")]
    [InlineData(CardModifiers.Control, "3")]
    [InlineData(CardModifiers.Control | CardModifiers.Shift, "1,2,3")]
    public void PartlySelectedBinUsesOneMemberSetOperation(CardModifiers modifiers, string expected)
    {
        var state = new CanvasCardState();
        var card = Card([.1, .2, 2.1], DistributionMode.Histogram, 1);
        state.Replace(card);
        state.Activate("1");
        state.Activate("3", BrushSelectMode.Add);
        var spec = state.Spec!;
        var hit = Distribution.Geometry(spec, Theme.Paper).Marks[0].Hit;
        var p = new Point2(hit.X + hit.W / 2, hit.Y + hit.H / 2);
        var controller = new CardInteractionController();
        var frame = Frame(spec);
        Assert.Equal(CardResponse.Capture, controller.Down(frame, p, p, modifiers).Response);
        var eventRevision = state.Current.EventRevision;
        var action = controller.Up(frame, p, p, modifiers).Action!;
        Assert.Equal(["1", "2"], action.Members!);
        action.Apply(state);
        Assert.Equal(expected, string.Join(",", state.Current.SelectedKeys));
        Assert.Equal(eventRevision + 1, state.Current.EventRevision);

        controller.Down(Frame(state.Spec!), p, p, CardModifiers.None);
        state.Replace(card with { Distribution = new(DistributionMode.Histogram, BinWidth: 2) });
        Assert.Null(controller.Up(Frame(state.Spec!), p, p, CardModifiers.None).Action);
        Assert.Equal(expected, string.Join(",", state.Current.SelectedKeys));
    }

    [Fact]
    public void MappingUsesOriginalValuesAndReplacementThresholds()
    {
        var scale = ColourScale.Create([0, 1, 2], min: 0, max: 2, thresholds: [1], thresholdColours: ["#AABBCC"]);
        var card = Card([-5, null, 1, 20]);
        var coloured = Distribution.Create(card, scale.Legend, null);
        Assert.Equal(card.Values.Where(v => v.HasValue).Select(scale.ColourAt), coloured.Colors);
        Assert.Same(scale.Legend.Thresholds, coloured.Thresholds);
        var geometry = Distribution.Geometry(coloured, Theme.Paper);
        Assert.True(geometry.Min <= -5 && geometry.Max >= 20);
        var replacements = ThresholdSet.Resolve([10], ["#112233"], null, []);
        var replaced = Distribution.Create(card, scale.Legend, replacements);
        Assert.Equal("#112233", replaced.Colors[^1]);
        Assert.NotEqual("#AABBCC", replaced.Colors[1]);
        var colors = new[] { "#ABCDEF", "#ABCDEF", "#ABCDEF", "#ABCDEF" };
        Assert.Equal(colors[..3], Distribution.Create(card with { Colors = colors }, scale.Legend, replacements).Colors);
        Assert.Throws<ArgumentException>(() => Distribution.Create(card with { Colors = colors[..3] }, scale.Legend, null));
        Assert.Throws<ArgumentException>(() => Distribution.Create(card, scale.Legend with { Mapping = null }, null));
    }

    private static CardInteractionFrame Frame(CardSpec spec) => new(
        [new(spec, Theme.Paper, new(0, 0, spec.Width, 218), CardMeasure.Hits(spec, Theme.Paper))]);
}
