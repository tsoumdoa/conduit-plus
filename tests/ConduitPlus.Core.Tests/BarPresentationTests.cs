using System.Collections.Immutable;
using System.Drawing.Imaging;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

[Collection("Legend text metrics")]
public sealed class BarPresentationTests
{
    private static CardSpec Bar(params double?[] values) => new()
    {
        Id = "bar", Kind = CardKind.Bar, Title = "Bar", Values = values,
        Keys = Enumerable.Range(0, values.Length).Select(i => $"k{i}").ToArray(),
        Labels = Enumerable.Range(0, values.Length).Select(i => $"Row {i + 1}").ToArray()
    };

    [Theory]
    [InlineData(BarGoodWhen.Below, -4, true)] [InlineData(BarGoodWhen.Below, 0, true)] [InlineData(BarGoodWhen.Below, 4, false)]
    [InlineData(BarGoodWhen.Above, -4, false)] [InlineData(BarGoodWhen.Above, 0, true)] [InlineData(BarGoodWhen.Above, 4, true)]
    public void DirectionIncludesEqualityAndExplicitColorsWin(BarGoodWhen direction, double value, bool good)
    {
        var c = Bar(value) with { Threshold = 0, GoodWhen = direction };
        Assert.Equal(good, BarPresentation.IsGood(value, 0, direction));
        Assert.Equal(good ? Theme.Paper.Good : Theme.Paper.Bad, BarPresentation.Color(c, Theme.Paper, 0));
        Assert.Equal(Theme.Paper.Data, BarPresentation.Color(c with { HasThreshold = false }, Theme.Paper, 0));
        Assert.Equal("#AABBCC", BarPresentation.Color(c with { Colors = ["#AABBCC"] }, Theme.Paper, 0));
    }

    [Theory]
    [InlineData(-7, false)] [InlineData(-5, true)] [InlineData(-2, true)]
    [InlineData(0, true)] [InlineData(5, true)] [InlineData(9, false)]
    public void WithinIncludesBothToleranceEdgesAndColorsStillWin(double value, bool good)
    {
        var c = Bar(value) with { GoodWhen = BarGoodWhen.Within, Threshold = 5 };
        c.Validate();
        Assert.Null(BarPresentation.ThresholdWarning(c));
        Assert.Equal(good, BarPresentation.IsGood(value, 5, BarGoodWhen.Within));
        Assert.Equal(good ? Theme.Paper.Good : Theme.Paper.Bad, BarPresentation.Color(c, Theme.Paper, 0));
        Assert.Equal("#AABBCC", BarPresentation.Color(c with { Colors = ["#AABBCC"] }, Theme.Paper, 0));
        Assert.Equal(Theme.Paper.Data, BarPresentation.Color(c with { HasThreshold = false }, Theme.Paper, 0));
    }

    [Theory]
    [InlineData(-5, true)] [InlineData(0, true)] [InlineData(5, false)]
    public void InvalidOrMissingToleranceWarnsAndPaintsPlainBars(double threshold, bool supplied)
    {
        var c = Bar(-7) with { GoodWhen = BarGoodWhen.Within, Threshold = threshold, HasThreshold = supplied };
        c.Validate();
        Assert.Equal("Within needs a positive Threshold (the tolerance).", BarPresentation.ThresholdWarning(c));
        Assert.False(BarPresentation.UsesThreshold(c));
        if (threshold <= 0) Assert.False(BarPresentation.IsGood(0, threshold, BarGoodWhen.Within));
        var canvas = new Commands(); CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Contains(canvas.Rects, r => r.Fill == Theme.Paper.Data && r.Rect.Height == 8);
        Assert.DoesNotContain(canvas.Rects, r => r.Fill == Theme.Paper.Line && r.Rect.Height == 12);
        Assert.Empty(canvas.Lines);
        Assert.Equal("#AABBCC", BarPresentation.Color(c with { Colors = ["#AABBCC"] }, Theme.Paper, 0));
        Assert.Contains("Within needs a positive Threshold (the tolerance).", Tooltip.For(c, "k0")!);
        Assert.Throws<ArgumentException>(() => (c with { Threshold = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Threshold = -5, GoodWhen = BarGoodWhen.Below }).Validate());
    }

