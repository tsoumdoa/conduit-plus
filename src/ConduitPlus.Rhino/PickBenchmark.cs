using System.Diagnostics;
using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal static class PickBenchmark
{
    internal static string Run(RhinoDoc doc, RhinoView view)
    {
        var items = new List<(string Key, object Geometry)>();
        var breps = new List<Brep>();
        var viewport = view.ActiveViewport;
        using var session = new OverlaySession(doc, view, Guid.NewGuid());
        viewport.PushViewProjection();
        try
        {
            for (var i = 0; i < 2000; i++)
            {
                var x = i % 50 * 3d; var y = i / 50 * 3d;
                items.Add(($"M{i:0000}", new Line(new Point3d(x, y, 0), new Point3d(x + 2, y + 1, 0))));
            }
            for (var i = 0; i < 200; i++)
            {
                var x = i % 20 * 7.5; var y = i / 20 * 12d;
                var brep = new BoundingBox(new Point3d(x, y, -4), new Point3d(x + 4, y + 6, -2)).ToBrep();
                breps.Add(brep); items.Add(($"B{i:000}", brep));
            }
            var build = Stopwatch.StartNew();
            var link = new Link(items.Select(p => p.Key), items);
            var buildMilliseconds = build.Elapsed.TotalMilliseconds;
            session.SetLinks([link]);
            if (!viewport.ZoomBoundingBox(link.Bounds)) throw new InvalidOperationException("Cannot frame picking benchmark.");
            view.Redraw();
            OverlaySession[] sessions = [session];
            var size = viewport.Size;
            var positions = Enumerable.Range(0, 1200).Select(i => new System.Drawing.Point(
                (int)((i % 40 + .5) * size.Width / 40), (int)((i / 40 + .5) * size.Height / 30))).ToArray();
            for (var i = 0; i < 64; i++) ModelPicking.Pick(view, sessions, positions[i]);
            var samples = new List<double>(); var wires = 0; var faces = 0;
            for (var pass = 0; pass < 3; pass++)
            foreach (var point in positions)
            {
                var start = Stopwatch.GetTimestamp();
                var hit = ModelPicking.Pick(view, sessions, point);
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (hit is { } picked) { if (picked.Hit.IsMesh) faces++; else wires++; }
            }
            var sorted = samples.Order().ToArray();
            var p50 = sorted[(int)Math.Ceiling(.5 * sorted.Length) - 1];
            var p95 = sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1];
            File.WriteAllText(Path.Combine(OutputPaths.Root, "samples-pick.json"), CardJson.Serialize(new
            {
                measured = DateTimeOffset.Now, rhino = RhinoApp.Version.ToString(), runtime = Environment.Version.ToString(),
                size, lines = 2000, brepBoxes = 200, grid = new { columns = 40, rows = 30, passes = 3 },
                warmups = 64, buildMilliseconds, p50, p95, targetP95 = 5, meetsTarget = p95 <= 5, wires, faces, samples,
                includes = "PickContext setup/clipping, per-key projected bbox prefilter and cached proxy tests; excludes Link construction, card raster updates, redraw and physical input"
            }));
            return FormattableString.Invariant($"Model pick (2,000 lines + 200 brep boxes; 3,600 samples after 64 warmups): {p50:0.000} / {p95:0.000} ms p50 / p95; target ≤ 5 ms p95: {(p95 <= 5 ? "met" : "missed")}. Wire/face hits: {wires}/{faces}. Details: samples-pick.json.\n");
        }
        finally
        {
            foreach (var brep in breps) brep.Dispose();
            viewport.PopViewProjection();
        }
    }
}
