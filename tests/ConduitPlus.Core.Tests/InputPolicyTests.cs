using System.Collections.Immutable;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class InputPolicyTests
{
    private static CardSpec Bar(string id = "area", params string[] keys) => new() { Id = id, Kind = CardKind.Bar, Title = "Area", Keys = keys, Labels = keys, Values = keys.Select(_ => (double?)1).ToArray() };

    [Fact]
    public void IterationIdentityPreservesFirstIdAndKeepsOneAutomaticSlot()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.Equal(id, CardIdentity.ForIteration(id, 0));
        Assert.Equal(id + ":12", CardIdentity.ForIteration(id, 12));
        var cards = new[] { Bar(id, "a"), Bar(CardIdentity.ForIteration(id, 1), "b"), Bar("other", "c") };
        Assert.Equal(new[] { Slot.Tl, Slot.Tl, Slot.Tr }, SlotLayout.ResolveAll(cards));
        Assert.Equal(new[] { Slot.Tl, Slot.Br, Slot.Tr }, SlotLayout.ResolveAll([cards[0], cards[1] with { WiredSlot = Slot.Br }, cards[2]]));
        Assert.Equal("ordinary:12", CardIdentity.Source("ordinary:12"));
    }

    [Fact]
    public void NewNumberedLabelsAndLegacyLabelsRemainDistinct()
    {
        Assert.Equal(new[] { "1", "2", "3" }, InputKeys.Numbered(3));
        Assert.Equal(new[] { "M01", "M02", "M03" }, InputKeys.Numbered(3, true));
        Assert.Equal(new[] { "1", "2" }, InputKeys.FromLabels(["Bath", "Bath"], [], out var numbered));
        Assert.True(numbered);
        Assert.Equal(new[] { "Bath", "Bed" }, InputKeys.FromLabels(["Bath", "Bed"], [], out numbered));
        Assert.False(numbered);
        Assert.Equal(new[] { "a", "b" }, InputKeys.FromLabels(["Bath", "Bath"], ["a", "b"], out numbered));
        Assert.False(numbered);
    }

    [Fact]
    public void ExplicitRepeatedKeysHaveActionableOrdinalDiagnostic()
    {
        Assert.Equal("Keys repeat ('Bath' ×2); they must be unique in a card.", Assert.Throws<ArgumentException>(() => Bar("area", "Bath", "Bath").Validate()).Message);
        Assert.Null(InputKeys.Repeated(["Bath", "bath"]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GeometryDefaultsMapSingleBranchPerItemAndMultipleBranchesPerBranch(bool multiple)
    {
        int[] counts = multiple ? [2, 0, 1] : [3];
        var keys = InputKeys.ForGeometry(counts);
        Assert.Equal(new[] { "1", "2", "3" }, keys);
        var map = LinkKeyMapping.Create(counts, keys);
        Assert.Equal(multiple ? new[] { 0, 0, 2 } : new[] { 0, 1, 2 }, map.SelectMany(b => b));
    }

    [Fact]
    public void NoLinksOrUnkeyedCardsStayQuiet()
    {
        Assert.Empty(LinkDiagnostics.Compare([Bar("area", "a")], [], false));
        Assert.Empty(LinkDiagnostics.Compare([new CardSpec { Id = "metric" }], ["a"], true));
    }

    [Fact]
    public void ZeroOverlapWarnsWithTwoExamplesAndCaseSensitivity()
    {
        var notice = Assert.Single(LinkDiagnostics.Compare([Bar("area", "Bed 01", "Liv 02", "Bath")], ["R01", "R02", "R03"], true));
        Assert.True(notice.Warning);
        Assert.Equal("Link keys don't match any card keys, so clicks won't highlight geometry. Link: 'R01', 'R02'. Bar \"Area\": 'Bed 01', 'Liv 02'. Keys are case-sensitive. Wire the same keys into Link Keys.", notice.Message);
        Assert.True(Assert.Single(LinkDiagnostics.Compare([Bar("area", "r01")], ["R01"], true)).Warning);
    }

    [Fact]
    public void OneMatchingKeySuppressesGlobalWarningButUnmatchedCardGetsRemark()
    {
        var cards = new[] { Bar("area", "a", "b", "c"), Bar("loads", "LC1") with { Title = "Load cases" } };
        var notice = Assert.Single(LinkDiagnostics.Compare(cards, ["a"], true));
        Assert.False(notice.Warning);
        Assert.Contains("Load cases", notice.Message);
        Assert.Empty(LinkDiagnostics.Compare([cards[0]], ["a"], true));
    }

    [Fact]
    public void GroupsCompareMembersAndIgnoreEmptyAndDisabledTargets()
    {
        var group = Bar("area", "bedrooms") with { Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("bedrooms", new(["R01", "R02"])) };
        Assert.Empty(LinkDiagnostics.Compare([group], ["R02"], true));
        Assert.True(Assert.Single(LinkDiagnostics.Compare([group], ["other"], true)).Warning);
        Assert.Empty(LinkDiagnostics.Compare([group with { Groups = group.Groups.SetItem("bedrooms", new([])) }], ["other"], true));
        Assert.Empty(LinkDiagnostics.Compare([group], ["bedrooms"], true)); // Link collides with the group token, disabling it.
    }

    [Fact]
    public void ErroredLinkWithNoPublishedKeysStaysQuiet()
    {
        var card = Bar("area", "a");
        var membership = MembershipIndex.Create([new BrushRegistration("local", [card], [])]);
        Assert.Empty(LinkDiagnostics.Compare([card], [], true, membership));
    }

    [Fact]
    public void LinkWithoutGeometryStaysQuietEvenWithDeclaredKeys()
    {
        var card = Bar("area", "a");
        var membership = MembershipIndex.Create([new BrushRegistration("local", [card], ["b"])]);
        Assert.Empty(LinkDiagnostics.Compare([card], [], true, membership));
    }

    [Fact]
    public void EmptyLinkBranchStillDisablesCollidingGroup()
    {
        var card = Group("local", "G", "a");
        var membership = MembershipIndex.Create([new BrushRegistration("local", [card], ["G", "b"])]);
        Assert.Null(membership.Resolve("G", BrushTargetKind.Group));
        Assert.Empty(LinkDiagnostics.Compare([card], ["b"], true, membership));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ScopePeerIndividualOrConflictingGroupDisablesMismatchTarget(bool conflict)
    {
        var card = Group("local", "G", "a");
        var peer = conflict ? Group("peer", "G", "different") : Bar("peer", "G");
        var membership = MembershipIndex.Create([new BrushRegistration("local", [card], ["b"]), new BrushRegistration("peer", [peer], [])]);
        Assert.Null(membership.Resolve("G", BrushTargetKind.Group));
        Assert.Empty(LinkDiagnostics.Compare([card], ["b"], true, membership));
    }

    [Fact]
    public void ScopePeerGeometryDoesNotInventLocalMatches()
    {
        var card = Bar("local", "a");
        var membership = MembershipIndex.Create([new BrushRegistration("local", [card], ["b"]), new BrushRegistration("peer", [], ["a"])]);
        Assert.True(Assert.Single(LinkDiagnostics.Compare([card], ["b"], true, membership)).Warning);
    }

    private static CardSpec Group(string id, string key, params string[] members) => Bar(id, key) with
    { Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add(key, new(members.ToImmutableArray())) };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReversedAndInterleavedIterationsKeepOneSlotAndNextAllocation(bool reverse)
    {
        var id = Guid.NewGuid().ToString("N");
        var iterations = new[] { 0, 1, 2 };
        if (reverse) Array.Reverse(iterations);
        var cards = iterations.Select(i => Bar(CardIdentity.ForIteration(id, i), "a")).ToArray();
        Assert.Equal(new[] { Slot.Tl, Slot.Tl, Slot.Tl, Slot.Tr }, SlotLayout.ResolveAll([.. cards, Bar("next", "b")]));
        Assert.Equal(new[] { Slot.Tl, Slot.Tr, Slot.Tl, Slot.Br, Slot.Bl }, SlotLayout.ResolveAll([cards[0], Bar("other", "b"), cards[1], cards[2] with { WiredSlot = Slot.Br }, Bar("next", "c")]));
        Assert.Equal(new[] { Slot.Br, Slot.Tl, Slot.Tr }, SlotLayout.ResolveAll([cards[0] with { WiredSlot = Slot.Br }, cards[1], Bar("next", "c")]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IterationCardsStayAdjacentInTheirIncomingOrder(bool reverse)
    {
        var id = Guid.NewGuid().ToString("N");
        var a = Bar(CardIdentity.ForIteration(id, reverse ? 1 : 0), "a");
        var b = Bar(CardIdentity.ForIteration(id, reverse ? 0 : 1), "b");
        var other = Bar("other", "c") with { WiredSlot = Slot.Tl };
        var owner = Guid.NewGuid();
        var result = ViewportLayout.Solve(new(new(1920, 1080), 1, [new(owner, 1, Theme.Paper, [a, other, b])], TextMetrics.Estimate, new()));
        Assert.Empty(result.Hidden);
        Assert.Equal(new[] { a.Id, b.Id, other.Id }, result.Placements.OrderBy(p => p.Y).Select(p => p.CardId));
    }

    [Fact]
    public void UnsetThresholdNeverAddsPercentageToTooltip()
    {
        var card = Bar("bar", "a") with { HasThreshold = false, Threshold = 1 };
        Assert.DoesNotContain(Tooltip.For(card, "a")!, line => line.Contains("% of threshold", StringComparison.Ordinal));
        Assert.Contains("100% of threshold", Tooltip.For(card with { HasThreshold = true }, "a")!);
    }

    [Fact]
    public void CardSummaryUsesReadableTitleCountAndSlot()
    {
        Assert.Equal("Bar · Area − minimum · 17 bars · top left", (Bar("area", InputKeys.Numbered(17)) with { Title = "Area − minimum", WiredSlot = Slot.Tl }).ToString());
        Assert.Equal("Metric · Untitled · auto", new CardSpec { Id = "metric" }.ToString());
        Assert.True(CardJson.Deserialize<CardSpec>("{\"id\":\"old\"}").HasThreshold);
    }

    [Fact]
    public void UnsetBarThresholdUsesAccentAndNoMarkerWhileExplicitColoursWin()
    {
        var card = Bar("area", "a", "b") with { Values = [5, 10], HasThreshold = false };
        var canvas = new PaintCanvas(); CardPainter.Paint(canvas, card, Theme.Paper);
        Assert.Equal(new[] { Theme.Paper.Accent, Theme.Paper.Accent }, canvas.Bars);
        Assert.Equal(0, canvas.Lines);
        canvas = new(); CardPainter.Paint(canvas, card with { HasThreshold = true }, Theme.Paper);
        Assert.Equal(new[] { Theme.Paper.Bad, Theme.Paper.Bad }, canvas.Bars); Assert.Equal(2, canvas.Lines);
        canvas = new(); CardPainter.Paint(canvas, card with { Colors = ["#123456", "#654321"] }, Theme.Paper);
        Assert.Equal(new[] { "#123456", "#654321" }, canvas.Bars);
    }

    private sealed class PaintCanvas : ICanvas2D
    {
        public List<string> Bars { get; } = [];
        public int Lines { get; private set; }
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Estimate(text, size, font);
        public void Rect(Rect2 rect, string fill) { if (rect.Height == 8 && fill != Theme.Paper.Track) Bars.Add(fill); }
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Lines++;
        public void Text(string text, Point2 topLeft, double size, string color, string font) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
