using System.Diagnostics;
using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal static class LabelPaintBench
{
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    internal static void RunViewport(RhinoDoc doc, RhinoView view, Action pump)
    {
        var owner = Guid.NewGuid();
        var vp = view.MainViewport;
        var size = vp.Size;
        var camera = vp.CameraLocation;
        var target = vp.CameraTarget;
        var displayMode = vp.DisplayMode;
        var tangent = Vector3d.CrossProduct(vp.CameraDirection, vp.CameraUp);

        const int frames = 120;
        foreach (var heavy in new[] { true, false })
        {
            using var mesh = new Mesh();
            var blocker = Guid.Empty;
            var farPoint = Guid.Empty;
            if (heavy)
            {
                var depth = (camera + target) / 2;
                var centre = OnPlane(size.Width * .5, size.Height * .5, depth);
                var halfWidth = centre.DistanceTo(OnPlane(size.Width * .6, size.Height * .5, depth));
                var halfHeight = centre.DistanceTo(OnPlane(size.Width * .5, size.Height * .6, depth));
                var halfDepth = camera.DistanceTo(target) * .01;
                var box = new Box(new Plane(centre, tangent, vp.CameraUp),
                    new Interval(-halfWidth, halfWidth), new Interval(-halfHeight, halfHeight), new Interval(-halfDepth, halfDepth));
                using var surface = Mesh.CreateFromBox(box, 65, 65, 64);
                mesh.Append(surface);
                mesh.Faces.ConvertQuadsToTriangles();
                blocker = doc.Objects.AddMesh(mesh);
                if (blocker == Guid.Empty) throw new InvalidOperationException("Unable to add occluder fixture.");
                // Keep the automatic far clip behind the labels when the mesh is the only filled object.
                farPoint = doc.Objects.AddPoint(target + (target - camera) * .25);
                vp.DisplayMode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
                Console.WriteLine($"Triangle-heavy scene: {mesh.Faces.Count} triangles");
            }
            try
            {
                RunGrid(heavy);
            }
            finally
            {
                if (blocker != Guid.Empty)
                {
                    doc.Objects.Delete(blocker, true);
                    if (farPoint != Guid.Empty) doc.Objects.Delete(farPoint, true);
                    vp.DisplayMode = displayMode;
                }
            }
        }

        Point3d OnPlane(double x, double y, Point3d planePoint)
        {
            if (!vp.GetFrustumLine(x, y, out var line))
                throw new InvalidOperationException("Unable to project a grid point.");
            var normal = vp.CameraDirection;
            var t = ((planePoint - line.From) * normal) / (line.Direction * normal);
            return line.PointAt(t);
        }

        void RunGrid(bool heavy)
        {
            foreach (var count in new[] { 500, 5000 })
            {
                var anchors = new LabelAnchor[count];
                var columns = (int)Math.Ceiling(Math.Sqrt(count * size.Width / (double)size.Height));
                var rows = (count + columns - 1) / columns;
                for (var i = 0; i < count; i++)
                {
                    var point = OnPlane(15 + (size.Width - 30d) * (i % columns + .5) / columns,
                        15 + (size.Height - 30d) * (i / columns + .5) / rows, target);
                    anchors[i] = new(new(owner, "{0}"), null, i, point, new($"Room {i + 1}", null, null, null, 0));
                }

                OverlayManager.SetWithLabels(doc, view, owner, [], null, Theme.Paper, null, true, 1, "Labels", true, anchors);
                var session = OverlayManager.Get(doc, view, owner);
                var publication = Stopwatch.StartNew();
                while (publication.Elapsed < TimeSpan.FromSeconds(8) && session.InspectLabelFrame().Length == 0)
                {
                    pump();
                    view.Redraw();
                    Thread.Sleep(10);
                }
                if (session.InspectLabelFrame().Length == 0)
                {
                    var answers = session.InspectLabels();
                    throw new InvalidOperationException($"Missing viewport benchmark labels (snapshot {session.LabelScene?.Snapshot.HasValue}, " +
                        $"visible {answers.Count(item => item.Answer.Visible)}, occluded {answers.Count(item => item.Answer.Occluded)}).");
                }
                if (heavy)
                {
                    for (var i = 0; i < 12; i++) { view.Redraw(); pump(); }
                    var occluded = session.InspectLabels().Count(item => item.Answer is { Current: true, Occluded: true });
                    if (occluded == 0) throw new InvalidOperationException("The triangle-heavy fixture must occlude grid anchors.");
                    Console.WriteLine($"Triangle-heavy grid {count}: {occluded} verified occluded anchors");
                }
                foreach (var dpi in new[] { 1, 2 })
                foreach (var cold in heavy ? new[] { false } : new[] { false, true })
                {
                    session.SetLabelDpiForChecks(dpi);
                    var clock = new FrameClock();
                    session.SetLabelPaintBudgetForChecks(clock);
                    var samples = new OverlaySession.LabelTiming[frames];
                    var recurringColdStalls = 0;
                    // Allow managed tiered compilation and the changing visible raster set to settle.
                    var warmupFrames = cold ? 0 : 90;
                    for (var frame = -warmupFrames; frame < frames; frame++)
                    {
                        if (cold) session.ClearLabelRasters();
                        var timing = Redraw(frame);
                        if (frame >= 0) samples[frame] = timing;
                        if (cold && frame >= 0 && frame % 10 == 0)
                        {
                            var stalls = timing.Milliseconds > 16 ? 1 : 0;
                            for (var recovery = 1; recovery <= 4; recovery++)
                                if (Redraw(frame + recovery).Milliseconds > 16) stalls++;
                            if (stalls > 1) recurringColdStalls++;
                        }
                    }
                    var total = Percentiles(t => t.Milliseconds);
                    if (cold)
                    {
                        Console.WriteLine($"  Cold raster resets with a first-frame stall over 16 ms: {samples.Count(t => t.Milliseconds > 16)}/{frames}");
                        Console.WriteLine($"  Cold recovery trials with recurring stalls over 16 ms: {recurringColdStalls}/{frames / 10}");
                    }
                    Console.WriteLine($"Foreground {(heavy ? "50k " : "")}{count} {dpi}x {(cold ? "cold" : "warm")}: {total}");
                    if (!cold)
                    {
                        var beforeSettle = session.LabelFrameTiming.Sequence;
                        var wait = Stopwatch.StartNew();
                        OverlaySession.LabelTiming settled;
                        do
                        {
                            clock.Advance(LabelPaintBudget.PromoteInterval);
                            Thread.Sleep(10);
                            view.Redraw();
                            pump();
                            settled = session.LabelSettledTiming;
                            if (wait.Elapsed > TimeSpan.FromSeconds(3))
                                throw new InvalidOperationException($"Rhino did not draw a settled label frame (sequence {beforeSettle} to {settled.Sequence}, motion {settled.InMotion}).");
                        }
                        while (settled.Sequence <= beforeSettle);
                        Console.WriteLine(FormattableString.Invariant(
                            $"  Motion pills {samples.Min(t => t.PillCount)}-{samples.Max(t => t.PillCount)}, settled {settled.PillCount} pills in {settled.Milliseconds:F2} ms"));
                        pump();
                    }
                    var slowest = samples.MaxBy(t => t.Milliseconds);
                    Console.WriteLine(FormattableString.Invariant($"  Slowest {slowest.Milliseconds:F2} ms (classify {slowest.Classify:F2}, candidates {slowest.Candidates:F2}, layout {slowest.Layout:F2}, pills {slowest.Pills:F2}, dots {slowest.Dots:F2}, rings {slowest.Rings:F2})"));
                    Console.WriteLine($"  Classify {Percentiles(t => t.Classify)}, candidates {Percentiles(t => t.Candidates)}, " +
                        $"layout {Percentiles(t => t.Layout)}, pills {Percentiles(t => t.Pills)}, " +
                        $"dots {Percentiles(t => t.Dots)}, rings {Percentiles(t => t.Rings)} (p95/p99 ms)");

                    OverlaySession.LabelTiming Redraw(int frame)
                    {
                        clock.Advance(TimeSpan.FromMilliseconds(16));
                        vp.SetCameraLocation(camera + tangent * (camera.DistanceTo(target) * .001 * Math.Sin(frame / 8d)), false);
                        var previous = session.LabelFrameTiming.Sequence;
                        view.Redraw();
                        pump();
                        var timing = session.LabelFrameTiming;
                        if (timing.Sequence == previous || !timing.InMotion)
                            throw new InvalidOperationException("Rhino did not complete the requested foreground redraw.");
                        return timing;
                    }

                    string Percentiles(Func<OverlaySession.LabelTiming, double> stage)
                    {
                        var sorted = samples.Select(stage).Order().ToArray();
                        return FormattableString.Invariant($"{sorted[113]:F2}/{sorted[118]:F2}");
                    }
                }
                vp.SetCameraLocation(camera, false);
                OverlayManager.Remove(owner);
            }
        }
    }

    internal static void Run()
    {
        var output = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")
            ?? throw new InvalidOperationException("Set CONDUITPLUS_RESULTS.");
        const int frames = 120;
        var palette = LabelPalette.FromTheme(Theme.Paper);
        foreach (var dpi in new[] { 1, 2 })
        foreach (var count in new[] { 500, 5000 })
        foreach (var coincident in new[] { false, true })
        {
            var layout = new LabelLayout();
            using var cache = new LabelRasterCache<Bitmap>();
            var candidates = Enumerable.Range(0, count).Select(i => new LabelCandidate(
                new(Guid.Empty, new(Guid.Empty, "{0}"), null, i),
                coincident ? new(15 * dpi, 15 * dpi) : new((15 + i % 75 * 22) * dpi, (15 + i / 75 * 22) * dpi),
                0, LabelActivity.Ordinary, new(true, false, true), 42, 32)).ToArray();
            var keys = Enumerable.Range(0, count).Select(i => new LabelRasterKey(i.ToString(), null, palette, dpi, LabelForm.Short, 32, 1)).ToArray();
            var projected = new LabelCandidate[count];
            var size = new Size2(1700 * dpi, 1510 * dpi);
            var dotKey = new LabelRasterKey("", null, palette, dpi, LabelForm.Dot, 5, 1);
            using var dot = RasterDrawing.RenderLabel(dotKey);
            using var target = new Bitmap((int)size.Width, (int)size.Height);
            using var graphics = Graphics.FromImage(target);
            var timings = new List<double>(frames);
            foreach (var cold in new[] { false, true })
            {
                timings.Clear();
                for (var frame = -5; frame < frames; frame++)
                {
                    if (cold) cache.Dispose();
                    var shift = (frame % 8) * dpi;
                    for (var i = 0; i < count; i++)
                        projected[i] = candidates[i] with { Anchor = new(candidates[i].Anchor.X + shift, candidates[i].Anchor.Y) };
                    var start = Stopwatch.GetTimestamp();
                    var placements = layout.Place(projected, size, dpi, [], ranked: true);
                    var uploads = 8;
                    foreach (var placement in placements)
                    {
                        graphics.DrawImageUnscaled(dot, (int)(placement.Dot.X * dpi), (int)(placement.Dot.Y * dpi));
                        if (placement.Form == LabelForm.Dot) continue;
                        var key = keys[placement.Identity.PointIndex!.Value];
                        if (!cache.TryGet(key, out var image))
                        {
                            if (uploads-- <= 0) continue;
                            image = RasterDrawing.RenderLabel(key);
                            cache.Add(key, image, (long)image.Width * image.Height * 8);
                        }
                        graphics.DrawImageUnscaled(image, (int)(placement.Pill.X * dpi), (int)(placement.Pill.Y * dpi));
                    }
                    if (frame >= 0) timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                timings.Sort();
                Console.WriteLine(FormattableString.Invariant($"Layout + GDI {count} {dpi}x {(coincident ? "coincident" : "grid")} {(cold ? "cold" : "warm")}: p95 {timings[113]:F2} ms, p99 {timings[118]:F2} ms"));
            }
        }

        foreach (var theme in Theme.Presets)
        foreach (var dpi in new[] { 1, 2 })
        {
            var key = new LabelRasterKey("Room 101", Color.Orange.ToArgb(), LabelPalette.FromTheme(theme.Resolve(false)), dpi, LabelForm.Full, 80, 1);
            using var pill = RasterDrawing.RenderLabel(key);
            pill.Save(Path.Combine(output, $"label-{theme.Name}-{dpi}x.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
