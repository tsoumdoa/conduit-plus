using System.Globalization;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class TableInputTests
{
    [Fact]
    public void SquareColumnsTransposeExplicitlyAndRowsKeepTheirOrientation()
    {
        string?[][] branches = [["R01", "R02"], ["10", "20"]];
        var columns = TableInputs.Create(["Room", "Area"], null, branches);
        Assert.Equal(new[] { "R01", "10" }, columns.Rows[0].Cells);
        Assert.Equal(new[] { "R02", "20" }, columns.Rows[1].Cells);
        Assert.Equal(new[] { "R01", "R02" }, columns.Rows.Select(r => r.Key));
        var rows = TableInputs.Create(["Room", "Area"], branches, null);
        Assert.Equal(branches[0], rows.Rows[0].Cells);
        Assert.Equal(new[] { "R01", "10" }, rows.Rows.Select(r => r.Key));
        Assert.Null(rows.Warning);
        Assert.Equal("R01", branches[0][0]);
    }

    [Fact]
    public void EmptyBranchesCountAsSuppliedAndEmptyTablesAreValid()
    {
        Assert.Empty(TableInputs.Create(["A", "B"], null, [[], []]).Rows);
        Assert.Empty(TableInputs.Create(["A"], [], null).Rows);
        Assert.Equal("Set Rows or Columns.", TableInputs.Create(["A"], null, null).Warning);
        Assert.Equal("Use Rows or Columns, not both.", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A"], [], [])).Message);
        Assert.Equal("Columns has 0 branches but there are 1 Headers.", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A"], null, [])).Message);
    }

    [Fact]
    public void RectangularColumnsTransposeWithoutLosingNullOrEmptyCells()
    {
        var result = TableInputs.Create(["Room", "Value", "Note"], null, [["a", "b"], ["1", null], ["", "text"]]);
        Assert.Equal(2, result.Rows.Length);
        Assert.Equal(new[] { "a", "1", "" }, result.Rows[0].Cells);
        Assert.Equal(new[] { "b", null, "text" }, result.Rows[1].Cells);
    }

    [Fact]
    public void ShapeErrorsIncludeCountsAndNeverPad()
    {
        Assert.Equal("Columns has 3 branches but there are 4 Headers.", Assert.Throws<ArgumentException>(() =>
            TableInputs.Create(["A", "B", "C", "D"], null, [[], [], []])).Message);
        var ragged = Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B"], null, [["1"], ["2", "3"]])).Message;
        Assert.Contains("branch 0: 1, branch 1: 2", ragged);
        Assert.Contains("branch 0: 0, branch 1: 2", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B"], [[], ["2", "3"]], null)).Message);
        Assert.Equal("Key count must match row count.", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A"], [["a"]], null, [])).Message);
    }

    [Theory]
    [InlineData("Office", "Office", "First column repeats ('Office' ×2), so rows are keyed 1…n.")]
    [InlineData("", "b", null)]
    [InlineData("  ", "b", null)]
    [InlineData(null, "b", null)]
    [InlineData("\u0001sort:0", "b", null)]
    public void InvalidDefaultKeysFallBackWithWarning(string? a, string? b, string? warning)
    {
        var result = TableInputs.Create(["Room"], [[a], [b]], null);
        Assert.Equal(new[] { "1", "2" }, result.Rows.Select(r => r.Key));
        Assert.NotNull(result.Warning);
        if (warning is not null) Assert.Equal(warning, result.Warning);
    }

    [Fact]
    public void KeysAreRawOrdinalStringsAndExplicitKeysOverrideDefaults()
    {
        var result = TableInputs.Create(["Key"], [["Office"], ["office"], [" 1.234 "]], null);
        Assert.Equal(new[] { "Office", "office", " 1.234 " }, result.Rows.Select(r => r.Key));
        Assert.Null(result.Warning);
        var explicitKeys = TableInputs.Create(["Key"], [["Office"], ["Office"]], null, ["a", "b"]);
        Assert.Equal(new[] { "a", "b" }, explicitKeys.Rows.Select(r => r.Key));
        Assert.Null(explicitKeys.Warning);
    }

    [Theory]
    [InlineData("-12.345", 2, "−12.35")]
    [InlineData("-0.0001", 2, "0")]
    [InlineData("-0.005", 2, "−0.01")]
    [InlineData("-0.0006", 3, "−0.001")]
    [InlineData("-0", 12, "0")]
    [InlineData("0", 2, "0.00")]
    [InlineData("0.0001", 2, "0.00")]
    [InlineData("1.23", 0, "1")]
    [InlineData("1.23", 12, "1.230000000000")]
    [InlineData("1,000", 1, "1000.0")]
    [InlineData("2e3", 2, "2000.00")]
    [InlineData("NaN", 2, "NaN")]
    [InlineData("Infinity", 2, "Infinity")]
    [InlineData("Office", 2, "Office")]
    public void FormattingIsInvariantDisplayOnly(string raw, int decimals, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal(expected, TablePresentation.Format(raw, decimals)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void UnsetPrecisionPreservesTextAndRawValuesStillSortWhenDisplayRoundsToATie()
    {
        Assert.Equal(" -1.2300 ", TablePresentation.Format(" -1.2300 ", null));
        Assert.Equal("—", TablePresentation.Format(null, null));
        Assert.Throws<ArgumentException>(() => TablePresentation.Format("x", -1));
        Assert.Throws<ArgumentException>(() => TablePresentation.Format("1", 13));
        var rows = TableInputs.Create(["Value"], [["1.234"], ["1.231"]], null).Rows;
        var card = new CardSpec { Id = "table", Kind = CardKind.Table, Headers = ["Value"], Rows = rows, Decimals = 2, SortColumn = 0 };
        Assert.All(rows, r => Assert.Equal("1.23", TablePresentation.Format(r.Cells[0], card.Decimals)));
        Assert.Equal(new[] { "1.231", "1.234" }, TableSort.Rows(card).Select(r => r.Key));
        Assert.Equal("1.234", rows[0].Cells[0]);
        Assert.Throws<ArgumentException>(() => (card with { Decimals = 13 }).Validate());
    }
}
