using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

internal static class ExportChecks
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Instance)!.Invoke(obj, args);
    private static void Field(object obj, string name, object value) => obj.GetType().GetField(name, Instance)!.SetValue(obj, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(RhinoDoc doc, RhinoView fullView, object fullSession, CardSpec[] fullCards, string output, bool safe, int scale, GeometryBase[]? geometry = null)
    {
        var full = new[] { Capture(doc, fullView, fullSession, fullCards, scale, output) };
        File.WriteAllText(Path.Combine(output, "full-capture-checks.json"), CardJson.Serialize(full));
        if (!safe)
        {
            // Vector viewport export reads document geometry, whereas the headless demo uses conduit base wires.
            var ids = geometry is not null ? geometry.Select(g => doc.Objects.Add(g)).ToArray()
                : DemoTruss.Create(bays: 4).Members.Select(m => doc.Objects.AddLine(new Point3d(m.Start.X, 0, m.Start.Y), new Point3d(m.End.X, 0, m.End.Y))).ToArray();
            try { ConduitPlusPdf.RunAutomated(doc, fullView); }
            finally { foreach (var id in ids) doc.Objects.Delete(id, true); }
        }
    }
    internal static void RunCompact(RhinoDoc doc, RhinoView fullView, object fullSession, CardSpec[] fullCards, string output, int scale, string state)
    {
        Console.WriteLine("Preparing compact fixture.");
        var theme = (Theme)Get(fullSession, "Theme");
        OverlayManager.Remove((Guid)Get(fullSession, "Owner"));
        var compactOutput = Path.Combine(output, "compact"); Directory.CreateDirectory(compactOutput);
        using var cleanup = new RestoreOutput(output);
        Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", compactOutput);
        var view = fullView;
        var owner = Guid.NewGuid();
        try
        {
            doc.Views.ActiveView = view;
            var truss = DemoTruss.Create(bays: 4);
            CardSpec[] cards = [fullCards[0] with { Width = 180, WiredSlot = Slot.Tl }];
            var link = new Link(truss.Members.Select(m => m.Key), truss.Members.Select(m => (m.Key, (object)new Line(new Point3d(m.Start.X, 0, m.Start.Y), new Point3d(m.End.X, 0, m.End.Y))))) { ShowBase = true };
            OverlayManager.Set(doc, view, owner, cards, [link], theme: theme);
            Console.WriteLine("Compact fixture published.");
            var session = typeof(OverlayManager).GetMethod("Get", Internal)!.Invoke(null, [doc, view, (Guid?)owner])!;
            view.ActiveViewport.SetCameraLocations(new(12, 0, 1.5), new(31, -32, 17));
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(new Point3d(-8, -10, -1), new Point3d(32, 10, 18)));
            var dpi = Dpi(view);
            if (state == "tooltip")
            {
                // Supply a ready tooltip without depending on a hidden window's physical cursor/timer.
                Call(session, "Cursor", new Point2(400, 280), true);
                var tooltip = RasterDrawing.RenderTooltip(["M04", "Tooltip export sentinel"], theme, dpi);
                Field(session, "_tooltipBitmap", tooltip); Field(session, "_tooltipDisplay", new DisplayBitmap(tooltip)); Field(session, "_tooltipReady", true);
            }
            if (state == "selected") Call(session, "Select", "model", "M04", BrushSelectMode.Toggle);
            Console.WriteLine($"Capturing {state} state.");
            var capture = Capture(doc, view, session, cards, scale, compactOutput);
            File.WriteAllText(Path.Combine(output, "compact-capture-check.json"), CardJson.Serialize(new { state, capture, selected = OverlayManager.Snapshot(owner).SelectedKeys }));
        }
        finally { OverlayManager.Remove(owner); }
    }
    private static object Capture(RhinoDoc doc, RhinoView view, object session, CardSpec[] cards, int scale, string output)
    {
        var before = (long)Get(session, "FrameCount");
        typeof(ConduitPlusCapture).GetMethod("Capture", Internal)!.Invoke(null, [doc, view, scale]);
        var after = (long)Get(session, "FrameCount"); Require(after > before, "Capture did not draw a new overlay frame.");
        using var image = new Bitmap(Path.Combine(output, "captures", $"raster-{scale}x.png"));
        var size = view.ActiveViewport.Size;
        Require(image.Width == size.Width * scale && image.Height == size.Height * scale, "PNG dimensions mismatch.");
        var theme = (Theme)Get(session, "Theme");
        var surface = GdiCanvas.ParseColor(theme.Surface);
        var layouts = SlotLayout.Place(cards, theme, new(image.Width, image.Height), Dpi(view) * scale);
        for (var i = 0; i < layouts.Length; i++)
        for (var j = i + 1; j < layouts.Length; j++)
        {
            var a = layouts[i]; var b = layouts[j];
            Require(a.X + a.W <= b.X || b.X + b.W <= a.X || a.Y + a.H <= b.Y || b.Y + b.H <= a.Y, "Capture cards overlap.");
        }
        var regions = layouts.Select(l =>
        {
            Require(l.X >= 0 && l.Y >= 0 && l.X + l.W <= image.Width && l.Y + l.H <= image.Height, "Card outside capture.");
            var samples = 0; var covered = 0; var ink = 0;
            for (var y = (int)l.Y + 5 * scale; y < l.Y + l.H - 5 * scale; y += 3 * scale)
            for (var x = (int)l.X + 5 * scale; x < l.X + l.W - 5 * scale; x += 3 * scale)
            {
                samples++; var c = image.GetPixel(x, y); if (c.A >= surface.A * .98) covered++;
                if (c.A >= surface.A * .98 && Math.Abs(c.R - surface.R) + Math.Abs(c.G - surface.G) + Math.Abs(c.B - surface.B) > 90) ink++;
            }
            var fraction = covered / (double)samples;
            Require(fraction > .98 && ink > 5, "Missing card pixels/text.");
            return new { l.Id, l.X, l.Y, l.W, l.H, samples, covered, ink, fraction };
        }).ToArray();
        Console.WriteLine($"PNG {scale}x: {image.Width}x{image.Height}, frames {before}->{after}, {regions.Length} card regions.");
        return new { scale, image.Width, image.Height, before, after, regions };
    }
    private static double Dpi(RhinoView view) => (double)typeof(OverlayManager).Assembly.GetType("ConduitPlus.Rhino.UiContext")!.GetMethod("Dpi")!.Invoke(null, [view])!;
    private sealed class RestoreOutput(string output) : IDisposable { public void Dispose() => Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", output); }
}
