using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.FileIO;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusPdf : Command
{
    public override string EnglishName => "ConduitPlusPdf";
    protected override void OnHelp() => RhinoApp.WriteLine("Experimental PDF export with searchable cards and labels. Model can be Shaded or Vector.");
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        if (!CommandRunner.HasExportContent(doc, view)) return Result.Cancel;
        var model = new OptionToggle(UsesShading(view), "Vector", "Shaded");
        using var options = new GetOption();
        options.SetCommandPrompt("PDF export. Press Enter to export");
        options.AcceptNothing(true);
        options.AddOptionToggle("Model", ref model);
        GetResult result;
        do { result = options.Get(); } while (result == GetResult.Option);
        if (result != GetResult.Nothing) return options.CommandResult();
        var shaded = model.CurrentValue;
        return CommandRunner.RunExport(doc, view, () => Compose(doc, view, shaded));
    }
    private static bool UsesShading(RhinoView view) => view.ActiveViewport.DisplayMode?.DisplayAttributes.ShadingEnabled == true;

    public static void RunAutomated(RhinoDoc doc, RhinoView view) => RunAutomatedWithModel(doc, view, UsesShading(view));

    public static void RunAutomatedWithModel(RhinoDoc doc, RhinoView view, bool shaded)
    {
        UiContext.Ensure();
        OverlayManager.Start();
        CommandRunner.AwaitExportSceneForAutomation(doc, view);
        Compose(doc, view, shaded);
    }

    private static void Compose(RhinoDoc doc, RhinoView view, bool shaded)
    {
        RhinoApp.WriteLine("ConduitPlusPdf is experimental.");
        var sessions = OverlayManager.InView(doc, view);
        if (!CommandRunner.HasExportContent(doc, view)) return;
        OverlayManager.Settle(doc, view);
        using var labels = OverlaySession.SnapshotLabels(doc, view, 1);
        var snapshots = sessions.Select(s => (Layouts: OverlayManager.Placements(s, false), Unit: s.Unit)).ToArray();
        var omitted = sessions.Where(s => s.Visible).Select((s) => s.Cards.Length - snapshots[Array.IndexOf(sessions, s)].Layouts.Length).Sum();
        if (omitted > 0) RhinoApp.WriteLine($"Conduit+ PDF: {omitted} hidden cards omitted.");
        var visible = sessions.Select(s => s.Visible).ToArray();
        var size = view.MainViewport.Size;
        var dpi = UiContext.Dpi(view);
        // A screen physical pixel is 1/(96*dpi) inches; a PDF point is 1/72 inches.
        // Use the same physical page size and slot positions as the viewport.
        var resolution = 96 * dpi;
        Console.WriteLine("PDF: creating FilePdf writer.");
        var pdf = FilePdf.Create() ?? throw new InvalidOperationException("Rhino's PDF writer is unavailable.");
        try
        {
            foreach (var session in sessions) session.Visible = false; // Suppress conduit bitmap copies in the base page.
            int page;
            if (shaded)
            {
                var capture = new ViewCapture
                {
                    Width = size.Width, Height = size.Height, ScaleScreenItems = false,
                    TransparentBackground = true, DrawGrid = false, DrawAxes = false, DrawGridAxes = false
                };
                using var bitmap = capture.CaptureToBitmap(view)
                    ?? throw new InvalidOperationException("Rhino ViewCapture returned no shaded model bitmap.");
                page = pdf.AddPage(size.Width, size.Height, (int)Math.Round(resolution));
                if (page < 0) throw new InvalidOperationException("Rhino PDF failed to create a page.");
                pdf.DrawBitmap(page, bitmap, 0, 0, size.Width, size.Height, 0);
            }
            else
            {
                using var settings = new ViewCaptureSettings(view, size, resolution)
                {
                    RasterMode = false, DrawBackground = false, DrawGrid = false, DrawAxis = false
                };
                page = pdf.AddPage(settings);
                if (page < 0) throw new InvalidOperationException("Rhino PDF failed to capture the active viewport.");
            }
            labels?.DrawPdf(pdf, page);
            for (var i = 0; i < sessions.Length; i++)
            {
                if (!visible[i]) continue;
                var session = sessions[i];
                foreach (var layout in snapshots[i].Layouts)
                {
                    var spec = session.Cards.Single(c => c.Id == layout.Id);
                    using var canvas = new PdfCanvas(pdf, page, layout.X, layout.Y, snapshots[i].Unit, dpi);
                    CardPainter.Paint(canvas, BrushPresentation.WithoutHover(spec), session.Theme);
                }
            }
            var path = Path.Combine(OutputPaths.Captures, "overlay-vector.pdf");
            Console.WriteLine("PDF: writing page.");
            pdf.Write(path);
            RhinoApp.WriteLine($"Saved {path}");
        }
        finally
        {
            for (var i = 0; i < sessions.Length; i++) sessions[i].Visible = visible[i];
            (pdf as IDisposable)?.Dispose();
        }
    }
}
