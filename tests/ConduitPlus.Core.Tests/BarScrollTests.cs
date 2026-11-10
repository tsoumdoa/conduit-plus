using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public sealed class BarScrollTests
{
    [Fact]
    public void CompactBarsScrollAndRevealInDataOrder()
    {
        var c = new CardSpec { Id = "bar", Kind = CardKind.Bar, Form = new(6),
            Values = Enumerable.Range(0, 17).Select(i => (double?)i).ToArray(),
            Keys = Enumerable.Range(0, 17).Select(i => $"k{i}").ToArray(),
            Labels = Enumerable.Range(0, 17).Select(i => $"Label {i}").ToArray() };
        Assert.Equal(6, TableScroll.Visible(c));
        Assert.Equal(11, TableScroll.MaxOffset(c));
        Assert.Equal(0, TableScroll.Offset(c with { ScrollOffset = -5 }));
        Assert.Equal(11, TableScroll.Offset(c with { ScrollOffset = 99 }));
        foreach (var offset in new[] { 0, 5 })
        {
            var view = c with { ScrollOffset = offset };
            Assert.Equal(c.Keys.Skip(offset).Take(6), CardMeasure.DataHits(view, Theme.Paper).Select(h => h.Key));
            var bar = TableScroll.Bar(view, Theme.Paper)!.Value;
            Assert.Equal(36, bar.Track.Y);
            Assert.Equal(6 * Theme.Paper.RowHeight, bar.Track.Height);
            var size = CardMeasure.Measure(view, Theme.Paper);
            Assert.True(bar.Track.X + bar.Track.Width <= size.Width);
            Assert.True(bar.Track.Y + bar.Track.Height <= size.Height);
            Assert.True(bar.Thumb.Y >= bar.Track.Y && bar.Thumb.Y + bar.Thumb.Height <= bar.Track.Y + bar.Track.Height);
        }
        Assert.Equal(11, TableScroll.Reveal(c, "k16"));
        Assert.Equal("k16", BrushPresentation.FirstNewRow(c with { SelectedKeys = ["k16"] }, new HashSet<string>()));
        Assert.False(TableScroll.CanScroll(c with { Form = CardForm.None }));
    }
}
