using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ThemeTests
{
    [Theory]
    [InlineData("Paper", "Paper")]
    [InlineData("sMoKe", "Smoke")]
    [InlineData(" BLUEPRINT ", "Blueprint")]
    public void LookupIsCaseInsensitiveAndTrimsWhitespace(string name, string expected)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        Assert.Equal(expected, theme.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dark")]
    public void UnknownNamesFallBackToPaper(string? name)
    {
        Assert.False(Theme.TryGet(name, out var theme));
        Assert.Same(Theme.Paper, theme);
    }

    [Theory]
    [InlineData("#12151cCC", 18, 21, 28, 204)]
    [InlineData("#8CBEFF73", 140, 190, 255, 115)]
    [InlineData("#12345600", 18, 52, 86, 0)]
    [InlineData("#123456", 18, 52, 86, 255)]
    public void ColorsUseTrailingAlphaInCoreAndAdapters(string hex, byte r, byte g, byte b, byte a)
    {
        var color = ColorRgba.Parse(hex);
        Assert.Equal(new ColorRgba(r, g, b, a), color);
        Assert.Equal(color, ColorRgba.Parse(color.ToHex()));
        Assert.Equal(Color.FromArgb(a, r, g, b), GdiCanvas.ParseColor(hex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#123")]
    [InlineData("123456")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GG1234FF")]
    public void MalformedColorsAreRejected(string? hex) => Assert.Throws<FormatException>(() => ColorRgba.Parse(hex!));

    [Fact]
    public void ThemeSwitchInvalidatesAllCardRevisionsButRepeatedThemeReusesThem()
    {
        var cards = Cards().ToArray();
        var store = new CardStore();
        Assert.Equal(cards.Length, store.Update(cards).Count);
        var previous = store.Snapshot;
        foreach (var theme in new[] { Theme.Smoke, Theme.Blueprint, Theme.Paper, Theme.Paper with { Accent = "#123456" } })
        {
            Assert.Equal(cards.Length, store.Update(cards, theme).Count);
            Assert.All(store.Snapshot.Zip(previous), pair => Assert.True(pair.First.Rev > pair.Second.Rev));
            previous = store.Snapshot;
            Assert.Empty(store.Update(cards, theme with { }));
            Assert.Equal(previous.Select(c => c.Rev), store.Snapshot.Select(c => c.Rev));
        }
    }

    [Fact]
    public void EveryCardAndTooltipUsesOnlyThemeTokensOrExplicitLegendColors()
    {
        // Unique replacement tokens catch literals that might coincidentally equal a preset token.
        var sentinel = Theme.Smoke with
        {
            Surface = "#010101CC", Ink = "#020202", Muted = "#030303", Line = "#04040440",
            Track = "#05050530", Accent = "#060606", Good = "#070707", Bad = "#080808",
            Area = "#09090920", ThresholdBand = "#10101020", Highlight = "#11111140",
            TooltipSurface = "#121212CC", TooltipInk = "#131313", Border = "#14141480",
            BorderWidth = 1, Shadow = true, ShadowColor = "#15151508"
        };
        foreach (var theme in Theme.Presets.Append(sentinel))
        {
            var tokens = typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(string))
                .Select(p => (string)p.GetValue(theme)!).Where(s => s.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
            var cards = Cards().ToArray();
            Assert.Equal(Enum.GetValues<CardKind>().Order(), cards.Select(c => c.Kind).Distinct().Order());
            foreach (var card in cards)
            {
                card.Validate();
                var canvas = new RecordingCanvas();
                CardPainter.Paint(canvas, card, theme);
                Assert.Contains(theme.Surface, canvas.Colors);
                Assert.All(canvas.Colors, color => Assert.True(tokens.Contains(color) || card.Colors.Contains(color), $"{theme.Name}/{card.Kind}: unexpected {color}"));
                if (card.SelectedKeys.Length > 0 && card.Kind is CardKind.Bar or CardKind.Table or CardKind.Legend)
                {
                    Assert.Contains(theme.Highlight, canvas.Colors);
                    Assert.Contains(theme.Accent, canvas.Colors);
                    Assert.Contains("Clear", canvas.Texts);
                }
                if (theme.BorderWidth > 0) Assert.Contains(theme.Border, canvas.Colors);
                Assert.Equal(theme.Shadow, canvas.Colors.Contains(theme.ShadowColor));
            }
            var tooltip = new RecordingCanvas();
            TooltipPainter.Paint(tooltip, ["Member", "0.8"], theme);
            Assert.Contains(theme.TooltipSurface, tooltip.Colors);
            Assert.Contains(theme.TooltipInk, tooltip.Colors);
            Assert.All(tooltip.Colors, color => Assert.Contains(color, tokens));
            Assert.Equal(theme.Shadow, tooltip.Colors.Contains(theme.ShadowColor));
        }
    }

    [Theory]
    [InlineData("Paper", 1d)]
    [InlineData("Smoke", 1d)]
    [InlineData("Smoke", 1.5d)]
    [InlineData("Smoke", 2d)]
    [InlineData("Smoke", 4d)]
    [InlineData("Blueprint", 2d)]
    public void RasterPreservesSurfaceAlphaAndReservesOnlyShadowMargin(string name, double scale)
    {
        Theme.TryGet(name, out var theme);
        var card = new CardSpec { Id = "empty", Kind = CardKind.Text, Text = "" };
        var size = CardMeasure.Measure(card, theme);
        using var bitmap = RasterDrawing.Render(card, theme, scale);
        var margin = RasterDrawing.Margin(theme, scale);
        Assert.Equal((int)Math.Ceiling(size.Width * scale) + 2 * margin, bitmap.Width);
        Assert.Equal((int)Math.Ceiling(size.Height * scale) + 2 * margin, bitmap.Height);
        Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        var surface = ColorRgba.Parse(theme.Surface);
        // GDI+'s high-quality source-over path can round 0xCC alpha down by one.
        Assert.InRange(Math.Abs(surface.A - center.A), 0, 1);
        Assert.InRange(Math.Abs(surface.R - center.R), 0, 2);
        Assert.InRange(Math.Abs(surface.G - center.G), 0, 2);
        Assert.InRange(Math.Abs(surface.B - center.B), 0, 2);
        if (theme.Shadow)
        {
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.InRange(bitmap.GetPixel(bitmap.Width / 2, margin - (int)(2 * scale)).A, 1, 254);
        }
        if (theme == Theme.Blueprint)
            Assert.True(bitmap.GetPixel(0, bitmap.Height / 2).A > center.A);
    }

    [Fact]
    public void GdiCompositesStraightAlphaIntoPremultipliedBitmap()
    {
        using var bitmap = new Bitmap(24, 16, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var canvas = new GdiCanvas(graphics);
            canvas.Rect(new(0, 0, 8, 16), "#12151CCC");
            canvas.Rect(new(8, 0, 8, 16), "#FFFFFF");
            canvas.Rect(new(8, 0, 8, 16), "#12151CCC");
            canvas.Gradient(new(16, 0, 8, 16), ["#12151CCC", "#12151CCC"]);
        }
        Assert.InRange(bitmap.GetPixel(4, 8).A, 203, 204);
        Assert.InRange(bitmap.GetPixel(20, 8).A, 203, 204);
        var overWhite = bitmap.GetPixel(12, 8);
        Assert.Equal(255, overWhite.A);
        // HighQuality compositing is gamma-corrected; compare in linear light.
        var source = ColorRgba.Parse("#12151CCC");
        static double Blend(byte channel) => Math.Pow(.8 * Math.Pow(channel / 255d, 2.2) + .2, 1 / 2.2) * 255;
        Assert.InRange(Math.Abs(overWhite.R - Blend(source.R)), 0, 3);
        Assert.InRange(Math.Abs(overWhite.G - Blend(source.G)), 0, 3);
        Assert.InRange(Math.Abs(overWhite.B - Blend(source.B)), 0, 3);
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void TooltipRasterIncludesThemeFrameAndShadowWithoutClipping(string name)
    {
        Theme.TryGet(name, out var theme);
        using var measureBitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measureBitmap);
        using var canvas = new GdiCanvas(graphics);
        const double scale = 2;
        var size = TooltipPainter.Measure(canvas, ["Member", "0.8"], theme);
        using var bitmap = RasterDrawing.RenderTooltip(["Member", "0.8"], theme, scale);
        var margin = RasterDrawing.Margin(theme, scale);
        Assert.Equal((int)Math.Ceiling(size.Width * scale) + 2 * margin, bitmap.Width);
        Assert.Equal((int)Math.Ceiling(size.Height * scale) + 2 * margin, bitmap.Height);
        var color = bitmap.GetPixel(bitmap.Width / 2, margin + (int)((size.Height - 4) * scale));
        Assert.InRange(Math.Abs(ColorRgba.Parse(theme.TooltipSurface).A - color.A), 0, 1);
        if (theme.Shadow) Assert.InRange(bitmap.GetPixel(bitmap.Width / 2, margin - 4).A, 1, 254);
    }

    private static IEnumerable<CardSpec> Cards()
    {
        foreach (var card in StructuralDemo.Create().Concat([StructuralDemo.Line(), .. StructuralDemo.Extras()]))
        {
            var keys = BrushState.Keys(card).ToArray();
            // Legend colors represent user data; deliberately keep them independent of the theme.
            var spec = card.Kind == CardKind.Legend ? card with { Colors = card.Colors.Select((_, i) => i % 2 == 0 ? "#123456" : "#FEDCBA").ToArray() } : card;
            yield return spec with { HoverKey = keys.LastOrDefault(), SelectedKeys = keys.Take(1).ToArray(), OwnerHasSelection = true };
            if (card.Kind == CardKind.Table) yield return spec with { Id = card.Id + "-sorted", HoverKey = TableSort.Key(0), SortColumn = 0, SortDescending = true };
        }
        yield return new() { Id = "signed", Kind = CardKind.Bar, Values = [-.5, null, 1.2], Labels = ["A", "B", "C"], Keys = ["a", "b", "c"], HoverKey = "b" };
    }

    private sealed class RecordingCanvas : ICanvas2D
    {
        public List<string> Colors { get; } = [];
        public List<string> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => new(text.Length * size * .6, size * 1.2);
        public void Rect(Rect2 rect, string fill) => Colors.Add(fill);
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => Colors.AddRange(colors);
        public void RoundedRect(Rect2 rect, double radius, string fill) => Colors.Add(fill);
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Colors.Add(color);
        public void Text(string text, Point2 topLeft, double size, string color, string font) { Colors.Add(color); Texts.Add(text); }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => Colors.Add(color);
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) => Colors.Add(fill);
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
