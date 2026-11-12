using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;
using Tooltip = ConduitPlus.Core.Tooltip;

namespace ConduitPlus.Core.Tests;

public sealed class TooltipSortTests
{
    private static CardSpec Table(params string?[] cells) => new()
    {
        Id = "sort", Kind = CardKind.Table, Title = "Sorted results", Headers = ["Value"],
        Rows = cells.Select((v, i) => new TableRow($"r{i}", [v])).ToArray(), SortColumn = 0
    };
    [Fact]
    public void BarTooltipFormatsInvariantValuesThresholdMissingAndDistinctKeys()
    {
        var c = new CardSpec { Id = "bar", Kind = CardKind.Bar, Keys = ["m1"], Labels = ["Member 1"], Values = [.123456], Threshold = .5 };
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(new[] { "Member 1", "0.12", "24.691% of threshold", "m1" }, Tooltip.For(c, "m1"));
            Assert.Equal(new[] { "Member 1", "missing", "m1" }, Tooltip.For(c with { Values = [null] }, "m1"));
            Assert.Equal(new[] { "m1", "−0.5", "-100% of threshold" }, Tooltip.For(c with { Labels = ["m1"], Values = [-.5] }, "m1"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
    [Fact]
    public void BarTooltipUsesFullColumnPrecisionForSmallSignedValuesAndMissingRows()
    {
        var card = new CardSpec
        {
            Id = "small", Kind = CardKind.Bar, Keys = ["p", "n", "large", "missing"],
            Labels = ["Positive", "Negative", "Larger", "Missing"], Values = [.00012, -.00012, .5, null],
            GoodWhen = BarGoodWhen.Within, Threshold = .0001
        };
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var layout = BarPresentation.Layout(card, Theme.Paper);
            Assert.Equal(new[] { "0.00012", "−0.00012", "0.50000", "—" }, layout.Values);
            for (var i = 0; i < 3; i++) Assert.Equal(layout.Values[i], Tooltip.For(card, card.Keys[i])![1]);
            Assert.Equal("120% of threshold", Tooltip.For(card, "p")![2]);
            Assert.Equal("120% of threshold", Tooltip.For(card, "n")![2]);
            Assert.Equal(new[] { "Missing", "missing", "Within: good when −Threshold ≤ value ≤ Threshold", "missing" }, Tooltip.For(card, "missing"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
    [Fact]
    public void LineTableAndLegendTooltipsUseTheirOwnContent()
    {
        var line = StructuralDemo.Line();
        Assert.Equal(new[] { "LC01", "0.32" }, Tooltip.For(line, "LC01"));
        Assert.Equal(new[] { "LC05", "missing" }, Tooltip.For(line, "LC05"));
        var table = new CardSpec { Id = "table", Kind = CardKind.Table, Headers = ["Member", "Note", "Missing"], Rows = [new("id", ["M1", new string('x', 60), null])] };
        Assert.Equal(new[] { "Member: M1", "Note: " + new string('x', 39) + "…", "Missing: —" }, Tooltip.For(table, "id"));
        Assert.Equal(new[] { "Passing" }, Tooltip.For(StructuralDemo.Extras()[2], "passes"));
    }
    [Fact]
    public void TooltipReturnsNullForUnkeyedUnknownReservedAndEmptyContent()
    {
        CardSpec[] cards = [.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras()];
        foreach (var c in cards)
        {
            Assert.Null(Tooltip.For(c, null)); Assert.Null(Tooltip.For(c, ""));
            Assert.Null(Tooltip.For(c, "absent")); Assert.Null(Tooltip.For(c, TableSort.Key(0)));
        }
        Assert.Equal(new[] { "Max deflection", "18.4 mm" }, Tooltip.For(cards[0] with { Keys = ["key"] }, "key"));
        Assert.Null(Tooltip.For(StructuralDemo.Extras()[1] with { Keys = ["key"] }, "key"));
        Assert.Null(Tooltip.For(StructuralDemo.Extras()[0] with { Keys = ["key"] }, "key"));
        Assert.Null(Tooltip.For(StructuralDemo.Extras()[2] with { Labels = ["", "Fail"] }, "passes"));
    }
    [Theory]
    [InlineData(false, "r2,r1,r3,r0,r4,r5")]
    [InlineData(true, "r0,r1,r3,r2,r4,r5")]
    public void SortIsNumericInvariantStableAndKeepsNullAndEmptyLast(bool descending, string keys)
    {
        var c = Table("10", "2", "1.5", "2.0", null, "") with { SortDescending = descending };
        var before = c.Rows.ToArray();
        Assert.Equal(keys.Split(','), TableSort.Rows(c).Select(r => r.Key));
        Assert.Equal(before, c.Rows);
    }
    [Theory]
    [InlineData(false, "r1,r3,r0,r2,r4,r5")]
    [InlineData(true, "r2,r0,r1,r3,r4,r5")]
    public void StringSortIgnoresCaseAndIsStable(bool descending, string keys)
    {
        var c = Table("Beta", "alpha", "Zebra", "ALPHA", "", null) with { SortDescending = descending };
        Assert.Equal(keys.Split(','), TableSort.Rows(c).Select(r => r.Key));
    }
    [Fact]
    public void MixedPairsCompareAsStringsAndSortCycleRestoresOriginalOrder()
    {
        var c = Table("9", "apple");
        Assert.Equal(new[] { "r0", "r1" }, TableSort.Rows(c).Select(r => r.Key));
        c = Table("10", "2") with { SortColumn = null };
        var asc = TableSort.Cycle(c, 0); var desc = TableSort.Cycle(asc, 0); var original = TableSort.Cycle(desc, 0);
        Assert.Equal(0, asc.SortColumn); Assert.False(asc.SortDescending);
        Assert.Equal(0, desc.SortColumn); Assert.True(desc.SortDescending);
        Assert.Null(original.SortColumn); Assert.False(original.SortDescending);
        Assert.Same(c.Rows, TableSort.Rows(original));
        Assert.Same(c.Rows, TableSort.Rows(c with { SortColumn = -1 }));
        Assert.Same(c.Rows, TableSort.Rows(c with { SortColumn = 1 }));
        var other = TableSort.Cycle(desc with { Headers = ["A", "B"] }, 1);
        Assert.Equal(1, other.SortColumn); Assert.False(other.SortDescending);
    }
    [Fact]
    public void NumericSortUsesTheSameStrictInvariantGrammarAsFormatting()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(new[] { "r2", "r3", "r0", "r1" }, TableSort.Rows(Table("1000", "2e3", "1.5", "20")).Select(r => r.Key));
            Assert.Equal(new[] { "r1", "r0" }, TableSort.Rows(Table("−4.0", "-10")).Select(r => r.Key));
            // Comma text is compared as text, never rewritten/interpreted as 123 or 1234.
            Assert.Equal(new[] { "r0", "r1", "r2" }, TableSort.Rows(Table("1,23", "1,234", "2")).Select(r => r.Key));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
    [Fact]
    public void HeaderHitsAreBelowTitlesAndClearAndNeverBrushOrSelect()
    {
        var c = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table) with { OwnerHasSelection = true };
        var headers = TableSort.Headers(c, Theme.Paper); var clear = CardMeasure.Clear(c, Theme.Paper)!;
        Assert.Equal(c.Headers.Length, headers.Length);
        for (var i = 0; i < headers.Length; i++)
        {
            var h = headers[i]; var x = h.X + h.W / 2; var y = h.Y + h.H / 2;
            Assert.Equal(i, TableSort.Column(h.Key)); Assert.Equal(CardMeasure.BodyTop, h.Y);
            Assert.True(h.Y >= 32 && h.Y >= clear.Y + clear.H);
            Assert.False(CardMeasure.IsTitle(c, Theme.Paper, x, y));
            Assert.Equal(h, Assert.Single(CardMeasure.Hits(c, Theme.Paper), hit => hit.Contains(x, y)));
        }
        Assert.Null(TableSort.Column("row")); Assert.Null(TableSort.Column("\u0001sort:-1"));
        var brush = new BrushState(); brush.Prune([c], [TableSort.Key(0)]);
        Assert.False(brush.Toggle(TableSort.Key(0))); Assert.False(brush.Hover(TableSort.Key(0)));
        Assert.DoesNotContain(BrushState.Keys(c), CardMeasure.IsReserved);
    }
    [Theory]
    [InlineData("\u0001sort:0")]
    [InlineData("\u0001clear")]
    [InlineData("\u0001scrollbar")]
    [InlineData("\u0001anything")]
    public void AllReservedRowAndChartKeysAreRejected(string key)
    {
        Assert.Throws<ArgumentException>(() => (Table("1") with { Rows = [new(key, ["1"])] }).Validate());
        var bar = new CardSpec { Id = "bar", Kind = CardKind.Bar, Keys = [key], Labels = ["a"], Values = [1] };
        Assert.Throws<ArgumentException>(bar.Validate);
    }
    [Fact]
    public void SortedScrollHitsPaintedRowsAndSelectionStillUseKeys()
    {
        var c = Table("10", "2", "30", "1") with { MaxRows = 2, ScrollOffset = 1, SelectedKeys = ["r1"] };
        var hits = CardMeasure.DataHits(c, Theme.Paper);
        Assert.Equal(new[] { "r1", "r0" }, hits.Select(h => h.Key));
        var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Equal(new[] { "Sorted results", "Clear", "Value", "2", "10", "2–3 of 4" }, canvas.Texts.Select(t => t.Text));
        Assert.Contains(canvas.Rects, r => r.Fill == Theme.Paper.Accent && r.Rect.Y == hits[0].Y);
        Assert.Equal("r2", Assert.Single(CardMeasure.DataHits(c with { ScrollOffset = 99, MaxRows = 1 }, Theme.Paper)).Key);
        var header = TableSort.Headers(c, Theme.Paper)[0]; var hovered = new RecordingCanvas();
        CardPainter.Paint(hovered, c with { HoverKey = header.Key }, Theme.Paper);
        Assert.Contains(hovered.Rects, r => r.Fill == Theme.Paper.Track && r.Rect == new Rect2(header.X, header.Y, header.W, header.H));
        Assert.Single(hovered.Polygons);
        var descending = new RecordingCanvas(); CardPainter.Paint(descending, c with { SortDescending = true }, Theme.Paper);
        Assert.True(hovered.Polygons[0][2].Y < hovered.Polygons[0][0].Y);
        Assert.True(descending.Polygons[0][2].Y > descending.Polygons[0][0].Y);
    }
    [Fact]
    public void TooltipMeasuredExtentIsExactlyThePaintedSurfaceAndContainsEveryLine()
    {
        string[] lines = ["Member 1", "0.812", "81.2% of threshold"];
        var canvas = new RecordingCanvas(); var t = Theme.Paper;
        var size = TooltipPainter.Measure(canvas, lines, t); TooltipPainter.Paint(canvas, lines, t);
        Assert.Equal(new Rect2(0, 0, size.Width, size.Height), Assert.Single(canvas.Surfaces));
        Assert.Equal(lines, canvas.Texts.Select(t => t.Text));
        foreach (var call in canvas.Texts)
        {
            var textSize = canvas.MeasureText(call.Text, call.Size, t.Font);
            Assert.InRange(call.Point.X + textSize.Width, 0, size.Width - TooltipPainter.Padding);
            Assert.InRange(call.Point.Y + textSize.Height, 0, size.Height - TooltipPainter.Padding);
        }
    }
    [Fact]
    public void TooltipPositionOffsetsFlipsAndClampsAtViewportEdges()
    {
        Assert.Equal(new Point2(114, 114), TooltipPainter.Position(new(100, 100), new(80, 40), new(400, 300)));
        Assert.Equal(new Point2(296, 226), TooltipPainter.Position(new(390, 280), new(80, 40), new(400, 300)));
        Assert.Equal(new Point2(0, 0), TooltipPainter.Position(new(10, 10), new(800, 500), new(400, 300)));
        Assert.Equal(new Point2(128, 128), TooltipPainter.Position(new(100, 100), new(80, 40), new(400, 300), 2));
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void SortedTableAndTooltipRasterSmokeAndAlphaCorners(double scale)
    {
        var c = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table) with { SortColumn = 1, SelectedKeys = ["M12"] };
        using var table = RasterDrawing.Render(c, Theme.Paper, scale);
        var lines = Tooltip.For(StructuralDemo.Create()[4], "M01")!;
        using var tooltip = RasterDrawing.RenderTooltip(lines, Theme.Paper, scale);
        foreach (var bitmap in new[] { table, tooltip })
        {
            Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A); Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).A);
            Assert.Equal(0, bitmap.GetPixel(0, bitmap.Height - 1).A); Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - (int)(5 * scale)).A);
        }
        using var measureBitmap = new Bitmap(1, 1); using var graphics = Graphics.FromImage(measureBitmap); using var canvas = new GdiCanvas(graphics);
        var extent = TooltipPainter.Measure(canvas, lines, Theme.Paper);
        Assert.Equal((int)Math.Ceiling(extent.Width * scale), tooltip.Width);
        Assert.Equal((int)Math.Ceiling(extent.Height * scale), tooltip.Height);
        Assert.Equal(ColorTranslator.FromHtml("#1F2329").ToArgb(), tooltip.GetPixel((int)(5 * scale), (int)(5 * scale)).ToArgb());
        if (scale != 2) return;
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(output);
        table.Save(Path.Combine(output, "sorted-table-2x.png"), ImageFormat.Png);
        tooltip.Save(Path.Combine(output, "tooltip-2x.png"), ImageFormat.Png);
    }
    private sealed class RecordingCanvas : ICanvas2D
    {
        public List<Rect2> Surfaces { get; } = [];
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<(string Text, Point2 Point, double Size)> Texts { get; } = [];
        public List<Point2[]> Polygons { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Estimate(text, size, font);
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void RoundedRect(Rect2 rect, double radius, string fill) => Surfaces.Add(rect);
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft, size));
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) => Polygons.Add(points.ToArray());
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
