using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class HiddenListTests
{
    private static readonly Guid A = new(1, 0, 0, new byte[8]), B = new(2, 0, 0, new byte[8]);
    private static CardSpec Card(string id, string title = "", CardKind kind = CardKind.Metric) => new() { Id = id, Title = title, Kind = kind, Value = "1" };
    private static HiddenParticipant P(string label, Guid owner, CardSpec[] cards, params string[] hidden) => new(label, owner, cards, hidden.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void OneOwnerListsHiddenCardsInCardOrderWithoutHeading()
    {
        var rows = HiddenList.Build([P("Overlay", A, [Card("a", "Alpha"), Card("b", "Beta"), Card("c", "Gamma")], "c", "a")]);
        Assert.Equal(["Alpha", "Gamma"], rows.Select(r => r.Text));
        Assert.All(rows, r => Assert.NotNull(r.CardId));
    }

    [Fact]
    public void UntitledCardsUseKindAndPosition() =>
        Assert.Equal("Table 2", HiddenList.Build([P("Overlay", A, [Card("a", "Alpha"), Card("b", kind: CardKind.Table)], "b")]).Single().Text);

    [Fact]
    public void SeveralOwnersAreGroupedAndDuplicateLabelsNumbered()
    {
        var rows = HiddenList.Build([P("Overlay", A, [Card("a", "Alpha")], "a"), P("Overlay", B, [Card("b", "Beta")], "b")]);
        Assert.Equal(["Overlay", "Alpha", "Overlay (2)", "Beta"], rows.Select(r => r.Text));
        Assert.Null(rows[0].CardId); Assert.Equal(B, rows[3].Owner);
    }

    [Fact]
    public void OwnersWithNothingHiddenGetNoHeading()
    {
        var rows = HiddenList.Build([P("Rooms", A, [Card("a", "Alpha")]), P("Truss", B, [Card("b", "Beta")], "b")]);
        Assert.Equal(["Beta"], rows.Select(r => r.Text));
    }

    [Fact]
    public void PanelTruncatesWithMoreRowAndStaysInsideTheViewport()
    {
        var p = HiddenList.Layout(new(200, 100, 80, 24), 100, 22, 20);
        Assert.True(p.More > 0);
        Assert.True(p.Bounds.Y >= 0 && p.Bounds.X >= 0);
        Assert.True(p.Bounds.Y + p.Bounds.Height <= 100 + 1e-9);
        Assert.Equal(20, p.Rows.Length + p.More);
        Assert.All(p.Rows, r => Assert.True(r.Y >= p.Bounds.Y && r.Y + r.Height <= p.Bounds.Y + p.Bounds.Height + 1e-9));
    }

    [Fact]
    public void PanelShowsEveryRowWhenThereIsRoom()
    {
        var p = HiddenList.Layout(new(800, 600, 80, 24), 600, 22, 4);
        Assert.Equal(0, p.More);
        Assert.Equal(4, p.Rows.Length);
    }

    [Fact]
    public void FitShortensLongTextWithAnEllipsis()
    {
        double Measure(string s) => s.Length * 6;
        Assert.Equal("Short", HiddenList.Fit("Short", 100, Measure));
        var fitted = HiddenList.Fit("A very long card title that does not fit", 60, Measure);
        Assert.EndsWith("…", fitted);
        Assert.True(Measure(fitted) <= 60);
    }

    [Theory]
    [InlineData("Heading", "Sub", "Heading")]
    [InlineData("", "Sub", "Sub")]
    [InlineData("", "", "Title 1")]
    public void TitleRowsUseHeadingThenSubtitle(string title, string subtitle, string expected)
    {
        var card = Card("a", title, CardKind.Title) with { Subtitle = subtitle };
        Assert.Equal(expected, HiddenList.Build([P("Overlay", A, [card], "a")]).Single().Text);
    }

    [Fact]
    public void EmptyParticipantsProduceNoRows() => Assert.Empty(HiddenList.Build([]));
}
