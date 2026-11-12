using System.Drawing.Imaging;
using System.Globalization;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

[CollectionDefinition("Table text metrics", DisableParallelization = true)]
public sealed class TableTextMetricsCollection;

[Collection("Table text metrics")]
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

    [Fact]
    public void TwoThousandRowsReuseMeasurementsAcrossPaintHitsAndInteraction()
    {
        var previous = TextMetrics.Measurer;
        GdiCanvas.RegisterTextMetrics();
        var gdi = TextMetrics.Measurer;
        var canvas = new Commands { Measurer = gdi };
        TextMetrics.Measurer = canvas.MeasureText;
        try
        {
            var card = Fixture() with
            {
                SelectedKeys = [], SortColumn = null,
                Rows = Enumerable.Range(0, 2000).Select(i => new TableRow($"r{i}",
                    [$"Room {i}", (i * 1.234).ToString(CultureInfo.InvariantCulture), "-0.01234", "Ordinary room schedule note"])).ToArray()
            };
            var state = new CanvasCardState();
            state.Replace(card);
            card = state.Spec!;
            CardPainter.Paint(canvas, card, Theme.Paper);
            // Warm both viewports' fitted strings, then require every real interaction repaint
            // below to make zero measurements (including scroll/sort back into known content).
            CardPainter.Paint(canvas, card with { ScrollOffset = 1000 }, Theme.Paper);
            var first = canvas.MeasureCalls;
            Assert.InRange(first, 1, 800); // Headers + bounded width sample + visible-cell fitting, not 8,004 measurements.
            CardPainter.Paint(canvas, card, Theme.Paper);
            Assert.Equal(first, canvas.MeasureCalls);
            var headers = TableSort.Headers(card, Theme.Paper);
            foreach (var interact in new Action[] { () => state.Hover("r1"), () => state.Hover("r2"),
                () => state.Activate("r2"), () => state.Scroll(1000), () => state.Activate(TableSort.Key(1)) })
            {
                interact();
                Assert.Same(card.Rows, state.Spec!.Rows);
                Assert.Same(card.Headers, state.Spec.Headers);
                Assert.Same(headers, TableSort.Headers(state.Spec, Theme.Paper));
                Assert.Equal(first, canvas.MeasureCalls); // Actual CardStore snapshots, not a record-copy shortcut.
                CardPainter.Paint(canvas, state.Spec, Theme.Paper);
                Assert.Equal(first, canvas.MeasureCalls);
            }
            state.Scroll(1500); // A cold viewport may fit newly visible strings, but never resample widths.
            var interacting = state.Spec!;
            for (var i = 0; i < 10; i++) CardMeasure.Hits(interacting, Theme.Paper);
            Assert.Equal(first, canvas.MeasureCalls);
            CardPainter.Paint(canvas, interacting, Theme.Paper);
            var scrolled = canvas.MeasureCalls;
            Assert.InRange(scrolled - first, 0, 160); // Only newly visible strings may need fitting.
            CardPainter.Paint(canvas, interacting, Theme.Paper);
            Assert.Equal(scrolled, canvas.MeasureCalls);

            var oldColumns = TablePresentation.Columns(card, Theme.Paper);
            var changed = card with { Rev = 100, Rows = [.. card.Rows[..^1], new("last", [new string('W', 200), "text", "2", "x"])] };
            var newColumns = TablePresentation.Columns(changed, Theme.Paper);
            Assert.True(newColumns[0].Width > oldColumns[0].Width);
            Assert.False(newColumns[1].Numeric); // Last/offscreen row still determines alignment.
            Assert.True(canvas.MeasureCalls > scrolled);
            var count = canvas.MeasureCalls;
            TableSort.Headers(changed with { Width = 600 }, Theme.Paper with { Padding = 16 });
            Assert.Equal(count, canvas.MeasureCalls); // Geometry changes reuse content measurements.
            TableSort.Headers(card, Theme.Night);
            Assert.Equal(count, canvas.MeasureCalls); // Palette changes do not change font metrics.
            TableSort.Headers(card with { Decimals = 4 }, Theme.Paper);
            Assert.True(canvas.MeasureCalls > count);
            count = canvas.MeasureCalls;
            TableSort.Headers(card, Theme.Paper with { FontSize = 18 });
            Assert.True(canvas.MeasureCalls > count);
            count = canvas.MeasureCalls;
            TableSort.Headers(card, Theme.Paper with { Font = "Arial" });
            Assert.True(canvas.MeasureCalls > count);
            count = canvas.MeasureCalls;
            TableSort.Headers(card with { Headers = ["Space", "Area", "Delta", "Comment"] }, Theme.Paper);
            Assert.True(canvas.MeasureCalls > count);
            count = canvas.MeasureCalls;
            TextMetrics.Measurer = (text, size, font) => canvas.MeasureText(text, size, font);
            TableSort.Headers(card, Theme.Paper);
            Assert.True(canvas.MeasureCalls > count); // A host replacing its metrics provider invalidates too.
        }
        finally { TextMetrics.Measurer = previous; }
    }

    [Fact]
    public void StoreStillOwnsCallerArraysAndInvalidatesMutatedOffscreenData()
    {
        var input = Fixture();
        var store = new CardStore(); store.Update([input]);
        var owned = store.Snapshot[0];
        Assert.NotSame(input.Rows, owned.Rows);
        Assert.NotSame(input.Headers, owned.Headers);
        var before = TablePresentation.Columns(owned, Theme.Paper);
        input.Rows[^1].Cells[1] = "not numeric";
        Assert.Equal("Room", owned.Headers[0]);
        Assert.Equal("2", owned.Rows[^1].Cells[1]);
        store.Update([input]);
        Assert.NotSame(owned.Rows, store.Snapshot[0].Rows);
        Assert.True(before[1].Numeric);
        Assert.False(TablePresentation.Columns(store.Snapshot[0], Theme.Paper)[1].Numeric);
        input.Headers[0] = "Space";
        store.Update([input]);
        Assert.Equal("Space", store.Snapshot[0].Headers[0]);
    }

    [Fact]
    public void WidthsIncludeLongestDisplayTextOutsideTheSample()
    {
        var card = new CardSpec { Id = "outlier", Kind = CardKind.Table, Width = 600, Decimals = 2,
            Headers = ["Room", "Value"], Rows = Enumerable.Range(0, 2000)
                .Select(i => new TableRow($"r{i}", ["Room", i == 1 ? "1234567.89" : "1.23"])).ToArray() };
        var columns = TablePresentation.Columns(card, Theme.Paper);
        var ordinary = card with { Rows = card.Rows.Select(r => new TableRow(r.Key, ["Room", "1.23"])).ToArray() };
        Assert.True(columns[1].Width > TablePresentation.Columns(ordinary, Theme.Paper)[1].Width);
        Assert.True(columns[1].Width >= TextMetrics.Measurer("1234567.89", Theme.Paper.FontSize, Theme.Paper.Font).Width + 12);
    }

    [Theory]
    [InlineData("1.123", "2.123", "1", "2")]
    [InlineData("1.123", "1.223", "1.1", "1.2")]
    [InlineData("1.123", "1.133", "1.12", "1.13")]
    [InlineData("1.123", "1.124", "1.12", "1.12")]
    [InlineData("1.20", "2.30", "1.20", "2.30")]
    [InlineData("1.123", "text", "1.123", "text")]
    public void UnsetPrecisionAutomaticallyFormatsOnlyHighPrecisionNumericColumns(string a, string b, string expectedA, string expectedB)
    {
        var card = new CardSpec { Id = "auto", Kind = CardKind.Table, Width = 600,
            Headers = ["Analysis", "Text"], Rows = [new("a", [a, "48.74153519689177 m²"]), new("b", [b, "1,234.5678"])] };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        var cells = canvas.Texts.Where(t => t.Point.Y > CardMeasure.BodyTop + Theme.Paper.RowHeight).Select(t => t.Text).ToArray();
        Assert.Equal(new[] { expectedA, "48.74153519689177 m²", expectedB, "1,234.5678" }, cells);
        Assert.Equal(a, card.Rows[0].Cells[0]);
    }

    [Fact]
    public void LongCellsUseLogarithmicFittingAndReuseTheFittedText()
    {
        var previous = TextMetrics.Measurer;
        var canvas = new Commands { Estimate = true };
        TextMetrics.Measurer = canvas.MeasureText;
        try
        {
            var card = new CardSpec
            {
                Id = "long", Kind = CardKind.Table, Width = 380, Headers = ["Room", "Note"],
                Rows = Enumerable.Range(0, 8).Select(i => new TableRow($"r{i}", [$"Room {i}", string.Concat(Enumerable.Repeat("😀e\u0301", 1250))])).ToArray()
            };
            CardPainter.Paint(canvas, card, Theme.Paper);
            Assert.InRange(canvas.MeasureCalls, 1, 65); // 5,000 UTF-16 units; one binary search shared by eight cells.
            var first = canvas.MeasureCalls;
            CardPainter.Paint(canvas, card, Theme.Paper);
            Assert.Equal(first, canvas.MeasureCalls);
            var fitted = canvas.Texts.Where(t => t.Point.Y > CardMeasure.BodyTop + Theme.Paper.RowHeight).ToArray()[1].Text;
            Assert.EndsWith("…", fitted);
            // Emoji and the accented letter are separate text elements; either is a valid endpoint.
            Assert.Contains(fitted.Length - 1, StringInfo.ParseCombiningCharacters(card.Rows[0].Cells[1]!));
        }
        finally { TextMetrics.Measurer = previous; }
    }

    [Theory]
    [InlineData(180, false)]
    [InlineData(180, true)]
    [InlineData(440, false)]
    [InlineData(440, true)]
    public void NumericHeaderAlignmentIsStableAndSortGlyphUsesThePadding(double width, bool sorted)
    {
        var card = Fixture() with { Width = width, SortColumn = sorted ? 1 : null };
        var columns = TablePresentation.Columns(card, Theme.Paper);
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        var header = canvas.Texts.Where(t => t.Point.Y == CardMeasure.BodyTop + 2).ToArray()[1];
        var end = header.Point.X + canvas.MeasureText(header.Text, Theme.Paper.FontSize, Theme.Paper.Font).Width;
        Assert.Equal(columns[1].X + columns[1].Width - 6, end, 6);
        Assert.InRange(header.Point.X, columns[1].X, columns[1].X + columns[1].Width);
        if (sorted)
        {
            var glyph = Assert.Single(canvas.Polygons);
            Assert.Equal(end + 1, glyph.Min(p => p.X), 6);
            Assert.True(glyph.Max(p => p.X) < columns[1].X + columns[1].Width);
        }
        else Assert.Empty(canvas.Polygons);
        Assert.Equal(TableSort.Headers(card, Theme.Paper), TableSort.Headers(TableSort.Cycle(card, 1), Theme.Paper));
    }

    private static CardSpec SixColumns() => new()
    {
        Id = "six", Kind = CardKind.Table, Title = "Room analysis", Width = 380, Decimals = 1,
        Headers = ["Room", "Level", "Area m²", "DA %", "UDI %", "Glare"],
        Rows = [new("R01", ["R01", "1", "25.5", "48.7", "61.4", "0.3"]),
            new("R02", ["R02", "2", "52.5", "51.3", "72.8", "0.2"]),
            new("R03", ["R03", "2", "6.5", "62.1", "81.5", "0.4"])]
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SixColumnsAtDefaultWidthKeepAllHeadersReadable(bool sorted)
    {
        var card = SixColumns() with { SortColumn = sorted ? 2 : null };
        var canvas = new Commands(); CardPainter.Paint(canvas, card, Theme.Paper);
        Assert.Equal(card.Headers, canvas.Texts.Where(t => t.Point.Y == CardMeasure.BodyTop + 2).Select(t => t.Text));
        Assert.All(canvas.Texts.Where(t => t.Point.Y >= CardMeasure.BodyTop + 2), text =>
            Assert.True(text.Clip.X + text.Clip.Width - text.Point.X - canvas.MeasureText(text.Text, Theme.Paper.FontSize, Theme.Paper.Font).Width >= 1));
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
            ("narrow-unsorted", Fixture() with { Width = 260, SortColumn = null }),
            ("structural", StructuralDemo.Create().Single(c => c.Kind == CardKind.Table) with { SortColumn = 1 }),
            ("six-380", SixColumns()), ("six-380-sorted", SixColumns() with { SortColumn = 2 }),
            ("raw-analysis", SixColumns() with { Decimals = null, Rows = [new("R01", ["R01", "1", "48.74153519689177", "51.1234567", "62.1234567", "0.321987"]),
                new("R02", ["R02", "2", "48.84276519358723", "51.2234567", "62.2234567", "0.421987"])] }) })
        {
            using var bitmap = RasterDrawing.Render(card, theme, 2);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
            bitmap.Save(Path.Combine(output, $"table-{label}-{name.ToLowerInvariant()}.png"), ImageFormat.Png);
        }
    }

    private sealed class Commands : ICanvas2D
    {
        public List<(string Text, Point2 Point, Rect2 Clip)> Texts { get; } = [];
        public List<Point2[]> Polygons { get; } = [];
        public bool Estimate { get; init; }
        public Func<string, double, string, Size2>? Measurer { get; init; }
        private Rect2 currentClip;
        public int MeasureCalls { get; private set; }
        public Size2 MeasureText(string text, double size, string font)
        {
            MeasureCalls++;
            return Estimate ? TextMetrics.Estimate(text, size, font) : (Measurer ?? TextMetrics.Measurer)(text, size, font);
        }
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft, currentClip));
        public void Rect(Rect2 rect, string fill) { }
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) => Polygons.Add(points.ToArray());
        public IDisposable Clip(Rect2 rect)
        {
            var old = currentClip; currentClip = rect;
            return new Scope(() => currentClip = old);
        }
        private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
    }
}
