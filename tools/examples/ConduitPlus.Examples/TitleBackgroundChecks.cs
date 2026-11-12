using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Display;

internal static class TitleBackgroundChecks
{
    internal static void Run(RhinoDoc document)
    {
        using var render = document.RenderSettings;
        var originalStyle = render.BackgroundStyle;
        var originalTop = render.BackgroundColorTop;
        var originalBottom = render.BackgroundColorBottom;
        try
        {
            // Get an attribute snapshot without registering/changing any display mode. RhinoCore's
            // native CopyDisplayMode can access-violate when run repeatedly in a hidden host.
            var mode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.WireframeId);
            var attributes = mode.DisplayAttributes;
            var resolve = typeof(RhinoTheme).GetMethod("ForAttributes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            Theme Resolve() => (Theme)resolve.Invoke(null, [Theme.Paper, attributes, document])!;
            attributes.SetFill(Color.Black, Color.White);
            var gradient = Resolve();
            Require(gradient.PlainTitleHalo is not null, "Black/white gradient needs a title halo.");
            attributes.SetFill(Color.White, Color.Black);
            Require(Resolve() == gradient, "Reversed extrema must retain readable ink and halo.");
            attributes.SetFill(Color.Black, Color.FromArgb(30, 40, 50), Color.FromArgb(50, 40, 30), Color.White);
            Require(Resolve().PlainTitleHalo is not null, "All four gradient corners must participate.");
            attributes.FillMode = DisplayPipelineAttributes.FrameBufferFillMode.Renderer;
            render.BackgroundStyle = BackgroundStyle.SolidColor;
            render.BackgroundColorTop = Color.Black;
            document.RenderSettings = render;
            Require(Resolve().PlainTitleInk == "#FFFFFF" && Resolve().PlainTitleHalo is null, "Rendered black background needs white title ink.");
            render.BackgroundColorTop = Color.White;
            document.RenderSettings = render;
            Require(Resolve().PlainTitleInk == "#000000", "Rendered white background needs black title ink.");
            render.BackgroundStyle = BackgroundStyle.Gradient;
            render.BackgroundColorBottom = Color.Black;
            document.RenderSettings = render;
            Require(Resolve().PlainTitleHalo is not null, "Rendered gradient needs both render colours.");
            Console.WriteLine("Title backgrounds: two/four-colour gradients, reversed gradient and Rendered solid/gradient settings passed.");
        }
        finally
        {
            render.BackgroundStyle = originalStyle;
            render.BackgroundColorTop = originalTop;
            render.BackgroundColorBottom = originalBottom;
            document.RenderSettings = render;
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
