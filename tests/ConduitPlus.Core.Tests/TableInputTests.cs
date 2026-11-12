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
        Assert.StartsWith("Columns has 0 branches but there is 1 Header.", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A"], null, [])).Message);
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
        Assert.StartsWith("Columns has 3 branches but there are 4 Headers.", Assert.Throws<ArgumentException>(() =>
            TableInputs.Create(["A", "B", "C", "D"], null, [[], [], []])).Message);
        var ragged = Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B"], null, [["1"], ["2", "3"]])).Message;
        Assert.Contains("'A' has 1 item, 'B' has 2 items", ragged);
        Assert.Contains("row 0 has 0", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B"], [[], ["2", "3"]], null)).Message);
        Assert.Equal("Key count must match row count.", Assert.Throws<ArgumentException>(() => TableInputs.Create(["A"], [["a"]], null, [])).Message);
    }

    [Fact]
    public void ShapeErrorsAreBoundedAndSuggestTheCorrectTreeOrientation()
    {
        var message = Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B", "C", "D"],
            Enumerable.Range(0, 300).Select(_ => new string?[] { "a", "b", "c" }).ToArray(), null)).Message;
        Assert.Contains("300 of 300 rows don't", message);
        Assert.Contains("row 2 has 3", message);
        Assert.DoesNotContain("row 3 has", message);
        Assert.True(message.Length < 250);
        Assert.Equal("Columns has 1 branch but there are 4 Headers. Put each column in its own branch (e.g. Entwine the column lists).",
            Assert.Throws<ArgumentException>(() => TableInputs.Create(["A", "B", "C", "D"], null, [["1"]])).Message);
        Assert.Contains("wire the tree into Columns", Assert.Throws<ArgumentException>(() =>
            TableInputs.Create(["Room", "DA", "UDI"], [["a", "b"], ["1", "2"], ["3", "4"]], null)).Message);
        var unequal = Assert.Throws<ArgumentException>(() => TableInputs.Create(["Room", "DA", "UDI", "Area"], null,
            [["a", "b"], ["1"], ["1", "2"], []])).Message;
        Assert.Contains("'DA' has 1 item", unequal);
        Assert.Contains("'Area' has 0 items", unequal);
        Assert.DoesNotContain("'UDI'", unequal);
        Assert.Contains("First column has empty cells", TableInputs.Create(["Room"], [[""]], null).Warning);
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
    [InlineData("-0.0001", 2, "0.00")]
    [InlineData("-0.005", 2, "−0.01")]
    [InlineData("-0.0006", 3, "−0.001")]
    [InlineData("-0", 12, "0.000000000000")]
    [InlineData("0", 2, "0.00")]
    [InlineData("0.0001", 2, "0.00")]
    [InlineData("1.23", 0, "1")]
    [InlineData("1.23", 12, "1.230000000000")]
    [InlineData("1,000", 1, "1,000")]
    [InlineData("1,23", 2, "1,23")]
    [InlineData("1,234", 2, "1,234")]
    [InlineData("1,234.56", 2, "1,234.56")]
    [InlineData("1.234,56", 2, "1.234,56")]
    [InlineData("1\u202f234,56", 2, "1\u202f234,56")]
    [InlineData("12%", 2, "12%")]
    [InlineData("3 m²", 2, "3 m²")]
    [InlineData("−4.0", 2, "−4.00")]
    [InlineData("", 2, "")]
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

    [Theory]
    [InlineData("1,23", false)]
    [InlineData("1,234", false)]
    [InlineData("12%", false)]
    [InlineData("3 m²", false)]
    [InlineData("−4.0", true)]
    [InlineData("", false)]
    [InlineData("NaN", false)]
    [InlineData(" 1.2 ", false)]
    [InlineData("1.2\n", false)]
    [InlineData(".5", false)]
    [InlineData("1.", false)]
    [InlineData("+12.3e−2", true)]
    [InlineData("1e999", false)]
    public void NumericGrammarAcceptsOnlySignedInvariantFiniteNumbers(string raw, bool numeric)
    {
        Assert.Equal(numeric, TablePresentation.TryNumber(raw, out _));
        var card = new CardSpec { Id = "grammar", Kind = CardKind.Table, Headers = ["Value"], Rows = [new("r", [raw])] };
        Assert.Equal(numeric, TablePresentation.Columns(card, Theme.Paper)[0].Numeric);
    }

    [Theory]
    [InlineData(0, "0.01")]
    [InlineData(2, "0.0001")]
    [InlineData(12, "0.0000000000001")]
    public void RoundedZeroRetainsPrecisionForBothSignsAndExactZero(int decimals, string small)
    {
        var expected = decimals == 0 ? "0" : "0." + new string('0', decimals);
        foreach (var raw in new[] { small, "-" + small, "−" + small, "0", "-0", "−0" })
            Assert.Equal(expected, TablePresentation.Format(raw, decimals));
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
