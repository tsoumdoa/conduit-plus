using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class InputOptionsTests
{
    [Fact] public void SlotsCoverEnumAndLabels()
    {
        Assert.Equal(new[] { "Automatic", "Top left", "Top right", "Bottom left", "Bottom right", "Left centre", "Right centre" }, InputOptions.Slots.Select(o => o.Label));
        Assert.Equal(Enum.GetValues<Slot>().Select(s => s.ToString().ToLowerInvariant()).Order(), InputOptions.Slots.Skip(1).Select(o => o.Value!).Order());
        Assert.All(InputOptions.Slots.Skip(1), o => Assert.True(o.Matches(o.Value!.ToUpperInvariant())));
        Assert.True(InputOptions.Slots[0].Matches(null));
        Assert.True(InputOptions.Slots[0].Matches(""));
        Assert.DoesNotContain(InputOptions.Slots, o => o.Matches("unknown"));
    }
    [Fact] public void ThemesCoverPresets()
    {
        Assert.Null(InputOptions.Themes[0].Value);
        Assert.Equal(Theme.Presets.Select(t => t.Name), InputOptions.Themes.Skip(1).Select(o => o.Value));
        Assert.All(InputOptions.Themes.Skip(1), o => Assert.True(o.Matches(o.Value!.ToLowerInvariant())));
    }
}
