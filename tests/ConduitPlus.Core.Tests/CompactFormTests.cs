using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public sealed class CompactFormTests
{
    [Fact]
    public void FormsShareMeasurementAndBoundHits()
    {
        CardSpec[] cards = [.. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras(),
            new() { Id = "test", Kind = CardKind.Metric, Keys = ["metric"], Value = "42" },
            new() { Id = "test", Kind = CardKind.Text, Text = string.Join(" ", Enumerable.Repeat("long text", 100)) },
            new() { Id = "test", Kind = CardKind.Table, Headers = ["Name"], MaxRows = 12, Rows = Enumerable.Range(0,12).Select(i => new TableRow(i.ToString(), [i.ToString()])).ToArray() }];
        CardForm[] forms = [CardForm.None, new(Compact: true), new(5), new(3)];
        foreach (var card in cards)
        foreach (var form in forms)
        {
            var c = card with { Form = form };
            var size = CardMeasure.Measure(c, Theme.Paper);
            Assert.Equal(FormMeasure.Size(c, Theme.Paper, form, TextMetrics.Measurer), size);
            // Bar scrolling and its row hits are implemented separately.
            foreach (var hit in CardMeasure.Hits(c, Theme.Paper))
            {
                Assert.InRange(hit.X, 0, size.Width);
                Assert.InRange(hit.Y, 0, size.Height);
                Assert.InRange(hit.X + hit.W, 0, size.Width);
                Assert.InRange(hit.Y + hit.H, 0, size.Height);
            }
        }
    }
    [Fact]
    public void CompactTextIndicatesTruncation()
    {
        var c = new CardSpec { Id = "test", Kind = CardKind.Text, Text = string.Join(" ", Enumerable.Repeat("word", 200)), Form = new(Compact: true) };
        var layout = TextMetrics.Layout(c, Theme.Paper);
        Assert.Equal(2, layout.Lines.Length);
        Assert.EndsWith("…", layout.Lines[1]);
    }
}

