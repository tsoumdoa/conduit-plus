using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Geometry;
using Point = System.Drawing.Point;

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusLabelSpike : Command
{
    public override string EnglishName => "ConduitPlusLabelSpike";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (doc.Views.ActiveView is not { } view) return Result.Failure;
        // Interactive runs choose the blocker source here instead of through environment variables.
        var sources = new[] { "Diagnostic", "GHStandard", "GHCustom" };
        var choice = 0;
        var get = new global::Rhino.Input.Custom.GetOption();
        get.SetCommandPrompt("Label depth spike: choose the blocker source, then press Enter to run");
        get.AcceptNothing(true);
        while (true)
        {
            get.ClearCommandOptions();
            var index = get.AddOptionList("Source", sources, choice);
            var result = get.Get();
            if (result == global::Rhino.Input.GetResult.Nothing) break;
            if (result != global::Rhino.Input.GetResult.Option) return Result.Cancel;
            if (get.OptionIndex() == index) choice = get.Option().CurrentListOptionIndex;
        }
        string? external = choice switch { 1 => "Standard", 2 => "Custom", _ => null };
        return CommandRunner.Run(() => Run(doc, view, external));
    }

    public static void RunAutomated(RhinoDoc doc, RhinoView view) =>
        Run(doc, view, Environment.GetEnvironmentVariable("CONDUITPLUS_SPIKE_GH"));

    private static void Run(RhinoDoc doc, RhinoView view, string? external)
    {
        RhinoApp.WriteLine("Experimental label depth benchmark. Use an empty model and a maximized model viewport.");
        var root = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ConduitPlus", "artifacts", "labels-depth");
        if (!string.IsNullOrEmpty(external)) root = Path.Combine(root, external.ToLowerInvariant());
        Directory.CreateDirectory(root);
        RhinoApp.WriteLine($"Results: {Path.Combine(root, "labels-depth.json")}");
        var viewport = view.ActiveViewport;
        var originalMode = viewport.DisplayMode;
        var reports = new List<object>();
        var viewportId = viewport.Id;
        var previousModified = doc.Modified;
        viewport.PushViewProjection();
        try
        {
            foreach (var modeId in new[] { DisplayModeDescription.ShadedId, DisplayModeDescription.WireframeId })
            foreach (var parallel in new[] { false, true })
            {
                viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(modeId);
                if (parallel) viewport.ChangeToParallelProjection(true);
                else viewport.ChangeToPerspectiveProjection(true, 50);
                foreach (var count in new[] { 500, 5000 })
                foreach (var source in string.IsNullOrEmpty(external)
                    ? new[] { Blocker.RhinoObject, Blocker.EarlyPreview, Blocker.LatePreview }
                    : new[] { Blocker.ExternalPreview })
                {
                    var label = $"{viewport.DisplayMode.EnglishName}/{(parallel ? "parallel" : "perspective")}/{count}/{source}";
                    RhinoApp.WriteLine(label);
                    reports.Add(RunCase(doc, view, viewport, viewportId, source, count, label, modeId == DisplayModeDescription.ShadedId));
                    Save();
                }
            }
        }
        catch (Exception error)
        {
            reports.Add(new { error = error.ToString() });
            Save();
            throw;
        }
        finally
        {
            viewport.PopViewProjection();
            viewport.DisplayMode = originalMode;
            doc.Modified = previousModified;
            view.Redraw();
        }

        void Save() => File.WriteAllText(Path.Combine(root, "labels-depth.json"),
            JsonSerializer.Serialize(new { rhino = RhinoApp.Version.ToString(), os = Environment.OSVersion.ToString(),
                viewport = viewport.Size, dpiScale = UiContext.Dpi(view), runLabel = Environment.GetEnvironmentVariable("CONDUITPLUS_SPIKE_DISPLAY"), external, reports },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private enum Blocker { RhinoObject, EarlyPreview, LatePreview, ExternalPreview }

    private static object RunCase(RhinoDoc doc, RhinoView view, RhinoViewport viewport, Guid viewportId,
        Blocker source, int count, string label, bool shaded)
    {
        using var mesh = PlaneMesh(0);
        using var movedMesh = PlaneMesh(4);
        using var material = new DisplayMaterial(Color.SteelBlue);
        var diagnostic = new Preview(viewportId, mesh, movedMesh, material, source, shaded);
        var dots = new Dots(viewportId, count);
        // Registration order places the late diagnostic after the foreground dots.
        if (source is Blocker.EarlyPreview or Blocker.ExternalPreview) diagnostic.Enabled = true;
        dots.Enabled = true;
        if (source == Blocker.LatePreview) diagnostic.Enabled = true;
        var id = Guid.Empty;
        var movedId = Guid.Empty;
        var frames = new List<Frame>(600);
        try
        {
            if (source == Blocker.RhinoObject)
            {
                id = doc.Objects.AddMesh(mesh);
                movedId = doc.Objects.AddMesh(movedMesh);
                if (id == Guid.Empty || movedId == Guid.Empty) throw new InvalidOperationException("Adding the reference mesh failed.");
            }
            for (var i = -20; i < 600; i++)
            {
                var phase = Math.Max(i, 0);
                var angle = 2 * Math.PI * Math.Min(phase, 449) / 450;
                var distance = 35 + 5 * Math.Sin(angle);
                var camera = new Point3d(3 * Math.Sin(angle), 2 * Math.Cos(angle), distance);
                viewport.SetCameraLocations(Point3d.Origin, camera);
                var shifted = source != Blocker.ExternalPreview && phase >= 450 && phase / 25 % 2 != 0;
                diagnostic.Shifted = shifted;
                if (id != Guid.Empty)
                {
                    doc.Objects.Show(shifted ? movedId : id, true);
                    doc.Objects.Hide(shifted ? id : movedId, true);
                }
                dots.Measure = false;
                var start = Stopwatch.GetTimestamp();
                view.Redraw();
                var baseline = Ms(start);
                dots.Measure = true;
                dots.Last = null;
                start = Stopwatch.GetTimestamp();
                view.Redraw();
                var frameTime = Ms(start);
                if (dots.Error is { } error) throw new InvalidOperationException("Depth capture callback failed.", error);
                var sample = dots.Last ?? throw new InvalidOperationException("Redraw did not invoke the benchmark viewport foreground conduit.");
                if (i < 0) continue;
                var (falseHits, leaks, checkedAnchors) = dots.Check(viewport, shaded || source == Blocker.LatePreview, shifted ? 4 : 0);
                frames.Add(new Frame(baseline, frameTime, frameTime - baseline, sample.Capture, sample.Sampling,
                    sample.Classification, sample.Draw, sample.Hits, sample.Nested, falseHits, leaks, checkedAnchors));
            }
        }
        finally
        {
            dots.Enabled = diagnostic.Enabled = false;
            if (id != Guid.Empty) doc.Objects.Delete(id, true);
            if (movedId != Guid.Empty) doc.Objects.Delete(movedId, true);
        }

        var wall = Stats(frames.Select(f => f.Delta));
        var stalls = frames.Count(f => f.Delta >= 16);
        var falseHitsTotal = frames.Sum(f => f.FalseHits);
        var leaksTotal = frames.Sum(f => f.Leaks);
        return new { label, viewport = viewport.Size, wall, baseline = Stats(frames.Select(f => f.Baseline)),
            capture = Stats(frames.Select(f => f.Capture)), sampling = Stats(frames.Select(f => f.Sampling)),
            classification = Stats(frames.Select(f => f.Classification)), draw = Stats(frames.Select(f => f.Draw)),
            stalls, falseHits = falseHitsTotal, leaks = leaksTotal,
            observedGatesPassed = wall.P95 <= (count == 500 ? 2 : 5) && wall.P99 <= (count == 500 ? 4 : 8)
                && stalls <= 1 && falseHitsTotal == 0 && leaksTotal == 0 && frames.All(f => f.CheckedAnchors > 0),
            frames };
    }

    private static Mesh PlaneMesh(int offset)
    {
        var mesh = new Mesh();
        mesh.Vertices.Add(-6 + offset, -4, 0);
        mesh.Vertices.Add(2 + offset, -4, 0);
        mesh.Vertices.Add(2 + offset, 5, 0);
        mesh.Vertices.Add(-6 + offset, 5, 0);
        mesh.Faces.AddFace(0, 1, 2, 3);
        mesh.Normals.ComputeNormals();
        return mesh;
    }

    private sealed class Preview(Guid viewportId, Mesh mesh, Mesh movedMesh, DisplayMaterial material, Blocker source, bool shaded) : DisplayConduit
    {
        public bool Shifted { get; set; }
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (e.Viewport.Id == viewportId) e.IncludeBoundingBox(new BoundingBox(-9, -7, -4, 9, 7, 4));
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (source == Blocker.EarlyPreview) Paint(e);
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (source == Blocker.LatePreview) Paint(e);
        }

        private void Paint(DrawEventArgs e)
        {
            if (e.Viewport.Id != viewportId) return;
            var blocker = Shifted ? movedMesh : mesh;
            e.Display.PushDepthTesting(true);
            e.Display.PushDepthWriting(true);
            try
            {
                if (shaded || source == Blocker.LatePreview) e.Display.DrawMeshShaded(blocker, material);
                else e.Display.DrawMeshWires(blocker, Color.SteelBlue);
            }
            finally
            {
                e.Display.PopDepthWriting();
                e.Display.PopDepthTesting();
            }
        }
    }

    private sealed class Dots(Guid viewportId, int count) : DisplayConduit
    {
        private readonly Point3d[] _anchors = Enumerable.Range(0, count)
            .Select(i => new Point3d((i % 50 + .5) * .34 - 8.5, (i / 50 + .5) * 13 / (count / 50) - 6.5,
                i % 5 == 0 ? 0 : i % 5 == 1 ? 3 : -3)).ToArray();
        private readonly (Point Pixel, double Depth, double Bias, bool Visible)?[] _projected = new (Point, double, double, bool)?[count];
        private readonly Dictionary<Point, Point3d> _samples = new();
        private bool _capturing;
        private int _nested;
        public bool Measure { get; set; }
        public Pass? Last { get; set; }
        public Exception? Error { get; private set; }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (e.Viewport.Id == viewportId) e.IncludeBoundingBox(new BoundingBox(-9, -7, -4, 9, 7, 4));
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (_capturing) { _nested++; return; }
            if (!Measure || Last is not null || Error is not null || e.Viewport.Id != viewportId) return;
            try { Last = Capture(e); }
            catch (Exception error) { Error = error; }
        }

        private Pass Capture(DrawEventArgs e)
        {
            var vp = e.Viewport;
            var size = vp.Size;
            if (!vp.GetFrustum(out _, out _, out _, out _, out var near, out var far))
                throw new InvalidOperationException("No valid viewport frustum.");
            var location = vp.CameraLocation;
            var direction = vp.CameraDirection;
            direction.Unitize();

            _samples.Clear();
            var start = Stopwatch.GetTimestamp();
            _nested = 0;
            _capturing = true;
            int hits;
            double capture;
            double sampling;
            try
            {
                using var suppression = OverlayManager.SuppressForDepthSpike();
                using var depth = new ZBufferCapture(vp);
                depth.SetDisplayMode(vp.DisplayMode.Id);
                hits = depth.HitCount();
                capture = Ms(start);
                start = Stopwatch.GetTimestamp();
                for (var i = 0; i < _anchors.Length; i++)
                {
                    _projected[i] = null;
                    var anchor = _anchors[i];
                    var distance = (anchor - location) * direction;
                    if (distance < near || distance > far) continue;
                    var screen = vp.WorldToClient(anchor);
                    if (!screen.IsValid || screen.X < 0 || screen.Y < 0 || screen.X >= size.Width || screen.Y >= size.Height) continue;
                    var pixel = new Point((int)screen.X, (int)screen.Y);
                    if (!_samples.ContainsKey(pixel))
                        _samples.Add(pixel, depth.WorldPointAt(pixel.X, pixel.Y));
                    vp.GetWorldToScreenScale(anchor, out var scale);
                    // Estimate the world-space step of a normalized 24-bit depth buffer.
                    var quantization = vp.IsParallelProjection ? far - near : (far - near) * distance * distance / (far * near);
                    _projected[i] = (pixel, distance, quantization / 16777215 + (scale > 0 ? .25 / scale : 0), false);
                }
                sampling = Ms(start);
            }
            finally { _capturing = false; }

            start = Stopwatch.GetTimestamp();
            for (var i = 0; i < _projected.Length; i++)
            {
                if (_projected[i] is not { } item) continue;
                var point = _samples[item.Pixel];
                item.Visible = !point.IsValid || item.Depth <= (point - location) * direction + item.Bias;
                _projected[i] = item;
            }
            var classification = Ms(start);
            start = Stopwatch.GetTimestamp();
            for (var i = 0; i < _projected.Length; i++)
            {
                if (_projected[i] is not { } item || (!item.Visible && i % 9 != 0)) continue;
                var color = item.Visible ? Color.OrangeRed : Color.FromArgb(115, Color.OrangeRed);
                e.Display.DrawRoundedRectangle(new PointF(item.Pixel.X, item.Pixel.Y), 4, 4, 2, color, 0, color);
            }
            return new Pass(capture, sampling, classification, Ms(start), hits, _nested);
        }

        public (int FalseHits, int Leaks, int CheckedAnchors) Check(RhinoViewport vp, bool solid, int offset)
        {
            int falseHits = 0, leaks = 0, checkedAnchors = 0;
            for (var i = 0; i < _projected.Length; i++)
            {
                if (_projected[i] is not { } item || !vp.GetFrustumLine(item.Pixel.X + .5, item.Pixel.Y + .5, out var ray)) continue;
                var dz = ray.To.Z - ray.From.Z;
                if (Math.Abs(dz) < 1e-9) continue;
                var t = -ray.From.Z / dz;
                var onPlane = ray.PointAt(t);
                var x = onPlane.X - offset;
                var edge = Math.Min(Math.Min(Math.Abs(x + 6), Math.Abs(x - 2)),
                    Math.Min(Math.Abs(onPlane.Y + 4), Math.Abs(onPlane.Y - 5)));
                if (edge < .2) continue;
                var hidden = solid && t >= 0 && t <= 1 && _anchors[i].Z < 0
                    && x > -6 && x < 2 && onPlane.Y > -4 && onPlane.Y < 5;
                checkedAnchors++;
                if (hidden && item.Visible) leaks++;
                if (!hidden && !item.Visible) falseHits++;
            }
            return (falseHits, leaks, checkedAnchors);
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    private static Percentiles Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double At(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
        return new Percentiles(At(.5), At(.95), At(.99), sorted[^1]);
    }

    private sealed record Percentiles(double P50, double P95, double P99, double Max);
    private sealed record Pass(double Capture, double Sampling, double Classification, double Draw, int Hits, int Nested);
    private sealed record Frame(double Baseline, double Wall, double Delta, double Capture, double Sampling,
        double Classification, double Draw, int Hits, int Nested, int FalseHits, int Leaks, int CheckedAnchors);
}
