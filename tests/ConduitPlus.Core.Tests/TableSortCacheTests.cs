using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class TableSortCacheTests
{
    [Fact]
    public void ReusesStableOrderByImmutableRowsColumnAndDirection()
    {
        var store = new CardStore();
        store.Update([new CardSpec { Id = "sort", Kind = CardKind.Table, Headers = ["Value", "Name"], SortColumn = 0,
            Rows = [new("ten", ["10", "b"]), new("two", ["2", "a"]), new("same", ["2", "b"]), new("blank", [null, "c"])] }]);
        var card = store.Snapshot[0];
        var original = card.Rows.ToArray();
        var sorted = TableSort.Rows(card);
        Assert.Equal(new[] { "two", "same", "ten", "blank" }, sorted.Select(r => r.Key));
        Assert.Same(sorted, TableSort.Rows(card with { Rev = 100, HoverKey = "ten", ScrollOffset = 2, SelectedKeys = ["two"] }));
        var descending = TableSort.Rows(card with { SortDescending = true });
        Assert.Equal(new[] { "ten", "two", "same", "blank" }, descending.Select(r => r.Key));
        Assert.Same(descending, TableSort.Rows(card with { SortDescending = true }));
        Assert.Same(sorted, TableSort.Rows(card));
        Assert.Equal(new[] { "two", "ten", "same", "blank" }, TableSort.Rows(card with { SortColumn = 1 }).Select(r => r.Key));
        Assert.Equal(original, card.Rows);
        Assert.Same(card.Rows, TableSort.Rows(card with { SortColumn = null }));
        Assert.Same(card.Rows, TableSort.Rows(card with { SortColumn = 99 }));
        var replacement = card with { Rows = [new("new", ["1", "d"]), .. card.Rows] }; // caller-owned: sorted fresh
        Assert.Equal("new", TableSort.Rows(replacement)[0].Key);
        Assert.Same(sorted, TableSort.Rows(card));
        Assert.Equal("two", BrushPresentation.FirstNewRow(card, new HashSet<string>(), new HashSet<string> { "two", "ten" }));
        Assert.Same(sorted, TableSort.Rows(card));
    }

    [Fact]
    public void CallerOwnedRowsAreNeverServedFromCache()
    {
        var rows = new TableRow[] { new("a", ["1"]), new("b", ["2"]), new("c", ["3"]) };
        var card = new CardSpec { Id = "t", Kind = CardKind.Table, Headers = ["V"], Rows = rows, SortColumn = 0 };
        Assert.Equal(new[] { "a", "b", "c" }, TableSort.Rows(card).Select(r => r.Key));
        rows[2].Cells[0] = "0"; // nested cell mutation
        Assert.Equal(new[] { "c", "a", "b" }, TableSort.Rows(card).Select(r => r.Key));
        rows[0] = new("z", ["-1"]); // row replacement
        Assert.Equal(new[] { "z", "c", "b" }, TableSort.Rows(card).Select(r => r.Key));
        Assert.Equal(0, TableScroll.Reveal(card with { MaxRows = 1 }, "z"));
    }
}