    [Fact]
    public void WithinSortsByMagnitudeWithStableTiesAndMissingLast()
    {
        var c = Bar(-7, null, 5, -5, 0, 9, -2, 2, null) with { GoodWhen = BarGoodWhen.Within, Threshold = 5, BarSort = BarOrder.WorstFirst };
        Assert.Equal(new[] { 5, 0, 2, 3, 6, 7, 4, 1, 8 }, BarPresentation.Order(c));
        Assert.Equal(new[] { 4, 6, 7, 2, 3, 0, 5, 1, 8 }, BarPresentation.Order(c with { BarSort = BarOrder.BestFirst }));
        Assert.Equal(BarPresentation.Order(c), BarPresentation.Order(c with { HasThreshold = false }));
        Assert.Equal(Enumerable.Range(0, 9), BarPresentation.Order(c with { BarSort = BarOrder.Original }));
        Assert.Equal(new[] { "Row 1", "−7", "140% of threshold", "Within ±Threshold is good; the part beyond it is marked.", "k0" }, Tooltip.For(c, "k0"));
    }

    [Theory]
    [InlineData("Paper")] [InlineData("Night")] [InlineData("Smoke")] [InlineData("Blueprint")]
    public void WithinPaintsOneCorridorAndCenterWithoutTracks(string name)
    {
        Theme.TryGet(name, out var theme);
        // Both bounds must fit even when data is one-sided, inside the tolerance, or entirely missing.
        foreach (var values in new double?[][] { [-7, 0, 9, null], [9], [-7], [2], [null] })
        {
            var c = Bar(values) with { GoodWhen = BarGoodWhen.Within, Threshold = 5 };
            var canvas = new Commands(); CardPainter.Paint(canvas, c, theme);
            Assert.DoesNotContain(canvas.Rects, r => r.Fill == theme.Track && r.Rect.Height == 8);
            var band = Assert.Single(canvas.Rects, r => r.Fill == theme.ToleranceBand);
            var hits = CardMeasure.DataHits(c, theme);
            Assert.Equal(hits[0].Y, band.Rect.Y);
            Assert.Equal(hits[^1].Y + hits[^1].H - hits[0].Y, band.Rect.Height);
            Assert.Single(canvas.Lines);

        }
    }

    [Theory]
    [InlineData("Paper")] [InlineData("Night")] [InlineData("Smoke")] [InlineData("Blueprint")] [InlineData("Auto")]
    public void WithinCorridorPreservesInteractionFeedback(string name)
    {
        Theme.TryGet(name, out var t);
        Assert.True(ColorRgba.Parse(t.ToleranceBand).A < 255);
        var c = Bar(-5, 0, 5) with { GoodWhen = BarGoodWhen.Within, Threshold = 5, SelectedKeys = ["k0"], HoverKey = "k1" };
        var canvas = new Commands(); CardPainter.Paint(canvas, c, t);
        var band = Assert.Single(canvas.Rects, r => r.Fill == t.ToleranceBand);
        var selection = Assert.Single(canvas.Rects, r => r.Fill == t.Highlight);
        var hover = Assert.Single(canvas.Rects, r => r.Fill == t.Track);
        Assert.True(canvas.Rects.IndexOf(selection) < canvas.Rects.IndexOf(band));
        Assert.True(canvas.Rects.IndexOf(hover) < canvas.Rects.IndexOf(band));
        Assert.Contains(t.Accent, canvas.Outlines);
    }

    [Fact]
    public void WithinSignsFollowUsedToleranceAndFormattedNonzeroText()
    {
        var c = Bar(9, 0, .0000001, -7, null) with { GoodWhen = BarGoodWhen.Within, Threshold = 5 };
        var layout = BarPresentation.Layout(c, Theme.Paper);
        Assert.Equal(new[] { "+9.000000", "0.000000", "0.000000", "\u22127.000000", "\u2014" }, layout.Values);
        for (var i = 0; i < 4; i++) Assert.Equal(layout.Values[i], Tooltip.For(c, c.Keys[i])![1]);
        foreach (var fallback in new[] { c with { HasThreshold = false }, c with { Threshold = 0 }, c with { Threshold = -1 } })
            Assert.Equal("9.000000", BarPresentation.Layout(fallback, Theme.Paper).Values[0]);
        Assert.Equal("0", BarPresentation.DisplayValue(.1, 0, true));
        Assert.Equal("0.00", BarPresentation.DisplayValue(.001, 2, true));
    }

