using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.FileIO;

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusPdf : Command
{
    public override string EnglishName => "ConduitPlusPdf";
    protected override void OnHelp() => RhinoApp.WriteLine("Experimental vector PDF export; output and typography remain unverified.");
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        return CommandRunner.Run(() => RunAutomated(doc, view));
    }
    public static void RunAutomated(RhinoDoc doc, RhinoView view)
    {
        UiContext.Ensure();
        RhinoApp.WriteLine("ConduitPlusPdf is experimental: export and typography remain unverified.");
        var sessions = OverlayManager.InView(doc, view);
        if (sessions.Length == 0)
        {
            OverlayManager.Set(doc, view, Guid.Empty, [.. StructuralDemo.Create(), StructuralDemo.Line()]);
            sessions = OverlayManager.InView(doc, view);
        }
        var visible = sessions.Select(s => s.Visible).ToArray();
        var size = view.ActiveViewport.Size;
        var dpi = UiContext.Dpi(view);
        // A screen physical pixel is 1/(96*dpi) inches; a PDF point is 1/72 inches.
        // Use the same physical page size and slot positions as the viewport.
        const double pointsPerCss = 72d / 96;
        var resolution = 96 * dpi;
        Console.WriteLine("PDF: creating FilePdf writer.");
        var pdf = FilePdf.Create() ?? throw new InvalidOperationException("Rhino's PDF writer is unavailable.");
        try
        {
            foreach (var session in sessions) session.Visible = false; // Suppress conduit bitmap copies in the base page.
            using var settings = new ViewCaptureSettings(view, size, resolution)
            {
                RasterMode = false, DrawBackground = false, DrawGrid = false, DrawAxis = false
            };
            Console.WriteLine("PDF: capturing vector viewport page.");
            var page = pdf.AddPage(settings);
            Console.WriteLine($"PDF: page {page}; painting vector cards.");
            if (page < 0) throw new InvalidOperationException("Rhino PDF failed to capture the active viewport.");
            for (var i = 0; i < sessions.Length; i++)
            {
                if (!visible[i]) continue;
                var session = sessions[i];
                foreach (var layout in SlotLayout.Place(session.Cards, session.Theme, new(size.Width / dpi, size.Height / dpi)))
                {
                    var spec = session.Cards.Single(c => c.Id == layout.Id);
                    using var canvas = new PdfCanvas(pdf, page, layout.X * pointsPerCss, layout.Y * pointsPerCss, pointsPerCss);
                    CardPainter.Paint(canvas, spec, session.Theme);
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
