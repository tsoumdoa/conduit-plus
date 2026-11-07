using System.Drawing.Imaging;
using ConduitPlus.Core;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Input;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal static class OutputPaths
{
    public static string Root => Path.GetFullPath(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ConduitPlus"));
    public static string Captures { get { var path = Path.Combine(Root, "captures"); Directory.CreateDirectory(path); return path; } }
}

internal static class CommandRunner
{
    public static Result Run(Action action)
    {
        if (OverlayManager.Busy) { RhinoApp.WriteLine("Conduit+ is already preparing cards."); return Result.Cancel; }
        UiContext.Ensure();
        OverlayManager.Busy = true;
        try { action(); return Result.Success; }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+: {e}"); return Result.Failure; }
        finally { OverlayManager.Busy = false; }
    }
}

public sealed class ConduitPlusDemo : Command
{
    public override string EnglishName => "ConduitPlusDemo";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        return CommandRunner.Run(() =>
        {
            var members = new List<(string Key, object Geometry)>();
            // Five bays: lower chord, upper chord, posts and diagonals (M01–M20).
            for (var i = 0; i < 5; i++)
            {
                var x = i * 5d;
                members.Add(($"M{i + 1:00}", new Line(new Point3d(x, 0, 0), new Point3d(x + 5, 0, 0))));
                members.Add(($"M{i + 6:00}", new Line(new Point3d(x, 0, 5), new Point3d(x + 5, 0, 5))));
                members.Add(($"M{i + 11:00}", new Line(new Point3d(x, 0, 0), new Point3d(x, 0, 5))));
                members.Add(($"M{i + 16:00}", new Line(new Point3d(x, 0, 0), new Point3d(x + 5, 0, 5))));
            }
            var link = new Link(members.Select(m => m.Key), members) { ShowBase = true };
            OverlayManager.Set(doc, view, Guid.Empty, [.. StructuralDemo.Create(), StructuralDemo.Line()], [link]);
        });
    }
}

public sealed class ConduitPlusCapture : Command
{
    public override string EnglishName => "ConduitPlusCapture";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        double multiplier = 2;
        var result = RhinoGet.GetNumber("Capture scale (1, 2 or 4)", true, ref multiplier, 1, 4);
        if (result != Result.Success) return result;
        if (multiplier is not (1 or 2 or 4)) return Result.Failure;
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        return CommandRunner.Run(() => Capture(doc, view, (int)multiplier));
    }

    public static void RunAutomated(RhinoDoc doc, RhinoView view, int scale, bool compact = false)
    {
        UiContext.Ensure();
        var fixture = StructuralDemo.Create();
        if (compact) fixture = [fixture[0] with { Width = 180 }];
        OverlayManager.Set(doc, view, Guid.Empty, fixture);
        Capture(doc, view, scale);
    }

    private static void Capture(RhinoDoc doc, RhinoView view, int multiplier)
    {
        UiContext.Ensure();
        if (multiplier is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(multiplier));
        var sessions = OverlayManager.InView(doc, view);
        if (sessions.Length == 0)
        {
            OverlayManager.Set(doc, view, Guid.Empty, StructuralDemo.Create());
            sessions = OverlayManager.InView(doc, view);
        }
        var size = view.ActiveViewport.Size;
        var visible = sessions.Where(s => s.Visible).ToArray();
        var before = visible.Select(s => s.FrameCount).ToArray();
        try
        {
            // Every owner's cards use the requested resolution, including Grasshopper sessions.
            foreach (var session in sessions) session.CaptureMultiplier = multiplier;
            var capture = new ViewCapture
            {
                Width = checked(size.Width * multiplier), Height = checked(size.Height * multiplier),
                ScaleScreenItems = false, TransparentBackground = true,
                DrawAxes = false, DrawGrid = false, DrawGridAxes = false
            };
            using var image = capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("Rhino ViewCapture returned no bitmap.");
            if (visible.Where((s, i) => s.FrameCount == before[i]).Any())
                throw new InvalidOperationException("ViewCapture did not draw the overlay; refusing to save a blank PNG.");
            var path = Path.Combine(OutputPaths.Captures, $"raster-{multiplier}x.png");
            image.Save(path, ImageFormat.Png);
            File.WriteAllText(Path.ChangeExtension(path, ".json"), CardJson.Serialize(new
            {
                multiplier, dpi = UiContext.Dpi(view), width = image.Width, height = image.Height,
                sessions = visible.Select(s => new { s.Owner, raster = s.RasterCaptureState }).ToArray()
            }));
            RhinoApp.WriteLine($"Saved {path}");
        }
        finally
        {
            foreach (var session in sessions) session.CaptureMultiplier = 1;
            view.Redraw();
        }
    }
}
