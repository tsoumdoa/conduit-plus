using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public sealed class TitleTests
{
    private static CardSpec Title => new() { Id = "title", Kind = CardKind.Title, Title = string.Join(" ", Enumerable.Repeat("Heading", 40)), Subtitle = string.Join(" ", Enumerable.Repeat("Subtitle", 40)), Text = string.Join(" ", Enumerable.Repeat("Description", 80)) };
    [Fact] public void Validation()
    {
        Title.Validate();
        Assert.Throws<ArgumentException>(() => (Title with { Title = " ", Subtitle = "", Text = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (Title with { Keys = ["a"] }).Validate());
        (Title with { Colors = ["#1F8A5B"] }).Validate();
        Assert.Throws<ArgumentException>(() => (Title with { Colors = ["#1F8A5B", "#000000"] }).Validate());
        Assert.Throws<ArgumentException>(() => (Title with { Colors = ["red"] }).Validate());
        foreach (var slot in Enum.GetValues<Slot>())
            if (slot is Slot.Lc or Slot.Rc) Assert.Throws<ArgumentException>(() => (Title with { WiredSlot = slot }).Validate());
            else (Title with { WiredSlot = slot }).Validate();
        Assert.Throws<ArgumentException>(() => new CardSpec { Id = "m", WiredSlot = Slot.Tc }.Validate());
        Assert.Throws<ArgumentException>(() => new CardSpec { Id = "m", WiredSlot = Slot.Bc }.Validate());
        Assert.Empty(CardMeasure.Hits(Title, Theme.Paper));
        Assert.Empty(CardMeasure.DataHits(Title, Theme.Paper));
    }
    [Fact] public void SlotsRemainStable()
    {
        CardSpec m = new() { Id = "m" };
        Assert.Equal(new[] { Slot.Tl, Slot.Tl, Slot.Tr, Slot.Br, Slot.Bl }, SlotLayout.ResolveAll([Title, m, m, Title with { WiredSlot = Slot.Br }, m]));
        Assert.Equal(new[] { Slot.Br, Slot.Tr }, SlotLayout.ResolveAll([m with { WiredSlot = Slot.Br }, m]));
    }
    private sealed class TextCanvas : ICanvas2D
    {
        public List<(string Text, Point2 At, double Size)> Texts { get; } = [];
        public List<string> Colors { get; } = [];
        public List<Rect2> Surfaces { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => TextMetrics.Measurer(text, size, font);
        public void Rect(Rect2 rect, string fill) { }
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void RoundedRect(Rect2 rect, double radius, string fill) => Surfaces.Add(rect);
        public void Line(Point2 from, Point2 to, string color, double width = 1) { }
        public void Text(string text, Point2 topLeft, double size, string color, string font) { Texts.Add((text, topLeft, size)); Colors.Add(color); }
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Nothing();
        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }

    [Theory]
    [InlineData(null, "left")] [InlineData(Slot.Tl, "left")] [InlineData(Slot.Bl, "left")]
    [InlineData(Slot.Tc, "centre")] [InlineData(Slot.Bc, "centre")]
    [InlineData(Slot.Tr, "right")] [InlineData(Slot.Br, "right")]
    public void LinesAlignWithThePosition(Slot? slot, string expected)
    {
        var c = new CardSpec { Id = "t", Kind = CardKind.Title, Title = "Truss review", Subtitle = "4 bays and a much longer subtitle line", WiredSlot = slot };
        var size = CardMeasure.Measure(c, Theme.Paper);
        var canvas = new TextCanvas();
        CardPainter.Paint(canvas, c, Theme.Paper);
        Assert.Equal(2, canvas.Texts.Count);
        foreach (var (text, at, fontSize) in canvas.Texts)
        {
            var w = TextMetrics.Measurer(text, fontSize, fontSize == 26 ? Theme.Paper.Font + " Semibold" : Theme.Paper.Font).Width;
            var left = at.X; var right = size.Width - (at.X + w);
            if (expected == "left") Assert.Equal(TitleMetrics.PlainPadding, left, 6);
            else if (expected == "right") Assert.Equal(TitleMetrics.PlainPadding, right, 6);
            else Assert.Equal(left, right, 6);
        }
    }

    [Fact]
    public void PlainTitlesDrawOnlyTextAndCardTitlesDrawASurface()
    {
        var plain = new TextCanvas(); CardPainter.Paint(plain, new() { Id = "t", Kind = CardKind.Title, Title = "Heading" }, Theme.Paper);
        Assert.Single(plain.Texts); Assert.Empty(plain.Surfaces);
        var card = new TextCanvas(); CardPainter.Paint(card, new() { Id = "t", Kind = CardKind.Title, Title = "Heading", Background = TitleBackground.Card }, Theme.Paper);
        Assert.Single(card.Texts); Assert.Single(card.Surfaces);
    }

    [Theory]
    [InlineData(TitleBackground.Plain, null, "#000000", "#000000")]
    [InlineData(TitleBackground.Plain, "#C2334D", "#C2334D", "#C2334D")]
    [InlineData(TitleBackground.Card, null, "ink", "muted")]
    [InlineData(TitleBackground.Card, "#C2334D", "#C2334D", "#C2334D")]
    public void TextColourDefaultsToBlackAndFollowsTheColourInput(TitleBackground background, string? colour, string heading, string description)
    {
        foreach (var theme in new[] { Theme.Paper, Theme.Night })
        {
            var canvas = new TextCanvas();
            CardPainter.Paint(canvas, new() { Id = "t", Kind = CardKind.Title, Title = "Heading", Subtitle = "Sub", Text = "Description", Background = background, Colors = colour is null ? [] : [colour] }, theme);
            string Expected(string v) => v == "ink" ? theme.Ink : v == "muted" ? theme.Muted : v;
            Assert.Equal(new[] { Expected(heading), Expected(heading), Expected(description) }, canvas.Colors);
        }
    }

    [Fact]
    public void ShortTitleShrinkWrapsAndLongTitleStopsAtMaxWidth()
    {
        var shortTitle = CardMeasure.Measure(new() { Id = "t", Kind = CardKind.Title, Title = "Hi" }, Theme.Paper);
        Assert.True(shortTitle.Width < 100);
        var longTitle = CardMeasure.Measure(Title with { Form = new(Width: 250) }, Theme.Paper);
        // Wrapped at word boundaries: close to, never over, the limit.
        Assert.InRange(longTitle.Width, 200, 250);
    }

    [Fact] public void FormsWrapAndMeasure()
    {
        foreach (var form in new CardForm[] { CardForm.None, new(Width: 180, DescriptionLines: 2), new(DropSubtitle: true), new(TitleLines: 1), new(DescriptionLines: 0) })
        {
            var c = Title with { Form = form };
            var l = TitleMetrics.Layout(c, Theme.Paper, TextMetrics.Measurer);
            Assert.Equal(form.TitleLines ?? 2, l.Title.Length);
            Assert.Equal(form.DropSubtitle ? 0 : 2, l.Subtitle.Length);
            Assert.Equal(form.DescriptionLines ?? 4, l.Description.Length);
            foreach (var block in new[] { l.Title, l.Subtitle, l.Description }.Where(b => b.Length > 0)) Assert.EndsWith("…", block[^1]);
            Assert.True(l.Size.Width <= (form.Width ?? 400));
            Assert.Equal(l.Size, CardMeasure.Measure(c, Theme.Paper));
            Assert.Equal(l.Size, FormMeasure.Size(Title, Theme.Paper, form, TextMetrics.Measurer));
        }
    }
}
