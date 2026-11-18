using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CardInteractionControllerTests
{
    private static readonly Theme Theme = ConduitPlus.Core.Theme.Paper;
    private static CardSpec Spec => new() { Id = "card", Kind = CardKind.Bar, Width = 280, Keys = ["a", "b", "c", "d", "e"], Labels = ["A", "B", "C", "D", "E"], Values = [1, 2, 3, 4, 5], MaxRows = 2 };
    private static CardInteractionFrame Frame(CardSpec? spec = null) { var s = spec ?? Spec; return CardInteractionFrame.Stack([s], Theme, new([s], Theme)); }
    private static Point2 Row(CardInteractionFrame f, int index = 0) { var h = f.Cards[0].Hits[index]; return new(h.X + 1, h.Y + 1); }
    [Theory]
    [InlineData(CardModifiers.None, BrushSelectMode.Replace)]
    [InlineData(CardModifiers.Shift, BrushSelectMode.Add)]
    [InlineData(CardModifiers.Control, BrushSelectMode.Remove)]
    [InlineData(CardModifiers.Shift | CardModifiers.Control, BrushSelectMode.Toggle)]
    public void CommitsOriginalModifiersEvenIfReleasedBeforeButton(CardModifiers modifiers, BrushSelectMode expected)
    {
        var c = new CardInteractionController(); var f = Frame(); var p = Row(f);
        Assert.Equal(CardResponse.Capture, c.Down(f, p, new(10, 10), modifiers).Response);
        var result = c.Up(f, p, new(10, 10), CardModifiers.None);
        Assert.Equal(expected, result.Action!.Mode); Assert.Equal("a", result.Action.Key);
        Assert.Equal(CardResponse.Release, result.Response);
        Assert.Equal(CardResponse.Pass, c.Up(f, p, new(10, 10), CardModifiers.None).Response);
    }
    [Theory]
    [InlineData(4, true)]
    [InlineData(4.01, false)]
    public void FourControlPixelThreshold(double distance, bool commit)
    {
        var c = new CardInteractionController(); var f = Frame(); var p = Row(f);
        c.Down(f, p, new(0, 0), CardModifiers.None);
        Assert.Equal(commit, c.Up(f, p, new(distance, 0), CardModifiers.None).Action is not null);
    }
    [Fact]
    public void ReleaseMustMatchCardAndKeyAndMovingAwayCannotBeUndone()
    {
        var c = new CardInteractionController(); var f = Frame(); var p = Row(f);
        c.Down(f, p, p, CardModifiers.None);
        Assert.Null(c.Up(f, Row(f, 1), p, CardModifiers.None).Action);
        c.Down(f, p, p, CardModifiers.None);
        c.Move(f, p, new(p.X + 5, p.Y), CardModifiers.None);
        Assert.Null(c.Up(f, p, p, CardModifiers.None).Action);
        c.Down(f, p, p, CardModifiers.None);
        var other = Frame(Spec with { Id = "other" });
        Assert.Null(c.Up(other, p, p, CardModifiers.None).Action);
    }
    [Fact]
    public void CancellationRollsBackThumbAndStillOwnsRelease()
    {
        var f = Frame(Spec with { ScrollOffset = 1 }); var bar = TableScroll.Bar(f.Cards[0].Spec, Theme)!.Value;
        var p = new Point2(bar.Thumb.X + 1, bar.Thumb.Y + 1); var c = new CardInteractionController();
        Assert.Equal(CardResponse.Capture, c.Down(f, p, p, CardModifiers.None).Response);
        Assert.Equal(CardActionKind.Scroll, c.Move(f, new(p.X, p.Y + 20), p, CardModifiers.None).Action!.Kind);
        Assert.Equal(1, c.Cancel().Action!.Offset); Assert.False(c.Active); Assert.True(c.OwnsRelease);
        Assert.Equal(CardResponse.Release, c.Up(f, p, p, CardModifiers.None).Response);
        Assert.False(c.OwnsRelease);
    }
    [Fact]
    public void AddedModifierOrDisableCancelsAndReleaseIsSwallowed()
    {
        var c = new CardInteractionController(); var f = Frame(); var p = Row(f);
        c.Down(f, p, p, CardModifiers.None);
        Assert.Equal(CardResponse.Release, c.Move(f, p, p, CardModifiers.Shift).Response);
        Assert.Null(c.Up(f, p, p, CardModifiers.None).Action);
        c.Down(f, p, p, CardModifiers.None);
        Assert.Null(c.Up(f, p, p, CardModifiers.None, enabled: false).Action);
    }
    [Fact]
    public void HostPolicyConsumesOnlyPinnedBodyAndPassesNavigationAndGaps()
    {
        var c = new CardInteractionController(); var f = Frame(); var padding = new Point2(1, 1);
        Assert.Equal(CardResponse.Pass, c.Down(f, padding, padding, CardModifiers.None).Response);
        Assert.Equal(CardResponse.Capture, c.Down(f, padding, padding, CardModifiers.None, consumeBody: true).Response);
        Assert.Null(c.Up(f, padding, padding, CardModifiers.None).Action);
        Assert.Equal(CardResponse.Pass, c.Down(f, new(-1, -1), padding, CardModifiers.None, consumeBody: true).Response);
        Assert.Equal(CardResponse.Pass, c.Down(f, padding, padding, CardModifiers.Alt, consumeBody: true).Response);
        Assert.Equal(CardResponse.Pass, c.Down(f, Row(f), padding, CardModifiers.None, busy: true).Response);
        Assert.Equal(CardResponse.Pass, c.Down(f, Row(f), padding, CardModifiers.None, leftOnly: false).Response);
        Assert.Equal(CardResponse.Pass, c.Down(f, padding, padding, CardModifiers.Shift, consumeBody: true).Response);
    }
    [Fact]
    public void SurfaceAndSlotPolicyPreservesInlineDefaults()
    {
        var spec = Spec with { WiredSlot = Slot.Br };
        var inline = new CanvasCardState(); inline.Replace(spec); Assert.Null(inline.Spec!.WiredSlot);
        var pinned = new CanvasCardState("canvas-pinned", retainSlots: true); pinned.Bind(null, Guid.NewGuid()); pinned.Replace(spec);
        Assert.Equal(Slot.Br, pinned.Spec!.WiredSlot);
        pinned.Activate("a"); Assert.Equal("canvas-pinned", pinned.Current.Events.Last().SourceSurface);
    }
    [Fact]
    public void ANewPressForgetsAReleaseKeptAfterCancellation()
    {
        var c = new CardInteractionController(); var f = Frame(); var p = Row(f);
        c.Down(f, p, new(10, 10), CardModifiers.None); c.Cancel();
        Assert.True(c.OwnsRelease); // the cancelled gesture's release is still ours
        // That release happened elsewhere; the next press is not on a card and must not leave its release swallowed.
        Assert.Equal(CardResponse.Pass, c.Down(f, new(-50, -50), new(0, 0), CardModifiers.None).Response);
        Assert.Equal(CardResponse.Pass, c.Up(f, new(-50, -50), new(0, 0), CardModifiers.None).Response);
    }
}
