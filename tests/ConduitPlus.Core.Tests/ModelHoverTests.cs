using System.Collections.Frozen;
using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ModelHoverTests
{
    [Theory]
    [InlineData("canvas", false)]
    [InlineData("model", true)]
    [InlineData("card", true)]
    [InlineData("label", true)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void ModelHoverRequiresViewportOrigin(string? surface, bool expected)
    {
        var snapshot = new BrushState().Current with
        {
            HoverOrigin = surface is null ? null : new BrushOrigin("participant", surface, 1),
            HoveredKeys = new[] { "member" }.ToFrozenSet(StringComparer.Ordinal)
        };

        Assert.Equal(expected, snapshot.ModelHoveredKeys.Contains("member"));
        Assert.Contains("member", (IReadOnlySet<string>)snapshot.HoveredKeys);
    }
}
