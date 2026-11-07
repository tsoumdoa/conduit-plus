using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class LayoutTests
{
    private static readonly Size2 Viewport = new(1600, 1200);
    private static CardSpec Card(string id, Slot? slot = null, int? order = null) => new()
    { Id = id, Kind = CardKind.Metric, Title = id, ManualSlot = slot, OrderSlot = order.HasValue ? slot : null, ManualOrder = order };
    private static Dictionary<string, LayoutOverride> Merge(Dictionary<string, LayoutOverride> before, Dictionary<string, LayoutOverride> batch)
    { var next = new Dictionary<string, LayoutOverride>(before, StringComparer.Ordinal); foreach (var p in batch) next[p.Key] = p.Value; return next; }

    [Fact]
    public void PrecedenceAndRanksStayBoundToTheirSlot()
    {
        var card = Card("a", Slot.Bl, 0) with { WiredSlot = Slot.Tr };
        Assert.Equal(Slot.Tr, SlotLayout.Resolve(card, 0));
        Assert.Equal(Slot.Bl, SlotLayout.Resolve(card with { WiredSlot = null }, 0));
        Assert.Equal(Slot.Rc, SlotLayout.Resolve(card with { WiredSlot = null, ManualSlot = null }, 5));
        CardSpec[] cards = [Card("unranked", Slot.Tr), card];
        Assert.Equal(new[] { "unranked", "a" }, LayoutEditing.Stack(cards, Slot.Tr).Select(c => c.Id));
        cards[1] = card with { OrderSlot = Slot.Tr };
        Assert.Equal(new[] { "a", "unranked" }, LayoutEditing.Stack(cards, Slot.Tr).Select(c => c.Id));
    }
    [Fact]
    public void RankTiesAndUnsetRanksKeepInputOrderAndPaintOrder()
    {
        CardSpec[] cards = [Card("unset", Slot.Tl), Card("tie1", Slot.Tl, 2), Card("first", Slot.Tl, 0), Card("tie2", Slot.Tl, 2), Card("max", Slot.Tl, int.MaxValue), Card("unset2", Slot.Tl)];
        var order = new[] { "first", "tie1", "tie2", "max", "unset", "unset2" };
        Assert.Equal(order, LayoutEditing.Stack(cards, Slot.Tl).Select(c => c.Id));
        var layouts = SlotLayout.Place(cards, Theme.Paper, Viewport);
        Assert.Equal(cards.Select(c => c.Id), layouts.Select(l => l.Id));
        Assert.Equal(order, layouts.OrderBy(l => l.Y).Select(l => l.Id));
    }
    [Fact]
    public void AutoAssignmentUsesOriginalIndexBeforeSortingOrRemoving()
    {
        var cards = Enumerable.Range(0, 13).Select(i => Card($"c{i}") with { ManualOrder = 12 - i, OrderSlot = (Slot)(i % 6) }).ToArray();
        var layouts = SlotLayout.Place(cards, Theme.Paper, Viewport);
        foreach (var slot in Enum.GetValues<Slot>())
        {
            var expected = cards.Where((_, i) => i % 6 == (int)slot).Reverse().Select(c => c.Id);
            Assert.Equal(expected, LayoutEditing.Stack(cards, slot).Select(c => c.Id));
            Assert.Equal(expected, layouts.Where(l => cards.First(c => c.Id == l.Id) is var c && SlotLayout.Resolve(c, Array.IndexOf(cards, c)) == slot).OrderBy(l => l.Y).Select(l => l.Id));
        }
        Assert.Equal(new[] { "c7" }, LayoutEditing.Stack(cards, Slot.Tr, "c1").Select(c => c.Id));
        var next = LayoutEditing.Apply(cards, LayoutEditing.Drop(cards, new Dictionary<string, LayoutOverride>(), "c12", Slot.Tr, 1));
        Assert.Equal(Slot.Bl, SlotLayout.Resolve(next[2], 2));
        Assert.Equal(Slot.Rc, SlotLayout.Resolve(next[11], 11));
    }
    [Theory]
    [InlineData("a", 2, "b,c,a")]
    [InlineData("c", 0, "c,a,b")]
    [InlineData("b", 2, "a,c,b")]
    [InlineData("b", 0, "b,a,c")]
    public void SameSlotDropRemovesBeforeInsertion(string dragged, int index, string expected)
    {
        CardSpec[] cards = [Card("a", Slot.Br), Card("b", Slot.Br), Card("c", Slot.Br)];
        var batch = LayoutEditing.Drop(cards, new Dictionary<string, LayoutOverride>(), dragged, Slot.Br, index);
        Assert.Equal(3, batch.Count);
        var next = LayoutEditing.Apply(cards, batch);
        Assert.Equal(expected.Split(','), LayoutEditing.Stack(next, Slot.Br).Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, batch.Values.Select(v => v.Order!.Value).Order());
        var placed = SlotLayout.Place(next, Theme.Paper, Viewport);
        Assert.Equal(expected.Split(','), placed.OrderBy(l => l.Y).Select(l => l.Id));
        Assert.Equal(Viewport.Height - 12, placed.Max(l => l.Y + l.H));
    }
    [Theory]
    [InlineData(Slot.Tl)]
    [InlineData(Slot.Tr)]
    [InlineData(Slot.Bl)]
    [InlineData(Slot.Br)]
    [InlineData(Slot.Lc)]
    [InlineData(Slot.Rc)]
    public void EmptySlotInsertionAndAnchoredPlacement(Slot slot)
    {
        var source = slot == Slot.Tl ? Slot.Tr : Slot.Tl;
        CardSpec[] cards = [Card("a", source)];
        var frozen = SlotLayout.Place(cards, Theme.Paper, Viewport);
        Assert.Equal(0, LayoutEditing.InsertionIndex(cards, frozen, "a", slot, -100));
        var batch = LayoutEditing.Drop(cards, new Dictionary<string, LayoutOverride>(), "a", slot, 99);
        Assert.Equal(new LayoutOverride { ManualSlot = slot, OrderSlot = slot, Order = 0 }, batch["a"]);
        var placed = Assert.Single(SlotLayout.Place(LayoutEditing.Apply(cards, batch), Theme.Paper, Viewport));
        Assert.Equal(slot is Slot.Lc or Slot.Rc ? (Viewport.Height - placed.H) / 2 : slot is Slot.Bl or Slot.Br ? Viewport.Height - 12 - placed.H : 36, placed.Y);
        Assert.Equal(slot is Slot.Tr or Slot.Br or Slot.Rc ? Viewport.Width - placed.W - 12 : 12, placed.X);
        foreach (var scale in new[] { 1d, 2 })
        {
            var zone = LayoutEditing.Zone(slot, Viewport, scale);
            Assert.Equal(slot, LayoutEditing.NearestSlot(new(zone.X + zone.Width / 2, zone.Y + zone.Height / 2), Viewport, scale));
        }
    }
    [Theory]
    [InlineData(Slot.Bl)]
    [InlineData(Slot.Lc)]
    public void FrozenMidpointsExcludeDraggedCardAndSortTopToBottom(Slot slot)
    {
        CardSpec[] cards = [Card("a", slot, 2), Card("b", slot, 0), Card("c", slot, 1)];
        var frozen = SlotLayout.Place(cards, Theme.Paper, Viewport, 2);
        var others = frozen.Where(l => l.Id != "c").OrderBy(l => l.Y).ToArray();
        Assert.Equal(0, LayoutEditing.InsertionIndex(cards, frozen, "c", slot, others[0].Y));
        Assert.Equal(1, LayoutEditing.InsertionIndex(cards, frozen, "c", slot, others[0].Y + others[0].H / 2));
        Assert.Equal(1, LayoutEditing.InsertionIndex(cards, frozen, "c", slot, others[1].Y + others[1].H / 2 - .01));
        Assert.Equal(2, LayoutEditing.InsertionIndex(cards, frozen, "c", slot, others[1].Y + others[1].H / 2));
    }
    [Fact]
    public void BatchNormalizesWiredAndAutomaticNeighboursWithoutPinningThem()
    {
        CardSpec[] cards = [Card("drag", Slot.Tl), Card("auto"), Card("wired") with { WiredSlot = Slot.Tr }, Card("manual", Slot.Tr)]; // auto at original index 1 = Tr
        var before = new Dictionary<string, LayoutOverride> { ["absent"] = new() { ManualSlot = Slot.Rc } };
        var batch = LayoutEditing.Drop(cards, before, "drag", Slot.Tr, 1);
        Assert.Equal(4, batch.Count); Assert.DoesNotContain("absent", batch.Keys);
        Assert.Null(batch["auto"].ManualSlot); Assert.Null(batch["wired"].ManualSlot);
        Assert.Equal(Slot.Tr, batch["manual"].ManualSlot);
        var all = Merge(before, batch); Assert.Contains("absent", all.Keys);
        var next = LayoutEditing.Apply(cards, all);
        Assert.Equal(new[] { "auto", "drag", "wired", "manual" }, LayoutEditing.Stack(next, Slot.Tr).Select(c => c.Id));
        var layouts = SlotLayout.Place(next, Theme.Paper, Viewport);
        Assert.True(layouts[2].Y > SlotLayout.Place(cards, Theme.Paper, Viewport)[2].Y);
        next[2] = next[2] with { WiredSlot = Slot.Br };
        Assert.Equal(Slot.Br, SlotLayout.Resolve(next[2], 2));
        Assert.Equal(Slot.Tr, next[2].OrderSlot); // its saved Tr rank cannot leak to Br
        Assert.Empty(LayoutEditing.Drop(next, all, "wired", Slot.Tl, 0));
    }
    [Fact]
    public void EffectiveNoOpsDoNotCreateOverridesAndResetRestoresOriginals()
    {
        CardSpec[] originals = [Card("a", Slot.Tl), Card("b", Slot.Tl)];
        Assert.Empty(LayoutEditing.Drop(originals, new Dictionary<string, LayoutOverride>(), "a", Slot.Tl, 0));
        var next = LayoutEditing.Apply(originals, LayoutEditing.Drop(originals, new Dictionary<string, LayoutOverride>(), "a", Slot.Br, 0));
        Assert.Equal(Slot.Br, next[0].ManualSlot); Assert.Equal(Slot.Tl, originals[0].ManualSlot);
        Assert.Equal(originals, LayoutEditing.Apply(originals, new Dictionary<string, LayoutOverride>()));
    }
    [Fact]
    public void NamedOverrideJsonRoundTripsAndCardStoreTracksLayoutChanges()
    {
        var overrides = new Dictionary<string, LayoutOverride> { ["a"] = new() { ManualSlot = Slot.Rc, OrderSlot = Slot.Rc, Order = 3 }, ["absent"] = new() { OrderSlot = Slot.Tr, Order = 0 } };
        var json = LayoutEditing.Serialize(overrides);
        var copy = LayoutEditing.Read(json, out var invalid); Assert.False(invalid);
        Assert.Equal(json, LayoutEditing.Serialize(copy)); Assert.Equal(overrides["a"], copy["a"]);
        CardSpec[] cards = [Card("a")]; var store = new CardStore(); store.Update(cards); var old = store.Snapshot;
        var changed = LayoutEditing.Apply(cards, copy);
        Assert.Single(store.Update(changed)); Assert.True(store.Snapshot[0].Rev > old[0].Rev);
        Assert.Empty(store.Update(changed));
        Assert.Equal(CardJson.Serialize(changed), CardJson.Serialize(CardJson.Deserialize<CardSpec[]>(CardJson.Serialize(changed))));
    }
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("{}", true)]
    [InlineData("{", true)]
    [InlineData("null", true)]
    [InlineData("[]", true)]
    [InlineData("{\"version\":2,\"cards\":{}}", true)]
    [InlineData("{\"version\":\"1\",\"cards\":{}}", true)]
    [InlineData("{\"version\":1,\"cards\":null}", true)]
    [InlineData("{\"version\":1,\"cards\":{}}", false)]
    public void MissingInvalidAndFuturePayloadsAreTolerated(string? json, bool expectedInvalid)
    { Assert.Empty(LayoutEditing.Read(json, out var invalid)); Assert.Equal(expectedInvalid, invalid); }
    [Theory]
    [InlineData("\"manualSlot\":99")]
    [InlineData("\"manualSlot\":\"99\"")]
    [InlineData("\"manualSlot\":\"invalid\"")]
    [InlineData("\"orderSlot\":-1,\"order\":0")]
    [InlineData("\"orderSlot\":\"tr\",\"order\":-1")]
    [InlineData("\"order\":1")]
    [InlineData("\"orderSlot\":\"tr\"")]
    [InlineData("\"orderSlot\":\"tr\",\"order\":1.5")]
    public void InvalidEntriesDoNotDiscardValidOrAbsentCards(string badFields)
    {
        var json = "{\"version\":1,\"cards\":{\"bad\":{" + badFields + "},\"absent\":{\"manualSlot\":\"lc\"},\" \":{},\"null\":null}}";
        var result = LayoutEditing.Read(json, out var invalid);
        Assert.True(invalid); Assert.Equal(Slot.Lc, Assert.Single(result).Value.ManualSlot);
    }
    [Fact]
    public void TitleGestureExcludesControlsAndUntitledBodyText()
    {
        var titled = Card("title"); Assert.True(CardMeasure.IsTitle(titled, Theme.Paper, 100, 31)); Assert.False(CardMeasure.IsTitle(titled, Theme.Paper, 100, 32));
        var text = new CardSpec { Id = "text", Kind = CardKind.Text, Text = "Starts at 12 pixels" };
        Assert.False(CardMeasure.IsTitle(text, Theme.Paper, 12, 12)); Assert.False(CardMeasure.IsTitle(text, Theme.Paper, 100, 16));
        var grip = CardMeasure.Grip(text); Assert.True(CardMeasure.IsTitle(text, Theme.Paper, grip.X + grip.Width / 2, grip.Y + grip.Height / 2));
        var bar = StructuralDemo.Create()[4] with { OwnerHasSelection = true };
        var clear = CardMeasure.Clear(bar, Theme.Paper)!;
        Assert.False(CardMeasure.IsTitle(bar, Theme.Paper, clear.X + 5, clear.Y + 5));
        Assert.False(CardMeasure.IsTitle(bar, Theme.Paper, 30, 45));
    }
    [Fact]
    public void NearestZoneTiesAreDeterministic()
    {
        var left = LayoutEditing.Zone(Slot.Tl, Viewport); var right = LayoutEditing.Zone(Slot.Tr, Viewport);
        Assert.Equal(Slot.Tl, LayoutEditing.NearestSlot(new((left.X + left.Width / 2 + right.X + right.Width / 2) / 2, left.Y + left.Height / 2), Viewport));
    }
}
