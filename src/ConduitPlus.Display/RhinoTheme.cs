using ConduitPlus.Core;
using Rhino.Runtime;
using Rhino.Display;
using Rhino.ApplicationSettings;

namespace ConduitPlus.Rhino;

/// <summary>Rhino UI appearance detection stays outside the pure Core theme policy.</summary>
public static class RhinoTheme
{
    // Verified in the pinned RhinoCommon 8.20.25147.11001-rc reference assembly with ILSpy.
    // Do not use UsingDefaultDarkModeColors: that tests an unmodified default color scheme.
    public static Theme Resolve(Theme selection) => selection == Theme.Auto
        ? selection.Resolve(HostUtils.RunningInDarkMode) : selection;

    public static Theme ForViewport(Theme selection, RhinoViewport viewport)
    {
        var background = AppearanceSettings.ViewportBackgroundColor;
        var attributes = viewport.DisplayMode.DisplayAttributes;
        if (attributes.FillMode is DisplayPipelineAttributes.FrameBufferFillMode.SolidColor
            or DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color or DisplayPipelineAttributes.FrameBufferFillMode.Gradient4Color)
        {
            attributes.GetFill(out var topLeft, out var bottomLeft, out var topRight, out var bottomRight);
            // A representative background for plain titles; captures retain the resolved ink even when transparent.
            background = attributes.FillMode switch
            {
                DisplayPipelineAttributes.FrameBufferFillMode.SolidColor => topLeft,
                DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color => System.Drawing.Color.FromArgb(
                    (topLeft.R + bottomLeft.R) / 2, (topLeft.G + bottomLeft.G) / 2, (topLeft.B + bottomLeft.B) / 2),
                _ => System.Drawing.Color.FromArgb((topLeft.R + bottomLeft.R + topRight.R + bottomRight.R) / 4,
                    (topLeft.G + bottomLeft.G + topRight.G + bottomRight.G) / 4, (topLeft.B + bottomLeft.B + topRight.B + bottomRight.B) / 4)
            };
        }
        return Resolve(selection) with { PlainTitleInk = TitleMetrics.InkForBackground(new(background.R, background.G, background.B)) };
    }
}
