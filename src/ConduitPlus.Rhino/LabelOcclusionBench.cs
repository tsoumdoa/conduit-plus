using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using ConduitPlus.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Environment = System.Environment;

namespace ConduitPlus.Rhino;

internal static class LabelOcclusionBench
{
    internal static void RunAutomated(RhinoDoc doc, RhinoView view)
    {
        var destination = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")
            ?? throw new InvalidOperationException("Set CONDUITPLUS_RESULTS.");
        var checks = new Dictionary<string, bool>();
        var cases = new List<BenchmarkCase>();
        var viewport = view.ActiveViewport;
        Camera(viewport, 0);
        var changes = RunFixtures(doc, viewport, checks);
        foreach (var parallel in new[] { false, true })
        {
            if (parallel) viewport.ChangeToParallelProjection(true);
            foreach (var divisions in new[] { 158, 500 })
            {
                var triangles = Grid(divisions);
                var timer = Stopwatch.StartNew();
                var index = Task.Run(() => OccluderIndex.Build(triangles)).GetAwaiter().GetResult();
                var buildMs = timer.Elapsed.TotalMilliseconds;
                foreach (var deviceScale in new[] { 1, 2 })
                foreach (var count in new[] { 500, 5000 })
                {
                    var anchors = Enumerable.Range(0, count).Select(i => new Point3d(
                        ((i * 137 % 997) / 997d - 0.5) * 44,
                        ((i * 151 % 991) / 991d - 0.5) * 44, i % 2 == 0 ? -3 : 3)).ToArray();
                    var schedule = new OcclusionSchedule(count);
                    var segments = new OcclusionSegment?[count];
                    var projection = new double[600];
                    var query = new double[600];
                    var total = new double[600];
                    var last = new int[count];
                    var maxGap = 0;
                    var mismatches = 0;
                    var settledFrames = 0;
                    var budget = count == 500 ? 1.6 : 3.5;
                    Func<bool> timeRemaining = () => timer.Elapsed.TotalMilliseconds < budget;
                    for (var frame = -100; frame < 604; frame++)
                    {
                        if (frame < 600) Camera(viewport, frame);
                        var revision = Math.Min(frame, 599);
                        timer.Restart();
                        var projector = new LabelOcclusionScene.Projection(viewport, deviceScale);
                        for (var i = 0; i < count; i++)
                            segments[i] = projector.Project(anchors[i]);
                        var projected = timer.Elapsed.TotalMilliseconds;
                        schedule.Classify(index, 1, revision, segments, [], timeRemaining, (count + 3) / 4);
                        var elapsed = timer.Elapsed.TotalMilliseconds;
                        var allCurrent = true;
                        for (var i = 0; i < count; i++)
                        {
                            var answer = schedule.Answer(i, 1, revision);
                            allCurrent &= answer.Current;
                            if (answer.Current)
                            {
                                last[i] = frame;
                                if (answer.Occluded != Expected(segments[i])) mismatches++;
                            }
                            if (frame >= 0 && frame < 600 && segments[i].HasValue)
                                maxGap = Math.Max(maxGap, frame - last[i]);
                        }
                        if (frame >= 599 && allCurrent && settledFrames == 0) settledFrames = frame - 598;
                        if (frame >= 0 && frame < 600)
                        {
                            projection[frame] = projected;
                            query[frame] = elapsed - projected;
                            total[frame] = elapsed;
                        }
                    }
                    var totals = Stats(total);
                    var stalls = total.Count(ms => ms >= 16);
                    cases.Add(new(parallel, deviceScale, count, triangles.Length, buildMs,
                        Stats(projection), Stats(query), totals, maxGap, mismatches, settledFrames, stalls,
                        totals.p95 <= (count == 500 ? 2 : 5) && totals.p99 <= (count == 500 ? 4 : 8)
                            && stalls < 2 && maxGap < 4 && mismatches == 0 && settledFrames is > 0 and <= 4));
                }
            }
        }
        File.WriteAllText(Path.Combine(destination, "label-occlusion.json"),
            JsonSerializer.Serialize(new { os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount,
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), checks, changes, cases },
                new JsonSerializerOptions { WriteIndented = true }));
        if (checks.Values.Any(ok => !ok) || cases.Any(c => !c.passed))
            throw new InvalidOperationException("One or more occlusion gates failed. See label-occlusion.json.");
    }

    private static Dictionary<string, object> RunFixtures(RhinoDoc doc, RhinoViewport viewport, Dictionary<string, bool> checks)
    {
        var changes = new Dictionary<string, object>();
        using var gh = new GH_Document { Enabled = true, PreviewMode = GH_PreviewMode.Shaded };
        GH_Document.EnableSolutions = true;
        var server = new GH_ComponentServer();
        typeof(Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, server);
        var nativeAssembly = typeof(Instances).Assembly;
        var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(Assembly), typeof(string), typeof(GH_LoadingMechanism)], null)!;
        parse.Invoke(server, [nativeAssembly, nativeAssembly.Location, GH_LoadingMechanism.Direct]);
        var proxy = server.ObjectProxies.First(p => p.Guid == LabelOcclusionScene.CustomPreviewGuid);
        Console.WriteLine($"Native Custom Preview GUID {proxy.Guid}");
        var shaded = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
        var wire = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.WireframeId);
        viewport.DisplayMode = shaded;
        using var scene = new LabelOcclusionScene(doc, viewport, [gh]);
        var layer = doc.Layers.Add(new Layer { Name = "Label occlusion fixture" });
        using var box = new BoundingBox(new Point3d(-8, -8, -1), new Point3d(8, 8, 1)).ToBrep();
        var id = doc.Objects.AddBrep(box, new ObjectAttributes { LayerIndex = layer });
        Check("Rhino box", true);
        var beforeOrbit = scene.Snapshot;
        Camera(viewport, 60);
        scene.Refresh();
        checks["Camera change reuses scene snapshot"] = scene.Snapshot == beforeOrbit;
        var hidden = doc.Layers[layer];
        hidden.IsVisible = false;
        Check("Hidden layer", false);
        hidden.IsVisible = true;
        viewport.DisplayMode = wire;
        Check("Rhino wireframe", false);
        viewport.DisplayMode = shaded;
        var moved = doc.Objects.Transform(id, Transform.Translation(50, 0, 0), true);
        Check("Moved blocker", false);
        doc.Objects.Delete(moved, true);

        using var mesh = new Mesh();
        mesh.Vertices.Add(-8, -8, 0);
        mesh.Vertices.Add(8, -8, 0);
        mesh.Vertices.Add(8, 8, 0);
        mesh.Vertices.Add(-8, 8, 0);
        mesh.Faces.AddFace(0, 1, 2, 3);
        var standard = new MeshProducer(mesh);
        var floating = new Param_Mesh();
        floating.PersistentData.Append(new GH_Mesh(mesh));
        floating.Hidden = true;
        var custom = (GH_Component)server.EmitObject(proxy.Guid);
        custom.Hidden = true;
        gh.AddObject(standard, false);
        gh.AddObject(floating, false);
        gh.AddObject(custom, false);
        custom.Params.Input[0].AddSource(floating);
        gh.NewSolution(false);
        Check("GH standard output", true);
        standard.Hidden = true;
        Check("GH owner preview off", false);
        floating.Hidden = false;
        Check("GH floating parameter", true);
        floating.Hidden = true;
        custom.Hidden = false;
        Check("GH native Custom Preview", true);
        viewport.DisplayMode = wire;
        Check("GH shaded in Rhino wireframe", true);
        gh.PreviewMode = GH_PreviewMode.Wireframe;
        Check("GH wireframe preview", false);
        gh.PreviewMode = GH_PreviewMode.Disabled;
        Check("GH global preview off", false);
        gh.PreviewMode = GH_PreviewMode.Shaded;
        gh.PreviewFilter = GH_PreviewFilter.Selected;
        Check("GH selected-only unselected", false);
        custom.Attributes.Selected = true;
        Check("GH selected-only selected", true);
        custom.Locked = true;
        Check("GH owner disabled", false);
        return changes;

        void Check(string name, bool blocked)
        {
            Camera(viewport, 0);
            var timer = Stopwatch.StartNew();
            var copy = new List<double>();
            do
            {
                var start = timer.Elapsed.TotalMilliseconds;
                scene.Refresh();
                copy.Add(timer.Elapsed.TotalMilliseconds - start);
                if (scene.Snapshot.HasValue) break;
                Thread.Sleep(1);
            } while (timer.ElapsedMilliseconds < 5000);
            changes[name] = new { harvestAndCopy = Stats(copy.ToArray()), publicationMs = timer.Elapsed.TotalMilliseconds,
                workerMs = scene.LastWorkerMilliseconds };
            var snapshot = scene.Snapshot ?? throw new TimeoutException(name);
            var points = new[] { new Point3d(0, 0, -4), new Point3d(16, 0, -4) };
            var rays = points.Select(new LabelOcclusionScene.Projection(viewport, 1).Project).ToArray();
            var schedule = new OcclusionSchedule(2);
            schedule.Classify(snapshot.Index, snapshot.Generation, 1, rays, [0], () => true);
            var inside = schedule.Answer(0, snapshot.Generation, 1);
            var clear = schedule.Answer(1, snapshot.Generation, 1);
            checks[name] = inside.Visible && inside.Occluded == blocked && inside.HasHit(true)
                && inside.HasHit(false) == !blocked && inside.Opacity(true) == (blocked ? 0.45 : 1)
                && clear.HasHit(false);
            Console.WriteLine($"{name}: {checks[name]} ({inside}, clear {clear})");
        }
    }

    private sealed class MeshProducer(Mesh mesh) : GH_Component("Fixture Mesh", "Fixture", "Headless mesh fixture", "Test", "Test")
    {
        public override Guid ComponentGuid => new("ed2d4eb4-5102-4758-90fa-020b7c83062a");
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) =>
            p.AddMeshParameter("Mesh", "M", "Fixture mesh", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da) => da.SetData(0, new GH_Mesh(mesh));
    }

    private static void Camera(RhinoViewport viewport, int frame)
    {
        var angle = frame * (Math.PI / 300);
        var distance = 68 + 6 * Math.Cos(angle * 2);
        var location = new Point3d(11 * Math.Cos(angle), 11 * Math.Sin(angle), distance);
        viewport.SetCameraLocations(Point3d.Origin, location);
        viewport.CameraUp = Vector3d.YAxis;
        using var projection = new ViewportInfo(viewport);
        projection.SetFrustumNearFar(1, 150);
        viewport.SetViewProjection(projection, false);
    }

    private static OcclusionTriangle[] Grid(int size)
    {
        var triangles = new OcclusionTriangle[size * size * 2];
        var step = 32d / size;
        var i = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var a = new OcclusionPoint(-16 + x * step, -16 + y * step, 0);
            var b = new OcclusionPoint(-16 + (x + 1) * step, a.Y, 0);
            var c = new OcclusionPoint(b.X, -16 + (y + 1) * step, 0);
            var d = new OcclusionPoint(a.X, c.Y, 0);
            triangles[i++] = new(a, b, c);
            triangles[i++] = new(a, c, d);
        }
        return triangles;
    }

    private static bool Expected(OcclusionSegment? segment)
    {
        if (segment is not { } ray || ray.Anchor.Z >= 0 || ray.Near.Z < 0) return false;
        var point = ray.Near + (ray.Anchor - ray.Near) * (ray.Near.Z / (ray.Near.Z - ray.Anchor.Z));
        return Math.Abs(point.X) <= 16 && Math.Abs(point.Y) <= 16;
    }

    private sealed record BenchmarkCase(bool parallel, int deviceScale, int count, int triangles, double buildMs,
        Percentiles projection, Percentiles queries, Percentiles total, int maxGap, int mismatches,
        int settledFrames, int stalls, bool passed);

    private sealed record Percentiles(double p50, double p95, double p99);

    private static Percentiles Stats(double[] values)
    {
        Array.Sort(values);
        return new(At(0.50), At(0.95), At(0.99));
        double At(double percentile) => values[(int)Math.Ceiling(percentile * values.Length) - 1];
    }
}

