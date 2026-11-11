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
    [InlineData("Smoke")]
    [InlineData("Blueprint")]
    public void EmittedCardsRenderWithIdenticalModelRgbInEveryTheme(string name)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        foreach (var p in Palettes.All)
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
            Assert.Equal(128 * (scale.Legend.Colors.Length - 1), bands.Length);
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
