using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ThresholdTests
{
    [Fact]
    public void SnapshotRepaintsForMappingOrCountChanges()
    {
        CardSpec Create(double value) => ColourScale.Create([value], min: 0, max: 2,
            thresholds: [1], thresholdColours: ["#123456"]).Legend;
        var store = new CardStore();
        Assert.Single(store.Update([Create(2)]));
        Assert.Empty(store.Update([Create(2)]));
        var changed = Assert.Single(store.Update([Create(0)]));
        Assert.Equal(0, changed.Thresholds!.Entries[0].Count);
        Assert.Single(store.Update([changed with { Mapping = changed.Mapping! with { Mid = .5 } }]));
    }
    [Fact]
    public void OutermostInclusiveWinnersAreIndependentOfOrderAndCountedOnce()
    {
        double[] thresholds = [4, 2, 8, 6, 4];
        string[] colours = ["#AA0000", "#BB0000", "#0000AA", "#0000BB", "#000000"];
        string[] sides = ["Above", "Above", "Below", "Below", "Above"];
        var result = ColourScale.Create([null, double.NaN, -1, 2, 4, 6, 9], min: 0, max: 5,
            below: "#FFFFFF", above: "#FFFFFF", thresholds: thresholds, thresholdColours: colours, thresholdSides: sides);
        Assert.Equal(new[] { Palettes.Missing, Palettes.Missing, "#0000BB", "#BB0000", "#AA0000", "#AA0000", "#AA0000" }, result.Colours);
        var set = result.Legend.Thresholds!;
        Assert.Equal(new[] { 0, 1, 3, 1 }, set.Entries.Select(e => e.Count));
        Assert.Null(set.WinnerFor(double.PositiveInfinity));
        Assert.Contains(result.Messages, m => m.Text.Contains("overlap"));
        Assert.Contains(result.Messages, m => m.Text.Contains("Duplicate"));
        Assert.Contains(result.Messages, m => m.Text.Contains("outside the scale"));
        Assert.DoesNotContain(result.Messages, m => m.Text.Contains("are outside"));
        var reversed = ThresholdSet.Resolve(thresholds.Take(4).Reverse().ToArray(), colours.Take(4).Reverse().ToArray(),
            sides.Take(4).Reverse().ToArray(), []);
        Assert.Equal(set.Entries.Select(e => e with { Count = 0 }), reversed.Entries);
        var below = ThresholdSet.Resolve([2, 4], ["#112233"], ["below"], []);
        Assert.Equal(2, below.WinnerFor(2)!.Value);
        Assert.Equal(4, below.WinnerFor(3)!.Value);
        Assert.Null(below.WinnerFor(5));
    }

    [Fact]
    public void InvalidInputsFailAndOrphanedInputsWarn()
    {
        Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve([double.NaN], ["#112233"], null, []));
        Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve([1], null, null, []));
        Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve([1], ["#112233", "#332211"], null, []));
        Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve([1], ["#112233"], ["Above", "Below"], []));
        Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve([1], ["#112233"], ["1"], []));
        List<ColourMessage> messages = [];
        Assert.Empty(ThresholdSet.Resolve([], [], ["unknown"], messages).Entries);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(ColourMessageLevel.Warning, m.Level));
    }

    [Fact]
    public void NullItemsUseTheInvalidTextDiagnostics()
    {
        var sides = Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve(
            [1, 2, 3], ["#123456"], ["Above", null!, "Below"], []));
        Assert.Equal("Unknown threshold side ''; choose Above or Below.", sides.Message);
        var colours = Assert.Throws<ArgumentException>(() => ThresholdSet.Resolve(
            [1, 2, 3], ["#123456", null!, "#654321"], null, []));
        Assert.Equal("Threshold colours must be #RRGGBB strings.", colours.Message);
    }

    [Fact]
    public void CollapsedClipAndUncaughtOutlierSurviveStoreAndJson()
    {
        var values = Enumerable.Repeat<double?>(2, 100).Append(5).ToArray();
        var scale = ColourScale.Create(values, rangeMode: ColourRangeMode.ClipOutliers,
            clipPercent: 5, thresholds: [6], thresholdColours: ["#123456"]);
        var store = new CardStore();
        store.Update([scale.Legend]);
        var snapshot = Assert.Single(store.Snapshot);
        var restored = CardJson.Deserialize<CardSpec>(CardJson.Serialize(snapshot));
        Assert.True(snapshot.Mapping!.HasCollapsedClip);
        Assert.True(restored.Mapping!.HasCollapsedClip);
        Assert.Null(restored.Mapping.Thresholds.WinnerFor(5));
        Assert.NotEqual(scale.Colours[0], scale.Colours[^1]);
        Assert.Equal(scale.Colours[^1], restored.Mapping.ColourAt(5));
    }

    [Fact]
    public void WidenedDomainInvalidatesLayoutAndMovesTheFollowingCard()
    {
        var legend = ColourScale.Create([24, 25], min: 23, max: 26, slot: Slot.Tl,
            thresholds: [24, 25], thresholdColours: ["#123456"]).Legend;
        var next = new CardSpec { Id = "next", Kind = CardKind.Metric, WiredSlot = Slot.Tl };
        var owner = Guid.NewGuid();
        LayoutRequest Request(CardSpec card) =>
            new(new(1600, 900), 1, [new(owner, 1, Theme.Paper, [card, next])], TextMetrics.Estimate, new());
        var narrow = Request(legend);
        var wide = Request(legend with { Min = 0, Max = 100 });
        Assert.NotEqual(ViewportLayout.Signature(narrow), ViewportLayout.Signature(wide));
        var before = ViewportLayout.Solve(narrow).Placements;
        var after = ViewportLayout.Solve(wide).Placements;
        var extra = after.Single(p => p.CardId == legend.Id).Height - before.Single(p => p.CardId == legend.Id).Height;
        Assert.True(extra > 0);
        Assert.Equal(extra, after.Single(p => p.CardId == next.Id).Y - before.Single(p => p.CardId == next.Id).Y);
    }

    [Fact]
    public void MappingSurvivesSnapshotRoundTripWithExactModelColours()
    {
        double?[] values = [-2, -1, 0, double.Epsilon, .25, .5, 1, 2, null];
        var inputs = Palettes.All.SelectMany(p => new[] { new[] { p.Name }, new[] { p.Name + " reversed" } })
            .Append(["#012345", "#345678", "#789ABC", "#ABCDEF"]);
        foreach (var palette in inputs)
        {
            var scale = ColourScale.Create(values, palette, 0, 1, double.Epsilon,
                below: "#000001", above: "#000002", thresholds: [.5], thresholdColours: ["#000003"]);
            var saved = CardJson.Deserialize<CardSpec>(CardJson.Serialize(scale.Legend));
            Assert.Equal(scale.Colours, values.Select(saved.Mapping!.ColourAt));
            Assert.Equal(scale.Legend.Thresholds!.Entries.AsEnumerable(), saved.Thresholds!.Entries);
        }
        var constant = ColourScale.Create([2, 2], thresholds: [2], thresholdColours: ["#123456"]);
        Assert.Equal(new[] { "#123456", "#123456" }, constant.Colours);
        Assert.Equal(constant.Colours[0], constant.Legend.Mapping!.ColourAt(2));
    }
}
