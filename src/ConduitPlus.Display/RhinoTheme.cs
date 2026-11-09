using ConduitPlus.Core;
using Rhino.Runtime;

namespace ConduitPlus.Rhino;

/// <summary>Rhino UI appearance detection stays outside the pure Core theme policy.</summary>
public static class RhinoTheme
{
    // Verified in the pinned RhinoCommon 8.20.25147.11001-rc reference assembly with ILSpy.
    // Do not use UsingDefaultDarkModeColors: that tests an unmodified default color scheme.
    public static Theme Resolve(Theme selection) => selection == Theme.Auto
        ? selection.Resolve(HostUtils.RunningInDarkMode) : selection;
}