    [Theory]
    [InlineData(-9)] [InlineData(-5)] [InlineData(0)] [InlineData(5)] [InlineData(9)]
    public void WithinSplitsExactlyAtTolerance(double value)
    {
        var c = Bar(value) with { GoodWhen = BarGoodWhen.Within, Threshold = 5 };
        var t = Theme.Paper; var canvas = new Commands(); CardPainter.Paint(canvas, c, t);
        var h = CardMeasure.DataHits(c, t)[0]; var layout = BarPresentation.Layout(c, t);
        var span = (h.W - layout.LabelWidth - layout.ValueWidth) / 2;
        var origin = h.X + layout.LabelWidth + span;
        var scale = span / Math.Max(5, Math.Abs(value));
        var inside = Assert.Single(canvas.Rects, r => r.Fill == t.Data);
        Assert.Equal(Math.Min(5, Math.Abs(value)) * scale, inside.Rect.Width, 6);
        Assert.Equal(value < 0 ? origin - inside.Rect.Width : origin, inside.Rect.X, 6);
        var bad = canvas.Rects.Where(r => r.Fill == t.Bad).ToArray();
        if (Math.Abs(value) <= 5) Assert.Empty(bad);
        else
        {
            var outside = Assert.Single(bad).Rect;
            Assert.Equal((Math.Abs(value) - 5) * scale, outside.Width, 6);
            Assert.Equal(value < 0 ? origin - Math.Abs(value) * scale : origin + 5 * scale, outside.X, 6);
        }
    }

    [Fact]
    public void WithinCorridorSpansOnlyVisibleRowsAndSignsArePainted()
    {
        var c = Bar(1, 9, -7, 0, 2) with { GoodWhen = BarGoodWhen.Within, Threshold = 5, MaxRows = 3, ScrollOffset = 1 };
        var t = Theme.Paper; var canvas = new Commands(); CardPainter.Paint(canvas, c, t);
        var hits = CardMeasure.DataHits(c, t);
        var band = Assert.Single(canvas.Rects, r => r.Fill == t.ToleranceBand).Rect;
        Assert.Equal(hits[0].Y, band.Y);
        Assert.Equal(hits[^1].Y + hits[^1].H, band.Y + band.Height);
        Assert.DoesNotContain(canvas.Rects, r => r.Fill == t.Track && r.Rect.Height == 8);
        Assert.Contains(canvas.Texts, x => x.Text == "+9");
        Assert.Contains(canvas.Texts, x => x.Text == "\u22127");
        Assert.Contains(canvas.Texts, x => x.Text == "0");
        var center = Assert.Single(canvas.Lines);
        Assert.Equal(band.X + band.Width / 2, center.From.X, 6);
        Assert.Equal(band.Y, center.From.Y);
        Assert.Equal(band.Y + band.Height, center.To.Y);
    }

    [Fact]
    public void TextOptionsAndRangesHaveExactDiagnostics()
    {
        Assert.Equal(BarGoodWhen.Below, BarPresentation.ParseGoodWhen(null));
        Assert.Equal(BarGoodWhen.Above, BarPresentation.ParseGoodWhen(" aBoVe "));
        Assert.Equal(BarGoodWhen.Within, BarPresentation.ParseGoodWhen(" wItHiN "));
        Assert.Equal(new[] { "Below", "Above", "Within" }, InputOptions.BarGoodWhen.Select(o => o.Label));
        Assert.True(InputOptions.BarGoodWhen[2].Matches("within"));
        Assert.Equal("Good when must be Below, Above or Within.", Assert.Throws<ArgumentException>(() => (Bar(1) with { GoodWhen = (BarGoodWhen)99 }).Validate()).Message);
        Assert.Equal(BarOrder.WorstFirst, BarPresentation.ParseSort(" WORST FIRST "));
        Assert.Equal(BarOrder.Original, BarPresentation.ParseSort(null));
        Assert.True(InputOptions.BarGoodWhen[0].Matches(null));
        Assert.True(InputOptions.BarSort[0].Matches(null));
        Assert.Equal("Good when must be Below, Above or Within.", Assert.Throws<ArgumentException>(() => BarPresentation.ParseGoodWhen("higher")).Message);
        Assert.Equal("Sort must be Original, Worst first or Best first.", Assert.Throws<ArgumentException>(() => BarPresentation.ParseSort("descending")).Message);
        foreach (var n in new[] { -1, 201 })
            Assert.Equal("Visible rows must be 0–200.", Assert.Throws<ArgumentException>(() => (Bar(1) with { MaxRows = n }).Validate()).Message);
        foreach (var n in new[] { 0, 1, 12, 200 }) (Bar(1) with { MaxRows = n }).Validate();
        Assert.Equal(12, Bar(1).MaxRows);
        Assert.Equal(12, CardJson.Deserialize<CardSpec>("{\"id\":\"old\",\"kind\":\"bar\"}").MaxRows);
    }

