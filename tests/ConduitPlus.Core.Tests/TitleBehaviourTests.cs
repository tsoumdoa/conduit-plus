using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class TitleBehaviourTests
{
    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    private static CardSpec Title(string id, Slot slot, string title = "Truss review", string subtitle = "", string text = "") =>
        new() { Id = id, Kind = CardKind.Title, Title = title, Subtitle = subtitle, Text = text, WiredSlot = slot };
    private static CardSpec Metric(string id, Slot slot) => new() { Id = id, Kind = CardKind.Metric, Title = id, Value = "1", WiredSlot = slot };
    private static CardSpec Wide(string id, Slot slot) => new() { Id = id, Kind = CardKind.Metric, Title = id, Value = "1", Width = 600, WiredSlot = slot };
    private static ViewportSolution Solve(double w, double h, LayoutOptions? options = null, params LayoutParticipant[] owners) =>
        ViewportLayout.Solve(new(new(w, h), 1, owners, TextMetrics.Estimate, options ?? new()));
    private static LayoutParticipant Owner(int n, params CardSpec[] cards) => new(Id(n), 1, Theme.Paper, cards);
    private static CardPlacement At(ViewportSolution s, string id) => s.Placements.Single(p => p.CardId == id);

    [Fact]
    public void SixPositionsSitInTheirCornersAndEdges()
    {
        var s = Solve(1600, 900, owners: Owner(1, Title("tl", Slot.Tl), Title("tc", Slot.Tc), Title("tr", Slot.Tr), Title("bl", Slot.Bl), Title("bc", Slot.Bc), Title("br", Slot.Br)));
        Assert.Empty(s.Hidden);
        Assert.Equal((12d, 30d), (At(s, "tl").X, At(s, "tl").Y));
        Assert.Equal(1600 - 12, At(s, "tr").X + At(s, "tr").Width, 6);
        Assert.Equal(30, At(s, "tr").Y, 6);
        Assert.Equal(800, At(s, "tc").X + At(s, "tc").Width / 2, 6);
        Assert.Equal(30, At(s, "tc").Y, 6);
        Assert.Equal(900 - 12, At(s, "bl").Y + At(s, "bl").Height, 6);
        Assert.Equal(900 - 12, At(s, "br").Y + At(s, "br").Height, 6);
        Assert.Equal(800, At(s, "bc").X + At(s, "bc").Width / 2, 6);
    }

    [Fact]
    public void SideTitlePushesOnlyItsColumn()
    {
        var s = Solve(1600, 900, owners: Owner(1, Title("t", Slot.Tl, subtitle: "4 bays"), Metric("left", Slot.Tl), Metric("right", Slot.Tr)));
        Assert.Equal(At(s, "t").Y + At(s, "t").Height + 8, At(s, "left").Y, 6);
        Assert.Equal(30, At(s, "right").Y, 6);
    }

    [Fact]
    public void MiddleTitleDoesNotPushColumns()
    {
        var s = Solve(1600, 900, owners: Owner(1, Title("t", Slot.Tc), Metric("left", Slot.Tl), Metric("right", Slot.Tr)));
        Assert.Equal(30, At(s, "left").Y, 6);
        Assert.Equal(30, At(s, "right").Y, 6);
    }

    [Fact]
    public void MiddleTitleNarrowsToTheGapBetweenWideColumns()
    {
        var long_ = string.Join(" ", Enumerable.Repeat("A long heading that has to wrap", 6));
        var s = Solve(1600, 900, owners: Owner(1, Title("t", Slot.Tc, long_), Wide("left", Slot.Tl), Wide("right", Slot.Tr)));
        var t = At(s, "t");
        Assert.True(t.X >= At(s, "left").X + At(s, "left").Width);
        Assert.True(t.X + t.Width <= At(s, "right").X);
    }

    [Fact]
    public void TitlesAtOnePositionStackAcrossOwners()
    {
        var s = Solve(1600, 900, owners: [Owner(1, Title("a", Slot.Tl), Title("b", Slot.Tl)), Owner(2, Title("c", Slot.Tl))]);
        var stack = s.Placements.OrderBy(p => p.Y).ToArray();
        Assert.Equal(["a", "b", "c"], stack.Select(p => p.CardId));
        for (var i = 1; i < stack.Length; i++) Assert.Equal(stack[i - 1].Y + stack[i - 1].Height + 8, stack[i].Y, 6);
    }

    [Fact]
    public void BottomLeftTitleRespectsTheAxesReserve()
    {
        var s = Solve(1600, 900, new LayoutOptions { BottomLeftReserve = 100 }, Owner(1, Title("t", Slot.Bl)));
        Assert.Equal(900 - 12 - 100, At(s, "t").Y + At(s, "t").Height, 6);
    }

    [Fact]
    public void PortraitTitlesSitAboveAndBelowTheBands()
    {
        var s = Solve(600, 1200, owners: Owner(1, Title("top", Slot.Tc), Title("bottom", Slot.Bc), Metric("left", Slot.Tl), Metric("right", Slot.Tr)));
        Assert.True(At(s, "top").Y + At(s, "top").Height <= At(s, "left").Y);
        Assert.True(At(s, "right").Y + At(s, "right").Height <= At(s, "bottom").Y);
    }

    [Fact]
    public void ShrinkingShortensTitlesBeforeHidingThemAndHidesThemLast()
    {
        var cards = new[] { Title("t", Slot.Tl, "Truss utilisation review", "4 bays · 24 m span", string.Join(" ", Enumerable.Repeat("Members above 1.0 need review before issue.", 4))) }
            .Concat(Enumerable.Range(0, 6).Select(i => Metric($"m{i}", Slot.Tl))).ToArray();
        string[] seen = [];
        foreach (var (w, h) in new[] { (1600d, 900d), (900d, 560d), (700d, 420d), (520d, 330d), (400d, 260d), (300d, 200d) })
        {
            var s = Solve(w, h, owners: Owner(1, cards));
            var steps = s.Report.CompactSteps;
            if (steps.Contains("TitleDescriptions0")) Assert.Contains("TitleDescriptions2", steps);
            var titleHidden = s.Hidden.Any(x => x.CardId == "t");
            if (titleHidden) Assert.All(cards.Where(c => c.Kind != CardKind.Title), c => Assert.Contains(s.Hidden, x => x.CardId == c.Id));
            if (s.Placements.FirstOrDefault(p => p.CardId == "t") is { } t && s.Hidden.Count > 0)
                Assert.True(t.Form.DescriptionLines == 0 || !steps.Contains("TitleDescriptions0"));
            seen = [.. seen, .. steps];
        }
        Assert.Contains("TitleDescriptions2", seen);
    }

    [Fact]
    public void AddingATitleDoesNotMoveOtherCardsAutomaticSlots()
    {
        CardSpec Plain(string id) => new() { Id = id, Kind = CardKind.Metric, Title = id, Value = "1" };
        var without = Solve(1600, 900, owners: Owner(1, Plain("a"), Plain("b")));
        var with = Solve(1600, 900, owners: Owner(1, Title("t", Slot.Bc), Plain("a"), Plain("b")));
        Assert.Equal(At(without, "a").X, At(with, "a").X, 6);
        Assert.Equal(At(without, "b").X, At(with, "b").X, 6);
    }
}
