using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public class LabelPickingTests
{
    private static LabelPlacement Label(double x, LabelForm form = LabelForm.Dot) =>
        new(new(Guid.NewGuid(), default, null, 0), new(x - 2.5, 17.5, 5, 5),
            form == LabelForm.Dot ? default : new(20, 11, 60, 18), form, false, 1);

    [Fact]
    public void HudAndUnpublishedProjectionTakePrecedence()
    {
        var label = Label(10);
        Assert.Equal(label, LabelPicking.Hit([label], new(10, 20), true, false));
        Assert.Null(LabelPicking.Hit([label], new(10, 20), false, false));
        Assert.Null(LabelPicking.Hit([label], new(10, 20), true, true));
        Assert.Null(LabelPicking.Hit([], new(10, 20), true, false));
    }

    [Fact]
    public void OverlappingPillsFollowPaintOrderAndDemotedPillsLoseTheirBounds()
    {
        var bottom = Label(0, LabelForm.Full);
        var top = Label(10, LabelForm.Short);
        Assert.Equal(top, LabelPicking.Hit([bottom, top], new(30, 20), true, false));
        var demoted = top with { Form = LabelForm.Dot };
        Assert.Equal(bottom, LabelPicking.Hit([bottom, demoted], new(30, 20), true, false));
    }

    [Fact]
    public void DotsUseFivePixelRadiusAndPreferNearestThenTopmost()
    {
        var left = Label(10);
        var right = Label(16);
        Assert.Equal(left, LabelPicking.Hit([left, right], new(12, 20), true, false));
        Assert.Equal(right, LabelPicking.Hit([left, right], new(13, 20), true, false));
        Assert.Null(LabelPicking.Hit([left], new(14, 24), true, false));
    }

    [Fact]
    public void LabelMemberSelectionUpdatesContainingGroupAndClearAndPruneCountOnce()
    {
        var group = new CardSpec { Id = "group", Kind = CardKind.Metric, Keys = ["g"],
            Groups = System.Collections.Immutable.ImmutableDictionary<string, GroupDefinition>.Empty.Add("g", new(["a", "b"])) };
        var brush = new BrushState();
        brush.Prune([group], ["a", "b", "point"]);
        var source = new BrushActionSource(Guid.NewGuid(), "label", "label");
        Assert.True(brush.Select("a", BrushSelectMode.Replace, source, BrushTargetKind.Member));
        Assert.Equal(["a"], brush.Current.SelectedKeys);
        Assert.Equal(1, brush.Current.GroupStates["g"].SelectedMemberCount);
        Assert.False(brush.Select("a", BrushSelectMode.Add, source, BrushTargetKind.Member));
        Assert.True(brush.Clear(source));
        Assert.False(brush.Clear(source));
        Assert.True(brush.Select("point", BrushSelectMode.Toggle, source, BrushTargetKind.Member));
        var revision = brush.Current.EventRevision;
        brush.Prune([group], ["a", "b"]);
        Assert.Empty(brush.Current.SelectedKeys);
        Assert.Equal(revision, brush.Current.EventRevision);
        Assert.Equal(3, revision);
    }

    [Fact]
    public void PopoverKeepsSideAndItsExitRegionConnectsToTheDot()
    {
        var dot = Label(100).Dot;
        var (pill, left) = LabelPicking.Popover(dot, 50, new(200, 100), true);
        Assert.True(left);
        Assert.True(LabelPicking.Retains(dot, pill, new(96, 20)));
        Assert.True(LabelPicking.Retains(dot, pill, new(pill.X - 3, 20)));
        Assert.False(LabelPicking.Retains(dot, pill, new(pill.X - 3.1, 20)));
        Assert.False(LabelPicking.Popover(Label(10).Dot, 50, new(200, 100), left).Left);
    }
}
