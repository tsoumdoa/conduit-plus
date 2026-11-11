using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

[Collection("Legend text metrics")]
public sealed class KeyLegendTests : IDisposable
{
    private readonly Func<string, double, string, Size2> _original = TextMetrics.Measurer;
    public KeyLegendTests() => TextMetrics.Measurer = Measure;
    public void Dispose() => TextMetrics.Measurer = _original;
    private static Size2 Measure(string text, double size, string font) => new(text.Length * size / 2, size + 2);
    private static CardSpec Legend() => new()
    {
        Id = "key-legend", Kind = CardKind.Legend, Title = "Legend",
        Colors = ["#FFFFFF", "#00E5FF"], Labels = ["Existing", "New"], ValueLabels = ["12", "4"]
    };

    [Fact]
    public void ValueLabelsAreOptionalButMustMatchCategoricalColors()
    {
        var card = Legend();
        card.Validate();
        (card with { ValueLabels = [] }).Validate();
        (card with { ValueLabels = ["", ""] }).Validate();
        Assert.Throws<ArgumentException>(() => (card with { ValueLabels = ["12"] }).Validate());
        Assert.Throws<ArgumentException>(() => (card with { ValueLabels = ["12", "4", "1"] }).Validate());
        Assert.Throws<ArgumentException>(() => (card with { Min = 0, Max = 1 }).Validate());
        (card with { Min = 0, Max = 1, ValueLabels = [] }).Validate();
        Assert.Throws<ArgumentException>(() => (card with { Max = 1 }).Validate());
    }

    [Fact]
    public void ValueLabelsRoundTripAndMutationsDirtyOnlyTheirCard()
    {
        var card = Legend();
        var json = CardJson.Serialize(card);
        Assert.Contains("\"valueLabels\":[\"12\",\"4\"]", json);
        var copy = CardJson.Deserialize<CardSpec>(json);
        copy.Validate();
        Assert.Equal(card.ValueLabels, copy.ValueLabels);
        Assert.Equal(json, CardJson.Serialize(copy));
        Assert.Empty(CardJson.Deserialize<CardSpec>("{\"id\":\"legacy\",\"kind\":\"legend\"}").ValueLabels);
        var store = new CardStore();
        CardSpec[] cards = [card, card with { Id = "other", ValueLabels = [] }];
        store.Update(cards);
        var revision = store.Snapshot[0].Rev;
        Assert.Empty(store.Update(cards));
        card.ValueLabels[0] = "99";
        Assert.Equal("12", store.Snapshot[0].ValueLabels[0]);
        var dirty = Assert.Single(store.Update(cards));
        Assert.Equal(card.Id, dirty.Id);
        Assert.True(dirty.Rev > revision);
        Assert.Equal("99", dirty.ValueLabels[0]);
    }

    [Theory]
    [InlineData("Paper", 22)]
    [InlineData("Night", 22)]
    [InlineData("Smoke", 22)]
    [InlineData("Blueprint", 22)]
    [InlineData("Paper", 30)]
    public void SwatchesAndInsetEdgesAreCentredAndUseThemeInk(string themeName, double rowHeight)
    {
        Theme.TryGet(themeName, out var preset);
        var theme = preset with { RowHeight = rowHeight };
        var card = Legend();
        var canvas = new RecordingCanvas();
        LegendCardPainter.Paint(canvas, card, theme);
        Assert.Equal(card.Colors.Length, canvas.Swatches.Count);
        Assert.Equal(card.Colors.Length, canvas.Edges.Count);
        for (var i = 0; i < card.Colors.Length; i++)
        {
            var (rect, radius, fill) = canvas.Swatches[i];
            Assert.Equal(new Rect2(theme.Padding, 36 + i * rowHeight + (rowHeight - 12) / 2, 12, 12), rect);
            Assert.Equal(theme.Radius == 0 ? 0 : 2, radius);
            Assert.Equal(card.Colors[i], fill);
            var edge = canvas.Edges[i];
            Assert.Equal(1, edge.Width);
            Assert.Equal(ColorRgba.Parse(theme.Ink).WithAlpha(0x29).ToHex(), edge.Color);
            Assert.Equal(rect.X + .5, edge.Points.Min(p => p.X), 8);
            Assert.Equal(rect.X + 11.5, edge.Points.Max(p => p.X), 8);
            Assert.Equal(rect.Y + .5, edge.Points.Min(p => p.Y), 8);
            Assert.Equal(rect.Y + 11.5, edge.Points.Max(p => p.Y), 8);
            Assert.Equal(edge.Points[0], edge.Points[^1]);
            if (theme.Radius == 0) Assert.Equal(5, edge.Points.Length);
            else
            {
                Assert.True(edge.Points.Length > 5);
                Assert.DoesNotContain(new Point2(rect.X + .5, rect.Y + .5), edge.Points);
            }
        }
        Assert.Equal(new Size2(120, 48 + card.Colors.Length * rowHeight), CardMeasure.Measure(card, theme));
    }

