using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class TableSortCacheTests
{
    [Fact]
    public void ReusesStableOrderByImmutableRowsColumnAndDirection()
    {
        var card = new CardSpec { Id = "sort", Kind = CardKind.Table, Headers = ["Value", "Name"], SortColumn = 0,
            Rows = [new("ten", ["10", "b"]), new("two", ["2", "a"]), new("same", ["2", "b"]), new("blank", [null, "c"])] };
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
        var replacement = card with { Rows = [new("new", ["1", "d"]), .. card.Rows] };
        Assert.Equal("new", TableSort.Rows(replacement)[0].Key);
        Assert.Same(sorted, TableSort.Rows(card));
        Assert.Equal("two", BrushPresentation.FirstNewRow(card, new HashSet<string>(), new HashSet<string> { "two", "ten" }));
        Assert.Same(sorted, TableSort.Rows(card));
    }
}
