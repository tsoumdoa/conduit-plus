using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class LabelAlignmentTests
{
    [Fact]
    public void LinkOverridesAlignBeforeMissingGeometryIsRemoved()
    {
        var styles = LabelAlignment.Align("{4}", ["empty", "B", "a"], 0, ["skip", "second", "third"],
            [], ["short"], [42], [-2, 9, 0]);
        Assert.Equal(["empty", "B", "a"], styles.Select(s => s.Key));
        Assert.Equal("second", styles[1].Text);
        Assert.All(styles, s => { Assert.Equal("short", s.ShortText); Assert.Equal(42, s.Swatch); });
        Assert.Equal(9, styles[1].Priority);
    }

    [Fact]
    public void DefaultsAndExplicitRepeatedKeys()
    {
        Assert.Equal(new LabelStyle("A", "A", null, null, 0),
            Assert.Single(LabelAlignment.Align("{0}", ["A"], 0, [], [], [], [], [])));
        var styles = LabelAlignment.Align("{0}", null, 2, ["caption"], ["K", "K"], [], [], []);
        Assert.All(styles, s => { Assert.Equal("caption", s.Text); Assert.Equal("K", s.Key); });
        Assert.Null(Assert.Single(LabelAlignment.Align("{0}", null, 1, ["caption"], [], [], [], [])).Key);
    }

    [Fact]
    public void IdentityUsesOwnerSourceAndOriginalMemberOrIndex()
    {
        var owner = Guid.NewGuid();
        var source = new LabelSource(Guid.NewGuid(), "{2}");
        var point = new LabelIdentity(owner, source, null, 0);
        Assert.NotEqual(point, point with { Owner = Guid.NewGuid() });
        Assert.NotEqual(point, point with { Source = source with { Path = "{3}" } });
        Assert.NotEqual(point, point with { PointIndex = 1 });
        Assert.NotEqual(new(owner, source, "A", null), new LabelIdentity(owner, source, "a", null));
    }

    [Fact]
    public void ReportsPathIndexAndInvalidAlignment()
    {
        Check("Priority [1]", null, 2, ["x"], [], [0, double.PositiveInfinity]);
        Check("Keys has 1", null, 2, ["x"], ["K"], []);
        Check("Keys cannot", ["K"], 0, [], ["K"], []);
        Check("either Link or Points", ["K"], 1, [], [], []);
        Check("Text has 0", null, 1, [], [], []);
        Check("Text [1]", null, 2, ["ok", " "], [], []);

        static void Check(string expected, string[]? linkKeys, int count, string[] text, string[] keys, double[] priority)
        {
            var error = Assert.Throws<ArgumentException>(() => LabelAlignment.Align("{7}", linkKeys, count, text, keys, [], [], priority));
            Assert.Contains("{7}", error.Message);
            Assert.Contains(expected, error.Message);
        }
    }
}
