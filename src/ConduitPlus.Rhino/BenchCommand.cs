using System.Diagnostics;
using System.Globalization;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Commands;
using Rhino.Display;

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusBench : Command
{
    public override string EnglishName => "ConduitPlusBench";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        return CommandRunner.Run(() => RunAutomated(doc, view));
    }
    public static void RunAutomated(RhinoDoc doc, RhinoView view)
    {
        UiContext.Ensure();
        OverlayManager.Start();
        Directory.CreateDirectory(OutputPaths.Root);
        RasterCacheChecks.Run(doc, view, OutputPaths.Root);
        RasterBenchmark.Run(OutputPaths.Root);
        var picking = PickBenchmark.Run(doc, view);
        // Use a separate owner so benchmarking never overwrites a live overlay's data or view state.
        var owner = Guid.NewGuid();
        var session = OverlayManager.Get(doc, view, owner);
        var updates = new List<double>();
        var hovers = new List<double>();
        try
        {
            session.Update(StructuralDemo.Create());
            Draw(session);
            for (var i = 0; i < 65; i++)
            {
                var cards = session.Cards.Select(c => c.Id == "metric-0"
                    ? c with { Value = (18.4 + i / 100d).ToString("0.00", CultureInfo.InvariantCulture) } : c).ToArray();
                var timer = Stopwatch.StartNew();
                session.Update(cards);
                var ms = timer.Elapsed.TotalMilliseconds;
                Draw(session);
                if (i >= 5) updates.Add(ms);
            }
            var source = StructuralDemo.Create().Single(c => c.Id == "members");
            var bar = source with { Width = 240, Values = source.Values.Take(6).ToArray(), Labels = source.Labels.Take(6).ToArray(), Keys = source.Keys.Take(6).ToArray() };
            session.Update([bar]);
            Draw(session);
            for (var i = 0; i < 65; i++)
            {
                var timer = Stopwatch.StartNew();
                session.Update([bar with { HoverKey = bar.Keys[i % bar.Keys.Length] }]);
                var ms = timer.Elapsed.TotalMilliseconds;
                Draw(session);
                if (i >= 5) hovers.Add(ms);
            }
            File.WriteAllText(Path.Combine(OutputPaths.Root, "samples-raster.json"), CardJson.Serialize(new
            {
                measured = DateTimeOffset.Now, rhino = RhinoApp.Version.ToString(), runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(), dpi = UiContext.Dpi(view), size = view.ActiveViewport.Size,
                warmups = 5, count = 60, updates, hovers, frames = session.FrameCount,
                includes = "spec/revision preparation + GDI+ paint + DisplayBitmap creation; excludes redraw, GPU completion and physical input latency"
            }));
            static string Stats(List<double> values)
            {
                var sorted = values.Order().ToArray();
                return FormattableString.Invariant($"{sorted[(int)Math.Ceiling(.5 * sorted.Length) - 1]:0.000} / {sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1]:0.000}");
            }
            File.WriteAllText(Path.Combine(OutputPaths.Root, "results-rhino.md"),
                $"# Raster benchmark\n\nMeasured {DateTimeOffset.Now:O}; Rhino {RhinoApp.Version}; .NET {Environment.Version}.\n\n" +
                $"60 samples after 5 warmups; nearest-rank p50 / p95 milliseconds.\n\nMetric update: {Stats(updates)} ms.\n\nCompact bar hover: {Stats(hovers)} ms.\n\n" +
                picking + "\n" +
                "Includes spec/revision work, GDI+ paint and DisplayBitmap creation; excludes redraw, GPU completion and physical input latency.\n" +
                "Per-card allocation/paint samples (200 after 20 warmups at 2x) are in samples-raster-micro.json. Cache lifecycle checks are in raster-cache-check.json.\n" +
                "This harness does not measure interactive orbit performance, multi-view overhead, focus, mixed monitors or full-scene high-resolution capture.\n");
            RhinoApp.WriteLine($"Benchmark finished: {Path.Combine(OutputPaths.Root, "results-rhino.md")}");
        }
        finally { OverlayManager.Remove(owner); view.Redraw(); }
    }
    private static void Draw(OverlaySession session)
    {
        var before = session.FrameCount;
        session.View.Redraw();
        RhinoApp.Wait();
        if (session.FrameCount == before)
        {
            var size = session.View.ActiveViewport.Size;
            using var bitmap = new ViewCapture { Width = size.Width, Height = size.Height, ScaleScreenItems = false }.CaptureToBitmap(session.View);
        }
        if (session.FrameCount == before) throw new InvalidOperationException("Rhino did not invoke DrawForeground.");
    }
}
