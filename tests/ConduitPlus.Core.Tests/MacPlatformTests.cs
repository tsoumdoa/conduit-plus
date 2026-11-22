using System.Drawing;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

/// <summary>Host-neutral policy behind the macOS adapter. Windows inputs must map to the original behaviour.</summary>
public sealed class MacPlatformTests
{
    [Theory]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, false, true, true, false, true)]
    [InlineData(false, true, true, false, true, true)]
    public void WindowsIntentIsRhinoButtonShiftAndCtrl(bool left, bool shift, bool ctrl, bool primary, bool expectShift, bool toggle) =>
        Assert.Equal(new PointerIntent(primary, expectShift, toggle), PointerModifiers.Resolve(false, left, shift, ctrl, command: true, physicalControl: true));

    [Fact]
    public void MacCommandTogglesAndControlClickIsSecondary()
    {
        Assert.Equal(new PointerIntent(true, false, true), PointerModifiers.Resolve(true, true, false, ctrlReported: false, command: true));
        // Rhino may report Command as Ctrl, or physical Control as Ctrl: only Command toggles.
        Assert.Equal(new PointerIntent(true, false, false), PointerModifiers.Resolve(true, true, false, ctrlReported: true, command: false));
        Assert.Equal(new PointerIntent(false, false, false), PointerModifiers.Resolve(true, true, false, ctrlReported: true, command: false, physicalControl: true));
        Assert.Equal(new PointerIntent(true, true, false), PointerModifiers.Resolve(true, true, true, false));
        Assert.False(PointerModifiers.Resolve(true, false, false, false).Primary);
    }

    [Fact]
    public void MacFlagsMapToCardModifiers()
    {
        Assert.Equal(CardModifiers.None, MacModifierMapping.ToCard(MacModifierFlags.None));
        Assert.Equal(CardModifiers.Control, MacModifierMapping.ToCard(MacModifierFlags.Command));
        Assert.Equal(CardModifiers.Shift | CardModifiers.Alt, MacModifierMapping.ToCard(MacModifierFlags.Shift | MacModifierFlags.Option));
        Assert.Equal(CardModifiers.Other, MacModifierMapping.ToCard(MacModifierFlags.Control));
        // Caps lock / function bits are ignored.
        Assert.Equal(CardModifiers.Control, MacModifierMapping.ToCard(MacModifierFlags.Command | (MacModifierFlags)(1UL << 16) | (MacModifierFlags)(1UL << 23)));
    }

    [Theory]
    [InlineData(1.0, false, 22, 1200)]       // one wheel line → one row
    [InlineData(-2.5, false, 22, -3000)]
    [InlineData(22, true, 22, 1200)]         // one row of finger travel
    [InlineData(-11, true, 22, -600)]
    [InlineData(3, true, 0, 3600)]           // degenerate row height clamps to one point
    [InlineData(0, true, 22, 0)]
    [InlineData(double.NaN, false, 22, 0)]
    public void MacScrollUnits(double delta, bool precise, double row, int expected) => Assert.Equal(expected, MacScroll.Units(delta, precise, row));

    [Fact]
    public void WheelAccumulatorDefaultsAreWindowsNotches()
    {
        var wheel = new WheelAccumulator<string>();
        wheel.Add("t", 60); Assert.False(wheel.HasWhole); Assert.Empty(wheel.Take());
        wheel.Add("t", 60); Assert.True(wheel.HasWhole);
        Assert.Equal([("t", -3)], wheel.Take());
        wheel.Add("t", -240); Assert.Equal([("t", 6)], wheel.Take());
    }

    [Fact]
    public void MacAccumulatorScrollsOneRowPerStepAndKeepsRemainders()
    {
        var wheel = new WheelAccumulator<string>(MacScroll.UnitsPerRow, 1);
        // Trackpad: positive deltas reveal earlier rows, like a Windows wheel rotated away.
        foreach (var _ in Enumerable.Range(0, 3)) wheel.Add("t", MacScroll.Units(8, true, 22));
        Assert.True(wheel.HasWhole);
        Assert.Equal([("t", -1)], wheel.Take());
        // 3 × 436 = 1308 units: one row taken, 108 carried. Reversing by 1308 lands exactly one row back.
        wheel.Add("t", MacScroll.Units(-8, true, 22));
        Assert.False(wheel.HasWhole);
        wheel.Add("t", MacScroll.Units(-8, true, 22) * 2);
        Assert.Equal([("t", 1)], wheel.Take());
        Assert.False(wheel.HasWhole);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WheelAccumulator<string>(0, 1));
    }

    private sealed record Target(string Name);

    [Fact]
    public void ClassicWheelEventsAreHitTestedIndividually()
    {
        var latch = new ScrollGestureLatch<Target>();
        var card = new Target("card");
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.None, () => card));
        Assert.Null(latch.Decide(ScrollPhase.None, ScrollPhase.None, () => null));
        Assert.False(latch.Active);
    }

    [Fact]
    public void GestureOverCardKeepsCardThroughMomentumEvenIfPointerTargetChanges()
    {
        var latch = new ScrollGestureLatch<Target>();
        var card = new Target("card");
        Assert.Same(card, latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => card));
        Assert.Same(card, latch.Decide(ScrollPhase.Changed, ScrollPhase.None, () => null));
        Assert.Same(card, latch.Decide(ScrollPhase.Ended, ScrollPhase.None, () => null));
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.Began, () => null));
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.Changed, () => null));
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.Ended, () => null));
        Assert.False(latch.Active);
    }

    [Fact]
    public void GestureStartedOverRhinoNeverJumpsIntoACard()
    {
        var latch = new ScrollGestureLatch<Target>();
        var card = new Target("card");
        Assert.Null(latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => null));
        Assert.Null(latch.Decide(ScrollPhase.Changed, ScrollPhase.None, () => card));
        Assert.Null(latch.Decide(ScrollPhase.None, ScrollPhase.Changed, () => card));
        // The next gesture decides afresh.
        Assert.Same(card, latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => card));
    }

    [Fact]
    public void MayBeginDecidesAndCancelledWithoutMovementResets()
    {
        var latch = new ScrollGestureLatch<Target>();
        var card = new Target("card"); var other = new Target("other");
        Assert.Same(card, latch.Decide(ScrollPhase.MayBegin, ScrollPhase.None, () => card));
        Assert.Same(card, latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => other)); // Began keeps the MayBegin owner
        latch.Reset();
        Assert.Same(card, latch.Decide(ScrollPhase.MayBegin, ScrollPhase.None, () => card));
        Assert.Same(card, latch.Decide(ScrollPhase.Cancelled, ScrollPhase.None, () => null));
        Assert.False(latch.Active);
        Assert.Null(latch.Decide(ScrollPhase.Changed, ScrollPhase.None, () => card)); // an unseen gesture stays with Rhino
    }

    [Fact]
    public void FontFallbackIsIdentityOnWindows() =>
        Assert.Equal("Segoe UI", FontFallback.Resolve("Segoe UI", false, _ => false));

    [Theory]
    [InlineData("Segoe UI", "Helvetica Neue,Helvetica,Arial", "Helvetica Neue")]
    [InlineData("segoe ui", "Helvetica,Arial", "Helvetica")]
    [InlineData("Menlo", "Menlo,Helvetica Neue", "Menlo")]
    [InlineData("Missing", "Arial", "Arial")]
    [InlineData("", "", "Helvetica Neue")]
    public void FontFallbackOnMac(string family, string installed, string expected)
    {
        var set = installed.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, FontFallback.Resolve(family, true, set.Contains));
    }

    [Fact]
    public void FontFallbackSurvivesAThrowingProbe() =>
        Assert.Equal("Helvetica Neue", FontFallback.Resolve("Segoe UI", true, _ => throw new InvalidOperationException()));

    [Theory]
    [InlineData(2400, 1200, 2)]
    [InlineData(1200, 1200, 1)]
    [InlineData(2365, 1200, 2)]
    [InlineData(1800, 1200, 1.5)]
    [InlineData(600, 1200, 1)]    // never below 1
    [InlineData(12000, 1200, 4)]  // never above 4
    [InlineData(0, 1200, 1)]
    [InlineData(1200, 0, 1)]
    [InlineData(double.NaN, 1200, 1)]
    public void ViewScaleFromGeometry(double pixels, double points, double expected) => Assert.Equal(expected, HostScale.FromViewGeometry(pixels, points));

    [Fact]
    public void ViewScaleOverrideWins()
    {
        Assert.Equal(1.25, HostScale.FromViewGeometry(2400, 1200, 1.25));
        Assert.Equal(2, HostScale.ParseOverride("2"));
        Assert.Equal(1.5, HostScale.ParseOverride("1.5"));
        Assert.Null(HostScale.ParseOverride("abc"));
        Assert.Null(HostScale.ParseOverride("-1"));
        Assert.Null(HostScale.ParseOverride(null));
    }

    [Fact]
    public void CalibrationLearnsConstantOffset()
    {
        var c = new PointerCalibration();
        Assert.Equal(new Point2(10, 20), c.Apply(new(10, 20)));
        c.Observe(new(110, 70), new(100, 40));
        Assert.Equal(new Point2(10, 30), c.Correction);
        Assert.Equal(new Point2(60, 80), c.Apply(new(50, 50)));
        c.Observe(new(210, 170), new(200, 140));
        Assert.Equal(0, c.LastError, 6);
        Assert.Equal(2, c.Samples);
        c.Reset(); Assert.Equal(default, c.Correction);
    }

    [Fact]
    public void CalibrationIgnoresASingleStaleSampleButFollowsARealShift()
    {
        var c = new PointerCalibration();
        c.Observe(new(110, 70), new(100, 40));          // offset (10, 30)
        c.Observe(new(150, 90), new(100, 40));          // one outlier (stalled cursor): ignored
        Assert.Equal(new Point2(10, 30), c.Correction);
        c.Observe(new(111, 71), new(100, 40));          // within tolerance: keeps tracking
        Assert.Equal(new Point2(11, 31), c.Correction);
        c.Observe(new(100, 100), new(100, 40));         // real shift (e.g. a title band appeared)...
        c.Observe(new(101, 100), new(100, 41));         // ...confirmed by the next sample
        Assert.Equal(new Point2(1, 59), c.Correction);
    }

    [Fact]
    public void ClaimedGestureStaysClaimedWhenTheHitIsUnavailable()
    {
        // Suspended input (command/busy) decides with a null hit: a claimed gesture keeps its owner until it ends.
        var latch = new ScrollGestureLatch<Target>();
        var card = new Target("card");
        latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => card);
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.Changed, () => null));
        Assert.Same(card, latch.Decide(ScrollPhase.None, ScrollPhase.Ended, () => null));
        Assert.Null(latch.Decide(ScrollPhase.Began, ScrollPhase.None, () => null)); // a new gesture goes to Rhino
    }

    [Theory]
    [InlineData(1.0, 1.0, 2.0, 2.0)]   // GH transform lacks Retina: add it
    [InlineData(2.0, 1.0, 2.0, 1.0)]   // transform already carries Retina
    [InlineData(3.0, 1.5, 2.0, 1.0)]
    [InlineData(1.5, 1.5, 1.0, 1.0)]   // non-Retina display
    [InlineData(double.NaN, double.NaN, 2.0, 2.0)] // pinned (screen-space) cards
    public void CanvasRatio(double deviceScale, double zoom, double backing, double expected) => Assert.Equal(expected, CanvasScale.Ratio(deviceScale, zoom, backing));

    [Fact]
    public void CanvasRatioOverride() => Assert.Equal(1, CanvasScale.Ratio(1, 1, 2, overrideRatio: 1));

    [Fact]
    public void MacClipboardPayloadIsTransparentAndOpaqueTwoTimesPngWithoutHover()
    {
        var state = new CanvasCardState();
        state.Replace(new() { Id = "bar", Kind = CardKind.Bar, Keys = ["a", "b"], Labels = ["A", "B"], Values = [.5, .8] });
        state.Activate("a"); state.Hover("b");
        var theme = Theme.Smoke;
        CardSpec[] cards = [state.Spec!];
        var (png, opaque) = CardClipboard.MacPayload(cards, theme);
        using var transparent = new Bitmap(new MemoryStream(png));
        using var flat = new Bitmap(new MemoryStream(opaque));
        var size = new CanvasStackLayout(cards, theme).Size;
        Assert.Equal((int)Math.Ceiling(size.Width * 2), transparent.Width);
        Assert.Equal(transparent.Size, flat.Size);
        Assert.Equal(0, transparent.GetPixel(0, 0).A);
        var background = CardImageExport.Background(theme);
        var corner = flat.GetPixel(0, 0);
        Assert.Equal((255, background.R, background.G, background.B), (corner.A, corner.R, corner.G, corner.B));
        // Same pixels as the Windows payload's PNG: hover is stripped, selection kept.
        using var reference = RasterDrawing.Render(BrushPresentation.WithoutHover(cards[0]), theme, CardClipboard.Scale);
        Assert.Equal(reference.Size, transparent.Size);
        Assert.Throws<InvalidOperationException>(() => CardClipboard.MacPayload([], theme));
    }
}
