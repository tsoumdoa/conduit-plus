using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

// The host measurer is process-wide. Isolate fake-metric tests from GDI registration in other tests.
[CollectionDefinition("Legend text metrics", DisableParallelization = true)]
public sealed class LegendTextMetricsCollection { }

[Collection("Legend text metrics")]
public sealed class CategoricalLegendWidthTests : IDisposable
{
    private readonly Func<string, double, string, Size2> _original = TextMetrics.Measurer;
    private static readonly Theme Theme = Theme.Paper with { FontSize = 12, Font = "Test font" };
    private static Size2 Measure(string text, double size, string font) => new(text.Length * size / 2, size + 2);
    private static CardSpec Legend => new()
    {
        Id = "legend", Kind = CardKind.Legend, Title = "Key", Labels = ["A", "B"], Colors = ["#FFFFFF", "#000000"]
    };
    public CategoricalLegendWidthTests() => TextMetrics.Measurer = Measure;
    public void Dispose() => TextMetrics.Measurer = _original;
    private static double Width(CardSpec c) => LegendCardPainter.CategoricalWidth(c, Theme, Measure);

    [Fact]
    public void ShortLabelsUseMinimumAndIgnoreAuthoredAndFormWidths()
    {
        foreach (var width in new[] { 180d, 280, 1200 })
            foreach (var form in new[] { CardForm.None, new CardForm(Width: 30), new CardForm(Compact: true, Width: 900) })
            {
                var c = Legend with { Width = width, Form = form };
                Assert.Equal(120, Width(c));
                Assert.Equal(new Size2(120, 92), FormMeasure.Size(c, Theme, form, Measure));
                Assert.Equal(new Size2(120, 92), CardMeasure.Measure(c, Theme));
            }
    }

    [Fact]
    public void WidestLabelValueAndTitleDriveWidthAndUseThemeMetrics()
    {
        var c = Legend with { Labels = ["tiny", new string('a', 20)] };
        Assert.Equal(170, Width(c)); // 24 padding + 6 tick gutter + 20 swatch/gap + 120 label.
        Assert.Equal(214, Width(c with { ValueLabels = ["123456", "1"] }));
        Assert.Equal(204, Width(Legend with { Title = new string('t', 30) }));
        var theme = Theme with { Padding = 13.25, FontSize = 13, Font = "Custom font" };
        var measured = LegendCardPainter.CategoricalWidth(c, theme, (text, size, font) =>
        {
            Assert.Equal(theme.FontSize, size);
            Assert.Equal(theme.Font, font);
            return Measure(text, size, font);
        });
        Assert.Equal(183, measured); // 182.5 rounds up.
    }

    [Fact]
    public void CappedLabelsEllipsizeAndValuesRemainIntactAtTheRightEdge()
    {
        var c = Legend with { Width = 1200, Labels = [new string('a', 100), "B"], ValueLabels = ["1234", "9"] };
        var canvas = new RecordingCanvas();
        CardPainter.Paint(canvas, c, Theme);
        Assert.Equal(280, Width(c));
        var label = Assert.Single(canvas.Texts, text => text.Text.EndsWith('…'));
        var value = Assert.Single(canvas.Texts, text => text.Text == "1234");
        Assert.Equal(268, value.At.X + Measure(value.Text, 12, Theme.Font).Width);
        Assert.Equal(value.At.X - 8, label.Clip.X + label.Clip.Width);
        Assert.True(Measure(label.Text, 12, Theme.Font).Width <= label.Clip.Width);
        Assert.True(Measure(label.Text[..^1] + "a…", 12, Theme.Font).Width > label.Clip.Width);
        Assert.Equal(280, Width(Legend with { Title = new string('t', 100) }));
        Assert.Equal(280, Width(Legend with { ValueLabels = [new string('9', 100), ""] }));
    }

