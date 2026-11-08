using System.Collections.Immutable;
using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class SelectionModeTests
{
    private static CardSpec Group => new() { Id = "group", Kind = CardKind.Metric, Keys = ["g"],
        Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("g", new(["a", "b"])) };
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<BrushSelectMode>())
            for (var mask = 0; mask < 8; mask++)
                foreach (var group in new[] { false, true }) yield return [mode, mask, group];
    }
    [Theory, MemberData(nameof(Cases))]
    public void ModesChangeOnlyEffectiveMembersOnce(BrushSelectMode mode, int mask, bool group)
    {
        var brush = new BrushState(); brush.Prune([Group], ["a", "b", "c"]);
        var keys = new[] { "a", "b", "c" };
        var before = keys.Where((_, i) => (mask & (1 << i)) != 0).ToHashSet(StringComparer.Ordinal);
        foreach (var key in before) brush.Select(key, BrushSelectMode.Add);
        var target = group ? new[] { "a", "b" } : new[] { "a" };
        var expected = new HashSet<string>(before);
        if (mode == BrushSelectMode.Replace) expected.Clear();
        if (mode == BrushSelectMode.Remove || mode == BrushSelectMode.Toggle && target.All(before.Contains)) expected.ExceptWith(target);
        else expected.UnionWith(target);
        var affected = before.Except(expected).Concat(expected.Except(before)).Order(StringComparer.Ordinal).ToArray();
        var snapshot = brush.Current; var notifications = 0; brush.Changed += _ => notifications++;
        Assert.Equal(affected.Length > 0, brush.Select(group ? "g" : "a", mode));
        Assert.Equal(expected.Order(StringComparer.Ordinal), brush.Current.SelectedKeys.Order(StringComparer.Ordinal));
        Assert.Equal(expected.Count(k => k is "a" or "b"), brush.Current.GroupStates["g"].SelectedMemberCount);
        Assert.Equal(affected.Length > 0 ? 1 : 0, notifications);
        Assert.Equal(snapshot.SelectionRevision + notifications, brush.Current.SelectionRevision);
        Assert.Equal(snapshot.EventRevision + notifications, brush.Current.EventRevision);
        if (notifications == 0) Assert.Same(snapshot, brush.Current);
        else { var action = brush.Current.Events[^1]; Assert.Equal("click", action.Kind); Assert.Equal(affected, action.AffectedKeys); }
    }
    [Theory]
    [InlineData(false, false, BrushSelectMode.Replace)]
    [InlineData(true, false, BrushSelectMode.Add)]
    [InlineData(false, true, BrushSelectMode.Remove)]
    [InlineData(true, true, BrushSelectMode.Toggle)]
    public void Modifiers(bool shift, bool ctrl, BrushSelectMode mode) => Assert.Equal(mode, BrushSelection.FromModifiers(shift, ctrl));

    [Fact]
    public void ReplaceAcrossSharedParticipantsAndCanvasNoOps()
    {
        using var domain = new InteractionDomain(new(1, Guid.NewGuid()));
        var a = domain.RegisterParticipant("a"); var b = domain.RegisterParticipant("b");
        domain.Stage(domain.BeginPublication(a)!, new("a", [Group], ["a", "b"]));
        domain.Stage(domain.BeginPublication(b)!, new("b", [], ["c"])); domain.Commit();
        var source = new BrushActionSource(Guid.NewGuid(), "model");
        domain.Select(a, "g", BrushSelectMode.Replace, BrushTargetKind.Group, source);
        var notifications = 0; domain.Changed += _ => notifications++;
        Assert.True(domain.Select(b, "c", BrushSelectMode.Replace, BrushTargetKind.Member, source));
        Assert.Equal(new[] { "c" }, domain.Current.SelectedKeys);
        Assert.Equal(new[] { "a", "b", "c" }, domain.Current.Events[^1].AffectedKeys);
        Assert.False(domain.Select(b, "c", BrushSelectMode.Replace, BrushTargetKind.Member, source));
        Assert.False(domain.Select(a, "a", BrushSelectMode.Remove, BrushTargetKind.Member, source));
        Assert.Equal(1, notifications);
        var canvas = new CanvasCardState(); canvas.Replace(Group);
        canvas.Activate("g"); var revision = canvas.Current.SelectionRevision;
        canvas.Activate("g"); canvas.Activate("g", BrushSelectMode.Add);
        Assert.Equal(revision, canvas.Current.SelectionRevision);
    }
}
