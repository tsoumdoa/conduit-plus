using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class SelectionReadabilityTests
{
    [Fact]
    public void PaperSelectionIsDistinctFromHoverAndTrack()
    {
        Assert.Equal("#DCE5FF", Theme.Paper.Highlight);
        Assert.Equal("#EEF0F3", Theme.Paper.Track);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearUsesMutedInkInBothMetricForms(bool compact)
    {
        var card = new CardSpec { Id = "metric", Kind = CardKind.Metric, Value = "42", Keys = ["a"], SelectedKeys = ["a"], Form = new(Compact: compact) };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        Assert.Equal(Theme.Paper.Muted, Assert.Single(canvas.Texts, t => t.Text == "Clear").Color);
    }

    [Theory]
    [InlineData(CardKind.Metric)]
    [InlineData(CardKind.Table)]
    public void CompactMetricAndTableKeepFullTicksInsideTheHitRegion(CardKind kind)
    {
        var card = new CardSpec { Id = "card", Kind = kind, Value = "42", Keys = kind == CardKind.Metric ? ["a"] : [],
            Headers = ["Room"], Rows = [new("a", ["Living"])], SelectedKeys = ["a"], Form = new(Compact: true) };
        var hit = Assert.Single(CardMeasure.DataHits(card, Theme.Paper));
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        Assert.Contains((new Rect2(hit.X, hit.Y, hit.W, hit.H), Theme.Paper.Highlight), canvas.Rects);
        Assert.Contains((new Rect2(hit.X, hit.Y, 3, hit.H), Theme.Paper.Accent), canvas.Rects);
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void SelectedLineSamplesHaveAccentRuleAndRingButMissingSamplesHaveOnlyRule(string name)
    {
        Theme.TryGet(name, out var theme);
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Width = 320, Values = [1, null, 2], Labels = ["First", "Missing", "Last"], Keys = ["a", "b", "c"], SelectedKeys = ["a", "b"] };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, theme);
        var rules = canvas.Lines.Where(l => l.Color == theme.Accent && l.From.X == l.To.X).ToArray();
        Assert.Equal(2, rules.Length); Assert.All(rules, l => Assert.Equal(1, l.Width));
        var marker = Assert.Single(canvas.Rects, r => r.Fill == theme.Accent && r.Rect.Width == 8);
        var ring = Assert.Single(canvas.Rects, r => r.Fill == theme.Surface && r.Rect.Width == 12);
        Assert.Equal(new Rect2(marker.Rect.X - 2, marker.Rect.Y - 2, 12, 12), ring.Rect);
        Assert.Equal(44, marker.Rect.X + 4);
        Assert.Equal(3, CardMeasure.DataHits(card, theme).Length);
    }

    [Fact]
    public void LineLabelsMeasureEndpointsThenFitInteriorsAndKeepEveryHit()
    {
        var labels = new[] { "First", "Wide interior", "i", "Fourth", "Last" };
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Width = 240, Labels = labels, Keys = labels, Values = [1, 2, 3, 4, 5] };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        var painted = canvas.Texts.Where(t => t.At.Y == 190).ToArray();
        Assert.Equal("First", painted[0].Text); Assert.Equal(44, painted[0].At.X);
        Assert.Equal("Last", painted[^1].Text);
        Assert.Equal(220, painted[^1].At.X + canvas.MeasureText(painted[^1].Text, 10, Theme.Paper.Font).Width);
        Assert.True(painted.Length < labels.Length); Assert.True(painted.Length > 2);
        for (var i = 1; i < painted.Length; i++)
            Assert.True(painted[i].At.X >= painted[i - 1].At.X + canvas.MeasureText(painted[i - 1].Text, 10, Theme.Paper.Font).Width + 4);
        Assert.Equal(labels, CardMeasure.DataHits(card, Theme.Paper).Select(h => h.Key));
    }

    [Fact]
    public void CollidingEndpointsAreBothEllipsizedInwardWithoutDroppingTheirHits()
    {
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Width = 160, Labels = ["A very long first endpoint", "Middle", "An even longer last endpoint"], Keys = ["a", "b", "c"], Values = [1, 2, 3] };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        var labels = canvas.Texts.Where(t => t.At.Y == 190).ToArray();
        Assert.Equal(2, labels.Length); Assert.All(labels, l => Assert.EndsWith("…", l.Text));
        Assert.Equal(44, labels[0].At.X);
        Assert.Equal(140, labels[1].At.X + canvas.MeasureText(labels[1].Text, 10, Theme.Paper.Font).Width);
        Assert.True(labels[0].At.X + canvas.MeasureText(labels[0].Text, 10, Theme.Paper.Font).Width + 4 <= labels[1].At.X);
        Assert.Equal(3, CardMeasure.DataHits(card, Theme.Paper).Length);
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#CCCCCC", "#000000")]
    [InlineData("#777777", "#000000")]
    [InlineData("#666666", "#FFFFFF")]
    [InlineData("#12151C", "#FFFFFF")]
    [InlineData("#000000", "#FFFFFF")]
    public void PlainTitleUsesHostBackgroundAndExplicitColourStillWins(string background, string expected)
    {
        Assert.Equal(expected, TitleMetrics.InkForBackground(ColorRgba.Parse(background)));
        foreach (var palette in new[] { Theme.Paper, Theme.Night, Theme.Smoke, Theme.Blueprint })
        {
            var theme = palette with { PlainTitleInk = TitleMetrics.InkForBackground(ColorRgba.Parse(background)) };
            var title = new CardSpec { Id = "title", Kind = CardKind.Title, Title = "Plain title", Subtitle = "Subtitle", Text = "Description" };
            var canvas = new Commands(); CardPainter.Paint(canvas, title, theme);
            Assert.All(canvas.Texts, t => Assert.Equal(expected, t.Color));
            canvas = new(); CardPainter.Paint(canvas, title with { Colors = ["#C2334D"] }, theme);
            Assert.All(canvas.Texts, t => Assert.Equal("#C2334D", t.Color));
            var store = new CardStore(); store.Update([title], palette);
            var revision = store.Snapshot[0].Rev;
            store.Update([title], palette with { PlainTitleInk = "#FFFFFF" });
            Assert.True(store.Snapshot[0].Rev > revision);
        }
    }

    [Fact]
    public void GeometryHighlightKeepsWhiteCasingNeutralWashAndSelectedPrecedence()
    {
        Assert.Equal(new GeometryHighlight(7, 3, .2), GeometryHighlight.Resolve(true, true));
        Assert.Equal(GeometryHighlight.Selected, GeometryHighlight.Resolve(true, false));
        Assert.Equal(new GeometryHighlight(4, 2, 0), GeometryHighlight.Resolve(false, true));
        Assert.Null(GeometryHighlight.Resolve(false, false));
        Assert.Equal("#FFFFFF", GeometryHighlight.Casing);
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void RasterSelectionTickAndFixtures(string name)
    {
        Theme.TryGet(name, out var theme);
        var bar = new CardSpec { Id = "bar", Kind = CardKind.Bar, Title = "Area by room", Width = 360,
            Keys = ["a", "b", "c"], Labels = ["Living", "Bedrooms", "Kitchen"], Values = [28, 24, 16], Colors = ["#BA8F63", "#638AD2", "#66A88A"], Threshold = 30 };
        var brush = new BrushState();
        var legend = new CardSpec { Id = "legend", Kind = CardKind.Legend, Title = "Room types", Keys = ["bedrooms", "living"], Labels = ["Bedrooms", "Living"],
            Colors = ["#638AD2", "#BA8F63"], Groups = GroupInputs.Create(["bedrooms", "living"], 2, true, [new("{0}", ["a", "b"]), new("{1}", ["c"])]) };
        var metric = new CardSpec { Id = "metric", Kind = CardKind.Metric, Title = "Bedrooms", Value = "2", Width = 230, Keys = ["bedrooms"], Groups = legend.Groups.Remove("living") };
        var line = new CardSpec { Id = "line", Kind = CardKind.Line, Title = "Selected sample and missing sample", Width = 360,
            Keys = ["a", "b", "c", "d", "e", "f"], Labels = ["First sample", "Second sample", "Third sample", "Fourth sample", "Fifth sample", "Last sample"], Values = [3, 5, null, 4, 2, 3] };
        brush.Prune([bar, legend, metric, line], []); brush.Toggle("a"); brush.Toggle("c"); brush.Hover("bedrooms");
        var cards = brush.Project([bar, legend, metric, line]);
        using var selected = RasterDrawing.Render(cards[0], theme, 2);
        var hit = CardMeasure.DataHits(cards[0], theme)[0];
        var pixel = selected.GetPixel((int)((hit.X + 1.5) * 2), (int)((hit.Y + 11) * 2));
        var accent = ColorRgba.Parse(theme.Accent);
        Assert.InRange(Math.Abs(pixel.R - accent.R), 0, 2); Assert.InRange(Math.Abs(pixel.G - accent.G), 0, 2); Assert.InRange(Math.Abs(pixel.B - accent.B), 0, 2);
        var output = Environment.GetEnvironmentVariable("CONDUITPLUS_PRB_VISUALS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        using var gallery = new Bitmap(1520, 1580);
        using var graphics = Graphics.FromImage(gallery); graphics.Clear(Color.FromArgb(225, 225, 225));
        graphics.FillRectangle(Brushes.Black, 760, 0, 760, 1580);
        for (var column = 0; column < 2; column++)
        {
            var background = column == 0 ? new ColorRgba(225, 225, 225) : new ColorRgba(0, 0, 0);
            var hostTheme = theme with { PlainTitleInk = TitleMetrics.InkForBackground(background) };
            var y = 16;
            void Draw(CardSpec card)
            {
                using var bitmap = RasterDrawing.Render(card, hostTheme, 2);
                graphics.DrawImageUnscaled(bitmap, column * 760 + 16, y); y += bitmap.Height + 16;
            }
            Draw(new() { Id = "title", Kind = CardKind.Title, Title = name + " · Plain Title", Subtitle = "Full, partial and hover states", Form = new(Width: 360) });
            Draw(cards[0]); Draw(cards[1]); Draw(cards[2]); Draw(cards[3]);
            Draw(legend with { SelectedKeys = ["bedrooms"], GroupStates = cards[1].GroupStates.SetItem("bedrooms", new(2, 2, 0, false, true)), OwnerHasSelection = true });
        }
        gallery.Save(Path.Combine(output, "cards-" + name.ToLowerInvariant() + ".png"), ImageFormat.Png);
    }

    private sealed class Commands : ICanvas2D
    {
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<(Point2 From, Point2 To, string Color, double Width)> Lines { get; } = [];
        public List<(string Text, Point2 At, string Color)> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => new(text.Sum(c => c == 'i' ? 2 : 6), size + 2);
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Lines.Add((from, to, color, width));
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft, color));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Nothing();
        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }
}
