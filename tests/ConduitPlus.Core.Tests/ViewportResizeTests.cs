using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class ViewportResizeTests
{
    private static readonly Guid Owner = new(1, 0, 0, new byte[8]);
    private static CardSpec Metric(string id, Slot? slot = null, double width = 280) =>
        new() { Id = id, Kind = CardKind.Metric, Title = id, Value = "42", WiredSlot = slot, Width = width };
    private static ViewportSolution Solve(CardSpec[] cards, double w, double h, double dpi = 1) =>
        ViewportLayout.Solve(new(new(w, h), dpi, [new(Owner, 1, Theme.Paper, cards)], TextMetrics.Estimate, new(BottomLeftReserve: 72 * dpi)));
    private static bool Overlap(Rect2 a, Rect2 b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static CardSpec[] Architecture(bool rooms)
    {
        // Match the architecture examples: four top-left metrics, top-right bar, bottom-right table,
        // a top-left legend, and (for massing) another top-left bar and note.
        var keys = rooms ? DemoRooms.Create().Rooms.Select(r => r.Key).ToArray() : DemoMassing.Create().Blocks.Select(b => b.Key).ToArray();
        CardSpec[] cards = [.. Enumerable.Range(0, 4).Select(i => Metric($"metric{i}", Slot.Tl)),
            new() { Id = "bar", Kind = CardKind.Bar, WiredSlot = Slot.Tr, Keys = keys, Values = keys.Select(_ => (double?)1).ToArray(), Labels = keys },
            new() { Id = "table", Kind = CardKind.Table, WiredSlot = Slot.Br, Width = 600,
                Headers = ["Name", "Type", "Value", "Limit", "Delta", "Status"], Rows = keys.Select(k => new TableRow(k, [k, "Type", "42", "40", "2", "Pass"])).ToArray() },
            new() { Id = "legend", Kind = CardKind.Legend, WiredSlot = Slot.Tl, Labels = rooms ? ["Bedroom", "Living", "Kitchen", "Bath", "Study", "Hall"] : ["Residential", "Office", "Retail"],
                Colors = rooms ? ["#638AD2", "#BA8F63", "#66A88A", "#638AD2", "#BA8F63", "#66A88A"] : ["#638AD2", "#BA8F63", "#66A88A"] }];
        return rooms ? cards : [.. cards, new() { Id = "use", Kind = CardKind.Bar, WiredSlot = Slot.Tl, Labels = ["Residential", "Office", "Retail"], Values = [6156, 7680, 1140] },
            new() { Id = "note", Kind = CardKind.Text, WiredSlot = Slot.Tl, Title = "Schematic zoning study", Text = "100 × 80 m site · FAR limit 2.0 · height limit 30 m.\nGFA counts whole floorplates. Limits are fictitious.\nClick a face or row; change Extra floors." }];
    }

    [Fact]
    public void DashboardsStayOnTheirSidesAcrossWidthThresholdAndDpi()
    {
        CardSpec[][] dashboards = [[.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras()], Architecture(true), Architecture(false)];
        foreach (var cards in dashboards)
        foreach (var dpi in new[] { 1, 1.25, 2 })
        foreach (var height in new[] { 540, 900, 1200 })
        foreach (var width in Enumerable.Range(0, 55).Select(i => 1600 - i * 25).Concat([height + 1, height, height - 1]))
        {
            var w = width * dpi; var h = height * dpi;
            var s = Solve(cards, w, h, dpi);
            var slots = cards.Zip(SlotLayout.ResolveAll(cards)).ToDictionary(x => x.First.Id, x => x.Second);
            Assert.Equal(cards.Length, s.Placements.Count + s.Hidden.Count);
            foreach (var p in s.Placements)
            {
                Assert.Equal(slots[p.CardId], p.Slot);
                Assert.True(p.X >= 0 && p.Y >= 0 && p.X + p.Width <= w + 1e-6 && p.Y + p.Height <= h + 1e-6);
                var centre = p.X + p.Width / 2;
                if (p.Slot is Slot.Tr or Slot.Rc or Slot.Br) Assert.True(centre >= w / 2 - 1e-6);
                else Assert.True(centre <= w / 2 + 1e-6);
                var rect = new Rect2(p.X, p.Y, p.Width, p.Height);
                Assert.All(s.Placements.Where(q => q != p), q => Assert.False(Overlap(rect, new(q.X, q.Y, q.Width, q.Height))));
                if (s.Chip is { } chip) Assert.False(Overlap(rect, chip));
            }
        }
    }

    [Fact]
    public void BandsFillRightToLeftAndReserveTheChipAndAxesCorner()
    {
        CardSpec[] cards = [Metric("left", Slot.Tl, 600), Metric("right0", Slot.Tr, 100), Metric("right1", Slot.Tr, 100), Metric("right2", Slot.Tr, 100), Metric("unplaceable", Slot.Tr, 2000)];
        var s = Solve(cards, 600, 1200);
        Assert.Equal(LayoutOrientation.Bands, s.Report.Orientation);
        Assert.Equal([(Owner, "unplaceable")], s.Hidden);
        var right = s.Placements.Where(p => p.Slot == Slot.Tr).ToArray();
        Assert.Equal(3, right.Length);
        Assert.Equal(600 - 12 * .625, right[0].X + right[0].Width);
        Assert.True(right[0].X > right[1].X && right[1].X > right[2].X);
        Assert.All(right, p => Assert.True(p.Y + p.Height <= s.Chip!.Value.Y - 72));
        var chip = s.Chip!.Value;
        Assert.All(s.Placements, p => Assert.False(Overlap(new(p.X, p.Y, p.Width, p.Height), chip)));

        // While resizing, the right rail's bottom band follows the bottom-right corner and the top band stays put.
        var moved = ViewportLayout.Reanchor(s, new(600, 1200), new(500, 1000));
        foreach (var (before, after) in s.Placements.Zip(moved.Placements))
        {
            var onRight = before.Slot == Slot.Tr;
            Assert.Equal(before.X + (onRight ? -100 : 0), after.X);
            Assert.Equal(before.Y + (onRight ? -200 : 0), after.Y);
        }
    }

    [Theory]
    [InlineData(1600, 900)]
    [InlineData(1100, 700)]
    [InlineData(2400, 1400)]
    [InlineData(0, 0)]
    public void ReanchorPreservesEverySlotsEdgeDistanceOrCentreOffset(double w, double h)
    {
        var slots = new[] { Slot.Tl, Slot.Tr, Slot.Lc, Slot.Rc, Slot.Bl, Slot.Br, Slot.Tc, Slot.Bc };
        var placements = slots.Select((slot, i) => new CardPlacement(Owner, $"card{i}", 100 + i * 7, 80 + i * 11, 150, 60, new(Compact: true), slot))
            .Append(new(Guid.Empty, "card0", 1300, 100, 150, 60, CardForm.None, Slot.Tr)).ToArray();
        var frame = new ViewportSolution(placements, [(Owner, "hidden")], new Dictionary<Guid, double> { [Owner] = .625 },
            new Dictionary<Guid, double> { [Owner] = 1.25 }, new(1460, 850, 125, 35), new(new Dictionary<Guid, double>(), 1, 2, LayoutOrientation.Columns, [], 1));
        var from = new Size2(1600, 900); var to = new Size2(w, h);
        var result = ViewportLayout.Reanchor(frame, from, to);
        foreach (var (before, after) in frame.Placements.Zip(result.Placements))
        {
            var anchorX = before.Slot is Slot.Tr or Slot.Rc or Slot.Br ? 1d : before.Slot is Slot.Tc or Slot.Bc ? .5 : 0;
            var anchorY = before.Slot is Slot.Bl or Slot.Br or Slot.Bc ? 1d : before.Slot is Slot.Lc or Slot.Rc ? .5 : 0;
            Assert.Equal(before.X - from.Width * anchorX, after.X - to.Width * anchorX);
            Assert.Equal(before.Y - from.Height * anchorY, after.Y - to.Height * anchorY);
            Assert.Equal(before with { X = after.X, Y = after.Y }, after);
        }
        var chip = result.Chip!.Value;
        Assert.Equal(15, w - chip.X - chip.Width);
        Assert.Equal(15, h - chip.Y - chip.Height);
        Assert.Equal(frame.Chip!.Value.Width, chip.Width);
        Assert.Equal(frame.Chip!.Value.Height, chip.Height);
        Assert.Same(frame.Hidden, result.Hidden);
        Assert.Same(frame.Zoom, result.Zoom);
        Assert.Same(frame.Unit, result.Unit);
        Assert.Same(frame.Report, result.Report);
        var restored = ViewportLayout.Reanchor(result, to, from);
        Assert.Equal(frame.Placements, restored.Placements);
        Assert.Equal(frame.Chip, restored.Chip);
        Assert.Equal(100, frame.Placements[0].X);
        Assert.Equal(80, frame.Placements[0].Y);
    }

    [Fact]
    public void ReanchorUsesResolvedAutomaticSlotsIncludingTitles()
    {
        var cards = Enumerable.Range(0, 6).Select(i => Metric($"auto{i}")).Prepend(new() { Id = "title", Kind = CardKind.Title, Title = "Title", WiredSlot = Slot.Bc }).ToArray();
        var frame = Solve(cards, 1600, 900);
        Assert.Empty(frame.Hidden);
        Assert.Equal(SlotLayout.ResolveAll(cards), frame.Placements.Select(p => p.Slot));
        var moved = ViewportLayout.Reanchor(frame, new(1600, 900), new(1200, 700));
        Assert.Equal(-400, moved.Placements.Single(p => p.CardId == "auto1").X - frame.Placements.Single(p => p.CardId == "auto1").X);
        Assert.Equal(-100, moved.Placements.Single(p => p.CardId == "auto4").Y - frame.Placements.Single(p => p.CardId == "auto4").Y);
        Assert.Equal(-200, moved.Placements.Single(p => p.CardId == "title").X - frame.Placements.Single(p => p.CardId == "title").X);
        Assert.Same(frame, ViewportLayout.Reanchor(frame, new(1600, 900), new(1600, 900)));
        var empty = Solve([], 1600, 900);
        Assert.Null(ViewportLayout.Reanchor(empty, new(1600, 900), new(400, 300)).Chip);
    }
}
