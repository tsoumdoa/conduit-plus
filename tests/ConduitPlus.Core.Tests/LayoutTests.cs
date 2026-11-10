using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class LayoutTests
{
    [Fact]
    public void SlotsUseInputOrderAndStack()
    {
        var cards = Enumerable.Range(0, 8).Select(i => new CardSpec { Id = $"c{i}", Kind = CardKind.Metric, WiredSlot = Slot.Tl }).ToArray();
        Assert.Equal(Slot.Tl, SlotLayout.Resolve(cards[0], 5));
        for (var i = 0; i < 12; i++) Assert.Equal((Slot)(i % 6), SlotLayout.Resolve(cards[0] with { WiredSlot = null }, i));
        var placed = SlotLayout.Place(cards, Theme.Paper, new(1600, 1600));
        Assert.Equal(cards.Select(c => c.Id), placed.Select(c => c.Id));
        for (var i = 1; i < placed.Length; i++) Assert.True(placed[i].Y >= placed[i - 1].Y + placed[i - 1].H);
    }
}
