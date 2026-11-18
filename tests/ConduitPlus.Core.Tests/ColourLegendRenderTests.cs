using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

[Collection("Legend text metrics")]
public sealed class ColourLegendRenderTests
{
    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    public void DiagramStylesRenderOneItemPerSwatchWithoutCounts(string name)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        foreach (var p in Palettes.Styles)
        {
            string[] labels = ["Housing", "Office", "Retail", "Landscape", "Culture", "Leisure", "Services", "Circulation",
                "Education", "Health", "Workshops", "Storage", "Public space", "Transport", "Plant", "Future"];
            var result = ColourCategories.Create(labels, palette: [p.Name], title: p.Name);
            Assert.Empty(result.Messages); Assert.Empty(result.Legend.ValueLabels);
            using var image = RasterDrawing.Render(result.Legend, theme, 2);
            for (var i = 0; i < labels.Length; i++)
                Near(result.Colours[i], image.GetPixel((int)(2 * (theme.Padding + 6)),
                    (int)(2 * (CardMeasure.BodyTop + i * theme.RowHeight + theme.RowHeight / 2))), 0);
            Save(image, $"diagram-{p.Name}-{name}.png");
        }
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void EmittedCardsRenderWithIdenticalModelRgbInEveryTheme(string name)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        foreach (var p in Palettes.All)
        {
            if (p.Kind != PaletteKind.Categorical)
            {
                var values = Enumerable.Range(0, 17).Select(i => (double?)(i * 40d / 16)).ToArray();
                var scale = ColourScale.Create(values, [p.Name], min: 0, max: 40, title: p.Name, unit: "m²");
                using var ramp = RasterDrawing.Render(scale.Legend, theme, 2);
                var left = theme.Padding + Math.Max(2, TextMetrics.Measurer("0", 10, theme.Font).Width / 2);
                var right = scale.Legend.Width - theme.Padding - Math.Max(2, TextMetrics.Measurer("40", 10, theme.Font).Width / 2);
                for (var i = 1; i < 16; i++)
                {
                    var pixel = ramp.GetPixel((int)Math.Round(2 * (left + (right - left) * i / 16)), 2 * 45);
                    Near(scale.Colours[i], pixel, 5);
                }
                Save(ramp, $"scale-{p.Name}-{name}.png");
            }
            string[] order = ["Residential", "Office", "Retail", "Plant", "Store", "Circulation", "Other", "Future", "No data"];
            var cat = ColourCategories.Create(["Residential", "Office", "Residential", "Retail", null], ["R1", "R2", "R3", "R4", "R5"],
                [p.Name], order, p.Name + " uses");
            using var swatches = RasterDrawing.Render(cat.Legend, theme, 2);
            for (var i = 0; i < order.Length; i++)
                Near(cat.CategoryColours[i], swatches.GetPixel((int)(2 * (theme.Padding + 6)),
                    (int)(2 * (CardMeasure.BodyTop + i * theme.RowHeight + theme.RowHeight / 2))), 0);
            // Every model colour comes from the exact corresponding rendered swatch.
            for (var i = 0; i < cat.Colours.Length; i++)
            {
                var row = Array.IndexOf(cat.Categories, new[] { "Residential", "Office", "Residential", "Retail", "No data" }[i]);
                Assert.Equal(cat.CategoryColours[row], cat.Colours[i]);
            }
            Save(swatches, $"categories-{p.Name}-{name}.png");
        }
        var status = ColourScale.Create([0, 1, 1.2], ["Status"], 0, 1.2, 1, "Utilisation", "ratio");
        using var asymmetric = RasterDrawing.Render(status.Legend, theme, 2);
        Save(asymmetric, $"scale-Status-mid-1-{name}.png");
        var custom = ColourScale.Create([], ["#006466", "#F2DEBD", "#BF5B45", "#3A2449"], -2, 6, 1, "Custom four stops", "m");
        using var customImage = RasterDrawing.Render(custom.Legend, theme, 2);
        Save(customImage, $"scale-Custom-{name}.png");
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void ExtremeAndAsymmetricLegendsRenderWithoutThrowing(string name)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        var cases = new (string Name, ColourScaleResult Scale)[] {
            ("subnormal", ColourScale.Create([], min: 0, max: 8 * double.Epsilon)),
            ("smallest", ColourScale.Create([0, double.Epsilon])),
            ("large", ColourScale.Create([], min: -double.MaxValue, max: double.MaxValue)),
            ("constant", ColourScale.Create([1e16, 1e16], title: "Constant 1e16")),
            ("constant-max", ColourScale.Create([double.MaxValue])),
            ("Blue-Red-asymmetric", ColourScale.Create([], ["Blue-Red"], -1, 9, title: "Blue–Red, Mid 0")),
            ("Status-mid-099", ColourScale.Create([], ["Status"], 0, 1, .99, "Status, Mid 0.99")),
            ("Status-mid-near-end", ColourScale.Create([], ["Status"], 0, 1, Math.BitDecrement(1))),
            ("Custom-32", ColourScale.Create([], Enumerable.Range(0, 32).Select(i => i % 2 == 0 ? "#000000" : "#FFFFFF").ToArray(), 0, 1, title: "32 alternating stops"))
        };
        foreach (var (label, scale) in cases)
        {
            if (scale.Mid > scale.Min && scale.Mid < scale.Max)
                Assert.Equal(scale.Palette.ColourAt(.5), scale.ColourAt(scale.Mid));
            using var image = RasterDrawing.Render(scale.Legend, theme, 2);
            Assert.True(image.Width > 0 && image.Height > 0);
            Save(image, $"scale-{label}-{name}.png");
        }
    }

    [Theory]
    [InlineData("Paper", 1d)]
    [InlineData("Paper", 2d)]
    [InlineData("Night", 1d)]
    [InlineData("Night", 2d)]
    public void DevicePixelCentresAgreeWithModelAcrossOffGridBreakpoints(string name, double resolution)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        var custom = Enumerable.Range(0, 32).Select(i => i % 2 == 0 ? "#000000" : "#FFFFFF").ToArray();
        foreach (var scale in new[] { ColourScale.Create([], ["Status"], 0, 1, .99),
            ColourScale.Create([], ["Blue-Red"], -1, 9), ColourScale.Create([], custom, 0, 1, .373) })
        {
            using var image = RasterDrawing.Render(scale.Legend, theme, resolution);
            var (left, right) = BarBounds(scale.Legend, theme);
            var margin = RasterDrawing.Margin(theme, resolution);
            var y = margin + (int)(45 * resolution);
            // Assert requested probes on both sides of Mid, plus every interior
            // column (also detects uncovered seams at custom knots). The expected
            // value belongs to the sampled device centre, not the nominal probe:
            // a sharp ramp can change many bytes between those two positions.
            var probes = new[] { .98, .99, .995, .05, .1, .15, .372, .373, .374 }
                .Select(t => (int)Math.Floor(margin + (left + (right - left) * t) * resolution));
            var columns = Enumerable.Range((int)Math.Ceiling(margin + left * resolution),
                (int)Math.Floor(margin + right * resolution) - (int)Math.Ceiling(margin + left * resolution));
            foreach (var x in probes.Concat(columns).Distinct())
            {
                var t = ((x + .5 - margin) / resolution - left) / (right - left);
                var value = ColourRange.Lerp(scale.Min, scale.Max, t);
                var expected = scale.ColourAt(value); var pixel = image.GetPixel(x, y);
                var rgb = ColorRgba.Parse(expected);
                Assert.True(Math.Abs(pixel.R - rgb.R) <= 3 && Math.Abs(pixel.G - rgb.G) <= 3 && Math.Abs(pixel.B - rgb.B) <= 3,
                    $"{name} {resolution}x {scale.Palette.Name} Mid={scale.Mid} x={x} t={t} bounds={left}..{right}: expected {expected}, actual {pixel}");
                Assert.Equal(255, pixel.A);
            }
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    [InlineData(1.25d)]
    public void DeviceColumnsRespectTranslationClippingAndRestoreGraphicsState(double resolution)
    {
        using var image = new Bitmap(720, 120);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Magenta);
        graphics.TranslateTransform(7.25f, 3.5f);
        graphics.ScaleTransform((float)resolution, (float)resolution);
        using var canvas = new GdiCanvas(graphics);
        var scale = ColourScale.Create([], ["Status"], 0, 1, .99);
        var rect = new Rect2(10.2, 12.3, 250.7, 10);
        var before = graphics.Transform.Elements;
        // Rounded clip supplied by the caller must survive the device-space fill.
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        graphics.SetClip(path);
        canvas.Gradient(rect, scale.Legend.Colors, scale.Legend.ColorPositions);
        Assert.Equal(before, graphics.Transform.Elements);
        var left = 7.25 + rect.X * resolution; var right = 7.25 + (rect.X + rect.Width) * resolution;
        var y = (int)(3.5 + (rect.Y + rect.Height / 2) * resolution);
        for (var x = (int)Math.Ceiling(left) + 5; x < (int)Math.Floor(right) - 5; x++)
            Near(scale.ColourAt((x + .5 - left) / (right - left)), image.GetPixel(x, y), 3);
        Assert.Equal(Color.Magenta.ToArgb(), image.GetPixel((int)Math.Ceiling(left), (int)Math.Ceiling(3.5 + rect.Y * resolution)).ToArgb());
    }

    [Fact]
    public void PdfBandsUseExactStopIntervalsAndAgreeWithModel()
    {
        var custom = Enumerable.Range(0, 32).Select(i => i % 2 == 0 ? "#000000" : "#FFFFFF").ToArray();
        foreach (var scale in new[] { ColourScale.Create([], ["Status"], 0, 1, .99),
            ColourScale.Create([], ["Blue-Red"], -1, 9), ColourScale.Create([], custom, 0, 1, .373) })
        {
            var rect = new Rect2(20, 40, 240, 10);
            var bands = ColourGradient.Bands(rect, scale.Legend.Colors, scale.Legend.ColorPositions).ToArray();
            Assert.InRange(bands.Length, 1, 256 + 4 * (scale.Legend.Colors.Length - 1));
            Assert.Equal(rect.X, bands[0].Rect.X);
            Assert.Equal(rect.X + rect.Width, bands[^1].Rect.X + bands[^1].Rect.Width);
            for (var i = 0; i < bands.Length; i++)
            {
                var (band, colour) = bands[i];
                if (i > 0) Assert.Equal(bands[i - 1].Rect.X + bands[i - 1].Rect.Width, band.X);
                var t = (band.X + band.Width / 2 - rect.X) / rect.Width;
                Near(scale.ColourAt(ColourRange.Lerp(scale.Min, scale.Max, t)),
                    Color.FromArgb(colour.A, colour.R, colour.G, colour.B), 1);
            }
        }
    }

    [Theory]
    [InlineData("Paper", false)]
    [InlineData("Paper", true)]
    [InlineData("Night", false)]
    [InlineData("Night", true)]
    [InlineData("Smoke", false)]
    [InlineData("Smoke", true)]
    [InlineData("Blueprint", false)]
    [InlineData("Blueprint", true)]
    public void ExtendCapsRenderTheirColoursAndKeepEndpointLabelsOnTheBar(string name, bool compact)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        GdiCanvas.RegisterTextMetrics();
        const int resolution = 3;
        foreach (var (below, above) in new (string?, string?)[] { (null, null), ("#FFFFFF", null), (null, "#FFF4A3"), ("#FFFFFF", "#FFF4A3") })
        foreach (var (min, max, width) in new[] { (0d, 1d, 180d), (-123456d, 987654d, 280d) })
        {
            var scale = ColourScale.Create([], min: min, max: max, below: below, above: above);
            var card = scale.Legend with { Width = width, Form = new(Compact: compact) };
            var size = CardMeasure.Measure(card, theme);
            Assert.Equal(size, CardMeasure.Measure(card with { BelowColor = null, AboveColor = null }, theme));
            Assert.Empty(CardMeasure.Hits(card, theme));
            using var image = new Bitmap((int)(size.Width * resolution), (int)(size.Height * resolution));
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform(resolution, resolution);
            using var gdi = new GdiCanvas(graphics);
            var canvas = new LegendRecordingCanvas(gdi);
            CardPainter.Paint(canvas, card, theme);
            var bar = Assert.Single(canvas.Gradients);
            Assert.Equal(compact ? LegendCardPainter.GradientCompactBar : LegendCardPainter.GradientBar, bar.Height);
            Assert.Equal((below is null ? 0 : 1) + (above is null ? 0 : 1), canvas.Polygons.Count);
            foreach (var (colour, end, direction) in new[] { (below, bar.X, -1), (above, bar.X + bar.Width, 1) })
            {
                if (colour is null) continue;
                var points = Assert.Single(canvas.Polygons, p => p.Fill == colour).Points;
                Assert.Equal(3, points.Length);
                Assert.Contains(new Point2(end, bar.Y), points);
                Assert.Contains(new Point2(end, bar.Y + bar.Height), points);
                var tip = Assert.Single(points, p => p.X != end);
                Assert.True(direction * (tip.X - end) > 0);
                Assert.Equal(bar.Y + bar.Height / 2, tip.Y);
                var outline = Assert.Single(canvas.Outlines, o => o.Points.SequenceEqual(points.Append(points[0])));
                Assert.Equal(theme.Muted, outline.Colour);
                Assert.InRange(outline.Width, .5, 1);
                Assert.All(points, p =>
                {
                    Assert.InRange(p.X - outline.Width / 2, 0, size.Width);
                    Assert.InRange(p.X + outline.Width / 2, 0, size.Width);
                    Assert.InRange(p.Y - outline.Width / 2, 0, size.Height);
                    Assert.InRange(p.Y + outline.Width / 2, 0, size.Height);
                });
                // Probe well inside each triangle, away from antialiasing and the outline.
                Near(colour, image.GetPixel((int)((end + (tip.X - end) * .4) * resolution),
                    (int)((bar.Y + bar.Height / 2) * resolution)), 0);
            }
            foreach (var (value, x) in new[] { (min, bar.X), (max, bar.X + bar.Width) })
            {
                var label = Assert.Single(canvas.Labels, l => l.Text == value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture));
                var labelSize = canvas.MeasureText(label.Text, 10, theme.Font);
                Assert.Equal(x, label.At.X + labelSize.Width / 2, 8);
                Assert.InRange(label.At.X, 0, size.Width - labelSize.Width);
                Assert.InRange(label.At.Y + labelSize.Height, 0, size.Height);
                Assert.Contains(canvas.Lines, l => l.From == new Point2(x, bar.Y + bar.Height) &&
                    l.To == new Point2(x, bar.Y + bar.Height + 4));
            }
            // Reserving the caps must preserve the gradient's correspondence to model colours.
            var pixelX = (int)((bar.X + bar.Width * .3) * resolution);
            var fraction = ((pixelX + .5) / resolution - bar.X) / bar.Width;
            Near(scale.ColourAt(ColourRange.Lerp(min, max, fraction)), image.GetPixel(pixelX,
                (int)((bar.Y + bar.Height / 2) * resolution)), 3);
            Save(image, $"extend-{name}-{compact}-{min}-{below is not null}-{above is not null}.png");
        }
    }

    private sealed class LegendRecordingCanvas(ICanvas2D inner) : ICanvas2D
    {
        public List<Rect2> Gradients { get; } = [];
        public List<(Point2[] Points, string Fill)> Polygons { get; } = [];
        public List<(Point2[] Points, string Colour, double Width)> Outlines { get; } = [];
        public List<(Point2 From, Point2 To)> Lines { get; } = [];
        public List<(string Text, Point2 At)> Labels { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => inner.MeasureText(text, size, font);
        public void Rect(Rect2 rect, string fill) => inner.Rect(rect, fill);
        public void RoundedRect(Rect2 rect, double radius, string fill) => inner.RoundedRect(rect, radius, fill);
        public IDisposable Clip(Rect2 rect) => inner.Clip(rect);
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => Gradient(rect, colors, []);
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors, IReadOnlyList<double> positions)
        { Gradients.Add(rect); inner.Gradient(rect, colors, positions); }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill)
        { Polygons.Add((points.ToArray(), fill)); inner.FillPolygon(points, fill); }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1)
        { Outlines.Add((points.ToArray(), color, width)); inner.Polyline(points, color, width); }
        public void Line(Point2 from, Point2 to, string color, double width = 1)
        { Lines.Add((from, to)); inner.Line(from, to, color, width); }
        public void Text(string text, Point2 topLeft, double size, string color, string font)
        { Labels.Add((text, topLeft)); inner.Text(text, topLeft, size, color, font); }
    }

    private static (double Left, double Right) BarBounds(CardSpec card, Theme theme)
    {
        string Label(double v) => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
        return (theme.Padding + Math.Max(2, TextMetrics.Measurer(Label(card.Min!.Value), 10, theme.Font).Width / 2),
            card.Width - theme.Padding - Math.Max(2, TextMetrics.Measurer(Label(card.Max!.Value), 10, theme.Font).Width / 2));
    }

    private static void Near(string expected, Color pixel, int tolerance)
    {
        var rgb = ColorRgba.Parse(expected);
        Assert.Equal(255, pixel.A);
        Assert.InRange(Math.Abs(pixel.R - rgb.R), 0, tolerance);
        Assert.InRange(Math.Abs(pixel.G - rgb.G), 0, tolerance);
        Assert.InRange(Math.Abs(pixel.B - rgb.B), 0, tolerance);
    }

    // Opt-in review artifacts; ordinary unit test runs do not write into the checkout.
    private static void Save(Bitmap bitmap, string filename)
    {
        var folder = Environment.GetEnvironmentVariable("CONDUITPLUS_COLOUR_RENDERS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        bitmap.Save(Path.Combine(folder, filename), ImageFormat.Png);
    }
}
