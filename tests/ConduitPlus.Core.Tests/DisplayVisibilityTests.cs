using ConduitPlus.Core;
using Xunit;

public class DisplayVisibilityTests
{
    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    public void PreviewOnlyHidesPresentationWhileLockDisablesScope(bool hidden, bool locked, bool shown, bool scopeEnabled)
    {
        var visibility = DisplayVisibility.Evaluate(hidden, locked);
        Assert.Equal(shown, visibility.Shown);
        Assert.Equal(scopeEnabled, visibility.ScopeEnabled);
    }
}
