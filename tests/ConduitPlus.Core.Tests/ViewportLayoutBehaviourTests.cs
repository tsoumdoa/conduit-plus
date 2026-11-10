using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class ViewportLayoutBehaviourTests
{
    private static readonly LayoutOptions NoCompact = new() { EnableCompact = false };
    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    private static CardSpec Metric(string id, Slot? slot = null) => new() { Id = id, Kind = CardKind.Metric, Title = id, Value = "1", WiredSlot = slot };
    private static CardSpec Table(string id, int rows, Slot? slot = null, double width = 380, int maxRows = 8) => new()
    {
        Id = id, Kind = CardKind.Table, Title = id, Width = width, MaxRows = maxRows, WiredSlot = slot, Headers = ["A", "B"],
        Rows = Enumerable.Range(0, rows).Select(i => new TableRow($"{id}-{i}", ["x", "y"])).ToArray()
    };
    private static LayoutParticipant Owner(int n, params CardSpec[] cards) => new(Id(n), 1, Theme.Paper, cards);
    private static ViewportSolution Solve(double w, double h, LayoutOptions? options = null, double dpi = 1, params LayoutParticipant[] owners) =>
        ViewportLayout.Solve(new(new(w, h), dpi, owners, TextMetrics.Estimate, options ?? new()));
    private static CardPlacement At(ViewportSolution s, string id) => s.Placements.Single(p => p.CardId == id);
    private static CardSpec[] Structural() => [.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras()];

    [Fact]
    public void LargeViewportUsesNoFallbacks()
    {
        var s = Solve(3200, 1800, owners: Owner(1, Structural()));
        Assert.Equal(2, s.Report.BaseZoom[Id(1)]);
        Assert.Equal(1, s.Report.ShrinkMultiplier);
        Assert.Empty(s.Report.CompactSteps);
        Assert.Empty(s.Hidden);
        Assert.Null(s.Chip);
    }

    [Fact]
    public void RungsRunInOrder()
    {
        foreach (var (w, h) in new[] { (3200d, 1800d), (1920d, 1080d), (1280d, 720d), (900d, 500d), (640d, 400d), (480d, 360d) })
            foreach (var options in new[] { new LayoutOptions(), NoCompact })
            {
                var s = Solve(w, h, options, owners: Owner(1, Structural()));
                if (!options.EnableCompact) Assert.Empty(s.Report.CompactSteps);
                if (s.Report.CompactSteps.Count > 0 || s.Hidden.Count > 0) Assert.All(s.Zoom.Values, z => Assert.Equal(options.MinZoom, z));
                if (s.Hidden.Count > 0) Assert.NotNull(s.Chip);
            }
    }

    [Fact]
    public void SizeRaisesTheStartingZoom()
    {
        Assert.Equal(.625, Solve(960, 540, owners: Owner(1, Metric("a"))).Report.BaseZoom[Id(1)]);
        var large = Solve(960, 540, owners: new LayoutParticipant(Id(1), 1.5, Theme.Paper, [Metric("a")]));
        Assert.InRange(large.Report.BaseZoom[Id(1)], .875, .9375);
    }

    [Fact]
    public void ColumnsKeepTheirAnchors()
    {
        var s = Solve(1600, 900, owners: Owner(1, Metric("top", Slot.Tl), Metric("middle", Slot.Lc), Metric("bottom", Slot.Bl)));
        Assert.Equal(30, At(s, "top").Y);
        Assert.Equal(900 - 12, At(s, "bottom").Y + At(s, "bottom").Height);
        Assert.Equal(450, At(s, "middle").Y + At(s, "middle").Height / 2, 6);
    }

    [Fact]
    public void BottomLeftReserveKeepsTheCornerClear()
    {
        var options = new LayoutOptions { BottomLeftReserve = 100 };
        var s = Solve(1600, 900, options, owners: Owner(1, Metric("left", Slot.Bl), Metric("right", Slot.Br)));
        Assert.Equal(900 - 12 - 100, At(s, "left").Y + At(s, "left").Height);
        Assert.Equal(900 - 12, At(s, "right").Y + At(s, "right").Height);
        var tall = Solve(1600, 900, options with { EnableCompact = false }, owners: Owner(1, Enumerable.Range(0, 9).Select(i => Metric($"m{i}", Slot.Tl)).ToArray()));
        Assert.All(tall.Placements.Where(p => p.X < 200), p => Assert.True(p.Y + p.Height <= 900 - 12 - 100 + 1e-6));
        var portrait = Solve(600, 1200, options, owners: Owner(1, Metric("top", Slot.Tl), Metric("bottom", Slot.Tr)));
        // 600 × 1200 sits at the 0.625 zoom floor, so the margin is 12 × 0.625.
        Assert.Equal(1200 - 12 * .625 - 100, At(portrait, "bottom").Y + At(portrait, "bottom").Height);
    }

    [Fact]
    public void RightRailAlignsToTheRightEdge()
    {
        var s = Solve(1600, 900, owners: Owner(1, Metric("metric", Slot.Tr), Table("table", 4, Slot.Tr)));
        Assert.All(s.Placements, p => Assert.Equal(1600 - 12, p.X + p.Width, 6));
    }

    [Fact]
    public void TallStacksWrapIntoInwardColumns()
    {
        var s = Solve(1600, 600, NoCompact, owners: Owner(1, Enumerable.Range(0, 12).Select(i => Metric($"m{i}", Slot.Tl)).ToArray()));
        Assert.Empty(s.Hidden);
        Assert.True(s.Placements.Select(p => p.X).Distinct().Count() >= 2);
        Assert.Equal(At(s, "m0").X, s.Placements.Min(p => p.X));
    }

    [Fact]
    public void PortraitUsesTopAndBottomBands()
    {
        var s = Solve(600, 1200, owners: Owner(1, Metric("left", Slot.Tl), Metric("right", Slot.Tr)));
        Assert.Equal(LayoutOrientation.Bands, s.Report.Orientation);
        Assert.True(At(s, "left").Y + At(s, "left").Height <= At(s, "right").Y);
    }

    [Fact]
    public void RailsLeaveTheKeepClearArea()
    {
        var s = Solve(1600, 900, owners: Owner(1, Table("left", 4, Slot.Tl), Table("right", 4, Slot.Tr)));
        var left = s.Placements.Where(p => p.CardId == "left").Max(p => p.X + p.Width);
        var right = s.Placements.Where(p => p.CardId == "right").Min(p => p.X);
        Assert.True(right - left >= .35 * 1600 - 2 * 12 - 1e-6);
    }

    [Fact]
    public void WideCardMaySitAloneInItsRail()
    {
        var alone = Solve(1600, 900, NoCompact, owners: Owner(1, Table("wide", 3, Slot.Tl, width: 1100), Metric("right", Slot.Tr)));
        Assert.Empty(alone.Hidden);
        var both = Solve(1600, 900, NoCompact, owners: Owner(1, Table("a", 3, Slot.Tl, width: 1100), Table("b", 3, Slot.Tr, width: 1100)));
        Assert.Single(both.Hidden);
    }

    [Fact]
    public void HidingAlternatesBetweenOwners()
    {
        CardSpec[] Cards() => Enumerable.Range(0, 8).Select(i => Metric($"m{i}", Slot.Tl)).ToArray();
        var s = Solve(640, 400, NoCompact, owners: [Owner(1, Cards()), Owner(2, Cards())]);
        Assert.NotEmpty(s.Hidden);
        var counts = new[] { Id(1), Id(2) }.Select(o => s.Hidden.Count(h => h.Owner == o)).ToArray();
        Assert.True(Math.Abs(counts[0] - counts[1]) <= 1);
        foreach (var owner in new[] { Id(1), Id(2) })
        {
            var hidden = s.Hidden.Where(h => h.Owner == owner).Select(h => int.Parse(h.CardId[1..])).OrderBy(i => i).ToArray();
            Assert.Equal(Enumerable.Range(8 - hidden.Length, hidden.Length), hidden);
        }
    }

    [Fact]
    public void ReinsertionKeepsCardsBehindAnUnplaceableOne()
    {
        var s = Solve(1600, 900, NoCompact, owners: Owner(1, [Table("huge", 200, Slot.Tl, maxRows: 200), .. Enumerable.Range(0, 4).Select(i => Metric($"m{i}", Slot.Tl))]));
        Assert.Equal([(Id(1), "huge")], s.Hidden);
        Assert.Equal(4, s.Placements.Count);
    }

    [Fact]
    public void OwnerOrderDoesNotChangeTheResult()
    {
        var a = Owner(1, Structural()); var b = Owner(2, Metric("x", Slot.Tr), Table("t", 12, Slot.Br));
        Assert.Equal(CardJson.Serialize(Solve(1280, 720, owners: [a, b])), CardJson.Serialize(Solve(1280, 720, owners: [b, a])));
    }

    [Fact]
    public void EmptyRequestsPlaceNothing()
    {
        Assert.Empty(Solve(1600, 900).Placements);
        var s = Solve(1600, 900, owners: Owner(1));
        Assert.Empty(s.Placements);
        Assert.Null(s.Chip);
    }

    [Fact]
    public void CompactRowsNeverExceedAuthoredRows()
    {
        var table = Table("t", 10, maxRows: 3);
        Assert.Equal(FormMeasure.Size(table, Theme.Paper, CardForm.None, TextMetrics.Estimate), FormMeasure.Size(table, Theme.Paper, new CardForm(5), TextMetrics.Estimate));
    }

    [Fact]
    public void SignatureFollowsLayoutInputsOnly()
    {
        LayoutRequest Request(CardSpec card, double size = 1, double w = 1600, double dpi = 1) => new(new(w, 900), dpi, [new(Id(1), size, Theme.Paper, [card])], TextMetrics.Estimate, new());
        var table = Table("t", 10);
        var signature = ViewportLayout.Signature(Request(table));
        Assert.Equal(signature, ViewportLayout.Signature(Request(table with { SortColumn = 1, SortDescending = true, ScrollOffset = 3, HoverKey = "t-1", Rev = 9 })));
        Assert.NotEqual(signature, ViewportLayout.Signature(Request(Table("t", 11))));
        Assert.NotEqual(signature, ViewportLayout.Signature(Request(table with { WiredSlot = Slot.Br })));
        Assert.NotEqual(signature, ViewportLayout.Signature(Request(table, size: 1.25)));
        Assert.NotEqual(signature, ViewportLayout.Signature(Request(table, w: 1601)));
        Assert.NotEqual(signature, ViewportLayout.Signature(Request(table, dpi: 2)));
    }
}
