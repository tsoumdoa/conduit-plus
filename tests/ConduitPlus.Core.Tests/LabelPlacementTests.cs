using System.Drawing;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class LabelPlacementTests
{
    private static readonly Size2 View = new(400, 240);
    private static LabelCandidate Candidate(int index, double x, double y = 100) =>
        new(new(Guid.Empty, new(Guid.Empty, "{0}"), null, index), new(x, y), 0,
            LabelActivity.Ordinary, new(true, false, true), 35, 20);

    [Theory]
    [InlineData(18, LabelForm.Dot)]
    [InlineData(18.01, LabelForm.Short)]
    [InlineData(48, LabelForm.Short)]
    [InlineData(48.01, LabelForm.Full)]
    public void UsesStrictSpacingThresholds(double distance, LabelForm expected)
    {
        var frame = new LabelLayout().Place([Candidate(0, 100), Candidate(1, 100 + distance)], View, 1, []);
        Assert.Equal(expected, frame[0].Form);
    }

    [Fact]
    public void ShorteningPreservesGraphemes()
    {
        Assert.Equal("👩‍🔬e\u0301🇭🇰👍🏽Z…", LabelLayout.Shorten("👩‍🔬e\u0301🇭🇰👍🏽Zfinish"));
        Assert.Equal("abc", LabelLayout.Shorten("abc"));
    }

    [Fact]
    public void CapsMeasuredGlyphWidthWithoutSplittingGraphemes()
    {
        const string cluster = "👩‍🔬";
        var fit = LabelPainter.Fit(string.Concat(Enumerable.Repeat(cluster, 20)), Color.Orange.ToArgb(), "test",
            (text, _, _) => new(new System.Globalization.StringInfo(text).LengthInTextElements * 20, 16));
        Assert.Equal(string.Concat(Enumerable.Repeat(cluster, 6)) + "…", fit.Text);
        Assert.Equal(156, fit.Width);
    }

    [Fact]
    public void CoincidentOwnersUseTierThenPriorityThenStableIdentity()
    {
        var first = Candidate(0, 100);
        var second = first with { Identity = first.Identity with { Owner = new("00000000-0000-0000-0000-000000000001") } };
        Assert.Equal(first.Identity, Assert.Single(new LabelLayout().Place([second, first], View, 1, [])).Identity);
        Assert.Equal(second.Identity, Assert.Single(new LabelLayout().Place([second with { Priority = 1 }, first], View, 1, [])).Identity);
        var selected = second with { Activity = LabelActivity.Selected, Visibility = new(true, true, true) };
        var result = Assert.Single(new LabelLayout().Place([first with { Priority = 10 }, selected], View, 1, []));
        Assert.Equal(second.Identity, result.Identity);
        Assert.Equal(LabelForm.Full, result.Form);
        Assert.Equal(.45, result.Opacity);
    }

    [Fact]
    public void EdgeFlipAndObstaclesAreDpiInvariant()
    {
        var first = Candidate(0, 385);
        var occupied = new Rect2(380, 120, 15, 40);
        var second = Candidate(1, 390, 130);
        var expected = Assert.Single(new LabelLayout().Place([first, second], View, 1, [occupied]));
        Assert.True(expected.Left);
        LabelCandidate Double(LabelCandidate c) => c with { Anchor = new(c.Anchor.X * 2, c.Anchor.Y * 2) };
        Assert.Equal(expected, Assert.Single(new LabelLayout().Place([Double(first), Double(second)],
            new(800, 480), 2, [new(760, 240, 30, 80)])));
    }

    [Fact]
    public void KeepsSideAndFormUntilCollisionAndWaitsForContinuousPromotion()
    {
        var clock = new Clock();
        var layout = new LabelLayout(clock);
        var first = Candidate(0, 100);
        LabelPlacement Frame(double distance, params Rect2[] obstacles) =>
            layout.Place([first, Candidate(1, 100 + distance)], View, 1, obstacles)[0];
        Assert.Equal(LabelForm.Full, Frame(65).Form);
        Assert.Equal(LabelForm.Full, Frame(45).Form);
        Assert.Equal(LabelForm.Short, Frame(43).Form);
        Assert.Equal(LabelForm.Short, Frame(51).Form);
        Assert.Equal(LabelForm.Short, Frame(70).Form);
        clock.Now = 119;
        Assert.Equal(LabelForm.Short, Frame(70).Form);
        clock.Now = 120;
        Assert.Equal(LabelForm.Full, Frame(70).Form);
        Assert.Null(layout.RedrawAfter);
        Assert.Equal(LabelForm.Dot, Frame(70, new(20, 80, 75, 40), new(106, 80, 50, 40)).Form);
    }

    [Fact]
    public void CacheUsesResolvedKeysAndEvictsLeastRecentlyUsed()
    {
        using var cache = new LabelRasterCache<Resource>(8);
        var a = new LabelRasterKey("one", null, LabelPalette.FromTheme(Theme.Night), 1, LabelForm.Full, 30, 1);
        var b = a with { Text = "two" };
        var c = a with { Dpi = 2 };
        var first = new Resource();
        var second = new Resource();
        cache.Add(a, first, 4);
        cache.Add(b, second, 4);
        Assert.True(cache.TryGet(a, out _));
        cache.Add(c, new(), 4);
        Assert.True(second.Disposed);
        Assert.False(first.Disposed);
        Assert.False(cache.TryGet(a with { Swatch = 1 }, out _));
        Assert.False(cache.TryGet(a with { Palette = LabelPalette.FromTheme(Theme.Paper) }, out _));
        Assert.False(cache.TryGet(a with { Form = LabelForm.Short }, out _));
        cache.Retain(key => key.Dpi == 2);
        Assert.True(first.Disposed);
        Assert.Equal(4, cache.Bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllThemesRenderNeutralReadablePillsAndAuthoredDots(int index)
    {
        var palette = LabelPalette.FromTheme(Theme.Presets[index].Resolve(false));
        var key = new LabelRasterKey("Room 101", Color.Orange.ToArgb(), palette, 2, LabelForm.Full, 80, 1);
        using var pill = RasterDrawing.RenderLabel(key);
        using var dot = RasterDrawing.RenderLabel(key with { Form = LabelForm.Dot, Width = 5 });
        using var faded = RasterDrawing.RenderLabel(key with { Opacity = .45 });
        Assert.Equal((160, 36), (pill.Width, pill.Height));
        Assert.Equal(Color.Orange.ToArgb(), dot.GetPixel(5, 5).ToArgb());
        Assert.Equal(Color.Orange.ToArgb(), pill.GetPixel(12, 18).ToArgb());
        Assert.InRange(faded.GetPixel(120, 18).A, 109, 115);

        var ink = ColorRgba.Parse(palette.Ink);
        var surface = ColorRgba.Parse(palette.Surface);
        foreach (var background in new[] { 0, 255, 100 })
        {
            var alpha = surface.A / 255d;
            double Mix(byte value) => value * alpha + background * (1 - alpha);
            var foreground = Luminance(ink.R, ink.G, ink.B);
            var backing = Luminance(Mix(surface.R), Mix(surface.G), Mix(surface.B));
            Assert.True((Math.Max(foreground, backing) + .05) / (Math.Min(foreground, backing) + .05) >= 4.5);
        }
        static double Luminance(double r, double g, double b) => .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
        static double Linear(double value) => value / 255 <= .04045 ? value / 255 / 12.92 : Math.Pow((value / 255 + .055) / 1.055, 2.4);
    }

    private sealed class Resource : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
}
