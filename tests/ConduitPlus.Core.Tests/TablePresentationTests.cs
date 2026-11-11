using System.Drawing.Imaging;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class TablePresentationTests
{
    private static CardSpec Fixture() => new()
    {
        Id = "columns", Kind = CardKind.Table, Title = "Room schedule", Width = 440,
        Headers = ["Room", "Area m²", "Change", "Note"], Decimals = 2,
        Rows = TableInputs.Create(["Room", "Area m²", "Change", "Note"], null,
            [["Living / dining", "Office", "Bedroom 01", "Store"], ["44.125", "10.1", "18.678", "2"],
             ["-1.23", "1.234", "-0.0001", "12.3"], ["Main living space", "Work", "Quiet", ""]]).Rows,
        SelectedKeys = ["Office"], SortColumn = 1
    };

    [Fact]
    public void WidthsFitAllRowsAndHeaderHitsUseTheSameStableGeometry()
    {
        var card = Fixture() with { MaxRows = 1 };
        var columns = TablePresentation.Columns(card, Theme.Paper);
        Assert.True(columns[0].Width > columns[2].Width);
        Assert.Equal(card.Width - 2 * Theme.Paper.Padding, columns.Sum(c => c.Width), 6);
        var headers = TableSort.Headers(card, Theme.Paper);
        for (var i = 0; i < columns.Length; i++)
        {
            Assert.Equal(columns[i].X, headers[i].X);
            Assert.Equal(columns[i].Width, headers[i].W);
        }
        Assert.Equal(columns, TablePresentation.Columns(card with { ScrollOffset = 2, SortColumn = 0, SortDescending = true }, Theme.Paper));
        var longer = card with { Rows = [.. card.Rows, new("extra", [new string('W', 50), "1", "2", "x"])] };
        Assert.True(TablePresentation.Columns(longer, Theme.Paper)[0].Width > columns[0].Width);
    }

    [Fact]
    public void OnlyWhollyFiniteNumericColumnsAlignRightIncludingOffscreenValues()
    {
        var card = Fixture();
        Assert.Equal(new[] { false, true, true, false }, TablePresentation.Columns(card, Theme.Paper).Select(c => c.Numeric));
        var mixed = card with { MaxRows = 1, Rows = [new("a", ["1", "2", "3", "4"]), new("b", ["", null, "NaN", "Infinity"])] };
        Assert.All(TablePresentation.Columns(mixed, Theme.Paper), col => Assert.False(col.Numeric));
        Assert.All(TablePresentation.Columns(card with { Rows = [] }, Theme.Paper), col => Assert.False(col.Numeric));
    }

    [Theory]
    [InlineData(180)]
    [InlineData(440)]
    [InlineData(1200)]
    public void PaintedCellsStayWithinTheirColumnsAndNumericRightEdgesCoincide(double width)
    {
        var card = Fixture() with { Width = width };
        var columns = TablePresentation.Columns(card, Theme.Paper);
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        var cells = canvas.Texts.Where(t => t.Point.Y > CardMeasure.BodyTop + Theme.Paper.RowHeight).ToArray();
        Assert.Equal(card.Rows.Length * columns.Length, cells.Length);
        for (var i = 0; i < cells.Length; i++)
        {
            var col = columns[i % columns.Length]; var text = cells[i];
            var end = text.Point.X + canvas.MeasureText(text.Text, Theme.Paper.FontSize, Theme.Paper.Font).Width;
            Assert.InRange(text.Point.X, col.X, col.X + col.Width);
            Assert.InRange(end, col.X, col.X + col.Width + 0.001);
            if (col.Numeric) Assert.Equal(col.X + col.Width - 6, end, 6);
            else Assert.Equal(col.X + 6, text.Point.X, 6);
        }
        var many = card with { Headers = Enumerable.Repeat("", 40).ToArray(), Rows = [] };
        Assert.All(TablePresentation.Columns(many, Theme.Paper), col => Assert.True(double.IsFinite(col.Width) && col.Width >= 0));
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("Night")]
    public void RenderTableCardsForVisualInspection(string name)
    {
        Theme.TryGet(name, out var theme);
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "pr-c"); Directory.CreateDirectory(output);
        foreach (var (label, card) in new[] { ("columns", Fixture()), ("narrow", Fixture() with { Width = 260 }),
            ("structural", StructuralDemo.Create().Single(c => c.Kind == CardKind.Table) with { SortColumn = 1 }) })
        {
            using var bitmap = RasterDrawing.Render(card, theme, 2);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
            bitmap.Save(Path.Combine(output, $"table-{label}-{name.ToLowerInvariant()}.png"), ImageFormat.Png);
        }
    }

    private sealed class Commands : ICanvas2D
    {
        public List<(string Text, Point2 Point)> Texts { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Measurer(text, size, font);
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft));
        public void Rect(Rect2 rect, string fill) { }
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