    [Fact]
    public void StableSortKeepsMissingLastAndRetainsEveryRowFieldAndKeyState()
    {
        var c = Bar(2, null, -4, 2, 7, null) with { BarSort = BarOrder.WorstFirst, Colors = ["#000001", "#000002", "#000003", "#000004", "#000005", "#000006"],
            Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("k4", new(["m1", "m2"])), SelectedKeys = ["k4"], HoverKey = "k2" };
        Assert.Equal(new[] { 4, 0, 3, 2, 1, 5 }, BarPresentation.Order(c));
        Assert.Equal(new[] { 2, 0, 3, 4, 1, 5 }, BarPresentation.Order(c with { GoodWhen = BarGoodWhen.Above }));
        Assert.Equal(new[] { 2, 0, 3, 4, 1, 5 }, BarPresentation.Order(c with { BarSort = BarOrder.BestFirst }));
        Assert.Equal(new[] { 4, 0, 3, 2, 1, 5 }, BarPresentation.Order(c with { BarSort = BarOrder.BestFirst, GoodWhen = BarGoodWhen.Above }));
        Assert.Equal(new[] { 2, 0, 3, 4, 1, 5 }, BarPresentation.Order(c with { GoodWhen = BarGoodWhen.Above, HasThreshold = false }));
        Assert.Equal(Enumerable.Range(0, 6), BarPresentation.Order(c with { BarSort = BarOrder.Original }));
        var canvas = new Commands(); CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Equal(new[] { "Row 5", "Row 1", "Row 4", "Row 3", "Row 2", "Row 6" }, canvas.Texts.Where(x => x.Text.StartsWith("Row ")).Select(x => x.Text));
        Assert.Equal(c.Keys.Order(), CardMeasure.DataHits(c, Theme.Paper).Select(h => h.Key).Order());
        Assert.Contains(canvas.Rects, r => r.Fill == Theme.Paper.Highlight && r.Rect.Y == 36);
        Assert.Contains(canvas.Rects, r => r.Fill == "#000005" && r.Rect.Y == 43);
        Assert.Equal(new[] { "m1", "m2" }, c.Groups["k4"].Members);
        Assert.Equal(new[] { "Row 3", "−4", "-400% of threshold", "k2" }, Tooltip.For(c, "k2"));
    }

    [Fact]
    public void ScrollRevealAndCanvasRetainFullDataMembershipAndClampAfterShrink()
    {
        var c = Bar(Enumerable.Range(0, 40).Select(i => (double?)i).ToArray()) with { BarSort = BarOrder.WorstFirst };
        Assert.Equal(12, TableScroll.Visible(c)); Assert.Equal(28, TableScroll.MaxOffset(c));
        Assert.Equal(28, TableScroll.Reveal(c, "k0"));
        Assert.Equal("k30", BrushPresentation.FirstNewRow(c, new HashSet<string>(), new HashSet<string> { "k0", "k30" }));
        var state = new CanvasCardState(); state.Replace(c); state.Activate("k0");
        Assert.Equal(28, state.Spec!.ScrollOffset);
        Assert.Contains("k0", (IEnumerable<string>)state.Current.SelectedMemberKeys);
        state.Scroll(3); state.Hover("k30"); Assert.Equal(3, state.Spec.ScrollOffset);
        state.Replace(c with { Values = c.Values[..5], Keys = c.Keys[..5], Labels = c.Labels[..5] });
        Assert.Equal(0, state.Spec.ScrollOffset);
        Assert.Contains("k0", (IEnumerable<string>)state.Current.SelectedMemberKeys);
        Assert.False(TableScroll.CanScroll(c with { MaxRows = 0 }));
        Assert.Equal(40, TableScroll.Visible(c with { MaxRows = 0 }));
        Assert.Equal(4, TableScroll.Visible(c with { MaxRows = 0, Form = new(4) }));
        Assert.Equal("29–40 of 40", TableScroll.Footer(c with { ScrollOffset = 100 }));
        var bar = TableScroll.Bar(c, Theme.Paper)!.Value;
        Assert.Equal(28, TableScroll.OffsetForThumbTop(c, Theme.Paper, bar.Track.Y + bar.Track.Height));
        Assert.Equal(0, TableScroll.Offset(c with { ScrollOffset = -1 }));
    }