    [Fact]
    public void KeyedTitleReservesClearButtonBeforeSelection()
    {
        var plain = Legend with { Title = new string('t', 20) };
        var keyed = plain with { Keys = ["a", "b"] };
        Assert.Equal(144, Width(plain));
        Assert.Equal(188, Width(keyed));
        Assert.Null(CardMeasure.Clear(keyed, Theme));
        foreach (var c in new[] { keyed with { SelectedKeys = ["a"] }, keyed with { OwnerHasSelection = true } })
        {
            Assert.Equal(188, Width(c));
            var clear = Assert.IsType<HitRegion>(CardMeasure.Clear(c, Theme));
            Assert.Equal(new HitRegion(CardMeasure.ClearKey, 136, 7, 40, 22), clear);
            var canvas = new RecordingCanvas();
            CardPainter.Paint(canvas, c, Theme);
            var title = Assert.Single(canvas.Texts, text => text.Text == c.Title);
            Assert.Equal(clear.X - 4, title.Clip.X + title.Clip.Width);
            Assert.Equal(Measure(c.Title, 12, Theme.Font).Width, title.Clip.Width);
            Assert.Contains(new Rect2(clear.X, clear.Y, clear.W, clear.H), canvas.Surfaces);
        }
        var unkeyed = new RecordingCanvas();
        CardPainter.Paint(unkeyed, plain, Theme);
        Assert.Equal(120, Assert.Single(unkeyed.Texts, text => text.Text == plain.Title).Clip.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutPainterAndHitRegionsAgreeOnEffectiveWidth(bool keyed)
    {
        var c = Legend with
        {
            Width = 1200, Labels = [new string('a', 20), "B"], ValueLabels = ["123456", "1"],
            Keys = keyed ? ["a", "b"] : [], SelectedKeys = keyed ? ["a"] : [],
            Form = new(Width: 999), WiredSlot = Slot.Tr
        };
        var size = FormMeasure.Size(c, Theme, c.Form, Measure);
        Assert.Equal(214, size.Width);
        Assert.Equal(size, CardMeasure.Measure(c, Theme));
        var canvas = new RecordingCanvas();
        CardPainter.Paint(canvas, c, Theme);
        Assert.Equal(new Rect2(0, 0, size.Width, size.Height), canvas.Surfaces[0]);
        Assert.Equal(202, Assert.Single(canvas.Texts, text => text.Text == "123456").At.X + 36);
        Assert.Equal(keyed ? 2 : 0, CardMeasure.DataHits(c, Theme).Length);
        Assert.All(CardMeasure.DataHits(c, Theme), hit => Assert.Equal(size.Width - 24, hit.W));
        Assert.True(CardMeasure.IsTitle(c, Theme, size.Width - 1, 30));
        Assert.False(CardMeasure.IsTitle(c, Theme, size.Width, 30));
        var request = Request(c);
        var placement = Assert.Single(ViewportLayout.Solve(request).Placements);
        Assert.Equal(size.Width, placement.Width);
        Assert.Equal(1588, placement.X + placement.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GradientKeepsAuthoredWidth(bool compact)
    {
        var c = Legend with { Min = 0, Max = 1, Width = 432, Form = new(Compact: compact, Width: 120) };
        var size = FormMeasure.Size(c, Theme, c.Form, Measure);
        Assert.Equal(new Size2(432, compact ? 76 : 84), size);
        var canvas = new RecordingCanvas();
        CardPainter.Paint(canvas, c, Theme);
        Assert.Equal(new Rect2(0, 0, size.Width, size.Height), canvas.Surfaces[0]);
        Assert.Equal(432 - 2 * (Theme.Padding + 2.5), Assert.Single(canvas.Gradients).Width);
    }

    [Fact]
    public void LayoutWidthOverridesStayOnTitles()
    {
        var legend = Legend with { WiredSlot = Slot.Tr };
        var title = new CardSpec { Id = "title", Kind = CardKind.Title, Title = "Heading", WiredSlot = Slot.Tc };
        var request = Request(legend) with { Participants = [new(Guid.Empty, 1, Theme, [title, legend])] };
        var solution = ViewportLayout.Solve(request);
        Assert.NotNull(solution.Placements.Single(p => p.CardId == title.Id).Form.Width);
        var placement = solution.Placements.Single(p => p.CardId == legend.Id);
        Assert.Null(placement.Form.Width);
        Assert.Equal(120, placement.Width);
        Assert.Equal(120, CardMeasure.Measure(legend with { Form = placement.Form }, Theme).Width);
    }

    [Fact]
    public void FingerprintTracksTextAndKeyPresenceButIgnoresSelection()
    {
        var c = Legend;
        var signature = ViewportLayout.Signature(Request(c));
        foreach (var changed in new[]
        {
            c with { Labels = ["Longer label", "B"] }, c with { ValueLabels = ["12", "4"] },
            c with { Title = "Longer title" }, c with { Keys = ["a", "b"] }
        }) Assert.NotEqual(signature, ViewportLayout.Signature(Request(changed)));
        var valued = c with { ValueLabels = ["12", "4"] };
        Assert.NotEqual(ViewportLayout.Signature(Request(valued)), ViewportLayout.Signature(Request(valued with { ValueLabels = ["123", "4"] })));
        var keyed = c with { Keys = ["a", "b"] };
        Assert.Equal(ViewportLayout.Signature(Request(keyed)), ViewportLayout.Signature(Request(keyed with { SelectedKeys = ["a"], OwnerHasSelection = true })));
    }

    [Fact]
    public void CanvasRasterAndRevealedHiddenCardUseMeasuredWidth()
    {
        var c = Legend;
        var state = new CanvasCardState();
        state.Replace(c);
        var size = CardMeasure.Measure(state.Spec!, Theme);
        Assert.Equal(120, size.Width);
        var plan = CanvasRasterPlan.Create(size, 2);
        Assert.Equal(240, plan.Width);
        var form = ShownCard.Form(c, Theme, 1000, Measure);
        Assert.Equal(size, FormMeasure.Size(c, Theme, form, Measure));
        Assert.Equal(size.Width, ShownCard.Place(size, new(1600, 900), 12, 0, 1600).Width);
        // RasterDrawing registers the host's GDI metrics: verify the real bitmap through that same host path.
        GdiCanvas.RegisterTextMetrics();
        var rasterSize = CardMeasure.Measure(c, Theme);
        using var bitmap = RasterDrawing.Render(c, Theme, 2);
        Assert.Equal((int)(rasterSize.Width * 2) + 2 * RasterDrawing.Margin(Theme, 2), bitmap.Width);
    }

    private static LayoutRequest Request(CardSpec c) => new(new(1600, 900), 1, [new(Guid.Empty, 1, Theme, [c])], Measure, new());

    private sealed class RecordingCanvas : ICanvas2D
    {
        private readonly Stack<Rect2> _clips = new([new(-1e6, -1e6, 2e6, 2e6)]);
        public List<Rect2> Surfaces { get; } = [];
        public List<Rect2> Gradients { get; } = [];
        public List<(string Text, Point2 At, Rect2 Clip)> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => Measure(text, size, font);
        public void Rect(Rect2 rect, string fill) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) => Surfaces.Add(rect);
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => Gradients.Add(rect);
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft, _clips.Peek()));
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) { _clips.Push(rect); return new ClipScope(_clips); }
        private sealed class ClipScope(Stack<Rect2> clips) : IDisposable { public void Dispose() => clips.Pop(); }
    }
}
