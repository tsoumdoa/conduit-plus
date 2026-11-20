using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CardStoreTests
{
    [Fact]
    public void OwnedTableDataSurvivesEveryInteractionAndThemeRevision()
    {
        var input = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table);
        var store = new CardStore(); store.Update([input]);
        var original = store.Snapshot[0];
        foreach (var change in new Func<CardSpec, CardSpec>[]
        {
            c => c with { HoverKey = c.Rows[0].Key },
            c => c with { SelectedKeys = [c.Rows[1].Key], OwnerHasSelection = true },
            c => c with { ScrollOffset = 1 },
            c => c with { SortColumn = 1 },
            c => c with { SortDescending = true },
            c => c with { Form = new(VisibleRows: 3) },
            c => c with { Width = 500, Decimals = 2 }
        })
        {
            var before = store.Snapshot[0];
            var next = Assert.Single(store.Update([change(before)]));
            Assert.True(next.Rev > before.Rev);
            Assert.Same(original.Rows, next.Rows);
            Assert.Same(original.Rows[0].Cells, next.Rows[0].Cells);
            Assert.Same(original.Headers, next.Headers);
            Assert.Empty(store.Update([next with { Rev = -10 }]));
            Assert.Same(next, store.Snapshot[0]);
            // Equal fresh caller data follows the same content/revision semantics.
            Assert.Empty(store.Update([CardJson.Deserialize<CardSpec>(CardJson.Serialize(next))]));
        }
        var themed = Assert.Single(store.Update(store.Snapshot, Theme.Smoke));
        Assert.Same(original.Rows, themed.Rows);
        Assert.Empty(store.Update(store.Snapshot, Theme.Smoke));
        Assert.NotEqual(original.Rev, themed.Rev);
    }

    [Fact]
    public void CallerTableMutationAfterOwnedInteractionStillChangesRevisionAndSortOrder()
    {
        var input = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table) with { SortColumn = 0 };
        var store = new CardStore(); store.Update([input]);
        var first = store.Snapshot[0]; var order = TableSort.Rows(first);
        store.Update([first with { HoverKey = first.Rows[0].Key }]);
        input.Rows[^1].Cells[0] = "AAA";
        var changed = Assert.Single(store.Update([input]));
        Assert.NotSame(first.Rows, changed.Rows);
        Assert.NotSame(input.Rows, changed.Rows);
        Assert.NotSame(input.Rows[^1].Cells, changed.Rows[^1].Cells);
        Assert.Equal(input.Rows[^1].Key, TableSort.Rows(changed)[0].Key);
        Assert.Equal(first.Rows[0].Key, order[0].Key);
        input.Headers[0] = "Changed";
        Assert.Single(store.Update([input]));
        Assert.Equal("Member", first.Headers[0]);
        input.Rows[0] = input.Rows[0] with { Key = "replacement" };
        Assert.Single(store.Update([input]));
        Assert.NotEqual("replacement", first.Rows[0].Key);
    }

    [Fact]
    public void BarBulkArraysAreOwnedReusedAndComparedByValue()
    {
        var input = StructuralDemo.Create().Single(c => c.Kind == CardKind.Bar);
        var store = new CardStore(); store.Update([input]);
        var first = store.Snapshot[0];
        var selected = new[] { first.Keys[0] };
        var hover = Assert.Single(store.Update([first with { HoverKey = first.Keys[0], SelectedKeys = selected }]));
        Assert.Same(first.Values, hover.Values); Assert.Same(first.Labels, hover.Labels); Assert.Same(first.Keys, hover.Keys);
        selected[0] = "external mutation";
        Assert.Equal(first.Keys[0], hover.SelectedKeys[0]);
        Assert.Empty(store.Update([hover]));
        input.Values[^1] = 123;
        input.Labels[^1] = "Changed label";
        input.Keys[^1] = "changed-key";
        var changed = Assert.Single(store.Update([input]));
        Assert.Equal(123, changed.Values[^1]); Assert.Equal("Changed label", changed.Labels[^1]);
        Assert.Equal("changed-key", changed.Keys[^1]);
        Assert.NotEqual(123, first.Values[^1]); Assert.NotEqual("Changed label", first.Labels[^1]);
        Assert.NotSame(input.Values, changed.Values); Assert.NotSame(input.Keys, changed.Keys);
        Assert.Empty(store.Update([input]));
    }

    [Fact]
    public void NumericArrayComparisonPreservesSerializedSignedZeroChanges()
    {
        var input = new CardSpec { Id = "bar", Kind = CardKind.Bar, Values = [0], Labels = ["Zero"], Keys = ["zero"] };
        var store = new CardStore(); store.Update([input]);
        var first = store.Snapshot[0];
        input.Values[0] = -0d;
        var changed = Assert.Single(store.Update([input]));
        Assert.NotEqual(first.Rev, changed.Rev);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0d), BitConverter.DoubleToInt64Bits(changed.Values[0]!.Value));
        Assert.Empty(store.Update([input]));
    }

    [Fact]
    public void OwnedArraysDoNotSkipValidationOfChangedMetadata()
    {
        var store = new CardStore(); store.Update([StructuralDemo.Create().Single(c => c.Kind == CardKind.Table)]);
        var owned = store.Snapshot[0];
        Assert.Throws<ArgumentException>(() => store.Update([owned with { Headers = ["wrong shape"] }]));
        Assert.Throws<ArgumentException>(() => store.Update([owned with { Width = 0 }]));
        Assert.Throws<ArgumentException>(() => store.Update([owned with { Decimals = 20 }]));
        Assert.Same(owned, store.Snapshot[0]);
    }
}
