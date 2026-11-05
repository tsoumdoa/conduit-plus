using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using ConduitPlus.Core;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ConduitPlus.Rhino;

internal static class SpikePaths
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SpikePaths).Assembly.Location)!);
            while (dir is not null) { if (File.Exists(Path.Combine(dir.FullName, "ConduitPlus.sln"))) return Path.Combine(dir.FullName, "docs", "spike"); dir = dir.Parent; }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ConduitPlus", "spike");
        }
    }
    public static string Captures { get { var p = Path.Combine(Root, "captures"); Directory.CreateDirectory(p); return p; } }
}

internal static class CommandRunner
{
    // A Rhino command is synchronous. Pump UI messages while CoreWebView2 awaits COM completions.
    // No drawing callback waits; benchmark/capture operations block mouse updates through Busy.
    public static Result Run(Func<Task> action)
    {
        if (OverlayManager.Busy) { RhinoApp.WriteLine("Conduit+ is already preparing cards."); return Result.Cancel; }
        UiContext.Ensure(); OverlayManager.Busy = true;
        using var scope = UiContext.Scope();
        try
        {
            var task = action();
            while (!task.IsCompleted) { RhinoApp.Wait(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult(); return Result.Success;
        }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+: {e}"); Directory.CreateDirectory(SpikePaths.Root); File.AppendAllText(Path.Combine(SpikePaths.Root, "errors.log"), $"{DateTimeOffset.Now:O} {e}\n"); return Result.Failure; }
        finally { OverlayManager.Busy = false; }
    }
}

public sealed class ConduitPlusDemo : Command
{
    public override string EnglishName => "ConduitPlusDemo";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var renderer = RendererKind.Native;
        using var get = new GetOption(); get.SetCommandPrompt("Structural HUD renderer"); get.AcceptNothing(true);
        var index = get.AddOptionList("Renderer", ["Native", "Web"], 0);
        while (true)
        {
            var result = get.Get();
            if (result == GetResult.Option && get.OptionIndex() == index) { renderer = (RendererKind)get.Option().CurrentListOptionIndex; continue; }
            if (result is not (GetResult.Nothing)) return get.CommandResult();
            break;
        }
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        return CommandRunner.Run(() => OverlayManager.SetAsync(doc, view, Guid.Empty, StructuralDemo.Create(), renderer));
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
        var view = doc.Views.ActiveView; if (view is null) return Result.Failure;
        return CommandRunner.Run(async () =>
        {
            var s = OverlayManager.Get(doc, view);
            if (s.Cards.Length == 0) await s.UpdateAsync(StructuralDemo.Create(), RendererKind.Native);
            await CaptureAsync(s, (int)multiplier, true);
            s.View.Redraw();
        });
    }

    public static async Task RunAutomatedAsync(RhinoDoc doc, RhinoView view, string renderer, int scale, bool prepare)
    {
        UiContext.Ensure();
        if (scale is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(scale));
        var session = OverlayManager.Get(doc, view);
        await session.UpdateAsync(StructuralDemo.Create(), Enum.Parse<RendererKind>(renderer, true));
        for (var i = 0; i < 5; i++) { view.Redraw(); RhinoApp.Wait(); }
        await CaptureAsync(session, scale, prepare);
    }

    internal static async Task<string> CaptureAsync(OverlaySession s, int multiplier, bool prepareWeb)
    {
        var size = s.View.ActiveViewport.Size;
        try
        {
            s.CaptureMultiplier = multiplier;
            if (prepareWeb) await s.UpdateAsync(s.Cards, s.Renderer);
            var capture = new ViewCapture { Width = size.Width * multiplier, Height = size.Height * multiplier, ScaleScreenItems = false, TransparentBackground = true, DrawAxes = false, DrawGrid = false, DrawGridAxes = false };
            var framesBefore = s.FrameCount;
            using var image = capture.CaptureToBitmap(s.View) ?? throw new InvalidOperationException("Rhino ViewCapture returned no bitmap.");
            if (s.FrameCount == framesBefore) throw new InvalidOperationException("ViewCapture did not draw the overlay; refusing to save a blank PNG.");
            var name = $"{s.Renderer.ToString().ToLowerInvariant()}-{multiplier}x-{(prepareWeb ? "prepared" : "direct")}.png";
            var path = Path.Combine(SpikePaths.Captures, name); image.Save(path, ImageFormat.Png);
            File.WriteAllText(Path.ChangeExtension(path, ".json"), CardJson.Serialize(new { renderer = s.Renderer.ToString(), multiplier, prepareWeb, dpi = UiContext.Dpi(s.View), width = image.Width, height = image.Height, cornerAlpha = image.GetPixel(0, 0).A, cardAlpha = image.GetPixel((int)(20 * s.Scale), (int)(45 * s.Scale)).A }));
            RhinoApp.WriteLine($"Saved {path}"); return path;
        }
        finally
        {
            s.CaptureMultiplier = 1;
            if (prepareWeb) await s.UpdateAsync(s.Cards, s.Renderer);
        }
    }
}

public sealed class ConduitPlusBench : Command
{
    public override string EnglishName => "ConduitPlusBench";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView; if (view is null) return Result.Failure;
        return CommandRunner.Run(() => RunAutomatedAsync(doc, view));
    }
    private static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
    private static string Percentiles(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return $"{F(sorted[(int)Math.Ceiling(.5 * sorted.Length) - 1])} / {F(sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1])}";
    }
    private static (double Ms, double Fps) Redraw(RhinoView view, int count = 120)
    {
        for (var i = 0; i < 5; i++) { view.Redraw(); RhinoApp.Wait(); }
        var timer = Stopwatch.StartNew(); for (var i = 0; i < count; i++) { view.Redraw(); RhinoApp.Wait(); }
        return (timer.Elapsed.TotalMilliseconds / count, count / timer.Elapsed.TotalSeconds);
    }
    public static async Task RunAutomatedAsync(RhinoDoc doc, RhinoView view)
    {
        var session = OverlayManager.Get(doc, view);
        var original = session.Cards; var originalRenderer = session.Renderer;
        var report = new StringBuilder($"# Rhino renderer benchmark\n\nMeasured {DateTimeOffset.Now:O}. Rhino {RhinoApp.Version}; OS {Environment.OSVersion}; .NET {Environment.Version}.\n\nViewport {view.ActiveViewport.Size.Width} × {view.ActiveViewport.Size.Height}; DPI scale {F(UiContext.Dpi(view))}.\n\n60 measured updates + 60 hover changes after warmup; 120 Redraw + UI message-pump calls per scenario. No geometry is added to the owner's model.\n\n");
        report.AppendLine("| Renderer | Single metric update p50 / p95 ms | Single compact bar hover p50 / p95 ms | Redraw ms | Redraw FPS | FPS delta |\n|---|---:|---:|---:|---:|---:|");
        var notes = new StringBuilder();
        var measured = new List<RendererKind>();
        notes.AppendLine($"- Host context: {Environment.GetEnvironmentVariable("CONDUITPLUS_RUN_CONTEXT") ?? "Rhino command"}.");
        try
        {
            session.Visible = false; var baseline = Redraw(view); session.Visible = true;
            // Verify that Redraw actually paints. Hidden/minimized Rhino can just queue an invalidation.
            await session.UpdateAsync(StructuralDemo.Create(), RendererKind.Native);
            var before = session.FrameCount; view.Redraw(); RhinoApp.Wait();
            var canRedraw = session.FrameCount > before;
            report.AppendLine(canRedraw ? $"| No overlay | — | — | {F(baseline.Ms)} | {F(baseline.Fps)} | — |" : "| No overlay | — | — | not measured (no draw callback) | not measured | — |");
            if (!canRedraw) notes.AppendLine("- Hidden/minimized viewport did not draw on Redraw. Native latency below uses forced 1× ViewCapture draw callbacks; no redraw FPS is reported.");
            foreach (var renderer in new[] { RendererKind.Native, RendererKind.Web })
            {
                try
                {
                    RhinoApp.WriteLine($"Conduit+ benchmarking {renderer}…");
                    await session.UpdateAsync(StructuralDemo.Create(), renderer); DrawForLatency(session, canRedraw);
                    var updates = new List<double>(); var hovers = new List<double>();
                    for (var i = 0; i < 65; i++)
                    {
                        var cards = session.Cards.Select(c => c.Id == "metric-0" ? c with { Value = (18.4 + i / 100d).ToString("0.00", CultureInfo.InvariantCulture) } : c).ToArray();
                        var timer = Stopwatch.StartNew(); await session.UpdateAsync(cards, renderer); var prepare = timer.Elapsed.TotalMilliseconds;
                        DrawForLatency(session, canRedraw);
                        if (i >= 5) updates.Add(renderer == RendererKind.Web ? prepare : prepare + session.LastLayoutMs + session.LastCardPaintMs["metric-0"]);
                    }
                    var demo = session.Cards;
                    var bar = demo.Single(c => c.Id == "members") with { Width = 240, Values = demo.Single(c => c.Id == "members").Values.Take(6).ToArray(), Labels = demo.Single(c => c.Id == "members").Labels.Take(6).ToArray(), Keys = demo.Single(c => c.Id == "members").Keys.Take(6).ToArray() };
                    await session.UpdateAsync([bar], renderer); DrawForLatency(session, canRedraw);
                    for (var i = 0; i < 65; i++)
                    {
                        var timer = Stopwatch.StartNew(); await session.UpdateAsync([bar with { HoverKey = bar.Keys[i % 6] }], renderer); var prepare = timer.Elapsed.TotalMilliseconds; DrawForLatency(session, canRedraw);
                        if (i >= 5) hovers.Add(renderer == RendererKind.Web ? prepare : prepare + session.LastPaintMs);
                    }
                    await session.UpdateAsync(demo, renderer); DrawForLatency(session, canRedraw);
                    var frameCountBefore = session.FrameCount;
                    var frames = Redraw(view);
                    var frameCount = session.FrameCount - frameCountBefore;
                    var validFrames = canRedraw && frameCount >= 120;
                    var frameCells = validFrames ? $"{F(frames.Ms)} | {F(frames.Fps)} | {F((frames.Fps / baseline.Fps - 1) * 100)}%" : "not measured | not measured | not measured";
                    notes.AppendLine($"- {renderer} redraw callbacks: {frameCount} for 125 calls including warmup.");
                    report.AppendLine($"| {renderer} | {Percentiles(updates)} | {Percentiles(hovers)} | {frameCells} |");
                    File.WriteAllText(Path.Combine(SpikePaths.Root, $"samples-{renderer.ToString().ToLowerInvariant()}.json"), CardJson.Serialize(new { updates, hovers, redrawMs = validFrames ? (double?)frames.Ms : null, redrawFps = validFrames ? (double?)frames.Fps : null, frameCount }));
                    measured.Add(renderer);
                    await session.UpdateAsync(StructuralDemo.Create(8), renderer); // 10 cards, one view.
                    frameCountBefore = session.FrameCount;
                    var ten = Redraw(view); notes.AppendLine(canRedraw && session.FrameCount - frameCountBefore >= 120 ? $"- {renderer}, 10 cards / 1 view: {F(ten.Ms)} ms, {F(ten.Fps)} FPS, delta {F((ten.Fps / baseline.Fps - 1) * 100)}%." : $"- {renderer}, 10 cards / 1 view redraw overhead: not measured (insufficient draw callbacks).");
                }
                catch (Exception e) { report.AppendLine($"| {renderer} | not measured | not measured | not measured | not measured | — |"); notes.AppendLine($"- {renderer} failed: {e}"); }
                // Persist successful measurements even if a later scenario fails.
                Directory.CreateDirectory(SpikePaths.Root);
                File.WriteAllText(Path.Combine(SpikePaths.Root, "results-rhino.md"), report + "\n" + notes);
            }
            // Captures run after timing both renderers: an unstable driver/capture must not contaminate timings.
            if (Environment.GetEnvironmentVariable("CONDUITPLUS_SKIP_CAPTURE") == "1") notes.AppendLine("- Captures skipped by this harness run; use ConduitPlusCapture in normal Rhino to test the capture gate.");
            else foreach (var renderer in measured)
            {
                await session.UpdateAsync(StructuralDemo.Create(), renderer);
                foreach (var scale in new[] { 1, 2, 4 })
                {
                    try
                    {
                        await ConduitPlusCapture.CaptureAsync(session, scale, false);
                        await ConduitPlusCapture.CaptureAsync(session, scale, true);
                        notes.AppendLine($"- {renderer} {scale}× direct/prepared capture: saved.");
                    }
                    catch (Exception e) { notes.AppendLine($"- {renderer} capture {scale}× failed: {e.Message}"); }
                }
            }
        }
        finally
        {
            session.CaptureMultiplier = 1; session.Visible = true;
            await session.UpdateAsync(original.Length == 0 ? StructuralDemo.Create() : original, originalRenderer); view.Redraw();
            report.AppendLine("\nNative latency = spec/revision preparation + measured layout and immediate-mode paint in DrawForeground. Web latency = spec/revision preparation + HTML layout + sequential CDP card capture + PNG decode + DisplayBitmap creation, before redraw. GPU completion and end-to-end mouse input latency are not measured.\n");
            report.AppendLine("Redraw timings include the UI message pump and measure CPU wall time; these are not measured orbit FPS and cannot establish the ≤5% orbit gate. 10 cards × 4 viewports, mixed-monitor DPI changes, browser crash recovery in Rhino, focus behavior and human visual approval: not measured by this command. Review PNG alpha and text at 1×/2×/4×. Direct web captures intentionally reuse display-scale textures; prepared captures regenerate at target scale.\n");
            File.WriteAllText(Path.Combine(SpikePaths.Root, "results-rhino.md"), report + "\n" + notes);
            RhinoApp.WriteLine($"Benchmark finished: {Path.Combine(SpikePaths.Root, "results-rhino.md")}");
        }
    }
    private static void DrawForLatency(OverlaySession session, bool canRedraw)
    {
        var before = session.FrameCount;
        if (canRedraw) { session.View.Redraw(); RhinoApp.Wait(); }
        else
        {
            var size = session.View.ActiveViewport.Size;
            using var bitmap = new ViewCapture { Width = size.Width, Height = size.Height, ScaleScreenItems = false }.CaptureToBitmap(session.View);
        }
        if (session.FrameCount == before) throw new InvalidOperationException("Rhino did not invoke DrawForeground; no paint timing can be measured.");
    }
}
