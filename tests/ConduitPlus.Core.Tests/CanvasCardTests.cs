using System.Drawing;
using System.Drawing.Drawing2D;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

[Collection("Legend text metrics")]
public sealed class CanvasCardTests
{
    private static CardSpec Table(string id = "table", int rows = 20, int columns = 2) => new()
    {
        Id = id, Kind = CardKind.Table, MaxRows = 3,
        Headers = Enumerable.Range(0, columns).Select(i => "Column " + i).ToArray(),
        Rows = Enumerable.Range(0, rows).Select(i => new TableRow("row" + i,
            Enumerable.Range(0, columns).Select(_ => (rows - i).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray())).ToArray()
    };
    [Theory]
    [InlineData(.25, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2.5, 1.5)]
    public void CanvasLocalRoundTripAndEffectiveDeviceBasis(double zoom, double dpi)
    {
        var origin = new Point2(-175, 240);
        var local = new Point2(12, 58);
        Assert.Equal(local, CanvasCardGeometry.ToLocal(CanvasCardGeometry.ToCanvas(local, origin), origin));
        var effective = zoom * dpi;
        // Pan is only a translation, so it cannot change raster density.
        Assert.Equal(effective, CanvasCardGeometry.DeviceScale(new(500, -300), new(500 + effective, -300), new(500, -300 + effective)), 8);
        Assert.Equal(effective, CanvasCardGeometry.DeviceScale(new(0, 0), new(0, effective), new(-effective, 0)), 8);
        Assert.True(CanvasCardGeometry.WithinClick(new(10, 20), new(14, 20)));
        Assert.False(CanvasCardGeometry.WithinClick(new(10, 20), new(13, 23)));
    }
    [Fact]
    public void GdiTargetTransformIncludesPageScaleWithoutDoubleCountingDpi()
    {
        using var bitmap = new Bitmap(100, 100);
        bitmap.SetResolution(192, 192);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.PageUnit = GraphicsUnit.Pixel;
        graphics.TranslateTransform(40, 20);
        graphics.ScaleTransform(2, 2);
        PointF[] basis = [new(0, 0), new(1, 0), new(0, 1)];
        graphics.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, basis);
        var scale = CanvasCardGeometry.DeviceScale(new(basis[0].X, basis[0].Y), new(basis[1].X, basis[1].Y), new(basis[2].X, basis[2].Y));
        Assert.Equal(2, scale, 6); // The target is already in pixels despite its 192 DPI metadata.
    }
    [Fact]
    public void RasterPlansBoundActualCeiledAllocationAndPreserveUniformScale()
    {
        var random = new Random(427);
        for (var i = 0; i < 2000; i++)
        {
            var size = new Size2(180 + random.NextDouble() * 1020, 1 + random.NextDouble() * 10_000_000);
            var requested = .01 + random.NextDouble() * 20;
            var plan = CanvasRasterPlan.Create(size, requested);
            Assert.InRange(plan.Scale, double.Epsilon, requested);
            Assert.InRange(plan.Width, 1, CanvasRasterPlan.MaxDimension);
            Assert.InRange(plan.Height, 1, CanvasRasterPlan.MaxDimension);
            Assert.InRange(plan.Bytes, 4, CanvasRasterPlan.BitmapBudget);
            Assert.Equal((int)Math.Ceiling(size.Width * plan.Scale), plan.Width);
            Assert.Equal((int)Math.Ceiling(size.Height * plan.Scale), plan.Height);
        }
        var square = CanvasRasterPlan.Create(new(1200, 1200), 10);
        Assert.InRange(square.Width, 2047, 2048);
        Assert.Equal(square.Width, square.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasRasterPlan.Create(new(280, double.PositiveInfinity), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasRasterPlan.Create(new(280, 100), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasRasterPlan.Create(new(280, 0), 1));
    }
    [Fact]
    public void CacheEvictsLeastRecentlyVisibleAndReservesReplacementAlongsideOldFrame()
    {
        var bytes = CanvasRasterPlan.BitmapBudget;
        CanvasCacheEntry[] entries = [new("owner", bytes, 1), new("b", bytes, 8), new("c", bytes, 4), new("d", bytes, 6)];
        Assert.Equal(new[] { "c" }, CanvasCacheBudget.Evictions(entries, "owner", bytes));
        Assert.Empty(CanvasCacheBudget.Evictions(entries[..3], "owner", bytes));
        Assert.Equal(new[] { "c", "d" }, CanvasCacheBudget.Evictions([.. entries, new("e", bytes, 9)], "owner", bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasCacheBudget.Evictions(entries, "owner", bytes + 4));
    }
    [Fact]
    public void VisibleWorkingSetSharesBudgetWithAtomicReplacementHeadroom()
    {
        for (var count = 1; count <= 200; count++)
        {
            var budget = CanvasCacheBudget.PerVisibleCard(count);
            var plan = CanvasRasterPlan.Create(new(1200, 2000), 10, budget);
            Assert.InRange(plan.Bytes, 4, budget);
            Assert.True(plan.Bytes * (count + 1) <= CanvasRasterPlan.DocumentBudget);
        }
    }
    [Fact]
    public void ViewStateSurvivesIncomingRevisionsAndPrunesKeysAndClampsScrollAndSort()
    {
        var state = new CanvasCardState();
        var source = Table();
        state.Replace(source); state.Activate("row4"); state.Hover("row5"); state.Scroll(100); state.Activate(TableSort.Key(1));
        Assert.Equal(17, state.Spec!.ScrollOffset);
        Assert.Equal(1, state.Spec.SortColumn);
        var rev = state.Spec.Rev;
        state.Replace(source with { Rev = long.MaxValue, ScrollOffset = 0, SelectedKeys = ["row8"], HoverKey = "row8", SortColumn = 0 });
        Assert.Equal(rev, state.Spec!.Rev);
        Assert.Equal(new[] { "row4" }, state.Spec.SelectedKeys);
        Assert.Equal("row5", state.Spec.HoverKey);
        state.Replace(Table(rows: 5, columns: 1));
        Assert.Equal(new[] { "row4" }, state.Spec!.SelectedKeys);
        Assert.Null(state.Spec.HoverKey);
        Assert.Null(state.Spec.SortColumn);
        Assert.False(state.Spec.SortDescending);
        Assert.Equal(2, state.Spec.ScrollOffset);
        state.Replace(Table(rows: 2));
        Assert.Empty(state.Spec!.SelectedKeys); Assert.False(state.Spec.OwnerHasSelection); Assert.Equal(0, state.Spec.ScrollOffset);
    }
    [Fact]
    public void SourceReplacementAndEmptyInputResetLocalStateAndIndependentOwnersStayIndependent()
    {
        var first = new CanvasCardState(); var other = new CanvasCardState();
        first.Replace(Table()); other.Replace(Table());
        first.Activate("row0"); first.Hover("row1"); first.Scroll(5); first.Activate(TableSort.Key(0));
        Assert.Empty(other.Spec!.SelectedKeys); Assert.Equal(0, other.Spec.ScrollOffset); Assert.Null(other.Spec.SortColumn);
        first.Replace(Table("new-source"));
        Assert.Empty(first.Spec!.SelectedKeys); Assert.Null(first.Spec.HoverKey); Assert.Null(first.Spec.SortColumn); Assert.Equal(0, first.Spec.ScrollOffset);
        first.Activate("row0"); first.Replace(null); Assert.Null(first.Spec);
        first.Replace(Table("new-source")); Assert.Empty(first.Spec!.SelectedKeys);
    }
    [Fact]
    public void SortingSelectionAndClearFollowStableKeysWithoutMutatingInputArrays()
    {
        var source = Table(rows: 4);
        var state = new CanvasCardState(); state.Replace(source); state.Activate("row0"); state.Scroll(1);
        state.Activate(TableSort.Key(0));
        Assert.Equal(new[] { "row3", "row2", "row1", "row0" }, TableSort.Rows(state.Spec!).Select(r => r.Key));
        Assert.Equal(new[] { "row0" }, state.Spec!.SelectedKeys);
        state.Activate(TableSort.Key(0)); Assert.True(state.Spec!.SortDescending);
        state.Activate(TableSort.Key(0)); Assert.Null(state.Spec!.SortColumn);
        Assert.Equal("row0", source.Rows[0].Key);
        source.Rows[0].Cells[0] = "changed";
        Assert.Equal("4", state.Spec.Rows[0].Cells[0]);
        state.Activate(CardMeasure.ClearKey); Assert.Empty(state.Spec!.SelectedKeys);
        state.Activate("missing"); Assert.Empty(state.Spec!.SelectedKeys);
    }
    [Fact]
    public void FrameKeyInvalidatesContentThemeDimensionsAndDensityButIgnoresIncomingRevisionAndSlots()
    {
        var source = Table(); var state = new CanvasCardState(); state.Replace(source);
        var size = CardMeasure.Measure(state.Spec!, Theme.Paper);
        var key = new CanvasFrameKey(state.Spec!.Id, state.Spec.Rev, Theme.Paper, size, CanvasRasterPlan.Create(size, 1));
        state.Replace(source with { Rev = 900, WiredSlot = Slot.Br });
        Assert.Equal(key.Revision, state.Spec!.Rev);
        state.Replace(source with { Title = "New data", Rev = 0 }); Assert.NotEqual(key.Revision, state.Spec!.Rev);
        Assert.NotEqual(key, key with { Theme = Theme.Paper with { Ink = "#123456" } });
        Assert.NotEqual(key, key with { Size = size with { Width = 300 } });
        Assert.NotEqual(key, key with { Raster = CanvasRasterPlan.Create(size, 2) });
        Assert.Equal(key, key with { Theme = Theme.Paper with { } });
    }
    [Fact]
    public void BoundedRastersPaintAllSixKindsAndCommittedExportCanOmitHover()
    {
        GdiCanvas.RegisterTextMetrics();
        CardSpec[] cards = [.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras()];
        Assert.Equal(6, cards.Select(c => c.Kind).Distinct().Count());
        foreach (var source in cards)
        {
            var state = new CanvasCardState(); state.Replace(source);
            var key = BrushState.Keys(source).FirstOrDefault();
            if (key is not null) { state.Activate(key); state.Hover(key); }
            var export = state.Spec! with { HoverKey = null };
            Assert.Equal(state.Spec.SelectedKeys, export.SelectedKeys);
            var size = CardMeasure.Measure(export, Theme.Paper);
            var plan = CanvasRasterPlan.Create(size, 2);
            using var image = RasterDrawing.Render(export, Theme.Paper, plan.Scale);
            Assert.Equal(plan.Width, image.Width); Assert.Equal(plan.Height, image.Height);
            Assert.True(image.GetPixel(image.Width / 2, image.Height / 2).A > 0);
        }
    }
    [Fact]
    public void CanvasThemeMarginKeepsTheCardBodyAtItsMeasuredSize()
    {
        foreach (var theme in Theme.Presets.SelectMany(t => new[] { t.Resolve(false), t.Resolve(true) }).Distinct())
        {
            var spec = new CardSpec { Id = "m", Kind = CardKind.Metric, Title = "GIA", Value = "320", Unit = "m²" };
            var size = CardMeasure.Measure(spec, theme);
            using var image = RasterDrawing.Render(spec, theme, 2);
            var margin = RasterDrawing.Margin(theme, 2);
            Assert.Equal((int)Math.Ceiling(size.Width * 2) + 2 * margin, image.Width);
            Assert.Equal((int)Math.Ceiling(size.Height * 2) + 2 * margin, image.Height);
        }
    }
}
