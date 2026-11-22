using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ConduitPlus.Rhino;

/// <summary>
/// Host self-test, written for the first macOS session (also runs on Windows to produce a reference report).
/// Reports runtime/assembly resolution, view scale and coordinate mapping, renders every card kind to PNG,
/// exercises clipboard, ViewCapture PNG and PDF export, and reports input-routing counters. Run it once
/// before and once after the manual checklist so the second report captures wheel/trackpad counters.
/// Output: &lt;Documents&gt;/ConduitPlus/platform-check/&lt;timestamp&gt;/ (report.txt, report.json, PNGs).
/// </summary>
public sealed class ConduitPlusPlatformCheck : Command
{
    private static readonly Guid CheckOwner = new("6a1d6c39-4f0e-4f57-9d8e-5d0c4a2b7c11");
    public override string EnglishName => "ConduitPlusPlatformCheck";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null) return Result.Failure;
        var clipboard = new OptionToggle(true, "No", "Yes");
        var exports = new OptionToggle(true, "No", "Yes");
        var pointer = new OptionToggle(mode == RunMode.Interactive, "No", "Yes");
        var demo = new OptionToggle(true, "No", "Yes");
        if (mode == RunMode.Interactive)
        {
            var get = new GetOption();
            get.SetCommandPrompt("Conduit+ platform check. Press Enter to run (Clipboard overwrites the clipboard)");
            get.AcceptNothing(true);
            get.AddOptionToggle("Clipboard", ref clipboard);
            get.AddOptionToggle("Exports", ref exports);
            get.AddOptionToggle("PointerSampling", ref pointer);
            get.AddOptionToggle("DemoCards", ref demo);
            while (true)
            {
                var r = get.Get();
                if (r == GetResult.Option) continue;
                if (r == GetResult.Nothing) break;
                return Result.Cancel;
            }
        }
        Run(doc, view, clipboard.CurrentValue, exports.CurrentValue, pointer.CurrentValue, demo.CurrentValue);
        return Result.Success;
    }

    /// <summary>
    /// Unattended run (RhinoBench / safe-mode RhinoCore): no clipboard overwrite, no pointer sampling, and no PDF,
    /// because safe-mode FilePdf.Create stalls (docs/verification/capture-pdf.md).
    /// </summary>
    public static void RunAutomated(RhinoDoc doc, RhinoView view) => Run(doc, view, clipboard: false, exports: true, pointer: false, demo: true, pdf: false);

    private static void Run(RhinoDoc doc, RhinoView view, bool clipboard, bool exports, bool pointer, bool demo, bool pdf = true)
    {
        var report = new Report(Path.Combine(OutputPaths.Root, "platform-check", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        try
        {
            UiContext.Ensure();
            OverlayManager.Start();
            Environment(report);
            if (demo && !OverlayManager.InView(doc, view).Any(s => s.Visible && s.Cards.Length > 0))
                report.Step("demo cards", () => { ConduitPlusDemo.Place(doc, view, CheckOwner); view.Redraw(); return "placed the structural demo (all card kinds except Title) in the active view"; });
            ViewGeometry(report, view);
            Fonts(report);
            Rendering(report);
            if (clipboard) Clipboard(report);
            if (exports) Exports(report, doc, view, pdf);
            Input(report, doc, view);
            if (pointer) PointerSampling(report, view);
        }
        catch (Exception e) { report.Fail("platform check", e.ToString()); }
        report.Write();
        RhinoApp.WriteLine($"Conduit+ platform check: {report.Summary}. Report: {report.Folder}");
    }

    private static void Environment(Report r)
    {
        r.Info("os", RuntimeInformation.OSDescription);
        r.Info("architecture", RuntimeInformation.ProcessArchitecture.ToString());
        r.Info("runtime", RuntimeInformation.FrameworkDescription);
        r.Info("rhino", RhinoApp.Version.ToString());
        r.Info("platform adapter", HostInput.IsMac ? "macOS (AppKit)" : "Windows (Win32/WinForms)");
        r.Info("conduit+", typeof(OverlayManager).Assembly.GetName().Version?.ToString() ?? "?");
        foreach (var name in new[] { "System.Drawing.Common", "System.Windows.Forms", "Eto", "RhinoCommon" })
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            r.Info($"assembly {name}", assembly is null ? "not loaded" : $"{assembly.GetName().Version} @ {assembly.Location}");
        }
    }

    private static void ViewGeometry(Report r, RhinoView view)
    {
        var size = view.MainViewport.Size;
        r.Info("viewport", $"{view.MainViewport.Name} {size.Width}×{size.Height} (MainViewport.Size)");
        r.Info("client rectangle", view.ClientRectangle.ToString());
        r.Info("screen rectangle", view.ScreenRectangle.ToString());
        r.Info("chosen unit (UiContext.Dpi)", UiContext.Dpi(view).ToString("0.###"));
        try { r.Info("DisplayPipeline.DpiScale", view.DisplayPipeline.DpiScale.ToString("0.###")); } catch (Exception e) { r.Info("DisplayPipeline.DpiScale", e.Message); }
        if (!HostInput.IsMac) { r.Pass("view scale", "Windows GetDpiForWindow (unchanged)"); return; }
        r.Step("NSView geometry", () =>
        {
            var handle = view.Handle; var ns = MacView.NsView(view); var window = AppKit.Window(ns);
            r.Info("view handle", $"0x{handle:x} {(ns == IntPtr.Zero ? "not found in the AppKit window/view tree" : ns == handle ? "is an NSView" : "is an NSWindow")}");
            if (ns == IntPtr.Zero) throw new InvalidOperationException("RhinoView.Handle is neither an NSView nor an NSWindow of this app; scale falls back to 1 and wheel scrolling over cards is unavailable.");
            var bounds = AppKit.Bounds(ns); var backing = AppKit.BackingScale(window);
            r.Info("NSView class / bounds", $"{ObjC.ClassName(ns)} {bounds} flipped={AppKit.IsFlipped(ns)}");
            r.Info("window backingScaleFactor", backing.ToString("0.###"));
            var ratioX = size.Width / bounds.Size.Width; var ratioY = size.Height / bounds.Size.Height;
            r.Info("viewport/bounds ratio", $"x={ratioX:0.###} y={ratioY:0.###}");
            if (MacView.ScaleOverride is { } o) r.Info("CONDUITPLUS_MAC_VIEW_SCALE", o.ToString("0.###"));
            if (Math.Abs(ratioX - ratioY) > .05) throw new InvalidOperationException($"Viewport and NSView aspect differ (x {ratioX:0.###} vs y {ratioY:0.###}): the view handle is not the viewport surface. Check the pointer sampling section.");
            var verdict = Math.Abs(ratioX - backing) < .05 ? "Rhino viewport is in backing pixels; cards render at the Retina factor"
                : Math.Abs(ratioX - 1) < .05 ? "Rhino viewport is in points; cards render at 1× (sharpness on Retina: see checklist)"
                : "unexpected ratio; see checklist";
            return verdict;
        });
    }

    private static void Fonts(Report r)
    {
        r.Step("font", () =>
        {
            var family = GdiCanvas.Family("Segoe UI");
            using var font = new Font(family, 12, FontStyle.Regular, GraphicsUnit.Pixel);
            var mg = TextMetrics.Measurer("Mg", 12, "Segoe UI");
            var estimate = TextMetrics.Estimate("Mg", 12, "Segoe UI");
            r.Info("card font family", $"requested Segoe UI → {family} → System.Drawing face '{font.Name}'");
            r.Info("measure 'Mg' @12px", $"{mg.Width:0.##}×{mg.Height:0.##} (fallback estimate {estimate.Width:0.##}×{estimate.Height:0.##})");
            if (mg.Width <= 0 || mg.Height <= 0) throw new InvalidOperationException("Text measurement returned an empty size.");
            if (HostInput.IsMac && !string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"System.Drawing substituted '{font.Name}' for '{family}'. Measurement and paint still agree, but report this face.");
            return "measurement and paint use one installed family";
        });
    }

    private static IEnumerable<CardSpec> EveryKind() =>
    [
        new() { Id = "title", Kind = CardKind.Title, Title = "Conduit+ platform check", Subtitle = "Every card kind" },
        .. StructuralDemo.Create(2), StructuralDemo.Line(), .. StructuralDemo.Extras(),
    ];

    private static void Rendering(Report r)
    {
        var folder = Directory.CreateDirectory(Path.Combine(r.Folder, "cards")).FullName;
        foreach (var name in new[] { "Paper", "Night", "Smoke", "Blueprint" })
        {
            if (!Theme.TryGet(name, out var theme)) continue;
            foreach (var spec in EveryKind())
                foreach (var scale in new[] { 1d, 2d })
                    r.Step($"render {spec.Kind} '{spec.Id}' {name} {scale}×", () =>
                    {
                        var watch = Stopwatch.StartNew();
                        using var image = RasterDrawing.Render(spec, theme, scale);
                        var size = CardMeasure.Measure(spec, theme); var margin = RasterDrawing.Margin(theme, scale);
                        var expected = new Size((int)Math.Ceiling(size.Width * scale) + 2 * margin, (int)Math.Ceiling(size.Height * scale) + 2 * margin);
                        var ink = Ink(image);
                        if (name == "Paper" || scale == 2) image.Save(Path.Combine(folder, $"{name}-{spec.Id}-{scale}x.png"), ImageFormat.Png);
                        if (Math.Abs(image.Width - expected.Width) > 1 || Math.Abs(image.Height - expected.Height) > 1)
                            throw new InvalidOperationException($"bitmap {image.Width}×{image.Height}, expected {expected.Width}×{expected.Height}");
                        if (ink < .02) throw new InvalidOperationException($"only {ink:P1} of pixels are painted");
                        return $"{image.Width}×{image.Height}, {ink:P0} painted, {watch.Elapsed.TotalMilliseconds:0} ms";
                    });
            r.Step($"render stack {name} 2×", () =>
            {
                using var stack = RasterDrawing.RenderStack(EveryKind().Take(4).ToArray(), theme, 2);
                stack.Save(Path.Combine(folder, $"{name}-stack-2x.png"), ImageFormat.Png);
                return $"{stack.Width}×{stack.Height}";
            });
        }
        r.Step("render tooltip", () =>
        {
            using var tip = RasterDrawing.RenderTooltip(["M07", "Utilisation 1.12", "Requires review"], Theme.Paper, 2);
            tip.Save(Path.Combine(folder, "tooltip-2x.png"), ImageFormat.Png);
            return $"{tip.Width}×{tip.Height}";
        });
    }

    /// <summary>Fraction of pixels with any alpha (cards are drawn on transparent textures).</summary>
    private static double Ink(Bitmap image)
    {
        var painted = 0; var step = Math.Max(1, Math.Min(image.Width, image.Height) / 64);
        var total = 0;
        for (var y = 0; y < image.Height; y += step)
            for (var x = 0; x < image.Width; x += step) { total++; if (image.GetPixel(x, y).A > 0) painted++; }
        return total == 0 ? 0 : painted / (double)total;
    }

    private static void Clipboard(Report r)
    {
        r.Step("copy card as image", () =>
        {
            var cards = EveryKind().Skip(1).Take(2).ToArray();
            if (!CardClipboard.TryCopy(cards, Theme.Paper, out var error)) throw new InvalidOperationException(error);
            if (HostInput.IsMac)
            {
                var types = MacPasteboard.Describe();
                r.Info("pasteboard types", string.Join(", ", types));
                var png = MacPasteboard.Read("public.png");
                if (png.Length == 0) throw new InvalidOperationException("public.png missing from the pasteboard after copy");
                using var decoded = new Bitmap(new MemoryStream(png));
                if (!types.Any(t => t.StartsWith("public.tiff", StringComparison.Ordinal))) return $"PNG {decoded.Width}×{decoded.Height} on the pasteboard; TIFF fallback missing (paste into Keynote/Preview to check)";
                return $"PNG {decoded.Width}×{decoded.Height} + TIFF on the pasteboard; paste into Keynote/Pages/Preview to confirm";
            }
            return System.Windows.Forms.Clipboard.ContainsData("PNG") ? "PNG + DIB on the Windows clipboard" : throw new InvalidOperationException("PNG missing from the clipboard");
        });
    }

    private static void Exports(Report r, RhinoDoc doc, RhinoView view, bool pdf)
    {
        if (!OverlayManager.InView(doc, view).Any(s => s.Visible && s.Cards.Length > 0)) { r.Fail("exports", "no cards in the active view (enable DemoCards)"); return; }
        foreach (var multiplier in new[] { 1, 2 })
            r.Step($"ViewCapture PNG {multiplier}×", () =>
            {
                OverlayManager.Busy = true;
                try { ConduitPlusCapture.CaptureToFile(doc, view, multiplier, false); }
                finally { OverlayManager.Busy = false; }
                var path = Path.Combine(OutputPaths.Captures, $"raster-{multiplier}x.png");
                using var image = new Bitmap(path);
                return $"{path} {image.Width}×{image.Height}";
            });
        if (!pdf) { r.Info("vector PDF", "skipped (unattended safe-mode run)"); return; }
        r.Step("vector PDF", () =>
        {
            OverlayManager.Busy = true;
            try { ConduitPlusPdf.RunAutomated(doc, view); }
            finally { OverlayManager.Busy = false; }
            var path = Path.Combine(OutputPaths.Captures, "overlay-vector.pdf");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1000) throw new InvalidOperationException($"{path} missing or tiny");
            return $"{path} ({info.Length / 1024} KB); open it and compare with the viewport";
        });
    }

    private static void Input(Report r, RhinoDoc doc, RhinoView view)
    {
        r.Info("wheel/pointer route", OverlayManager.InputRoute);
        if (!HostInput.IsMac) { r.Pass("input routing", OverlayManager.InputRoute); return; }
        r.Info("monitor events (total / moves / scroll / scroll consumed)", $"{MacDiagnostics.MonitorEvents} / {MacDiagnostics.MoveEvents} / {MacDiagnostics.ScrollEvents} / {MacDiagnostics.ScrollSwallowed}");
        r.Info("last scroll event", MacDiagnostics.LastScroll ?? "none yet (scroll over a table, then run this command again)");
        r.Info("MouseCallback moves seen", MacDiagnostics.MouseCallbackMoves.ToString());
        r.Info("UiTimer ticks so far", MacDiagnostics.TimerTicks.ToString());
        var cal = MacView.Calibration(view);
        r.Info("pointer calibration (Rhino − AppKit)", $"{cal.Correction.X:0.#}, {cal.Correction.Y:0.#} px from {cal.Samples} samples, last change {cal.LastError:0.#} px");
        var (shift, command, control) = HostInput.MacModifiers();
        r.Info("modifiers now", $"shift={shift} command={command} control={control} escape={MacEscape.Down}");
        foreach (var fault in MacDiagnostics.FaultLog) r.Fail("adapter fault", fault);
        if (!OverlayManager.MacInput.Installed) r.Fail("input routing", "AppKit event monitor is not installed: wheel/trackpad scrolling over cards is unavailable");
        else r.Pass("input routing", "AppKit event monitor installed");
        r.Step("UiTimer (Eto) ticks", () =>
        {
            using var timer = new UiTimer(20);
            var ticks = 0; timer.Tick += (_, _) => ticks++;
            var watch = Stopwatch.StartNew(); timer.Start();
            while (watch.ElapsedMilliseconds < 300) { RhinoApp.Wait(); Thread.Sleep(5); }
            timer.Stop();
            if (ticks == 0) throw new InvalidOperationException("no ticks in 300 ms: hover, tooltips and wheel pacing would stall");
            return $"{ticks} ticks in {watch.ElapsedMilliseconds} ms (≈ {300 / 20} expected)";
        });
    }

    private sealed class Sampler(RhinoView view, Report report) : GetPoint
    {
        internal readonly List<double> Errors = [];
        protected override void OnMouseMove(GetPointMouseEventArgs e)
        {
            base.OnMouseMove(e);
            try
            {
                if (e.Viewport.Id != view.MainViewport.Id) return;
                var screen = HostView.PointerScreen;
                if (HostView.ToClient(view, screen) is not { } mapped) { report.Fail("pointer sample", "AppKit could not map the pointer"); return; }
                var native = HostInput.IsMac ? MacView.Native(view, screen) : null;
                if (HostInput.IsMac && native is { } n) MacView.Calibration(view).Observe(new(e.WindowPoint.X, e.WindowPoint.Y), n);
                var error = Math.Sqrt(Math.Pow(mapped.X - e.WindowPoint.X, 2) + Math.Pow(mapped.Y - e.WindowPoint.Y, 2));
                Errors.Add(error);
                if (Errors.Count <= 3 || Errors.Count % 25 == 0)
                    report.Info($"sample {Errors.Count}", $"Rhino {e.WindowPoint} mapped {mapped} native {(native is { } p ? $"({p.X:0.#}, {p.Y:0.#})" : "n/a")} in-view {OverlayManager.PointerInView(view, screen)}");
            }
            catch (Exception ex) { report.Fail("pointer sample", ex.Message); }
        }
    }

    private static void PointerSampling(Report r, RhinoView view)
    {
        var get = new Sampler(view, r);
        get.SetCommandPrompt("Move the pointer around the active viewport (corners too), then click or press Enter");
        get.AcceptNothing(true);
        get.Get();
        if (get.Errors.Count == 0) { r.Fail("pointer mapping", "no MouseMove samples in the active viewport"); return; }
        var max = get.Errors.Max(); var mean = get.Errors.Average();
        var verdict = $"{get.Errors.Count} samples, mean error {mean:0.#} px, max {max:0.#} px (after calibration)";
        if (max <= 3) r.Pass("pointer mapping", verdict); else r.Fail("pointer mapping", verdict + ": hover/wheel targets may be offset; send report.txt");
    }

    private sealed class Report(string folder)
    {
        private readonly List<(string Status, string Name, string Detail)> _lines = [];
        public string Folder { get; } = Directory.CreateDirectory(folder).FullName;
        public void Info(string name, string detail) => _lines.Add(("INFO", name, detail));
        public void Pass(string name, string detail) => _lines.Add(("PASS", name, detail));
        public void Fail(string name, string detail) => _lines.Add(("FAIL", name, detail));
        public void Step(string name, Func<string> run)
        {
            try { Pass(name, run()); }
            catch (Exception e) { Fail(name, e.Message); }
        }
        public string Summary => $"{_lines.Count(l => l.Status == "PASS")} passed, {_lines.Count(l => l.Status == "FAIL")} failed";
        public void Write()
        {
            File.WriteAllLines(Path.Combine(Folder, "report.txt"), _lines.Select(l => $"{l.Status,-4} {l.Name}: {l.Detail}").Prepend($"Conduit+ platform check — {Summary}"));
            File.WriteAllText(Path.Combine(Folder, "report.json"), CardJson.Serialize(_lines.Select(l => new { status = l.Status, name = l.Name, detail = l.Detail }).ToArray()));
            foreach (var l in _lines.Where(l => l.Status == "FAIL")) RhinoApp.WriteLine($"  FAIL {l.Name}: {l.Detail}");
        }
    }
}