    [Fact]
    public void NewGroupSelectionRevealsItsSortedRowWithoutSnappingOnRefresh()
    {
        var c = Bar(Enumerable.Range(0, 40).Select(i => (double?)i).ToArray()) with {
            BarSort = BarOrder.WorstFirst, Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("k0", new(["m1", "m2"])) };
        var state = new CanvasCardState(); state.Replace(c); state.Activate("k0");
        Assert.Equal(28, state.Spec!.ScrollOffset);
        Assert.Equal(2, state.Spec.GroupStates["k0"].SelectedMemberCount);
        state.Scroll(0); state.Refresh(); Assert.Equal(0, state.Spec.ScrollOffset);
        state.Hover("k0"); Assert.Equal(0, state.Spec.ScrollOffset);
    }

    [Fact]
    public void FormattingUsesFixedColumnPrecisionAndSuppressesNegativeZero()
    {
        var values = new double?[] { -4, .34, 12.5, -.0001, null, 1.234, 1.235 };
        Assert.Equal(5, BarPresentation.Precision(values));
        Assert.Equal(new[] { "−4.00", "0.34", "12.50", "0.00", "—", "1.23", "1.24" }, values.Select(v => BarPresentation.Format(v, 2)));
        Assert.Equal(0, BarPresentation.Precision([-4, 0, 12, null]));
        Assert.Equal(1, BarPresentation.Precision([1.1, 1.2]));
        Assert.Equal(2, BarPresentation.Precision([1.11, 1.12]));
        Assert.Equal("−4.25", MetricFormatting.Format("-4.25"));
        Assert.Equal("−4.3", MetricFormatting.Format("-4.25", 1));
        Assert.Equal("0.00", MetricFormatting.Format("-0.0001", 2));
        Assert.Equal("pre-design", MetricFormatting.Format("pre-design"));
    }

    [Theory]
    [InlineData(0, .005, 2, "0.00", "0.01")]
    [InlineData(0, -.005, 2, "0.00", "−0.01")]
    [InlineData(.005, -.005, 2, "0.01", "−0.01")]
    [InlineData(.5, 1, 1, "0.5", "1.0")]
    [InlineData(1.123, 2.123, 2, "1.12", "2.12")]
    [InlineData(1.123, 1.223, 2, "1.12", "1.22")]
    [InlineData(1.123, 1.133, 2, "1.12", "1.13")]
    [InlineData(1.123, 1.124, 2, "1.12", "1.12")]
    public void AutomaticPrecisionUsesTheSameAwayFromZeroRoundingAsDisplay(double a, double b, int expected, string first, string second)
    {
        var precision = BarPresentation.Precision([a, b]);
        Assert.Equal(expected, precision);
        Assert.Equal(first, BarPresentation.Format(a, precision));
        Assert.Equal(second, BarPresentation.Format(b, precision));
        Assert.Equal(new[] { first, second }, BarPresentation.Layout(Bar(a, b), Theme.Paper).Values);
    }

    [Theory]
    [InlineData(new double[] { -4, -0.12, 0, 0.34, 12.5 }, 2, new[] { "−4.00", "−0.12", "0.00", "0.34", "12.50" })]
    [InlineData(new double[] { 1, 2, 3 }, 0, new[] { "1", "2", "3" })]
    [InlineData(new double[] { 0.0012, 0.0034 }, 4, new[] { "0.0012", "0.0034" })]
    [InlineData(new double[] { 0, 0.005 }, 2, new[] { "0.00", "0.01" })]
    [InlineData(new double[] { 1e6, 2.5 }, 1, new[] { "1000000.0", "2.5" })]
    [InlineData(new double[] { -0.0001, 0.34 }, 5, new[] { "−0.00010", "0.34000" })]
    [InlineData(new double[] { 1234567890123456, 0.12 }, 2, new[] { "1234567890123456.00", "0.12" })]
    public void ColumnPrecisionIsUsedForEveryDisplayedBar(double[] values, int decimals, string[] expected)
    {
        var card = Bar(values.Select(v => (double?)v).ToArray());
        Assert.Equal(decimals, BarPresentation.Precision(card.Values));
        Assert.Equal(expected, BarPresentation.Layout(card, Theme.Paper).Values);
    }

