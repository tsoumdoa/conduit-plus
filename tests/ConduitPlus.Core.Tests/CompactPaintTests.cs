using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

/// <summary>Every form paints inside its measured card, after clipping.</summary>
public sealed class CompactPaintTests
{
    private static CardSpec[] Cards() =>
    [
        .. StructuralDemo.Create(), StructuralDemo.Line(), .. StructuralDemo.Extras(),
        new() { Id = "keyed", Kind = CardKind.Metric, Title = "Rooms below minimum with a long title", Keys = ["k"], Value = "1234.5", Unit = "m²" },
        new() { Id = "selected", Kind = CardKind.Metric, Title = "Selected", Keys = ["k"], Value = "8", SelectedKeys = ["k"] },
        new() { Id = "text", Kind = CardKind.Text, Title = "Note", Text = string.Join(" ", Enumerable.Repeat("long text", 100)) },
        new() { Id = "table", Kind = CardKind.Table, Title = "Rows", Headers = ["Name", "Value"], MaxRows = 12, Rows = Enumerable.Range(0, 12).Select(i => new TableRow($"r{i}", [$"Row {i}", i.ToString()])).ToArray() }
    ];

    [Fact]
    public void FormsPaintInsideTheirCard()
    {
        CardForm[] forms = [CardForm.None, new(Compact: true), new(5), new(3)];
        foreach (var card in Cards())
            foreach (var form in forms)
            {
                var c = card with { Form = form };
                var size = CardMeasure.Measure(c, Theme.Paper);
                var canvas = new BoundsCanvas();
                CardPainter.Paint(canvas, c, Theme.Paper);
                foreach (var (rect, what) in canvas.Drawn)
                    Assert.True(rect.X >= -.5 && rect.Y >= -.5 && rect.X + rect.Width <= size.Width + .5 && rect.Y + rect.Height <= size.Height + .5,
                        $"{c.Id} {c.Kind} {form}: {what} at {rect} is outside {size}");
            }
    }

    private sealed class BoundsCanvas : ICanvas2D
    {
        private readonly Stack<Rect2> _clips = new([new Rect2(-1e6, -1e6, 2e6, 2e6)]);
        public List<(Rect2 Rect, string What)> Drawn { get; } = [];
        private void Add(Rect2 r, string what)
        {
            var c = _clips.Peek();
            var x0 = Math.Max(r.X, c.X); var y0 = Math.Max(r.Y, c.Y);
            var x1 = Math.Min(r.X + r.Width, c.X + c.Width); var y1 = Math.Min(r.Y + r.Height, c.Y + c.Height);
            if (x1 > x0 && y1 > y0) Drawn.Add((new(x0, y0, x1 - x0, y1 - y0), what));
        }
        private static Rect2 Around(IEnumerable<Point2> points)
        {
            var p = points.ToArray();
            var x = p.Min(q => q.X); var y = p.Min(q => q.Y);
            return new(x, y, Math.Max(.001, p.Max(q => q.X) - x), Math.Max(.001, p.Max(q => q.Y) - y));
        }
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Measurer(text, size, font);
        public void Rect(Rect2 rect, string fill) => Add(rect, "rect");
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => Add(rect, "gradient");
        public void RoundedRect(Rect2 rect, double radius, string fill) => Add(rect, "rounded rect");
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Add(Around([from, to]), "line");
        public void Text(string text, Point2 topLeft, double size, string color, string font)
        {
            if (text.Length == 0) return;
            var s = MeasureText(text, size, font);
            Add(new(topLeft.X, topLeft.Y, s.Width, s.Height), $"text '{text}'");
        }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { if (points.Count > 0) Add(Around(points), "polyline"); }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { if (points.Count > 0) Add(Around(points), "polygon"); }
        public IDisposable Clip(Rect2 rect)
        {
            var c = _clips.Peek();
            var x0 = Math.Max(rect.X, c.X); var y0 = Math.Max(rect.Y, c.Y);
            _clips.Push(new(x0, y0, Math.Max(0, Math.Min(rect.X + rect.Width, c.X + c.Width) - x0), Math.Max(0, Math.Min(rect.Y + rect.Height, c.Y + c.Height) - y0)));
            return new Pop(_clips);
        }
        private sealed class Pop(Stack<Rect2> clips) : IDisposable { public void Dispose() => clips.Pop(); }
    }
}
