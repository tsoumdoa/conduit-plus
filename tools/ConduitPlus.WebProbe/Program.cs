using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using ConduitPlus.Core;
using ConduitPlus.Rendering;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Match Rhino's exact host assembly without shipping a second WebView2 assembly.
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "Microsoft.Web.WebView2.Core"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "System", "Microsoft.Web.WebView2.Core.dll")) : null;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var task = Run(args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("docs/spike"));
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
        try { task.GetAwaiter().GetResult(); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static string P(List<double> samples, double percentile) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * percentile) - 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    private static async Task Run(string output)
    {
        Directory.CreateDirectory(output); var captures = Path.Combine(output, "captures"); Directory.CreateDirectory(captures);
        var report = new StringBuilder($"# Rhino-free WebView2 probe\n\nMeasured {DateTimeOffset.Now:O}. Core API package 1.0.1938.49; .NET {Environment.Version}. This probe does not include Rhino, GPU upload or DisplayBitmap.\n\n| Host | Scale | Six cards p50 / p95 ms | Single 240×180 hover p50 / p95 ms | Alpha corner / center |\n|---|---:|---:|---:|---|\n");
        var notes = new StringBuilder();
        foreach (var mode in Enum.GetValues<HostMode>())
        {
            using var host = new WebSurface(mode);
            foreach (var scale in new[] { 1d, 1.5d, 2d, 4d })
            {
                try
                {
                    await host.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    var cards = StructuralDemo.Create();
                    var times = new List<double>(); var hovers = new List<double>(); string alpha = "";
                    for (var i = 0; i < 55; i++)
                    {
                        cards[0] = cards[0] with { Value = (18.4 + .01 * i).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Rev = i + 1 };
                        var clock = Stopwatch.StartNew(); var result = await host.CaptureAsync(cards, Theme.Paper, scale).WaitAsync(TimeSpan.FromSeconds(30));
                        if (i >= 5) times.Add(clock.Elapsed.TotalMilliseconds);
                        foreach (var c in result)
                        {
                            if (i == 54)
                            {
                                c.Bitmap.Save(Path.Combine(captures, $"probe-{mode}-{scale:0.0}x-{c.Spec.Id}.png"), ImageFormat.Png);
                                if (c.Spec.Id == "metric-0") alpha = $"{c.Bitmap.GetPixel(0, 0).A} / {c.Bitmap.GetPixel(c.Bitmap.Width / 2, c.Bitmap.Height / 2).A}; {c.Bitmap.Width}×{c.Bitmap.Height}";
                            }
                            c.Dispose();
                        }
                    }
                    var bar = cards[4] with { Width = 240, Values = cards[4].Values.Take(6).ToArray(), Labels = cards[4].Labels.Take(6).ToArray(), Keys = cards[4].Keys.Take(6).ToArray() };
                    for (var i = 0; i < 55; i++)
                    {
                        var clock = Stopwatch.StartNew(); var result = await host.CaptureAsync([bar with { HoverKey = bar.Keys[i % 6], Rev = i }], Theme.Paper, scale).WaitAsync(TimeSpan.FromSeconds(30));
                        if (i >= 5) hovers.Add(clock.Elapsed.TotalMilliseconds); foreach (var c in result) c.Dispose();
                    }
                    report.AppendLine($"| {mode} | {scale} | {P(times, .5)} / {P(times, .95)} | {P(hovers, .5)} / {P(hovers, .95)} | {alpha} |");
                    File.WriteAllText(Path.Combine(output, $"samples-probe-{mode}-{scale:0.0}x.json"), CardJson.Serialize(new { times, hovers }));
                    Console.WriteLine($"{mode} {scale}×: batch p95 {P(times, .95)} ms; hover p95 {P(hovers, .95)} ms");
                }
                catch (Exception e) { report.AppendLine($"| {mode} | {scale} | not measured | not measured | {e.GetType().Name}: {e.Message} |"); Console.WriteLine($"{mode} failed: {e}"); break; }
                finally { File.WriteAllText(Path.Combine(output, "results-webprobe.md"), report.ToString() + notes); }
            }
            try
            {
                var browser = host.BrowserProcessId;
                if (browser != 0)
                {
                    Process.GetProcessById((int)browser).Kill();
                    // Pump to receive ProcessFailed before reinitializing the controller.
                    await Task.Delay(500);
                    var timer = Stopwatch.StartNew(); var recovered = await host.CaptureAsync(StructuralDemo.Create(), Theme.Paper, 1).WaitAsync(TimeSpan.FromSeconds(30));
                    foreach (var c in recovered) c.Dispose();
                    notes.AppendLine($"\n{mode} browser kill/re-init/resync: {timer.Elapsed.TotalMilliseconds:0.00} ms, {recovered.Count} cards recovered. Runtime {host.RuntimeVersion}.\n");
                }
            }
            catch (Exception e) { notes.AppendLine($"\n{mode} crash recovery failed: {e.Message}\n"); }
            File.WriteAllText(Path.Combine(output, "results-webprobe.md"), report.ToString() + notes);
        }
    }
}
