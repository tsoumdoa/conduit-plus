using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class PinnedCanvasLayoutTests
{
    private static readonly Guid Owner = new("00000000-0000-0000-0000-000000000001");
    private static CardSpec Card(string id, Slot? slot = null, double width = 180) => new() { Id = id, Kind = CardKind.Metric, Value = "1", Width = width, WiredSlot = slot };
    private static PinnedLayoutRequest Request(CardSpec[] cards, double width = 1600, double height = 900, double dpi = 1, Rect2[]? exclusions = null) =>
        new(new(0, 0, width, height), dpi, [new(Owner, 1, Theme.Paper, cards)], TextMetrics.Estimate, exclusions ?? []);
    private static Rect2 Bounds(CardPlacement p) => new(p.X, p.Y, p.Width, p.Height);
    [Fact]
    public void AllSixSlotsStayOnTheirRailsWithInputOrder()
    {
        CardSpec[] cards = [Card("tl1", Slot.Tl), Card("tl2", Slot.Tl), Card("tr", Slot.Tr), Card("bl1", Slot.Bl), Card("bl2", Slot.Bl), Card("br", Slot.Br), Card("lc", Slot.Lc), Card("rc", Slot.Rc)];
        var result = PinnedCanvasLayout.Solve(Request(cards)); Assert.Empty(result.Hidden);
        CardPlacement P(string id) => result.Placements.Single(p => p.CardId == id);
        Assert.Equal(12, P("tl1").X); Assert.Equal(12, P("tl1").Y);
        Assert.Equal(1600 - 12 - 180, P("tr").X);
        Assert.True(P("tl2").Y > P("tl1").Y); Assert.True(P("bl2").Y > P("bl1").Y);
        Assert.Equal(900 - 12, P("bl2").Y + P("bl2").Height);
        Assert.True(P("lc").Y > P("tl2").Y); Assert.True(P("lc").Y < P("bl1").Y);
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(20, 20)] [InlineData(300, 900)]
    public void TinyAndNarrowClientsHideInsteadOfShrinking(double width, double height)
    {
        var result = PinnedCanvasLayout.Solve(Request([Card("a", Slot.Tl)], width, height));
        Assert.Empty(result.Placements); Assert.Equal((Owner, "a"), Assert.Single(result.Hidden));
    }
    [Fact]
    public void AutomaticSlotsGroupProducerIterationsAndMultipleOwnersKeepSeparateIdentities()
    {
        var owner2 = new Guid("00000000-0000-0000-0000-000000000002");
        CardSpec[] cards = [Card(Owner.ToString("N")), Card(CardIdentity.ForIteration(Owner.ToString("N"), 1)), Card("other")];
        // Use the existing identity source convention rather than infer grouping from keys.
        var slots = SlotLayout.ResolveAll(cards); Assert.Equal(slots[0], slots[1]);
        var r = Request(cards) with { Participants = [new(owner2, 1, Theme.Paper, [Card("shared", Slot.Tr)]), new(Owner, 1, Theme.Paper, [Card("shared", Slot.Tl)])] };
        var result = PinnedCanvasLayout.Solve(r);
        Assert.Equal(2, result.Placements.Count); Assert.Empty(result.Hidden);
        Assert.Equal(Owner, result.Placements[0].Owner); Assert.Equal(owner2, result.Placements[1].Owner);
        var auto = PinnedCanvasLayout.Solve(Request(cards)); Assert.Empty(auto.Hidden);
        foreach (var pair in cards.Select((c, i) => (c, slot: slots[i])))
        {
            var p = auto.Placements.Single(p => p.CardId == pair.c.Id);
            Assert.Equal(pair.slot is Slot.Tr or Slot.Br or Slot.Rc ? 1600 - 12 - 180 : 12, p.X);
        }
    }
    [Fact]
    public void ExclusionsAndOverflowPreserveFirstInputsAndReinsertOnResize()
    {
        var cards = Enumerable.Range(0, 8).Select(i => Card("a" + i, Slot.Tl)).ToArray();
        var exclusion = new Rect2(0, 0, 220, 120);
        var small = PinnedCanvasLayout.Solve(Request(cards, height: 400, exclusions: [exclusion]));
        Assert.NotEmpty(small.Hidden); Assert.Equal("a0", small.Placements[0].CardId);
        Assert.All(small.Placements, p => Assert.False(PinnedCanvasLayout.Intersects(Bounds(p), exclusion)));
        var large = PinnedCanvasLayout.Solve(Request(cards, height: 1200, exclusions: [exclusion]));
        Assert.Empty(large.Hidden); Assert.Equal(8, large.Placements.Count);
        Assert.All(large.Placements, p => Assert.Equal(180, p.Width));
    }
    [Fact]
    public void BottomOverflowStillAnchorsVisibleInputPrefixAsABlock()
    {
        var cards = Enumerable.Range(0, 8).Select(i => Card("a" + i, Slot.Br)).ToArray();
        var result = PinnedCanvasLayout.Solve(Request(cards, height: 400));
        Assert.NotEmpty(result.Placements); Assert.NotEmpty(result.Hidden);
        Assert.Equal("a0", result.Placements[0].CardId);
        Assert.Equal(388, result.Placements[^1].Y + result.Placements[^1].Height);
    }
    [Fact]
    public void TitleOnlyCentreSlotsAndOversizeCardsNeverTrapTheClient()
    {
        var title = new CardSpec { Id = "title", Kind = CardKind.Title, Title = "Dashboard", WiredSlot = Slot.Tc, Width = 300, Form = new(Width: 300) };
        var result = PinnedCanvasLayout.Solve(Request([title, Card("huge", Slot.Tl, 2000), Card("ordinary-centre", Slot.Bc)]));
        var p = Assert.Single(result.Placements); Assert.Equal("title", p.CardId);
        Assert.Equal(800, p.X + p.Width / 2, 0); Assert.Equal(2, result.Hidden.Count);
    }
    [Fact]
    public void FractionalDpiClientsAlwaysContainNonoverlappingFixedSizeCards()
    {
        var random = new Random(91);
        for (var i = 0; i < 150; i++)
        {
            var dpi = new[] { 1, 1.1, 1.25, 1.3, 1.5, 1.75, 2.25 }[i % 7];
            var r = Request(Enumerable.Range(0, 24).Select(n => Card("c" + n, (Slot)(n % 6))).ToArray(), random.Next(0, 2200), random.Next(0, 1600), dpi,
                [new(0, 0, 100 * dpi, 100 * dpi)]);
            var result = PinnedCanvasLayout.Solve(r);
            Assert.Equal(24, result.Placements.Count + result.Hidden.Count);
            foreach (var p in result.Placements)
            {
                Assert.True(PinnedCanvasLayout.Contains(r.Client, Bounds(p)));
                Assert.Equal(Math.Ceiling(180 * dpi), p.Width); Assert.Equal(Math.Ceiling(92 * dpi), p.Height);
                Assert.All(r.Exclusions, e => Assert.False(PinnedCanvasLayout.Intersects(Bounds(p), e)));
                foreach (var q in result.Placements.Where(q => q.CardId != p.CardId)) Assert.False(PinnedCanvasLayout.Intersects(Bounds(p), Bounds(q)));
            }
        }
    }
    [Theory]
    [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(1.75)] [InlineData(2.25)]
    public void HitTransformRoundTripsAndExcludesShadowsAndClippedContent(double dpi)
    {
        var t = new PinnedHitTransform(new(17, 23, 180 * dpi, 92 * dpi), dpi, new(20, 25, 1000, 1000));
        var local = new Point2(12.5, 37.25); var control = t.ToControl(local);
        var roundTrip = t.ToLocal(control); Assert.Equal(local.X, roundTrip.X, 10); Assert.Equal(local.Y, roundTrip.Y, 10);
        Assert.True(t.Contains(control)); Assert.False(t.Contains(new(16, 30))); Assert.False(t.Contains(new(18, 24)));
        Assert.False(t.Contains(new(17 + 180 * dpi, 30))); // right edge/shadow is not a hit
        var region = t.ToControl(new HitRegion("key", 5, 6, 7, 8)); Assert.Equal(17 + 5 * dpi, region.X); Assert.Equal(8 * dpi, region.Height);
    }
    [Fact]
    public void PlacementAndRasterIdentityHaveNoCanvasPanZoomDependency()
    {
        var request = Request([Card("card", Slot.Br)], dpi: 1.25);
        var first = PinnedCanvasLayout.Solve(request); var placement = Assert.Single(first.Placements);
        var theme = Theme.Paper; var size = new Size2(180, 92); var plan = CanvasRasterPlan.Create(size, 1.25);
        var key = new PinnedRasterKey(Owner, "card", 4, theme, size, plan);
        foreach (var pan in new[] { new Point2(-1000, 900), new Point2(200, -50) })
        foreach (var zoom in new[] { .1, .5, 1, 2, 20 })
        {
            // The host passes the same control client and DPI regardless of these definition-space values.
            Assert.Equal(placement, Assert.Single(PinnedCanvasLayout.Solve(request).Placements));
            var t = new PinnedHitTransform(Bounds(placement), 1.25, request.Client);
            Assert.Equal(new Point2(8, 9), t.ToLocal(t.ToControl(new Point2(8, 9))));
            Assert.Equal(key, new PinnedRasterKey(Owner, "card", 4, theme, size, plan));
            Assert.True(double.IsFinite(pan.X * zoom));
        }
        Assert.NotEqual(key, key with { Revision = 5 }); Assert.NotEqual(key, key with { Theme = Theme.Night });
        Assert.NotEqual(key, key with { Plan = CanvasRasterPlan.Create(size, 1.5) });
    }
}