    [Fact]
    public void MissingValuesDoNotAffectPrecisionAndNaNRemainsInvalidCardInput()
    {
        Assert.Equal(0, BarPresentation.Precision([null, null]));
        Assert.Equal(2, BarPresentation.Precision([null, double.NaN, -0.12, 0.34]));
        Assert.Equal(new[] { "−0.12", "—", "0.34" }, BarPresentation.Layout(Bar(-0.12, null, 0.34), Theme.Paper).Values);
        Assert.Throws<ArgumentException>(() => Bar(double.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => BarPresentation.Format(double.NaN, 2));
    }

    [Theory]
    [InlineData("-0.00")] [InlineData("−0.00")]
    public void NumericMinusPreservesZeroDecimals(string text)
    {
        Assert.Equal("0.00", NumericText.TrueMinus(text));
        Assert.Equal("0.00", MetricFormatting.Format(text));
        Assert.Equal("0.00", MetricFormatting.Format("-0.001", 2));
    }

    [Theory]
    [InlineData(CardKind.Bar)] [InlineData(CardKind.Table)]
    public void ScrollFooterUsesOnlyTheVisibleRange(CardKind kind)
    {
        var c = Bar(Enumerable.Range(0, 40).Select(i => (double?)i).ToArray()) with {
            Kind = kind, ScrollOffset = 19, MaxRows = 12, Headers = ["Value"],
            Rows = Enumerable.Range(0, 40).Select(i => new TableRow($"k{i}", ["value"])).ToArray() };
        Assert.Equal("20–31 of 40", TableScroll.Footer(c));
    }

    [Fact]
    public void GroupMemberReplacementRevealsFirstNewSortedRowEvenWhenSelectionShrinks()
    {
        var c = Bar(Enumerable.Range(0, 40).Select(i => (double?)i).ToArray()) with {
            BarSort = BarOrder.WorstFirst, MaxRows = 12,
            Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("k0", new(["m1", "m2", "m3"]))
                .Add("k1", new(["m2", "m3"])) };
        var state = new CanvasCardState(); state.Replace(c);
        state.Activate("m1"); Assert.Equal(28, state.Spec!.ScrollOffset);
        state.Scroll(0); state.Activate("m2");
        Assert.Equal(27, state.Spec.ScrollOffset); // k1 precedes k0; count stayed one in k0.
        state.Scroll(0); state.Activate("k1");
        Assert.Equal(27, state.Spec.ScrollOffset);
        Assert.Equal(2, state.Current.SelectedMemberKeys.Count);
        state.Scroll(0); state.Activate("m1"); // Replace two with one different member.
        Assert.Equal(28, state.Spec.ScrollOffset);
        state.Scroll(0); state.Refresh(); state.Hover("k0");
        state.Activate("m1"); Assert.Equal(0, state.Spec.ScrollOffset); // unchanged selection
        state.Activate("m1", BrushSelectMode.Remove); Assert.Equal(0, state.Spec.ScrollOffset);
    }

    [Fact]
    public void TwoThousandBarsReuseMeasurementsThroughRealCanvasStoreInteractionUpdates()
    {
        var original = TextMetrics.Measurer; var calls = 0;
        try
        {
            TextMetrics.Measurer = (text, size, font) => { calls++; return TextMetrics.Estimate(text, size, font); };
            var c = Bar(Enumerable.Range(0, 2000).Select(i => (double?)i).ToArray());
            var state = new CanvasCardState(); state.Replace(c);
            var owned = state.Spec!;
            var layout = BarPresentation.Layout(owned, Theme.Paper);
            Assert.Equal(4000, calls);
            void Reused()
            {
                Assert.Same(owned.Values, state.Spec!.Values); Assert.Same(owned.Labels, state.Spec.Labels);
                Assert.Same(layout, BarPresentation.Layout(state.Spec, Theme.Paper)); Assert.Equal(4000, calls);
            }
            state.Scroll(1); Reused();
            state.Hover("k3"); Reused();
            state.Activate("k1999"); Reused();
            state.Scroll(2); state.Refresh(); Reused();
            state.Replace(c with { BarSort = BarOrder.WorstFirst }); Reused();
            Assert.Same(layout, BarPresentation.Layout(state.Spec!, Theme.Night)); Assert.Equal(4000, calls);
            void Invalidated(CardSpec card, Theme theme)
            {
                var before = calls; BarPresentation.Layout(card, theme); Assert.True(calls > before);
            }
            Invalidated(state.Spec! with { Width = 600 }, Theme.Paper);
            Invalidated(state.Spec!, Theme.Paper with { Padding = 16 });
            Invalidated(state.Spec!, Theme.Paper with { FontSize = 18 });
            Invalidated(state.Spec!, Theme.Paper with { Font = "Arial" });
            TextMetrics.Measurer = (text, size, font) => { calls++; return TextMetrics.Estimate(text, size, font); };
            Invalidated(state.Spec!, Theme.Paper);
            var before = calls;
            c.Values[^1] = -123.45; state.Replace(c); BarPresentation.Layout(state.Spec!, Theme.Paper);
            Assert.True(calls > before); Assert.Equal(1999d, owned.Values[^1]);
            before = calls;
            c.Labels[^1] = "Changed offscreen label"; state.Replace(c); BarPresentation.Layout(state.Spec!, Theme.Paper);
            Assert.True(calls > before); Assert.Equal("Row 2000", owned.Labels[^1]);
        }
        finally { TextMetrics.Measurer = original; }
    }

    [Fact]
    public void StoreOwnsBarArraysAndDetectsCallerMutation()
    {
        var c = Bar(1, 2); var store = new CardStore(); store.Update([c]); var owned = store.Snapshot[0];
        Assert.NotSame(c.Values, owned.Values); Assert.NotSame(c.Labels, owned.Labels);
        store.Update([c with { HoverKey = "k0" }]);
        Assert.Same(owned.Values, store.Snapshot[0].Values); Assert.Same(owned.Labels, store.Snapshot[0].Labels);
        c.Values[1] = 3; store.Update([c]); Assert.NotSame(owned.Values, store.Snapshot[0].Values);
        Assert.Equal(2d, owned.Values[1]); Assert.Equal(3d, store.Snapshot[0].Values[1]);
        c.Labels[1] = "Changed"; store.Update([c]); Assert.NotSame(owned.Labels, store.Snapshot[0].Labels);
        Assert.Equal("Row 2", owned.Labels[1]); Assert.Equal("Changed", store.Snapshot[0].Labels[1]);
    }

    [Theory]
    [InlineData("Paper", "#515968")] [InlineData("Night", "#A7AEBB")]
    [InlineData("Smoke", "#B8BFCC")] [InlineData("Blueprint", "#B5CBE8")]
    public void PlainBarsUseNeutralDataWhileSelectionKeepsAccent(string name, string data)
    {
        Theme.TryGet(name, out var theme);
        Assert.Equal(data, theme.Data);
        var c = Bar(1) with { HasThreshold = false, SelectedKeys = ["k0"] };
        var canvas = new Commands(); CardPainter.Paint(canvas, c, theme);
        Assert.Contains(canvas.Rects, r => r.Fill == data && r.Rect.Height == 8);
        Assert.Contains(canvas.Rects, r => r.Fill == theme.Accent && r.Rect.Width == 3);
        Assert.Equal(Theme.Paper.Data, Theme.Auto.Resolve(false).Data);
        Assert.Equal(Theme.Night.Data, Theme.Auto.Resolve(true).Data);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MetricPaintUsesTrueMinusForDirectSpecs(bool compact)
    {
        var canvas = new Commands(); CardPainter.Paint(canvas, new CardSpec {
            Id = "metric", Kind = CardKind.Metric, Value = "-4.25", Form = new(Compact: compact) }, Theme.Paper);
        Assert.Contains(canvas.Texts, t => t.Text == "−4.25");
        Assert.DoesNotContain(canvas.Texts, t => t.Text == "-4.25");
    }

    [Fact]
    public void MeasuredColumnIsCachedAcrossViewChangesAndNumericRightEdgesCoincide()
    {
        var original = TextMetrics.Measurer; var calls = 0;
        try
        {
            TextMetrics.Measurer = (text, size, font) => { calls++; return TextMetrics.Estimate(text, size, font); };
            var c = Bar(-40000, .34, 12.5) with { Width = 420 };
            var layout = BarPresentation.Layout(c, Theme.Paper); var measured = calls;
            Assert.True(layout.ValueWidth > 55);
            Assert.Same(layout, BarPresentation.Layout(c with { ScrollOffset = 1, SelectedKeys = ["k1"], HoverKey = "k0", BarSort = BarOrder.WorstFirst }, Theme.Paper));
            Assert.Equal(measured, calls);
            var canvas = new Commands(); CardPainter.Paint(canvas, c, Theme.Paper);
            var texts = canvas.Texts.Where(x => layout.Values.Contains(x.Text)).ToArray();
            Assert.Equal(3, texts.Length);
            Assert.All(texts, x => Assert.Equal(c.Width - Theme.Paper.Padding, x.Point.X + TextMetrics.Estimate(x.Text, 12, Theme.Paper.Font).Width, 6));
            Assert.Equal(measured, calls); // no label/value measuring on paint
            var next = BarPresentation.Layout(c with { Values = [-1, .1, 2] }, Theme.Paper);
            Assert.True(calls > measured); Assert.True(next.ValueWidth < layout.ValueWidth);
        }
        finally { TextMetrics.Measurer = original; }
    }

    [Fact]
    public void ScaleIncludesOffscreenValues()
    {
        var c = Bar(1, 2, 100) with { MaxRows = 1, HasThreshold = false };
        var canvas = new Commands(); CardPainter.Paint(canvas, c, Theme.Paper);
        var track = Assert.Single(canvas.Rects, r => r.Fill == Theme.Paper.Track && r.Rect.Height == 8).Rect;
        var fill = Assert.Single(canvas.Rects, r => r.Fill == Theme.Paper.Data && r.Rect.Height == 8).Rect;
        Assert.Equal(track.Width / 100, fill.Width, 6);
    }

    [Theory]
    [InlineData("Paper")] [InlineData("Night")] [InlineData("Smoke")] [InlineData("Blueprint")]
    public void RenderBarFixturesForVisualInspection(string name)
    {
        Theme.TryGet(name, out var theme);
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "pr-d"); Directory.CreateDirectory(output);
        var signed = Bar(-4, -.12, 0, .34, 12.5, null) with { Threshold = 0, Width = 360 };
        var many = Bar(Enumerable.Range(0, 40).Select(i => (double?)(i + 1)).ToArray()) with { Width = 360, HasThreshold = false, SelectedKeys = ["k30"], HoverKey = "k28" };
        many = many with { ScrollOffset = TableScroll.Reveal(many, "k30") };
        foreach (var (label, card) in new[] {
            ("within", Bar(Enumerable.Range(-7, 17).Select(v => (double?)v).ToArray()) with {
                Title = "Within ±5 mm · worst first", GoodWhen = BarGoodWhen.Within, Threshold = 5, BarSort = BarOrder.WorstFirst, MaxRows = 0, Width = 360 }),
            ("within-interaction", Bar(-7, -5, 0, 5, 9, null) with {
                Title = "Within interaction", GoodWhen = BarGoodWhen.Within, Threshold = 5, Width = 360,
                SelectedKeys = ["k1"], HoverKey = "k3" }),
            ("signed-below", signed with { Title = "Signed deltas · good below zero" }),
            ("signed-above", signed with { Title = "Signed deltas · good above zero", GoodWhen = BarGoodWhen.Above }),
            ("scrolled-selection", many with { Title = "40 rows · selection revealed" }),
            ("precision-midpoints", Bar(-4, -.005, 0, .005, .34, 12.5) with { Width = 360, Threshold = 0, Title = "Fixed decimals · half-away-from-zero" }),
            ("precision-small", Bar(.0012, .0034, null) with { Width = 360, HasThreshold = false, Title = "Small values · two significant digits" }),
            ("precision-integers", Bar(1, 2, 3) with { Width = 360, HasThreshold = false, Title = "Whole values · no decimal places" }),
            ("precision-mixed-scale", Bar(1e6, 2.5) with { Width = 360, HasThreshold = false, Title = "Mixed scale · fixed column precision" }),
            ("worst-first", signed with { Values = [.34, null, 12.5, -4, 0, -.12], Title = "Worst first · above zero is good", GoodWhen = BarGoodWhen.Above, BarSort = BarOrder.WorstFirst }) })
        {
            using var bitmap = RasterDrawing.Render(card, theme, 2);
            bitmap.Save(Path.Combine(output, $"bar-{label}-{name.ToLowerInvariant()}.png"), ImageFormat.Png);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
        }
    }

    private sealed class Commands : ICanvas2D
    {
        public List<(string Text, Point2 Point)> Texts { get; } = [];
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<string> Outlines { get; } = [];
        public List<(Point2 From, Point2 To, string Color)> Lines { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Estimate(text, size, font);
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft));
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Lines.Add((from, to, color));
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => Outlines.Add(color);
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
