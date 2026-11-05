using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public static class ConduitPlusRenderProbe
{
    public static async Task RunAutomatedAsync(RhinoDoc doc, RhinoView view)
    {
        UiContext.Ensure();
        var cards = new CardStore();
        cards.Update([.. StructuralDemo.Create(), StructuralDemo.Line()]);
        using var renderer = new RasterCardRenderer();
        renderer.Prepare(cards.Snapshot, Theme.Paper, 2);
        var before = renderer.Prepared.ToDictionary(p => p.Key, p => p.Value);
        cards.Update(cards.Snapshot.Select(c => c.Id == "load-cases" ? c with { HoverKey = c.Keys[2] } : c));
        renderer.Prepare(cards.Snapshot, Theme.Paper, 2);
        if (renderer.RenderCount != 8 || before.Where(p => p.Key != "load-cases").Any(p => !ReferenceEquals(p.Value, renderer.Prepared[p.Key])))
            throw new InvalidOperationException("Raster hover invalidated an unaffected card.");
        cards.Update(cards.Snapshot.Select(c => c.Id == "load-cases" ? c with { SelectedKeys = [c.Keys[3]] } : c));
        renderer.Prepare(cards.Snapshot, Theme.Paper, 2);
        if (renderer.RenderCount != 9) throw new InvalidOperationException("Raster selection invalidated an unaffected card.");
        renderer.Prepare(cards.Snapshot, Theme.Paper, 2);
        if (renderer.RenderCount != 9) throw new InvalidOperationException("Clean raster cards were repainted.");
        renderer.Prepare(cards.Snapshot, Theme.Paper, 4);
        if (renderer.RenderCount != 16) throw new InvalidOperationException("Scale change did not regenerate every raster card.");
        renderer.Prepare(cards.Snapshot.Take(1).ToArray(), Theme.Paper, 4);
        if (renderer.Prepared.Count != 1) throw new InvalidOperationException("Removed raster cards remained cached.");
        using var bitmap = RasterDrawing.Render(StructuralDemo.Line(), Theme.Paper, 2);
        bitmap.Save(Path.Combine(SpikePaths.Captures, "raster-line-2x.png"), ImageFormat.Png);
        using var web = new WebSurface();
        var captures = await web.CaptureAsync([StructuralDemo.Line()], Theme.Paper, 2);
        try { captures[0].Bitmap.Save(Path.Combine(SpikePaths.Captures, "web-line-2x.png"), ImageFormat.Png); }
        finally { foreach (var capture in captures) capture.Dispose(); }
        Directory.CreateDirectory(SpikePaths.Root);
        File.WriteAllText(Path.Combine(SpikePaths.Root, "raster-cache-check.json"), CardJson.Serialize(new { passed = true, initial = 7, afterHover = 8, afterSelection = 9, afterClean = 9, afterScale = 16, remaining = renderer.Prepared.Count, webLineLayoutAndHits = "validated by WebSurface" }));
        RhinoApp.WriteLine("Raster cache checks and raster/web Line snapshots passed.");
    }
}
