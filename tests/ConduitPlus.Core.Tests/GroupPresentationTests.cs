using System.Collections.Immutable;
using System.Text.Json;
using ConduitPlus.Rhino;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class GroupPresentationTests
{
    private static CardSpec Group(CardKind kind) => new()
    {
        Id = "group", Kind = kind, Title = "Bedrooms", Value = "2", Keys = ["bedrooms"],
        Labels = ["Bedrooms"], Values = [2], Colors = ["#638AD2"],
        Groups = GroupInputs.Create(["bedrooms"], 1, true, [new("{0;3}", ["R2", "R1", "R1"])])
    };

    public static IEnumerable<object[]> Themes() => new[] { Theme.Paper, Theme.Night, Theme.Smoke, Theme.Blueprint,
        Theme.Auto with { Name = "AutoLight" }, Theme.Auto with { Name = "AutoDark" } }.SelectMany(t => new[] { CardKind.Bar, CardKind.Legend, CardKind.Metric }.Select(k => new object[] { t.Name, k }));

    [Theory]
    [MemberData(nameof(Themes))]
    public void PainterDistinguishesEveryGroupStateAndRetainsCategoryColors(string name, CardKind kind)
    {
        Theme.TryGet(name, out var theme);
        if (name.StartsWith("Auto", StringComparison.Ordinal)) theme = Theme.Auto.Resolve(name == "AutoDark");
        var card = Group(kind); var brush = new BrushState(); brush.Prune([card], []);
        var hit = Assert.Single(CardMeasure.DataHits(card, theme));
        var body = new Rect2(hit.X, hit.Y, hit.W, hit.H);
        RecordingCanvas Paint()
        {
            var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, brush.Project([card])[0], theme);
            canvas.Outlines.RemoveAll(o => o.Color != theme.Accent);
            if (kind is CardKind.Bar or CardKind.Legend) Assert.Contains(canvas.Rects, r => r.Fill == card.Colors[0]);
            return canvas;
        }
        void Strong(RecordingCanvas canvas, bool selected)
        {
            if (selected) Assert.Contains((body, theme.Highlight), canvas.Rects);
            Assert.Equal(selected, canvas.Rects.Contains((new(hit.X, hit.Y, 3, hit.H), theme.Accent)));
        }
        var plain = Paint(); Strong(plain, false); Assert.Empty(plain.Outlines);
        brush.Hover("R1");
        var related = Paint(); Strong(related, false); Assert.Single(related.Outlines);
        Assert.DoesNotContain(related.Rects, r => r.Rect == body); // related hover has no direct fill
        brush.Hover("bedrooms");
        var direct = Paint(); Strong(direct, false); Assert.Single(direct.Outlines);
        Assert.Contains((body, theme.Track), direct.Rects);
        brush.Hover(null); brush.Toggle("R1");
        var partial = Paint(); Strong(partial, false); Assert.Empty(partial.Outlines);
        Assert.Equal(kind != CardKind.Metric, partial.Rects.Contains((new(hit.X, hit.Y + hit.H / 4, 3, hit.H / 2), theme.Accent)));
        Assert.DoesNotContain((body, theme.Highlight), partial.Rects);
        Assert.DoesNotContain(partial.Lines, l => l.Color == theme.Accent);
        brush.Hover("bedrooms");
        var partialDirect = Paint();
        Assert.DoesNotContain(partialDirect.Rects, r => r.Rect == body);
        brush.Hover("R2");
        var partialHover = Paint(); Strong(partialHover, false); Assert.Single(partialHover.Outlines);
        Assert.Equal(kind != CardKind.Metric, partialHover.Rects.Contains((new(hit.X, hit.Y + hit.H / 4, 3, hit.H / 2), theme.Accent)));
        brush.Toggle("bedrooms");
        var fullHover = Paint(); Strong(fullHover, true); Assert.Single(fullHover.Outlines);
        brush.Hover(null); var full = Paint(); Strong(full, true); Assert.Empty(full.Outlines);
        brush.Toggle("bedrooms"); Assert.Empty(brush.Current.SelectedKeys);
    }

    [Fact]
    public void EffectiveMemberHoverPaintsAllLinkedEntriesWithoutExpandingRelatedGroups()
    {
        var table = new CardSpec { Id = "table", Kind = CardKind.Table, Headers = ["Room"], Rows = [new("R1", ["One"]), new("R2", ["Two"]), new("R3", ["Three"])] };
        var brush = new BrushState(); brush.Prune([Group(CardKind.Legend), table], []);
        brush.Hover("bedrooms");
        var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, brush.Project([table])[0], Theme.Paper);
        Assert.Equal(2, canvas.Outlines.Count);
        brush.Hover("R1"); canvas = new(); CardPainter.Paint(canvas, brush.Project([table])[0], Theme.Paper);
        Assert.Single(canvas.Outlines);
    }

    [Fact]
    public void LineEffectiveHoverOutlinesSamplesAndKeepsStrongSelection()
    {
        var card = new CardSpec { Id = "line", Kind = CardKind.Line, Keys = ["R1", "R2"], Labels = ["One", "Two"], Values = [1, 2] };
        var brush = new BrushState(); brush.Prune([Group(CardKind.Legend), card], []);
        brush.Hover("bedrooms"); brush.Toggle("R1");
        foreach (var theme in Theme.Presets.Select(t => t.Resolve(true)))
        {
            var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, brush.Project([card])[0], theme);
            Assert.Equal(2, canvas.Outlines.Count(o => o.Color == theme.Accent && o.Points.Length == 5 && o.Points[0] == o.Points[^1]));
            Assert.Contains(canvas.Lines, line => line.Color == theme.Accent && line.Width == 1 && line.From.X == line.To.X);
            Assert.Contains(canvas.Rects, r => r.Fill == theme.Accent && r.Rect.Width == 8 && r.Rect.Height == 8);
        }
        brush.Hover("R2");
        var direct = new RecordingCanvas(); CardPainter.Paint(direct, brush.Project([card])[0], Theme.Paper);
        var hit = CardMeasure.DataHits(card, Theme.Paper)[1];
        Assert.Contains((new Rect2(hit.X, hit.Y, hit.W, hit.H), Theme.Paper.Track), direct.Rects);
    }

    [Fact]
    public void DisabledAndEmptyTargetsRemainInertAndExportDropsAllHover()
    {
        var card = Group(CardKind.Metric); var brush = new BrushState(); brush.Prune([card], []);
        brush.Toggle("R1"); brush.Hover("bedrooms");
        var projected = brush.Project([card])[0]; var export = BrushPresentation.WithoutHover(projected);
        Assert.Null(export.HoverKey); Assert.Empty(export.HoveredKeys);
        Assert.Equal(1, export.GroupStates["bedrooms"].SelectedMemberCount);
        Assert.Equal(0, export.GroupStates["bedrooms"].HoveredMemberCount); Assert.False(export.GroupStates["bedrooms"].IsDirectTarget);
        Assert.True(export.OwnerHasSelection);
        var disabled = projected with { GroupStates = projected.GroupStates.SetItem("bedrooms", new(0, 0, 0, false, false)) };
        var canvas = new RecordingCanvas(); CardPainter.Paint(canvas, disabled, Theme.Paper);
        Assert.Empty(canvas.Outlines); Assert.DoesNotContain(canvas.Rects, r => r.Fill == Theme.Paper.Accent);
        var empty = card with { Groups = card.Groups.SetItem("bedrooms", new([])) };
        brush.Prune([empty], []); brush.Hover("bedrooms"); Assert.False(brush.Toggle("bedrooms"));
        Assert.Contains("0 members", Tooltip.For(brush.Project([empty])[0], "bedrooms")!);
        Assert.Equal("2", empty.Value); // numeric input is never recomputed from membership
    }

    [Fact]
    public void MetricBodyTitleAndClearHaveDisjointHitPriority()
    {
        var metric = Group(CardKind.Metric) with { OwnerHasSelection = true }; var theme = Theme.Paper;
        Assert.True(CardMeasure.IsTitle(metric, theme, 30, 15));
        Assert.DoesNotContain(CardMeasure.Hits(metric, theme), h => h.Contains(30, 15));
        var clear = CardMeasure.Clear(metric, theme)!;
        Assert.False(CardMeasure.IsTitle(metric, theme, clear.X + 3, clear.Y + 3));
        Assert.Equal(CardMeasure.ClearKey, Assert.Single(CardMeasure.Hits(metric, theme), h => h.Contains(clear.X + 3, clear.Y + 3)).Key);
        Assert.False(CardMeasure.IsTitle(metric, theme, 30, 50));
        Assert.Equal("bedrooms", Assert.Single(CardMeasure.Hits(metric, theme), h => h.Contains(30, 50)).Key);
        Assert.Empty(CardMeasure.DataHits(metric with { Keys = [], Groups = ImmutableDictionary<string, GroupDefinition>.Empty }, theme));
    }

    [Fact]
    public void InputShapingPreservesEmptyBranchesPathOrderAndOrdinalDeduplication()
    {
        var groups = GroupInputs.Create(["one", "empty"], 2, true, [new("{0;4}", ["b", "A", "b", "a"]), new("{7}", [])]);
        Assert.Equal(new[] { "A", "a", "b" }, groups["one"].Members); Assert.Empty(groups["empty"].Members);
        Assert.Empty(GroupInputs.Create(["individual"], 1, false, null));
        Assert.Empty(GroupInputs.Create([], 0, true, []));
        Assert.Throws<ArgumentException>(() => GroupInputs.Create(["default-label"], 1, false, [new("{0}", [])]));
        Assert.Throws<ArgumentException>(() => GroupInputs.Create([], 1, false, [new("{0}", ["R1"])]));
        Assert.Throws<ArgumentException>(() => GroupInputs.Create(["g"], 1, true, []));
        Assert.Throws<ArgumentException>(() => GroupInputs.Create(["g"], 1, true, [new("{0}", []), new("{2}", [])]));
        Assert.Throws<ArgumentException>(() => GroupInputs.Create(["g"], 1, true, [new("{0}", [])], gradient: true));
    }

    [Theory]
    [InlineData(null)] [InlineData(" ")] [InlineData("\u0001reserved")]
    public void InvalidMembersNameTheirPathAndItem(string? key)
    {
        var error = Assert.Throws<ArgumentException>(() => GroupInputs.Create(["g"], 1, true, [new("{4;9}", ["ok", key])]));
        Assert.Contains("path {4;9}, item [1]", error.Message);
        error = Assert.Throws<ArgumentException>(() => GroupInputs.Create(["g"], 1, true, [new("{4;9}", ["g"])]));
        Assert.Contains("path {4;9}, item [0]", error.Message);
    }

    [Fact]
    public void LocalCanvasUsesGroupsAndMemberTooltipsAndClear()
    {
        var state = new CanvasCardState(); state.Replace(Group(CardKind.Metric));
        state.Hover("R1"); Assert.Equal(1, state.Spec!.GroupStates["bedrooms"].HoveredMemberCount);
        state.Activate("R1"); Assert.True(state.Spec!.GroupStates["bedrooms"].IsPartiallySelected);
        state.Activate("bedrooms"); Assert.True(state.Spec!.GroupStates["bedrooms"].IsFullySelected);
        Assert.Contains("2 of 2 selected", state.TooltipFor("bedrooms")!);
        state.Activate(CardMeasure.ClearKey); Assert.False(state.Spec!.OwnerHasSelection);
        var brush = new BrushState(); brush.Prune([Group(CardKind.Legend)], []);
        Assert.Equal(new[] { "R1", "Group: Bedrooms" }, Tooltip.WithMemberGroups(["R1"], brush.Current.Membership, "R1"));
    }

    [Fact]
    public void GroupEventsUseTheExistingWireFieldsAndOneSortedMemberSnapshotPerAction()
    {
        using var legacy = JsonDocument.Parse(CardJson.Serialize(new OverlayInteraction(Guid.Empty, "old", "R1", "click")));
        Assert.Empty(legacy.RootElement.GetProperty("affectedKeys").EnumerateArray());
        var owner = Guid.NewGuid(); var brush = new BrushState(); brush.Prune([Group(CardKind.Bar)], []);
        brush.Toggle("bedrooms", BrushTargetKind.Group, new(owner, "group", "card"));
        var action = Assert.Single(brush.Current.Events);
        using var json = JsonDocument.Parse(CardJson.Serialize(OverlayInteraction.From(action)));
        var root = json.RootElement;
        Assert.Equal(owner, root.GetProperty("owner").GetGuid()); Assert.Equal("group", root.GetProperty("cardId").GetString());
        Assert.Equal("bedrooms", root.GetProperty("key").GetString()); Assert.Equal("click", root.GetProperty("kind").GetString());
        Assert.Equal("group", root.GetProperty("targetKind").GetString()); Assert.Equal("card", root.GetProperty("sourceSurface").GetString());
        Assert.Equal(new[] { "R1", "R2" }, root.GetProperty("affectedKeys").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "R1", "R2" }, root.GetProperty("selectedKeys").EnumerateArray().Select(e => e.GetString()));
        brush.Clear(new(owner, "group", "card"));
        using var clear = JsonDocument.Parse(CardJson.Serialize(OverlayInteraction.From(brush.Current.Events[^1])));
        Assert.Equal("domain", clear.RootElement.GetProperty("targetKind").GetString()); Assert.Empty(clear.RootElement.GetProperty("selectedKeys").EnumerateArray());
    }

    [Fact]
    public void GroupRevealChoosesFirstNewRowInDisplayOrderOnlyOnce()
    {
        var table = new CardSpec { Id = "table", Kind = CardKind.Table, Headers = ["Area"], MaxRows = 1, SortColumn = 0,
            Rows = [new("R1", ["3"]), new("R2", ["1"]), new("R3", ["2"])], SelectedKeys = ["R1", "R2", "R3"] };
        var previous = new HashSet<string>(StringComparer.Ordinal) { "R2" };
        Assert.Equal("R3", BrushPresentation.FirstNewRow(table, previous));
        Assert.Equal(1, TableScroll.Reveal(table, BrushPresentation.FirstNewRow(table, previous)!));
        previous.UnionWith(table.SelectedKeys); Assert.Null(BrushPresentation.FirstNewRow(table, previous));
    }

    private sealed class RecordingCanvas : ICanvas2D
    {
        public List<(Rect2 Rect, string Fill)> Rects { get; } = [];
        public List<(Point2 From, Point2 To, string Color, double Width)> Lines { get; } = [];
        public List<(Point2[] Points, string Color)> Outlines { get; } = [];
        public Size2 MeasureText(string text, double size, string font) => new(text.Length * size * .5, size * 1.5);
        public void Rect(Rect2 rect, string fill) => Rects.Add((rect, fill));
        public void RoundedRect(Rect2 rect, double radius, string fill) => Rects.Add((rect, fill));
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) { }
        public void Line(Point2 from, Point2 to, string color, double width = 1) => Lines.Add((from, to, color, width));
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => Outlines.Add((points.ToArray(), color));
        public void Text(string text, Point2 topLeft, double size, string color, string font) { }
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) { }
        public IDisposable Clip(Rect2 rect) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