    [Fact]
    public void ValuesAreRightAlignedAndReserveSpaceForEllipsizedLabels()
    {
        var theme = Theme.Paper;
        var card = Legend() with { Width = 180, Labels = [new string('a', 50), "New"] };
        var canvas = new RecordingCanvas();
        LegendCardPainter.Paint(canvas, card, theme);
        var value = Assert.Single(canvas.Texts, t => t.Text == "12");
        Assert.Equal(280 - theme.Padding, value.Position.X + canvas.MeasureText(value.Text, value.Size, value.Font).Width);
        Assert.Equal(theme.Muted, value.Color);
        var label = Assert.Single(canvas.Texts, t => t.Text.EndsWith('…'));
        Assert.Equal(theme.Padding + 20, label.Position.X);
        Assert.Equal(theme.Ink, label.Color);
        Assert.Equal(value.Position.X - 8, label.Clip.X + label.Clip.Width);
        Assert.True(canvas.MeasureText(label.Text, label.Size, label.Font).Width <= label.Clip.Width);
        Assert.True(canvas.MeasureText(label.Text[..^1] + "a…", label.Size, label.Font).Width > label.Clip.Width);
        Assert.All(canvas.Texts, text =>
        {
            Assert.Equal(theme.FontSize, text.Size);
            Assert.Equal(theme.Font, text.Font);
            var rowTop = text.Text is "4" or "New" ? 58 : 36;
            Assert.Equal(rowTop + (theme.RowHeight - canvas.MeasureText(text.Text, text.Size, text.Font).Height) / 2, text.Position.Y);
        });
        var withoutValues = new RecordingCanvas();
        LegendCardPainter.Paint(withoutValues, card with { ValueLabels = [] }, theme);
        Assert.True(withoutValues.Texts[0].Text.Length > label.Text.Length);
    }

    [Fact]
    public void OversizedValuesClipWithoutCoveringSwatchesOrDrawingAnUnfittingEllipsis()
    {
        var card = Legend() with { Width = 180, ValueLabels = [new string('9', 100), ""] };
        var canvas = new RecordingCanvas();
        LegendCardPainter.Paint(canvas, card, Theme.Paper);
        Assert.Contains(canvas.Texts, text => text.Text == "" && text.Clip.Width == 0);
        Assert.All(canvas.Texts, text =>
        {
            Assert.True(text.Clip.X >= Theme.Paper.Padding + 20);
            Assert.True(text.Clip.Width >= 0);
            Assert.True(text.Clip.X + text.Clip.Width <= 280 - Theme.Paper.Padding);
        });
    }

    [Fact]
    public void DisplayOnlyLegendWorksOnCanvasAndKeyedRowsKeepTheirHitRegions()
    {
        var card = Legend() with { Labels = ["Repeated", "Repeated"] };
        card.Validate(); // These are labels, so duplicates are valid.
        Assert.Empty(CardMeasure.Hits(card, Theme.Paper));
        Assert.Empty(BrushState.Keys(card));
        var state = new CanvasCardState();
        state.Replace(card);
        Assert.Equal(card.ValueLabels, state.Spec!.ValueLabels);
        Assert.Empty(CardMeasure.Hits(state.Spec, Theme.Paper));
        var keyed = card with { Keys = ["a", "b"], HoverKey = "b" };
        var hits = CardMeasure.DataHits(keyed, Theme.Paper);
        Assert.Equal(new HitRegion("b", 12, 58, 96, 22), hits[1]);
        var canvas = new RecordingCanvas();
        LegendCardPainter.Paint(canvas, keyed, Theme.Paper);
        Assert.Contains((new Rect2(hits[1].X, hits[1].Y, hits[1].W, hits[1].H), Theme.Paper.Track), canvas.Rects);
    }

    private sealed class RecordingCanvas : ICanvas2D
    {
        private readonly Stack<Rect2> _clips = new([new(-1e6, -1e6, 2e6, 2e6)]);
        public List<(Rect2 Rect, double Radius, string Fill)> Swatches { get; } = [];
        public List<(Point2[] Points, string Color, double Width)> Edges { get; } = [];
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<(string Text, Point2 Position, double Size, string Color, string Font, Rect2 Clip)> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => Measure(text, size, font);
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void RoundedRect(Rect2 rect, double radius, string fill) => Swatches.Add((rect, radius, fill));
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => Edges.Add((points.ToArray(), color, width));
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft, size, color, font, _clips.Peek()));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) { _clips.Push(rect); return new Scope(_clips); }
        private sealed class Scope(Stack<Rect2> clips) : IDisposable { public void Dispose() => clips.Pop(); }
    }
}
