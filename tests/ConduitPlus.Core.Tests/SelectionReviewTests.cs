using System.Drawing;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class SelectionReviewTests
{
    [Theory]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#666666", "#FFFFFF")]
    public void SolidRenderBackgroundChoosesContrastWithoutHalo(string background, string ink)
    {
        var choice = TitleMetrics.ChooseInk([ColorRgba.Parse(background)], ColorRgba.Parse(Theme.Paper.Surface));
        Assert.Equal(ink, choice.Ink);
        Assert.Null(choice.Halo);
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void GradientExtremesUseWorstContrastAndHaloAndExplicitColourWins(string name)
    {
        Theme.TryGet(name, out var theme);
        ColorRgba[] corners = [new(0, 0, 0), new(255, 255, 255), new(30, 50, 80), new(180, 200, 220)];
        var choice = TitleMetrics.ChooseInk(corners, ColorRgba.Parse(theme.Surface));
        Assert.NotNull(choice.Halo);
        Assert.Equal(choice, TitleMetrics.ChooseInk(Enumerable.Reverse(corners), ColorRgba.Parse(theme.Surface)));
        var resolved = theme with { PlainTitleInk = choice.Ink, PlainTitleHalo = choice.Halo };
        var title = new CardSpec { Id = "title", Kind = CardKind.Title, Title = "Gradient title" };
        using var bitmap = new Bitmap(600, 120);
        using var graphics = Graphics.FromImage(bitmap);
        using var canvas = new AuditCanvas(graphics);
        CardPainter.Paint(canvas, title, resolved);
        Assert.Contains(canvas.Texts, t => t.Color == choice.Halo);
        Assert.Equal(choice.Ink, canvas.Texts[^1].Color);
        canvas.Texts.Clear();
        CardPainter.Paint(canvas, title with { Colors = ["#C2334D"] }, resolved);
        Assert.Equal("#C2334D", Assert.Single(canvas.Texts).Color);
        canvas.Texts.Clear();
        CardPainter.Paint(canvas, title with { Background = TitleBackground.Card }, resolved);
        Assert.Equal(theme.Ink, Assert.Single(canvas.Texts).Color);
        var store = new CardStore(); store.Update([title], resolved);
        var revision = store.Snapshot[0].Rev;
        store.Update([title], resolved with { PlainTitleHalo = null });
        Assert.True(store.Snapshot[0].Rev > revision);
        var output = Environment.GetEnvironmentVariable("CONDUITPLUS_PRB_VISUALS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            using var gallery = new Bitmap(900, 440);
            using var drawing = Graphics.FromImage(gallery);
            using var down = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 450, 440), Color.Black, Color.White, 90);
            using var up = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(450, 0, 450, 440), Color.White, Color.Black, 90);
            drawing.FillRectangle(down, 0, 0, 450, 440);
            drawing.FillRectangle(up, 450, 0, 450, 440);
            using var raster = RasterDrawing.Render(title with { Title = name + " · Gradient Title", Subtitle = "Automatic ink and glyph halo", Form = new(Width: 300) }, resolved, 1.3);
            foreach (var x in new[] { 12, 462 })
                foreach (var y in new[] { 12, 300 }) drawing.DrawImageUnscaled(raster, x, y);
            gallery.Save(Path.Combine(output, "title-gradient-" + name.ToLowerInvariant() + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    [Fact]
    public void WorstCaseInkUsesEveryCornerRatherThanAverageOrSurfacePreference()
    {
        var choice = TitleMetrics.ChooseInk([new(0, 0, 0), new(120, 120, 120), new(140, 140, 140), new(160, 160, 160)], new(255, 255, 255));
        Assert.Equal("#FFFFFF", choice.Ink);
        Assert.NotNull(choice.Halo); // white wins worst-case contrast, but is below 3:1 on the light corner
        Assert.Equal("#FFFFFF", TitleMetrics.ChooseInk([new(0, 0, 0), new(30, 40, 50)], new(255, 255, 255)).Ink);
        Assert.Null(TitleMetrics.ChooseInk([new(0, 0, 0), new(30, 40, 50)], new(255, 255, 255)).Halo);
    }

    [Fact]
    public void LongLineLabelsHaveBoundedMeasurementWorkAndKeepTextElements()
    {
        var calls = 0;
        Size2 Measure(string text, double size, string font)
        {
            calls++;
            Assert.InRange(text.Length, 0, 258);
            Assert.False(text.Length > 1 && char.IsHighSurrogate(text[^2]) && text[^1] == '…');
            Assert.False(text.EndsWith("e…", StringComparison.Ordinal)); // don't split e + combining accent
            return new(text.Length * 6, 12);
        }
        var label = string.Concat(Enumerable.Repeat("😀e\u0301", 5000));
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Width = 280, Labels = [label, label + "last"], Values = [1, 2], Keys = ["a", "b"] };
        var labels = LineCardPainter.Labels(card, Theme.Paper, Measure);
        Assert.Equal(2, labels.Length);
        Assert.All(labels, l => Assert.EndsWith("…", l.Text));
        Assert.InRange(calls, 1, 24);
    }

    [Fact]
    public void SeventeenSamplesUseARegularStrideAndKeepBothEndpoints()
    {
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Width = 350,
            Labels = Enumerable.Range(0, 17).Select(i => $"Room {i:00}").ToArray() };
        var labels = LineCardPainter.Labels(card, Theme.Paper, (text, size, font) => new(text.Length * 5, 12));
        Assert.Equal(0, labels[0].Index); Assert.Equal(16, labels[^1].Index);
        Assert.True(labels.Length > 3);
        var stride = labels[1].Index;
        Assert.All(labels.SkipLast(1), label => Assert.Equal(0, label.Index % stride));
        for (var i = 1; i < labels.Length; i++)
            Assert.True(labels[i - 1].X + labels[i - 1].Width + 4 <= labels[i].X);
    }

    [Theory]
    [InlineData(CardKind.Bar, 1)]
    [InlineData(CardKind.Bar, 1.25)]
    [InlineData(CardKind.Bar, 2)]
    [InlineData(CardKind.Table, 1)]
    [InlineData(CardKind.Table, 1.25)]
    [InlineData(CardKind.Table, 2)]
    public void NumberedRoomLabelsFitTheirActualGdiClips(CardKind kind, double scale)
    {
        var card = new CardSpec { Id = "rooms", Kind = kind, Width = 350, Keys = ["R05", "R13"],
            Labels = ["Study 05", "Study 13"], Values = [16, 18],
            Headers = ["Room", "Type", "m²", "Min m²", "Δ m²", "Status"],
            Rows = [new("R05", ["Study 05", "Study", "16", "12", "4", "Over"]), new("R13", ["Study 13", "Study", "18", "18", "0", "OK"])], SelectedKeys = ["R05"] };
        using var bitmap = new Bitmap(900, 400);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.ScaleTransform((float)scale, (float)scale);
        using var canvas = new AuditCanvas(graphics);
        CardPainter.Paint(canvas, card, Theme.Paper);
        var names = canvas.Texts.Where(t => t.Text.StartsWith("Study ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, names.Length);
        Assert.All(names, t => Assert.True(t.Measured <= t.Available, $"{kind}: {t.Text} needs {t.Measured}, clip allows {t.Available}"));
    }

    [Fact]
    public void KeyedMetricAndDisplayOnlyLegendKeepTheirContentOrigins()
    {
        using var bitmap = new Bitmap(600, 180);
        using var graphics = Graphics.FromImage(bitmap);
        using var canvas = new AuditCanvas(graphics);
        var card = new CardSpec { Id = "metric", Kind = CardKind.Metric, Value = "320" };
        CardPainter.Paint(canvas, card, Theme.Paper);
        var plain = Assert.Single(canvas.Texts, t => t.Text == "320").At;
        canvas.Texts.Clear();
        CardPainter.Paint(canvas, card with { Keys = ["a"], SelectedKeys = ["a"] }, Theme.Paper);
        Assert.Equal(plain, Assert.Single(canvas.Texts, t => t.Text == "320").At);
        Assert.Equal(Theme.Paper.Padding, plain.X);
        canvas.Texts.Clear();
        CardPainter.Paint(canvas, new() { Id = "key", Kind = CardKind.Legend, Labels = ["Display only"], Colors = ["#638AD2"] }, Theme.Paper);
        Assert.Equal(Theme.Paper.Padding + 20, Assert.Single(canvas.Texts, t => t.Text == "Display only").At.X);
    }

    private sealed class AuditCanvas(Graphics graphics) : ICanvas2D, IDisposable
    {
        private readonly GdiCanvas _canvas = new(graphics);
        public List<(string Text, Point2 At, string Color, double Measured, double Available)> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => _canvas.MeasureText(text, size, font);
        public void Text(string text, Point2 at, double size, string color, string font)
        {
            Texts.Add((text, at, color, MeasureText(text, size, font).Width, graphics.ClipBounds.Right - at.X));
            _canvas.Text(text, at, size, color, font);
        }
        public void Rect(Rect2 r, string fill) => _canvas.Rect(r, fill);
        public void RoundedRect(Rect2 r, double radius, string fill) => _canvas.RoundedRect(r, radius, fill);
        public void Gradient(Rect2 r, IReadOnlyList<string> colors) => _canvas.Gradient(r, colors);
        public void Line(Point2 a, Point2 b, string color, double width = 1) => _canvas.Line(a, b, color, width);
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => _canvas.Polyline(points, color, width);
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) => _canvas.FillPolygon(points, fill);
        public IDisposable Clip(Rect2 r) => _canvas.Clip(r);
        public void Dispose() => _canvas.Dispose();
    }
}
