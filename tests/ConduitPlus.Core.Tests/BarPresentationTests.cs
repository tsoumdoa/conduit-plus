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
        Assert.Equal(Theme.Paper.Accent, BarPresentation.Color(c with { HasThreshold = false }, Theme.Paper, 0));
        Assert.Equal("#AABBCC", BarPresentation.Color(c with { Colors = ["#AABBCC"] }, Theme.Paper, 0));
    }

    [Fact]
    public void TextOptionsAndRangesHaveExactDiagnostics()
    {
        Assert.Equal(BarGoodWhen.Below, BarPresentation.ParseGoodWhen(null));
        Assert.Equal(BarGoodWhen.Above, BarPresentation.ParseGoodWhen(" aBoVe "));
        Assert.Equal(BarOrder.WorstFirst, BarPresentation.ParseSort(" WORST FIRST "));
        Assert.Equal(BarOrder.Original, BarPresentation.ParseSort(null));
        Assert.True(InputOptions.BarGoodWhen[0].Matches(null));
        Assert.True(InputOptions.BarSort[0].Matches(null));
        Assert.Equal("Good when must be Below or Above.", Assert.Throws<ArgumentException>(() => BarPresentation.ParseGoodWhen("higher")).Message);
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
        Assert.Equal(new[] { 4, 0, 3, 2, 1, 5 }, BarPresentation.Order(c with { GoodWhen = BarGoodWhen.Above, HasThreshold = false }));
        Assert.Equal(Enumerable.Range(0, 6), BarPresentation.Order(c with { BarSort = BarOrder.Original }));
        var canvas = new Commands(); CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Equal(new[] { "Row 5", "Row 1", "Row 4", "Row 3", "Row 2", "Row 6" }, canvas.Texts.Where(x => x.Text.StartsWith("Row ")).Select(x => x.Text));
        Assert.Equal(c.Keys.Order(), CardMeasure.DataHits(c, Theme.Paper).Select(h => h.Key).Order());
        Assert.Contains(canvas.Rects, r => r.Fill == Theme.Paper.Highlight && r.Rect.Y == 36);
        Assert.Contains(canvas.Rects, r => r.Fill == "#000005" && r.Rect.Y == 43);
        Assert.Equal(new[] { "m1", "m2" }, c.Groups["k4"].Members);
        Assert.Equal(new[] { "Row 3", "-4", "-400% of threshold", "k2" }, Tooltip.For(c, "k2"));
    }

    [Fact]
    public void ScrollRevealAndCanvasRetainFullDataMembershipAndClampAfterShrink()
    {
        var c = Bar(Enumerable.Range(0, 40).Select(i => (double?)i).ToArray()) with { BarSort = BarOrder.WorstFirst };
        Assert.Equal(12, TableScroll.Visible(c)); Assert.Equal(28, TableScroll.MaxOffset(c));
        Assert.Equal(28, TableScroll.Reveal(c, "k0"));
        Assert.Equal("k30", BrushPresentation.FirstNewRow(c with { SelectedKeys = ["k0", "k30"] }, new HashSet<string>()));
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
        Assert.Equal("Rows 29–40 of 40 · scroll", TableScroll.Footer(c with { ScrollOffset = 100 }));
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
    public void FormattingIsCompactInvariantAndSuppressesNegativeZero()
    {
        var values = new double?[] { -4, .34, 12.5, -.0001, null, 1.234, 1.235 };
        Assert.Equal(2, BarPresentation.Precision(values));
        Assert.Equal(new[] { "−4", "0.34", "12.5", "0", "—", "1.23", "1.24" }, values.Select(v => BarPresentation.Format(v, 2)));
        Assert.Equal(0, BarPresentation.Precision([-4, 0, 12, null]));
        Assert.Equal(1, BarPresentation.Precision([1.1, 1.2]));
        Assert.Equal(2, BarPresentation.Precision([1.11, 1.12]));
        Assert.Equal("−4.25", MetricFormatting.Format("-4.25"));
        Assert.Equal("−4.3", MetricFormatting.Format("-4.25", 1));
        Assert.Equal("0.00", MetricFormatting.Format("-0.0001", 2));
        Assert.Equal("pre-design", MetricFormatting.Format("pre-design"));
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
        var fill = Assert.Single(canvas.Rects, r => r.Fill == Theme.Paper.Accent && r.Rect.Height == 8).Rect;
        Assert.Equal(track.Width / 100, fill.Width, 6);
    }

    [Theory]
    [InlineData("Paper")] [InlineData("Night")]
    public void RenderBarFixturesForVisualInspection(string name)
    {
        Theme.TryGet(name, out var theme);
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "pr-d"); Directory.CreateDirectory(output);
        var signed = Bar(-4, -.12, 0, .34, 12.5, null) with { Threshold = 0, Width = 360 };
        var many = Bar(Enumerable.Range(0, 40).Select(i => (double?)(i + 1)).ToArray()) with { Width = 360, HasThreshold = false, SelectedKeys = ["k30"], HoverKey = "k28" };
        many = many with { ScrollOffset = TableScroll.Reveal(many, "k30") };
        foreach (var (label, card) in new[] {
            ("signed-below", signed with { Title = "Signed deltas · good below zero" }),
            ("signed-above", signed with { Title = "Signed deltas · good above zero", GoodWhen = BarGoodWhen.Above }),
            ("scrolled-selection", many with { Title = "40 rows · selection revealed" }),
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
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Estimate(text, size, font);
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft));
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
