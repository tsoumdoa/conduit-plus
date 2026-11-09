using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class ViewportNamesTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData(" ACTIVE viewport ")]
    public void ActiveValues(string? value) => Assert.Null(ViewportNames.Requested(value));
    [Fact] public void NamedValuesAreTrimmed() => Assert.Equal("Top", ViewportNames.Requested(" Top "));
    [Fact] public void ResolutionPreservesMatchingBindingAndReportsDuplicates()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        (Guid, string)[] views = [(a, "Top"), (b, "TOP")];
        Assert.Equal((b, true), ViewportNames.Resolve(views, "top", b));
        Assert.Equal((a, true), ViewportNames.Resolve(views, "top", Guid.NewGuid()));
        Assert.Equal(((Guid?)null, false), ViewportNames.Resolve(views, "Missing", a));
        Assert.Equal((a, false), ViewportNames.Resolve([(a, "Top"), (b, "Front")], "top", b));
    }
}
