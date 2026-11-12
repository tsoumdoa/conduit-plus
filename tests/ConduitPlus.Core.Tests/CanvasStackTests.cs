using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CanvasStackTests : IDisposable
{
    private readonly Func<string, double, string, Size2> _original = TextMetrics.Measurer;
    public void Dispose() => TextMetrics.Measurer = _original;

    private static CardSpec Table(string id, double width = 280) => new()
    {
        Id = id, Kind = CardKind.Table, Width = width, MaxRows = 2, Headers = ["Name", "Value"],
        Rows = Enumerable.Range(0, 10).Select(i => new TableRow(id + i, [id + i, (10 - i).ToString()])).ToArray()
    };

    [Fact]
    public void LayoutUsesInputOrderOwnWidthsAndEightPixelGapsAndRoutesLocalHits()
    {
        CardSpec[] cards = [Table("wide", 420), Table("narrow", 180), Table("middle", 300)];
        var layout = new CanvasStackLayout(cards, Theme.Paper);
        Assert.Equal(cards.Select(c => c.Id), layout.Cards.Select(c => c.Id));
        Assert.Equal(420, layout.Size.Width);
        Assert.Equal(layout.Cards.Sum(c => c.H) + 16, layout.Size.Height);
        for (var i = 0; i < cards.Length; i++)
        {
            var card = layout.Cards[i];
            Assert.Equal(0, card.X); Assert.Equal(cards[i].Width, card.W);
            Assert.Equal(i == 0 ? 0 : layout.Cards[i - 1].Y + layout.Cards[i - 1].H + 8, card.Y);
            var local = CardMeasure.DataHits(cards[i], Theme.Paper)[0];
            var point = new Point2(local.X + 1, card.Y + local.Y + 1);
            var hit = layout.Hit(point)!;
            Assert.Equal(card.Id, hit.CardId); Assert.Equal(local.Key, hit.Key);
            Assert.Equal(local.Y + card.Y, hit.Y);
            Assert.Null(layout.CardAt(new(card.W + 1, card.Y + 1)));
            Assert.Null(layout.Hit(new(10, card.Y + card.H + 4)));
        }
    }

    [Fact]
    public void ViewStateFollowsCardIdAcrossReorderAndChangesPruneAndClampEachCard()
    {
        var a = Table("a"); var b = Table("b");
        var state = new CanvasCardState(); state.ReplaceCards([a, b]);
        state.Scroll(a.Id, 7); state.Activate(b.Id, TableSort.Key(1)); state.Scroll(b.Id, 4);
        var revision = state.Revision;
        state.ReplaceCards([b with { Rev = 999 }, a]);
        Assert.True(state.Revision > revision);
        Assert.Equal(4, state.Specs[0].ScrollOffset); Assert.Equal(1, state.Specs[0].SortColumn);
        Assert.Equal(7, state.Specs[1].ScrollOffset); Assert.Null(state.Specs[1].SortColumn);
        state.ReplaceCards([a with { Rows = a.Rows[..3] }, b with { Headers = ["Only"], Rows = b.Rows.Select(r => r with { Cells = [r.Cells[0]] }).ToArray() }]);
        Assert.Equal(1, state.Specs[0].ScrollOffset); Assert.Null(state.Specs[1].SortColumn);
        state.ReplaceCards([a]); state.ReplaceCards([a, b]);
        Assert.Equal(0, state.Specs[1].ScrollOffset); Assert.Null(state.Specs[1].SortColumn);
    }

    [Fact]
    public void SelectionUnionHoverAndClearUseOnePrivateDomainWithoutCouplingOtherOwners()
    {
        var cards = new[] { Table("a"), Table("b"), Table("c") };
        var state = new CanvasCardState(); var other = new CanvasCardState();
        state.ReplaceCards(cards); other.ReplaceCards(cards);
        state.Activate("a", "a1"); state.Activate("b", "b2", BrushSelectMode.Add); state.Activate("c", "c3", BrushSelectMode.Toggle);
        Assert.Equal(new[] { "a1", "b2", "c3" }, state.Current.SelectedKeys);
        Assert.Empty(other.Current.SelectedKeys);
        state.Hover("c", "c0");
        Assert.Equal("c0", state.Specs[2].HoverKey); Assert.Null(state.Specs[0].HoverKey);
        Assert.NotNull(Tooltip.For(state.Specs[2], "c0", state.Membership));
        state.ReplaceCards(cards[..2]); Assert.Equal(new[] { "a1", "b2" }, state.Current.SelectedKeys); Assert.Null(state.Current.HoverKey);
        state.Activate("b", CardMeasure.ClearKey); Assert.Empty(state.Current.SelectedKeys);
        state.Activate("a", "a0"); state.Activate("b", "b0"); Assert.Equal(new[] { "b0" }, state.Current.SelectedKeys);
    }

    [Fact]
    public void RepeatedKeysRouteSortToTheRightCardAndReplacementPreservesSingleCardSemantics()
    {
        var a = Table("a"); var b = Table("b") with { Rows = a.Rows };
        var state = new CanvasCardState(); state.ReplaceCards([a, b]);
        state.Activate("b", TableSort.Key(0));
        Assert.Null(state.Specs[0].SortColumn); Assert.Equal(0, state.Specs[1].SortColumn);
        state.Activate("a", "a0"); state.ReplaceCards([b]); Assert.Contains("a0", state.Current.SelectedKeys);
        state.Replace(b with { Id = "replacement" }); Assert.Empty(state.Current.SelectedKeys); Assert.Null(state.Spec!.SortColumn);
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1)]
    [InlineData(2)]
    public void SingleCardRasterAndHitMapAreUnchanged(double scale)
    {
        GdiCanvas.RegisterTextMetrics();
        foreach (var theme in Theme.Presets.Select(t => t.Resolve(false)))
        {
            var card = Table("a"); var layout = new CanvasStackLayout([card], theme);
            Assert.Equal(CardMeasure.Measure(card, theme), layout.Size);
            Assert.Equal(CardMeasure.Hits(card, theme), layout.Cards[0].HitRegions);
            using var baseline = RasterDrawing.Render(card, theme, scale);
            using var stack = RasterDrawing.RenderStack([card], theme, scale);
            AssertPixelsEqual(baseline, stack);
        }
    }

    [Fact]
    public void TallSiblingsKeepSmallCardsAtFullDensityAndEachBitmapWithinBounds()
    {
        GdiCanvas.RegisterTextMetrics();
        var metric = new CardSpec { Id = "metric", Kind = CardKind.Metric, Title = "Floor area", Value = "320", Unit = "m²", Width = 240 };
        CardSpec[] cards = [metric, .. Enumerable.Range(0, 11).Select(i => Table(i.ToString(), 420) with
        { MaxRows = 200, Rows = Enumerable.Range(0, 200).Select(j => new TableRow(j.ToString(), ["Room", "42"])).ToArray() })];
        foreach (var theme in Theme.Presets.Select(t => t.Resolve(false)))
        {
            var layout = new CanvasStackLayout(cards, theme);
            var plans = CanvasStackRaster.Plan(layout, theme, 2);
            Assert.Equal(2, plans[0].Scale);
            var retained = CanvasStackRaster.RetainedCards(plans, 1, CanvasRasterPlan.DocumentBudget / 2);
            Assert.Contains(0, retained);
            Assert.True(retained.Sum(i => plans[i].Bytes) <= CanvasRasterPlan.DocumentBudget / 2);
            using var baseline = RasterDrawing.Render(metric, theme, 2);
            using var small = RasterDrawing.Render(cards[0], theme, plans[0].Scale);
            AssertPixelsEqual(baseline, small);
            for (var i = 0; i < cards.Length; i++)
            {
                var plan = plans[i];
                // Even under document pressure, siblings do not affect a card's individual limit.
                Assert.Equal(CanvasRasterPlan.Create(new(layout.Cards[i].W, layout.Cards[i].H), 2,
                    margin: theme.Shadow ? theme.ShadowExtent + 1 : 0), plan);
                using var image = RasterDrawing.Render(cards[i], theme, plan.Scale);
                Assert.Equal(plan.Width, image.Width); Assert.Equal(plan.Height, image.Height);
                Assert.InRange(plan.Bytes, 4, CanvasRasterPlan.BitmapBudget);
                Assert.InRange(image.Height, 1, 4096); Assert.InRange(image.Width, 1, 4096);
            }
        }
    }

    [Fact]
    public void DocumentPressureChangesRetentionNotDensityAndReservesAtomicReplacement()
    {
        var cards = Enumerable.Range(0, 12).Select(i => Table(i.ToString(), i == 0 ? 180 : 1200) with
        { MaxRows = i == 0 ? 2 : 200, Rows = Enumerable.Range(0, 200).Select(j => new TableRow(j.ToString(), ["Room", "42"])).ToArray() }).ToArray();
        const long budget = 20 * 1024 * 1024;
        var plans = CanvasStackRaster.Plan(new(cards, Theme.Paper), Theme.Paper, 2);
        Assert.Equal(2, plans[0].Scale);
        var retained = CanvasStackRaster.RetainedCards(plans, 1 | (1 << 11), budget);
        Assert.Contains(0, retained); Assert.Contains(11, retained);
        Assert.DoesNotContain(1, retained);
        Assert.True(retained.Sum(i => plans[i].Bytes) <= budget);
        var alone = CanvasRasterPlan.Create(CardMeasure.Measure(cards[11], Theme.Paper), 2);
        Assert.Equal(alone, plans[11]);
        // A multi-surface frame can exceed the single-bitmap limit; reserve its complete allocation.
        Assert.Empty(CanvasCacheBudget.Evictions([new("owner", budget, 0)], "owner", CanvasRasterPlan.DocumentBudget / 2));
        Assert.Throws<InvalidOperationException>(() => CanvasCacheBudget.Evictions(
            [new("owner", CanvasRasterPlan.DocumentBudget / 2 + 4, 0)], "owner", CanvasRasterPlan.DocumentBudget / 2));
    }

    private static void AssertPixelsEqual(Bitmap a, Bitmap b)
    {
        Assert.Equal(a.Size, b.Size);
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++) Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
    }
}
