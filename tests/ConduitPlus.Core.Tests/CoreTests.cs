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
        foreach (var c in StructuralDemo.Create().Append(StructuralDemo.Line()))
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            var size = CardMeasure.Measure(c, Theme.Paper);
            using var bitmap = new Bitmap((int)(size.Width * scale), (int)(size.Height * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); g.ScaleTransform((float)scale, (float)scale); using var canvas = new GdiCanvas(g); CardPainter.Paint(canvas, c, Theme.Paper); }
            bitmap.Save(Path.Combine(output, $"{c.Id}-{scale:0.0}x.png"), ImageFormat.Png);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A); Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 5).A);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void CardPainterRendersEveryCardKindThroughGdiCanvas(double scale)
    {
        var cards = StructuralDemo.Create().Append(StructuralDemo.Line()).GroupBy(c => c.Kind).Select(g => g.First()).ToArray();
        Assert.Equal(Enum.GetValues<CardKind>().Order(), cards.Select(c => c.Kind).Order());
        foreach (var card in cards)
        {
            var size = CardMeasure.Measure(card, Theme.Paper);
            using var bitmap = new Bitmap((int)(size.Width * scale), (int)(size.Height * scale), PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform((float)scale, (float)scale);
            using var canvas = new GdiCanvas(graphics);
            CardPainter.Paint(canvas, card, Theme.Paper);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 5).A);
        }
    }

    [Fact]
    public void RasterUsesPremultipliedAlphaWithTransparentCornersAndCoveredInterior()
    {
        foreach (var c in StructuralDemo.Create().Append(StructuralDemo.Line()))
        foreach (var scale in new[] { 1d, 2d, 4d })
        {
            using var bitmap = RasterDrawing.Render(c, Theme.Paper, scale);
            Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).A);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 8).A);
            Assert.Contains(Enumerable.Range(0, (int)(6 * scale)).SelectMany(x =>
                Enumerable.Range(0, (int)(6 * scale)).Select(y => bitmap.GetPixel(x, y).A)), alpha => alpha is > 0 and < 255);
        }
    }

    [Fact]
    public void GdiNestedClipsRestoreAndDoNotLeakTextOrFill()
    {
        using var bitmap = new Bitmap(100, 60, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var canvas = new GdiCanvas(graphics);
        using (canvas.Clip(new(10, 10, 50, 40)))
        {
            using (canvas.Clip(new(20, 20, 10, 10))) canvas.Rect(new(0, 0, 100, 60), "#FF0000");
            Assert.Equal(0, bitmap.GetPixel(35, 25).A);
            canvas.Rect(new(40, 30, 10, 10), "#0000FF");
            canvas.Text(new string('W', 100), new(10, 10), 12, "#000000", "Segoe UI");
        }
        Assert.Equal(255, bitmap.GetPixel(25, 25).A);
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(45, 35).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(61, 15).A);
        canvas.Rect(new(80, 40, 10, 10), "#00FF00");
        Assert.Equal(255, bitmap.GetPixel(85, 45).A);
    }

    [Fact]
    public void TableScrollClampsShowsVisibleRowsAndMapsThumb()
    {
        var table = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table); // 12 rows, 8 visible by default
        Assert.True(TableScroll.CanScroll(table));
        Assert.Equal(4, TableScroll.MaxOffset(table));
        Assert.Equal(48 + 9 * Theme.Paper.RowHeight, CardMeasure.Measure(table, Theme.Paper).Height);

        var scrolled = table with { ScrollOffset = 3 };
        var hits = CardMeasure.Hits(scrolled, Theme.Paper);
        Assert.Equal(8, hits.Length);
        Assert.Equal("M04", hits[0].Key);                                   // first on-screen row is data row 3
        Assert.Equal(36 + Theme.Paper.RowHeight, hits[0].Y);                // ...drawn in the first row slot
        Assert.Equal(4, TableScroll.Offset(table with { ScrollOffset = 99 })); // clamped
        Assert.Equal(0, TableScroll.Offset(table with { ScrollOffset = -5 }));

        var top = TableScroll.Bar(table, Theme.Paper)!.Value;
        var bottom = TableScroll.Bar(table with { ScrollOffset = 4 }, Theme.Paper)!.Value;
        Assert.Equal(top.Track.Y, top.Thumb.Y);
        Assert.Equal(bottom.Track.Y + bottom.Track.Height, bottom.Thumb.Y + bottom.Thumb.Height, 6);
        Assert.Equal(0, TableScroll.OffsetForThumbTop(table, Theme.Paper, top.Track.Y - 50));
        Assert.Equal(4, TableScroll.OffsetForThumbTop(table, Theme.Paper, bottom.Thumb.Y + 50));

        Assert.False(TableScroll.CanScroll(table with { MaxRows = 20 }));
        Assert.Null(TableScroll.Bar(table with { MaxRows = 20 }, Theme.Paper));
        Assert.Throws<ArgumentException>(() => (table with { MaxRows = 0 }).Validate());
    }

    [Fact]
    public void LineTicksCoverDomainWithNiceUniformSteps()
    {
        Assert.Equal(new[] { 0d, .5, 1, 1.5 }, ChartTicks.Nice(0, 1.12));
        Assert.Equal(new[] { -1d, -.5, 0, .5, 1 }, ChartTicks.Nice(-.7, .8));
        Assert.Equal(new[] { 0d, .005, .01, .015 }, ChartTicks.Nice(0, .012));
        foreach (var (min, max) in new[] { (2d, 2d), (-18d, 37d), (0d, .00012) })
        {
            var ticks = ChartTicks.Nice(min, max);
            Assert.True(ticks[0] <= min && ticks[^1] >= max);
            Assert.InRange(ticks.Length, 2, 7);
            Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(ticks[1] - ticks[0], pair.Second - pair.First, 10));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartTicks.Nice(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartTicks.Nice(2, 1));
    }

    [Fact]
    public void BrushingProjectsAcrossCardsAndSnapshotsCannotMutateState()
    {
        var cards = StructuralDemo.Create().Append(StructuralDemo.Line()).ToArray();
        var brush = new BrushState(); brush.Prune(cards, ["link-only"]);
        Assert.True(brush.Toggle("M04")); Assert.True(brush.Hover("M04"));
        var projected = brush.Project(cards);
        foreach (var c in projected.Where(c => c.Kind is CardKind.Bar or CardKind.Table))
        { Assert.Equal(new[] { "M04" }, c.SelectedKeys); Assert.Equal("M04", c.HoverKey); }
        Assert.Null(projected.Single(c => c.Kind == CardKind.Line).HoverKey);
        Assert.Empty(projected[0].SelectedKeys);
        Assert.Empty(cards[4].SelectedKeys); Assert.Null(cards[4].HoverKey);
        brush.Snapshot.SelectedKeys[0] = "mutated";
        Assert.Equal(new[] { "M04" }, brush.Snapshot.SelectedKeys);
        Assert.True(brush.Toggle("M04")); Assert.Empty(brush.Snapshot.SelectedKeys);
        Assert.False(brush.Toggle("m04")); // ordinal, case-sensitive
        Assert.False(brush.Hover("M04")); // unchanged hover
    }

    [Fact]
    public void PruningUsesUnionOfAllCardAndLinkKeysAndClearIsOwnerWide()
    {
        var cards = StructuralDemo.Create(); var brush = new BrushState();
        brush.Prune(cards, ["link-only", "M01"]);
        brush.Toggle("M01"); brush.Toggle("M20"); brush.Toggle("link-only"); brush.Hover("M20");
        var table = cards.Single(c => c.Kind == CardKind.Table);
        brush.Prune([table], ["link-only", "M01"]);
        Assert.Equal(new[] { "M01", "link-only" }, brush.Snapshot.SelectedKeys); Assert.Null(brush.Snapshot.HoverKey);
        brush.Hover("link-only");
        brush.Prune([], ["link-only"]);
        Assert.Equal(new[] { "link-only" }, brush.Snapshot.SelectedKeys); Assert.Equal("link-only", brush.Snapshot.HoverKey);
        brush.Prune([table], []);
        Assert.Empty(brush.Snapshot.SelectedKeys); Assert.Null(brush.Snapshot.HoverKey);
        Assert.False(brush.Clear()); brush.Toggle("M01"); Assert.True(brush.Clear());
        Assert.All(brush.Project(cards), c => Assert.Empty(c.SelectedKeys));
    }

    [Fact]
    public void ClearHitsOnlyKeyedCardsWithOwnerSelectionAndAvoidsScrollAndData()
    {
        var cards = StructuralDemo.Create().Append(StructuralDemo.Line()).ToArray();
        var brush = new BrushState(); brush.Prune(cards, ["link-only"]);
        Assert.All(cards, c => Assert.DoesNotContain(CardMeasure.Hits(c, Theme.Paper), h => h.Key == CardMeasure.ClearKey));
        brush.Toggle("link-only"); // even cards without a locally selected key can clear the owner
        foreach (var c in brush.Project(cards))
        {
            var hits = CardMeasure.Hits(c, Theme.Paper);
            if (!BrushState.Keys(c).Any()) { Assert.DoesNotContain(hits, h => h.Key == CardMeasure.ClearKey); continue; }
            var clear = Assert.Single(hits, h => h.Key == CardMeasure.ClearKey);
            Assert.True(clear.Contains(clear.X + clear.W / 2, clear.Y + clear.H / 2));
            Assert.True(clear.X >= c.Width / 2); Assert.True(clear.X + clear.W <= c.Width);
            Assert.All(CardMeasure.DataHits(c, Theme.Paper), h => Assert.True(clear.Y + clear.H <= h.Y));
            if (TableScroll.Bar(c, Theme.Paper) is { } bar) Assert.True(clear.Y + clear.H <= bar.Track.Y);
            // A selected table/bar must still paint after adding the extra control hit.
            using var bitmap = RasterDrawing.Render(c, Theme.Paper, 1);
            Assert.True(bitmap.Width > 0);
        }
        brush.Clear(); Assert.All(brush.Project(cards), c => Assert.Null(CardMeasure.Clear(c, Theme.Paper)));
    }

    [Fact]
    public void LinkMappingPrefersBranchesIncludingEmptyBranchesAndRepeatedKeys()
    {
        var mapping = LinkKeyMapping.Create([0, 2], ["same", "same"]); // branch count and item count both 2
        Assert.Empty(mapping[0]); Assert.Equal(new[] { 1, 1 }, mapping[1]);
        mapping = LinkKeyMapping.Create([2, 0, 1], ["a", "b", "c"]);
        Assert.Equal(new[] { 0, 0 }, mapping[0]); Assert.Empty(mapping[1]); Assert.Equal(new[] { 2 }, mapping[2]);
        mapping = LinkKeyMapping.Create([2, 0, 2], ["a", "b", "a", "A"]);
        Assert.Equal(new[] { 0, 1 }, mapping[0]); Assert.Empty(mapping[1]); Assert.Equal(new[] { 2, 3 }, mapping[2]);
        Assert.Empty(LinkKeyMapping.Create([], []));
        Assert.Empty(Assert.Single(LinkKeyMapping.Create([0], ["empty-branch"])));
    }

    [Fact]
    public void LinkMappingReportsInvalidKeyIndexAndMismatchedCounts()
    {
        var error = Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([2], ["a", " "]));
        Assert.Contains("Keys[1]", error.Message);
        error = Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([2, 3], ["a"]));
        Assert.Contains("2 branches", error.Message); Assert.Contains("5 items", error.Message);
        Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([-1], ["a"]));
    }
}
