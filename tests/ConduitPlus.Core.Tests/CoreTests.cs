using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CoreTests
{
    [Fact]
    public void SpecsRoundTripPreservesNullSignedValuesAndKeys()
    {
        var cards = StructuralDemo.Create(); cards[4] = cards[4] with { Values = [.. cards[4].Values.Take(18), null, -.2] };
        var json = CardJson.Serialize(cards);
        var copy = CardJson.Deserialize<CardSpec[]>(json);
        Assert.Equal(json, CardJson.Serialize(copy)); Assert.Null(copy[4].Values[18]); Assert.Equal(-.2, copy[4].Values[19]);
    }
    [Fact]
    public void RevisionsChangeOnlyForDirtyCardIncludingMutatedArrays()
    {
        var store = new CardStore(); var cards = StructuralDemo.Create();
        Assert.Equal(6, store.Update(cards).Count); var first = store.Snapshot;
        Assert.Empty(store.Update(cards)); cards[4].Values[0] = .123;
        var changed = Assert.Single(store.Update(cards)); Assert.Equal("members", changed.Id);
        Assert.True(changed.Rev > first[4].Rev); Assert.Equal(first[0].Rev, store.Snapshot[0].Rev);
        Assert.NotEqual(.123, first[4].Values[0]);
        store.Update(cards.Take(1)); Assert.Single(store.Snapshot);
        store.Update(cards); Assert.Equal(cards.Select(c => c.Id), store.Snapshot.Select(c => c.Id));
    }
    [Fact]
    public void LayoutPrecedenceAndScaleAreDeterministic()
    {
        var c = StructuralDemo.Create()[0] with { WiredSlot = Slot.Tr, ManualSlot = Slot.Bl };
        Assert.Equal(Slot.Tr, SlotLayout.Resolve(c, 0)); Assert.Equal(Slot.Bl, SlotLayout.Resolve(c with { WiredSlot = null }, 0)); Assert.Equal(Slot.Tl, SlotLayout.Resolve(c with { WiredSlot = null, ManualSlot = null }, 0));
        var layout = Assert.Single(SlotLayout.Place([c], Theme.Paper, new(1600, 1200), 2));
        Assert.Equal(1600 - (c.Width + 12) * 2, layout.X); Assert.Equal(184, layout.H);
    }
    [Fact]
    public void InvalidDataCannotCreateAmbiguousHitRegions()
    {
        var c = StructuralDemo.Create()[4];
        Assert.Throws<ArgumentException>(() => (c with { Keys = Enumerable.Repeat("same", 20).ToArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Threshold = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Values = [double.NaN] }).Validate());
        Assert.Throws<ArgumentException>(() => new CardStore().Update([c, c]));
    }
    [Fact]
    public void PainterWritesTransparentSnapshotsAtThreeScales()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(output);
        foreach (var c in StructuralDemo.Create())
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            var size = CardMeasure.Measure(c, Theme.Paper);
            using var bitmap = new Bitmap((int)(size.Width * scale), (int)(size.Height * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); g.ScaleTransform((float)scale, (float)scale); NativeCardPainter.Paint(new GdiCanvas(g), c, Theme.Paper); }
            bitmap.Save(Path.Combine(output, $"{c.Id}-{scale:0.0}x.png"), ImageFormat.Png);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A); Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 5).A);
        }
    }
}
