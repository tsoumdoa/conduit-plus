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
            using var image = RasterDrawing.Render(scale.Legend, theme, 2);
            Assert.True(image.Width > 0 && image.Height > 0);
            Save(image, $"scale-{label}-{name}.png");
        }
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
