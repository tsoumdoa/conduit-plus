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

    public static Theme ForViewport(Theme selection, RhinoViewport viewport, global::Rhino.RhinoDoc document)
        => ForAttributes(selection, viewport.DisplayMode.DisplayAttributes, document);

    internal static Theme ForAttributes(Theme selection, DisplayPipelineAttributes attributes, global::Rhino.RhinoDoc document)
    {
        System.Drawing.Color[] backgrounds;
        switch (attributes.FillMode)
        {
            case DisplayPipelineAttributes.FrameBufferFillMode.SolidColor:
            case DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color:
            case DisplayPipelineAttributes.FrameBufferFillMode.Gradient4Color:
                attributes.GetFill(out var topLeft, out var bottomLeft, out var topRight, out var bottomRight);
                backgrounds = attributes.FillMode switch
                {
                    DisplayPipelineAttributes.FrameBufferFillMode.SolidColor => [topLeft],
                    DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color => [topLeft, bottomLeft],
                    _ => [topLeft, bottomLeft, topRight, bottomRight]
                };
                break;
            case DisplayPipelineAttributes.FrameBufferFillMode.Renderer:
                var render = document.RenderSettings;
                backgrounds = render.BackgroundStyle switch
                {
                    BackgroundStyle.SolidColor => [render.BackgroundColorTop],
                    BackgroundStyle.Gradient => [render.BackgroundColorTop, render.BackgroundColorBottom],
                    // Image/environment pixels aren't available here. The halo protects either extreme.
                    _ => [System.Drawing.Color.Black, System.Drawing.Color.White]
                };
                break;
            case DisplayPipelineAttributes.FrameBufferFillMode.Bitmap:
                backgrounds = [System.Drawing.Color.Black, System.Drawing.Color.White];
                break;
            default:
                backgrounds = [AppearanceSettings.ViewportBackgroundColor];
                break;
        }
        var theme = Resolve(selection);
        // Compare all configured colours, never their average: a reversed gradient must stay readable.
        // Resolved ink AND halo participate in Theme equality / raster invalidation and survive exports.
        var choice = TitleMetrics.ChooseInk(backgrounds.Select(c => new ColorRgba(c.R, c.G, c.B)), ColorRgba.Parse(theme.Surface));
        return theme with { PlainTitleInk = choice.Ink, PlainTitleHalo = choice.Halo };
    }
}
