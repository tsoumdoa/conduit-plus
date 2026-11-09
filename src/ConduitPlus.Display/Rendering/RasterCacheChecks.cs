using System.Drawing.Imaging;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

internal static class RasterCacheChecks
{
    public static void Run(RhinoDoc doc, RhinoView view, string outputRoot)
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
        var captures = Path.Combine(outputRoot, "captures"); Directory.CreateDirectory(captures);
        bitmap.Save(Path.Combine(captures, "raster-line-2x.png"), ImageFormat.Png);
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        try
        {
            OverlayManager.Set(doc, view, owner, StructuralDemo.Create());
            OverlayManager.Set(doc, view, otherOwner, StructuralDemo.Create());
            var session = OverlayManager.Get(doc, view, owner);
            if (OverlayManager.InView(doc, view).Count(s => s.Owner == owner || s.Owner == otherOwner) != 2)
                throw new InvalidOperationException("Owners evicted one another's session.");
            session.ScrollTo("results", 3);
            session.SetHover("results", "M04");
            session.Update(StructuralDemo.Create());
            if (session.Card("results") is not { ScrollOffset: 3, HoverKey: "M04" })
                throw new InvalidOperationException("Incoming specs reset scroll or hover view state.");
            OverlayManager.Remove(owner);
            if (OverlayManager.InView(doc, view).Any(s => s.Owner == owner) || !OverlayManager.InView(doc, view).Any(s => s.Owner == otherOwner))
                throw new InvalidOperationException("Removing an owner affected another session.");
        }
        finally { OverlayManager.Remove(owner); OverlayManager.Remove(otherOwner); }
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "raster-cache-check.json"), CardJson.Serialize(new
        {
            passed = true, initial = 7, afterHover = 8, afterSelection = 9, afterClean = 9, afterScale = 16,
            remaining = renderer.Prepared.Count, independentOwners = true, scrollAndHoverSurviveSolves = true
        }));
        RhinoApp.WriteLine("Raster cache lifecycle checks passed.");
    }
}
