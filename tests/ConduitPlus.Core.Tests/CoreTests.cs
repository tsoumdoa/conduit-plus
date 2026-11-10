using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CoreTests
{
    public CoreTests() => GdiCanvas.RegisterTextMetrics();
    private static CardSpec[] AllCards() => [.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras()];
    [Fact]
    public void SpecsRoundTripPreservesNullSignedValuesAndKeys()
    {
        var cards = StructuralDemo.Create(); cards[4] = cards[4] with { Values = [.. cards[4].Values.Take(18), null, -.2] };
        var json = CardJson.Serialize(cards);
        var copy = CardJson.Deserialize<CardSpec[]>(json);
        Assert.Equal(json, CardJson.Serialize(copy)); Assert.Null(copy[4].Values[18]); Assert.Equal(-.2, copy[4].Values[19]);
    }
    [Fact]
    public void RevisionsChangeOnlyForDirtyCardIncludingMutatedArrays()
    {
        var store = new CardStore(); var cards = StructuralDemo.Create();
        Assert.Equal(6, store.Update(cards).Count); var first = store.Snapshot;
        Assert.Empty(store.Update(cards)); cards[4].Values[0] = .123;
        var changed = Assert.Single(store.Update(cards)); Assert.Equal("members", changed.Id);
        Assert.True(changed.Rev > first[4].Rev); Assert.Equal(first[0].Rev, store.Snapshot[0].Rev);
        Assert.NotEqual(.123, first[4].Values[0]);
        store.Update(cards.Take(1)); Assert.Single(store.Snapshot);
        store.Update(cards); Assert.Equal(cards.Select(c => c.Id), store.Snapshot.Select(c => c.Id));
    }
    [Fact]
    public void LayoutPrecedenceAndScaleAreDeterministic()
    {
        var c = StructuralDemo.Create()[0] with { WiredSlot = Slot.Tr };
        Assert.Equal(Slot.Tr, SlotLayout.Resolve(c, 0)); Assert.Equal(Slot.Tl, SlotLayout.Resolve(c with { WiredSlot = null }, 0));
        var layout = Assert.Single(SlotLayout.Place([c], Theme.Paper, new(1600, 1200), 2));
        Assert.Equal(1600 - (c.Width + 12) * 2, layout.X); Assert.Equal(184, layout.H);
    }
    [Fact]
    public void InvalidDataCannotCreateAmbiguousHitRegions()
    {
        var c = StructuralDemo.Create()[4];
        Assert.Throws<ArgumentException>(() => (c with { Keys = Enumerable.Repeat("same", 20).ToArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Threshold = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Values = [double.NaN] }).Validate());
        Assert.Throws<ArgumentException>(() => new CardStore().Update([c, c]));
    }
    [Fact]
    public void DeltaBarsPaintExplicitFailureColorAndFiniteZeroGeometry()
    {
        var c = new CardSpec { Id = "areas", Kind = CardKind.Bar, Values = [-4, 0], Labels = ["Residential", "Exact"], Keys = ["R1", "R2"], Threshold = 0, Colors = [Theme.Paper.Bad, Theme.Paper.Good] };
        var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Contains(canvas.Rects, r => r.Fill == Theme.Paper.Bad && r.Rect.Width > 0);
        var zero = new RecordingCanvas(); CardPainter.Paint(zero, c with { Values = [0, 0] }, Theme.Paper);
        Assert.All(zero.Rects, r => Assert.True(double.IsFinite(r.Rect.X) && double.IsFinite(r.Rect.Width)));
    }
    [Fact]
    public void PainterWritesTransparentSnapshotsAtThreeScales()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(output);
        foreach (var c in AllCards())
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            var size = CardMeasure.Measure(c, Theme.Paper);
            using var bitmap = new Bitmap((int)(size.Width * scale), (int)(size.Height * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); g.ScaleTransform((float)scale, (float)scale); using var canvas = new GdiCanvas(g); CardPainter.Paint(canvas, c, Theme.Paper); }
            bitmap.Save(Path.Combine(output, $"{c.Id}-{scale:0.0}x.png"), ImageFormat.Png);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A); Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 5).A);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void CardPainterRendersEveryCardKindThroughGdiCanvas(double scale)
    {
        var cards = AllCards().GroupBy(c => c.Kind).Select(g => g.First()).ToArray();
        Assert.Equal(Enum.GetValues<CardKind>().Order(), cards.Select(c => c.Kind).Order());
        foreach (var card in cards)
        {
            var size = CardMeasure.Measure(card, Theme.Paper);
            using var bitmap = new Bitmap((int)(size.Width * scale), (int)(size.Height * scale), PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform((float)scale, (float)scale);
            using var canvas = new GdiCanvas(graphics);
            CardPainter.Paint(canvas, card, Theme.Paper);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 5).A);
        }
    }

    [Fact]
    public void RasterUsesPremultipliedAlphaWithTransparentCornersAndCoveredInterior()
    {
        foreach (var c in AllCards())
        foreach (var scale in new[] { 1d, 2d, 4d })
        {
            using var bitmap = RasterDrawing.Render(c, Theme.Paper, scale);
            Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).A);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 8).A);
            Assert.Contains(Enumerable.Range(0, (int)(6 * scale)).SelectMany(x =>
                Enumerable.Range(0, (int)(6 * scale)).Select(y => bitmap.GetPixel(x, y).A)), alpha => alpha is > 0 and < 255);
        }
    }

    [Fact]
    public void GdiNestedClipsRestoreAndDoNotLeakTextOrFill()
    {
        using var bitmap = new Bitmap(100, 60, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var canvas = new GdiCanvas(graphics);
        using (canvas.Clip(new(10, 10, 50, 40)))
        {
            using (canvas.Clip(new(20, 20, 10, 10))) canvas.Rect(new(0, 0, 100, 60), "#FF0000");
            Assert.Equal(0, bitmap.GetPixel(35, 25).A);
            canvas.Rect(new(40, 30, 10, 10), "#0000FF");
            canvas.Text(new string('W', 100), new(10, 10), 12, "#000000", "Segoe UI");
        }
        Assert.Equal(255, bitmap.GetPixel(25, 25).A);
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(45, 35).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(61, 15).A);
        canvas.Rect(new(80, 40, 10, 10), "#00FF00");
        Assert.Equal(255, bitmap.GetPixel(85, 45).A);
    }

    [Fact]
    public void TableScrollClampsShowsVisibleRowsAndMapsThumb()
    {
        var table = StructuralDemo.Create().Single(c => c.Kind == CardKind.Table); // 12 rows, 8 visible by default
        Assert.True(TableScroll.CanScroll(table));
        Assert.Equal(4, TableScroll.MaxOffset(table));
        Assert.Equal(48 + 9 * Theme.Paper.RowHeight, CardMeasure.Measure(table, Theme.Paper).Height);

        var scrolled = table with { ScrollOffset = 3 };
        var hits = CardMeasure.DataHits(scrolled, Theme.Paper);
        Assert.Equal(8, hits.Length);
        Assert.Equal("M04", hits[0].Key);                                   // first on-screen row is data row 3
        Assert.Equal(36 + Theme.Paper.RowHeight, hits[0].Y);                // ...drawn in the first row slot
        Assert.Equal(4, TableScroll.Offset(table with { ScrollOffset = 99 })); // clamped
        Assert.Equal(0, TableScroll.Offset(table with { ScrollOffset = -5 }));

        var top = TableScroll.Bar(table, Theme.Paper)!.Value;
        var bottom = TableScroll.Bar(table with { ScrollOffset = 4 }, Theme.Paper)!.Value;
        Assert.Equal(top.Track.Y, top.Thumb.Y);
        Assert.Equal(bottom.Track.Y + bottom.Track.Height, bottom.Thumb.Y + bottom.Thumb.Height, 6);
        Assert.Equal(0, TableScroll.OffsetForThumbTop(table, Theme.Paper, top.Track.Y - 50));
        Assert.Equal(4, TableScroll.OffsetForThumbTop(table, Theme.Paper, bottom.Thumb.Y + 50));

        Assert.False(TableScroll.CanScroll(table with { MaxRows = 20 }));
        Assert.Null(TableScroll.Bar(table with { MaxRows = 20 }, Theme.Paper));
        Assert.Throws<ArgumentException>(() => (table with { MaxRows = 0 }).Validate());
    }

    [Fact]
    public void LineTicksCoverDomainWithNiceUniformSteps()
    {
        Assert.Equal(new[] { 0d, .5, 1, 1.5 }, ChartTicks.Nice(0, 1.12));
        Assert.Equal(new[] { -1d, -.5, 0, .5, 1 }, ChartTicks.Nice(-.7, .8));
        Assert.Equal(new[] { 0d, .005, .01, .015 }, ChartTicks.Nice(0, .012));
        foreach (var (min, max) in new[] { (2d, 2d), (-18d, 37d), (0d, .00012) })
        {
            var ticks = ChartTicks.Nice(min, max);
            Assert.True(ticks[0] <= min && ticks[^1] >= max);
            Assert.InRange(ticks.Length, 2, 7);
            Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(ticks[1] - ticks[0], pair.Second - pair.First, 10));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartTicks.Nice(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartTicks.Nice(2, 1));
    }

    [Fact]
    public void BrushingProjectsAcrossCardsAndSnapshotsCannotMutateState()
    {
        var cards = AllCards();
        var brush = new BrushState(); brush.Prune(cards, ["link-only"]);
        Assert.True(brush.Toggle("M04")); Assert.True(brush.Hover("M04"));
        var projected = brush.Project(cards);
        foreach (var c in projected.Where(c => c.Kind is CardKind.Bar or CardKind.Table))
        { Assert.Equal(new[] { "M04" }, c.SelectedKeys); Assert.Equal("M04", c.HoverKey); }
        Assert.Null(projected.Single(c => c.Kind == CardKind.Line).HoverKey);
        Assert.Empty(projected[0].SelectedKeys);
        Assert.Empty(cards[4].SelectedKeys); Assert.Null(cards[4].HoverKey);
        brush.Snapshot.SelectedKeys[0] = "mutated";
        Assert.Equal(new[] { "M04" }, brush.Snapshot.SelectedKeys);
        Assert.True(brush.Toggle("M04")); Assert.Empty(brush.Snapshot.SelectedKeys);
        Assert.False(brush.Toggle("m04")); // ordinal, case-sensitive
        Assert.False(brush.Hover("M04")); // unchanged hover
    }

    [Fact]
    public void PruningUsesUnionOfAllCardAndLinkKeysAndClearIsOwnerWide()
    {
        var cards = StructuralDemo.Create(); var brush = new BrushState();
        brush.Prune(cards, ["link-only", "M01"]);
        brush.Toggle("M01"); brush.Toggle("M20"); brush.Toggle("link-only"); brush.Hover("M20");
        var table = cards.Single(c => c.Kind == CardKind.Table);
        brush.Prune([table], ["link-only", "M01"]);
        Assert.Equal(new[] { "M01", "link-only" }, brush.Snapshot.SelectedKeys); Assert.Null(brush.Snapshot.HoverKey);
        brush.Hover("link-only");
        brush.Prune([], ["link-only"]);
        Assert.Equal(new[] { "link-only" }, brush.Snapshot.SelectedKeys); Assert.Equal("link-only", brush.Snapshot.HoverKey);
        brush.Prune([table], []);
        Assert.Empty(brush.Snapshot.SelectedKeys); Assert.Null(brush.Snapshot.HoverKey);
        Assert.False(brush.Clear()); brush.Toggle("M01"); Assert.True(brush.Clear());
        Assert.All(brush.Project(cards), c => Assert.Empty(c.SelectedKeys));
    }

    [Fact]
    public void ClearHitsOnlyKeyedCardsWithOwnerSelectionAndAvoidsScrollAndData()
    {
        var cards = AllCards();
        var brush = new BrushState(); brush.Prune(cards, ["link-only"]);
        Assert.All(cards, c => Assert.DoesNotContain(CardMeasure.Hits(c, Theme.Paper), h => h.Key == CardMeasure.ClearKey));
        brush.Toggle("link-only"); // even cards without a locally selected key can clear the owner
        foreach (var c in brush.Project(cards))
        {
            var hits = CardMeasure.Hits(c, Theme.Paper);
            if (!BrushState.Keys(c).Any()) { Assert.DoesNotContain(hits, h => h.Key == CardMeasure.ClearKey); continue; }
            var clear = Assert.Single(hits, h => h.Key == CardMeasure.ClearKey);
            Assert.True(clear.Contains(clear.X + clear.W / 2, clear.Y + clear.H / 2));
            Assert.True(clear.X >= c.Width / 2); Assert.True(clear.X + clear.W <= c.Width);
            Assert.All(CardMeasure.DataHits(c, Theme.Paper), h => Assert.True(clear.Y + clear.H <= h.Y));
            if (TableScroll.Bar(c, Theme.Paper) is { } bar) Assert.True(clear.Y + clear.H <= bar.Track.Y);
            // A selected table/bar must still paint after adding the extra control hit.
            using var bitmap = RasterDrawing.Render(c, Theme.Paper, 1);
            Assert.True(bitmap.Width > 0);
        }
        brush.Clear(); Assert.All(brush.Project(cards), c => Assert.Null(CardMeasure.Clear(c, Theme.Paper)));
    }

    [Fact]
    public void NewSpecsValidateRoundTripAndTrackDirtyArrays()
    {
        var cards = StructuralDemo.Extras(); foreach (var c in cards) c.Validate();
        var json = CardJson.Serialize(cards); Assert.Equal(json, CardJson.Serialize(CardJson.Deserialize<CardSpec[]>(json)));
        var text = cards.Single(c => c.Kind == CardKind.Text); var gradient = cards[0]; var categorical = cards[2];
        Assert.Throws<ArgumentException>(() => (text with { Width = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (text with { Width = 179 }).Validate());
        Assert.Throws<ArgumentException>(() => (text with { Keys = ["note"] }).Validate());
        foreach (var colors in new[] { Array.Empty<string>(), new[] { "red" }, new[] { "#12345G" }, new[] { "#FF123456" } })
            Assert.Throws<ArgumentException>(() => (categorical with { Colors = colors }).Validate());
        Assert.Throws<ArgumentException>(() => (categorical with { Labels = ["one"] }).Validate());
        Assert.Throws<ArgumentException>(() => (categorical with { Keys = ["one"] }).Validate());
        Assert.Throws<ArgumentException>(() => (categorical with { Keys = ["same", "same"] }).Validate());
        Assert.Throws<ArgumentException>(() => (categorical with { Keys = [CardMeasure.ClearKey, "ok"] }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Max = null }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Min = null }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Min = 1.2 }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Min = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Max = double.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Min = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Colors = ["#FFFFFF"] }).Validate());
        Assert.Throws<ArgumentException>(() => (gradient with { Keys = ["scale"] }).Validate());
        (categorical with { Keys = [] }).Validate();
        var store = new CardStore(); Assert.Equal(cards.Length, store.Update(cards).Count); var before = store.Snapshot;
        Assert.Empty(store.Update(cards)); cards[0].Colors[0] = "#000000";
        Assert.Equal(cards[0].Id, Assert.Single(store.Update(cards)).Id); Assert.Equal("#1F8A5B", before[0].Colors[0]);
        cards[1] = text with { Text = text.Text + "\nUpdated." }; Assert.Equal(text.Id, Assert.Single(store.Update(cards)).Id);
        cards[0] = cards[0] with { Max = 1.5 }; Assert.Equal(cards[0].Id, Assert.Single(store.Update(cards)).Id);
        Assert.Equal(6, StructuralDemo.Create().Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrappedTextMeasureEqualsPaintedContentExtent(bool fallback)
    {
        var original = TextMetrics.Measurer;
        try
        {
            if (fallback) TextMetrics.Measurer = TextMetrics.Estimate;
            foreach (var title in new[] { "", "Design note" })
            foreach (var width in new[] { 180d, 280, 500 })
            foreach (var text in new[] { "", "First line\n\nThird line", "First\r\nSecond\rThird", string.Join(" ", Enumerable.Repeat("Review structural results before issuing.", 12)), new string('W', 2000), string.Join("\n", Enumerable.Repeat("Long note", 40)) })
            {
                var c = new CardSpec { Id = "note", Kind = CardKind.Text, Title = title, Text = text, Width = width };
                var t = Theme.Paper; var layout = TextMetrics.Layout(c, t); var measured = CardMeasure.Measure(c, t);
                var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, c, t);
                var body = canvas.Texts.Skip(1).ToArray(); // first call is the optional header
                Assert.Equal(layout.Lines, body.Select(call => call.Text)); Assert.InRange(body.Length, 1, TextMetrics.MaxLines);
                Assert.Equal(measured.Height, body[^1].Position.Y + layout.LineHeight + t.Padding);
                Assert.Equal(measured.Height, canvas.Surfaces.Single().Height);
                Assert.All(body, call => Assert.True(TextMetrics.Measurer(call.Text, t.FontSize, t.Font).Width <= width - 2 * t.Padding));
                Assert.Empty(CardMeasure.Hits(c, t)); Assert.Empty(BrushState.Keys(c));
                if (text.StartsWith("First line", StringComparison.Ordinal)) Assert.Equal(new[] { "First line", "", "Third line" }, layout.Lines);
                if (text.Length >= 2000 || text.StartsWith("Long note", StringComparison.Ordinal)) { Assert.Equal(30, body.Length); Assert.EndsWith("…", body[^1].Text); }
            }
        }
        finally { TextMetrics.Measurer = original; }
    }

    [Fact]
    public void LegendPaintUsesNiceTicksMultiStopGradientAndCategoricalKeys()
    {
        var c = StructuralDemo.Extras()[0]; var t = Theme.Paper;
        Assert.Equal(new[] { 0d, .5, 1, 1.2 }, LegendCardPainter.Ticks(c));
        var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, c, t);
        Assert.Equal(c.Colors, Assert.Single(canvas.Gradients).Colors);
        Assert.Equal(new[] { c.Title, "0", "0.5", "1", "1.2", "ratio" }, canvas.Texts.Select(call => call.Text));
        Assert.Empty(CardMeasure.Hits(c, t));
        var signed = c with { Min = -.7, Max = .8 }; Assert.Equal(new[] { -.7, -.5, 0, .5, .8 }, LegendCardPainter.Ticks(signed));
        var categorical = StructuralDemo.Extras()[2]; var hits = CardMeasure.DataHits(categorical, t);
        Assert.Equal(categorical.Keys, hits.Select(h => h.Key));
        var brush = new BrushState(); brush.Prune([categorical], ["fails"]); Assert.True(brush.Toggle("fails")); Assert.True(brush.Hover("fails"));
        var selected = Assert.Single(brush.Project([categorical])); Assert.Equal(new[] { "fails" }, selected.SelectedKeys); Assert.Equal("fails", selected.HoverKey);
        var painted = new RecordingCanvas(); CardPainter.Paint(painted, selected, t);
        Assert.Contains(painted.Rects, r => r.Fill == t.Accent && r.Rect.Y == hits[1].Y);
        Assert.NotNull(CardMeasure.Clear(selected, t));
    }

    [Fact]
    public void LineHitsCoverEverySampleAndThresholdCanBeDisabled()
    {
        var c = StructuralDemo.Line(); var t = Theme.Paper; var hits = CardMeasure.DataHits(c, t);
        for (var i = 0; i < c.Values.Length; i++)
        {
            var x = 44 + i * (c.Width - 64) / (c.Values.Length - 1);
            Assert.Equal(c.Keys[i], Assert.Single(hits, h => h.Contains(x, 100)).Key);
        }
        var brush = new BrushState(); brush.Prune([c], []); brush.Toggle(c.Keys[^1]); brush.Hover(c.Keys[^1]);
        var projected = Assert.Single(brush.Project([c])); Assert.Equal(new[] { c.Keys[^1] }, projected.SelectedKeys); Assert.Equal(c.Keys[^1], projected.HoverKey);
        foreach (var threshold in new[] { 0d, 1d })
        {
            var line = c with { Threshold = threshold }; line.Validate(); var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, line, t);
            Assert.Equal(threshold > 0, canvas.Rects.Any(r => r.Fill == t.ThresholdBand));
            Assert.Equal(threshold > 0, canvas.Lines.Any(l => l.Color == t.Bad));
        }
        (c with { Threshold = 0, Values = Enumerable.Repeat<double?>(null, c.Values.Length).ToArray() }).Validate();
        Assert.Throws<ArgumentException>(() => (c with { Threshold = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Threshold = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (c with { Values = [1] }).Validate());
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void GradientRasterRetainsStopsAndTextFitsRegisteredMetrics(double scale)
    {
        var gradient = StructuralDemo.Extras()[0]; using var bitmap = RasterDrawing.Render(gradient, Theme.Paper, scale);
        var left = Theme.Paper.Padding + 16; var width = gradient.Width - 2 * left;
        var start = bitmap.GetPixel((int)((left + 2) * scale), (int)(46 * scale));
        var middle = bitmap.GetPixel((int)((left + width / 2) * scale), (int)(46 * scale));
        var end = bitmap.GetPixel((int)((left + width - 2) * scale), (int)(46 * scale));
        Assert.True(start.G > start.R); Assert.True(middle.R > 200 && middle.G > 150); Assert.True(end.R > end.G * 2);
        using var surface = new Bitmap(1000, 1000); using var graphics = Graphics.FromImage(surface); graphics.ScaleTransform((float)scale, (float)scale);
        using var canvas = new GdiCanvas(graphics);
        var note = StructuralDemo.Extras()[1] with { Width = 180, Text = string.Join(" ", Enumerable.Repeat("Check the structural utilisation envelope.", 10)) };
        var layout = TextMetrics.Layout(note, Theme.Paper);
        Assert.All(layout.Lines, line => Assert.InRange(canvas.MeasureText(line, Theme.Paper.FontSize, Theme.Paper.Font).Width, 0, note.Width - 2 * Theme.Paper.Padding));
        using var raster = RasterDrawing.Render(note, Theme.Paper, scale);
        Assert.Equal((int)(CardMeasure.Measure(note, Theme.Paper).Height * scale), raster.Height);
    }

    private sealed class RecordingCanvas : ICanvas2D
    {
        public List<(string Text, Point2 Position)> Texts { get; } = [];
        public List<Rect2> Surfaces { get; } = [];
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<(Point2 From, Point2 To, string Color)> Lines { get; } = [];
        public List<(Rect2 Rect, string[] Colors)> Gradients { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Measurer(text, size, font);
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => Gradients.Add((rect, colors.ToArray()));
        public void RoundedRect(Rect2 rect, double radius, string fill) => Surfaces.Add(rect);
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Lines.Add((from, to, color));
        public void Text(string text, Point2 topLeft, double size, string color, string font) => Texts.Add((text, topLeft));
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void LinkMappingPrefersBranchesIncludingEmptyBranchesAndRepeatedKeys()
    {
        var mapping = LinkKeyMapping.Create([0, 2], ["same", "same"]); // branch count and item count both 2
        Assert.Empty(mapping[0]); Assert.Equal(new[] { 1, 1 }, mapping[1]);
        mapping = LinkKeyMapping.Create([2, 0, 1], ["a", "b", "c"]);
        Assert.Equal(new[] { 0, 0 }, mapping[0]); Assert.Empty(mapping[1]); Assert.Equal(new[] { 2 }, mapping[2]);
        mapping = LinkKeyMapping.Create([2, 0, 2], ["a", "b", "a", "A"]);
        Assert.Equal(new[] { 0, 1 }, mapping[0]); Assert.Empty(mapping[1]); Assert.Equal(new[] { 2, 3 }, mapping[2]);
        Assert.Empty(LinkKeyMapping.Create([], []));
        Assert.Empty(Assert.Single(LinkKeyMapping.Create([0], ["empty-branch"])));
    }

    [Fact]
    public void LinkMappingReportsInvalidKeyIndexAndMismatchedCounts()
    {
        var error = Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([2], ["a", " "]));
        Assert.Contains("Keys[1]", error.Message);
        error = Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([2, 3], ["a"]));
        Assert.Contains("2 branches", error.Message); Assert.Contains("5 items", error.Message);
        Assert.Throws<ArgumentException>(() => LinkKeyMapping.Create([-1], ["a"]));
    }
}
