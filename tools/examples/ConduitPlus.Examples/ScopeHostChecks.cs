using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

internal static class ScopeHostChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    internal static void Run(GH_Document gh, RhinoDoc doc, RhinoView view, string output, bool capture)
    {
        Console.WriteLine("Checking shared Scope Canvas/Overlay adapters.");
        var canvas = gh.Objects.OfType<CanvasCardComponent>().Single();
        var overlay = gh.Objects.OfType<OverlayComponent>().Single();
        var scope = gh.Objects.OfType<ScopeComponent>().Single();
        var state = (CanvasCardState)Get(canvas, "State");
        var binding = (ScopeBinding)canvas.GetType().GetField("_scope", Internal)!.GetValue(canvas)!;
        var original = state.Spec!;
        var completed = new List<string>();
        var labelSession = Session(overlay);
        var scene = labelSession.GetType().GetField("_labelView", Internal)!.GetValue(labelSession)!;
        var frameComplete = scene.GetType().GetField("_frameComplete", Internal)!;
        for (var i = 0; i < 100 && !(bool)frameComplete.GetValue(scene)!; i++)
        {
            RhinoApp.Wait();
            view.Redraw();
            Thread.Sleep(10);
        }
        Require((bool)frameComplete.GetValue(scene)!, "Initial label frame did not publish.");
        var empty = new System.Drawing.Point(view.MainViewport.Size.Width / 2, 1);
        var manager = typeof(OverlayManager);
        var solves = 0; gh.SolutionEnd += (_, _) => solves++;
        state.Activate(CardMeasure.ClearKey, BrushSelectMode.Toggle); gh.NewSolution(false);
        var before = solves;
        state.Hover("R01"); state.Scroll(2); state.Activate(TableSort.Key(0), BrushSelectMode.Toggle);
        Require(solves == before, "Hover/sort/scroll solved synchronously.");
        Flush();
        Require(binding.Current.HoverOrigin?.Surface == "canvas", "Canvas hover origin missing.");
        Require(Cards(Session(overlay)).Single(c => c.Id == original.Id).HoveredKeys.Contains("R01"), "Canvas hover did not reach Overlay.");
        state.CancelHover();
        var labels = (LabelPlacement[])scene.GetType().GetMethod("InspectFrame", Internal)!.Invoke(scene, [])!;
        var dpi = (double)typeof(OverlayManager).Assembly.GetType("ConduitPlus.Rhino.UiContext")!
            .GetMethod("Dpi")!.Invoke(null, [view])!;
        var pick = labelSession.GetType().GetMethod("PickLabel", Static)!;
        var pointer = labels.Select(label => new System.Drawing.Point((int)Math.Round((label.Dot.X + label.Dot.Width / 2) * dpi),
                (int)Math.Round((label.Dot.Y + label.Dot.Height / 2) * dpi)))
            .First(point => pick.Invoke(null, [view, point, false]) is not null);
        var hover = manager.GetMethod("Hover", Static)!;
        var clearHover = manager.GetMethod("ClearHover", Static)!;
        CheckStationary("canvas", () => state.Hover("R01"), state.CancelHover);
        CheckStationary("card", () => hover.Invoke(null, [labelSession, "R01", false, original.Id]),
            () => clearHover.Invoke(null, [labelSession, false]));
        CheckStationary("model", () => hover.Invoke(null, [labelSession, "R01", true, null]),
            () => clearHover.Invoke(null, [labelSession, true]));
        CheckStationary(null, () => { }, () => { });
        completed.Add("Stationary pointer acquires a published label only when Canvas, card and model hovers leave it unclaimed");

        void CheckStationary(string? surface, Action claim, Action release)
        {
            Call(scene, "InvalidateLabelInput");
            manager.GetMethod("MovePointerForChecks", Static)!.Invoke(null, [view, pointer, true]);
            Require(pick.Invoke(null, [view, pointer, false]) is null, "Label input was already published.");
            claim();
            var previous = binding.Current.HoverOrigin;
            if (surface is not null) Require(previous is not null && binding.Current.HoveredKeys.Contains("R01"),
                $"Could not establish the {surface} hover.");
            Call(scene, "PublishLabelInput");
            Require(pick.Invoke(null, [view, pointer, false]) is not null, "Published label is not hittable.");
            manager.GetMethod("RecheckPointerForChecks", Static)!.Invoke(null, []);
            if (surface is null) Require(binding.Current.HoverOrigin?.Surface == "label", "Unclaimed pointer did not acquire label.");
            else Require(binding.Current.HoverOrigin == previous && binding.Current.HoveredKeys.Contains("R01"),
                $"Label publication displaced {surface} hover.");
            release();
            ForgetPointer();
        }
        state.CancelHover();
        state.Activate("R01", BrushSelectMode.Toggle);
        Require(solves == before && binding.Current.SelectedKeys.SequenceEqual(["R01"]), "Canvas action did not commit without solving.");
        Flush();
        Require(Cards(Session(overlay)).Single(c => c.Id == original.Id).SelectedKeys.SequenceEqual(["R01"]), "Canvas selection did not repaint Overlay.");
        gh.NewSolution(false);
        Require(solves == before + 1, "Canvas action did not coalesce into one solution.");
        Require(Output(canvas, 0).SequenceEqual(["R01"]) && Output(overlay, 0).SequenceEqual(["R01"]), "Scope Selection output differs between hosts.");
        Require(Output(overlay, 1).Last().Contains("\"sourceSurface\":\"canvas\"", StringComparison.Ordinal), "Overlay Events omitted Canvas action surface.");
        completed.Add("Canvas -> Overlay projection, whole-domain Selection, Canvas event, coalesced output solution; hover/sort/scroll repaint only");

        var second = Add(new OverlayComponent());
        second.Params.Input[0].AddSource(overlay.Params.Input[0].Sources.Single(s => s == canvas.Params.Input[0].Sources[0]));
        foreach (var source in overlay.Params.Input[1].Sources) second.Params.Input[1].AddSource(source);
        second.Params.Input[3].AddSource(scope.Params.Output[0]); gh.NewSolution(false);
        var events = binding.Current.EventRevision;
        Call(Session(second), "Select", "model", "R02", BrushSelectMode.Toggle);
        Require(binding.Current.EventRevision == events + 1, "Winning model owner emitted more than one action.");
        Require(state.Spec!.SelectedKeys.Order().SequenceEqual(new[] { "R01", "R02" }), "Model action did not project onto Canvas.");
        Flush();
        Require(Cards(Session(overlay)).Single(c => c.Id == original.Id).SelectedKeys.Order().SequenceEqual(new[] { "R01", "R02" }), "Second Overlay did not share state.");
        gh.NewSolution(false);
        Require(Output(second, 0).SequenceEqual(Output(canvas, 0)) && Output(second, 1).SequenceEqual(Output(overlay, 1)), "Shared Overlay outputs/history differ.");
        completed.Add("Two Overlays share selection/history and preserve separate sessions; model action -> Canvas with exactly one event");

        // An unscoped owner and an unscoped Canvas retain private authorities even for identical specs.
        var privateOwner = Guid.NewGuid();
        OverlayManager.Set(doc, view, privateOwner, [original]);
        var privateSession = Sessions().Single(s => (Guid)Get(s, "Owner") == privateOwner);
        Call(privateSession, "Select", original.Id, "R03", BrushSelectMode.Toggle);
        Require(OverlayManager.Snapshot(privateOwner).SelectedKeys.SequenceEqual(["R03"]) && !binding.Current.SelectedKeys.Contains("R03"), "Unscoped owner leaked selection.");
        OverlayManager.Remove(privateOwner);
        completed.Add("Identical unscoped Overlay owner remains isolated (private Canvas also checked by existing architecture harness)");

        // Only the actual pointer host may produce a tooltip from a shared highlight.
        Call(Session(overlay), "Cursor", new Point2(200, 200), true);
        Call(Session(second), "Cursor", new Point2(200, 200), true);
        Call(Session(second), "SetHover", original.Id, "R02");
        Require(Session(overlay).GetType().GetField("_tooltipTarget", Internal)!.GetValue(Session(overlay)) is null, "Remote scope hover spawned an Overlay tooltip.");
        state.Hover("R01"); Call(Session(second), "SetHover", null, null);
        Require(binding.Current.HoverOrigin?.Surface == "canvas", "A remote miss cleared Canvas hover."); state.CancelHover();
        completed.Add("Origin-safe remote misses and local-only Overlay tooltip target");

        canvas.Locked = true;
        var locked = binding.Current.EventRevision; state.Activate("R03", BrushSelectMode.Toggle);
        Require(binding.Current.EventRevision == locked, "Locked Canvas originated an action.");
        Call(Session(second), "Select", "model", "R03", BrushSelectMode.Toggle);
        canvas.Locked = false;
        Require(state.Spec!.SelectedKeys.Contains("R03"), "Unlock did not project latest snapshot without solving.");
        overlay.Locked = true; locked = binding.Current.EventRevision; Call(Session(overlay), "Select", "model", "R04", BrushSelectMode.Toggle);
        Require(binding.Current.EventRevision == locked, "Locked Overlay originated an action."); overlay.Locked = false;
        gh.NewSolution(false); completed.Add("Component lock blocks origins; unlock reprojects without data solve");

        var saved = binding.Current.SelectedKeys.ToArray(); before = solves;
        gh.Context = GH_DocumentContext.Unloaded;
        Require(Sessions().All(s => (Guid)Get(s, "Owner") != overlay.InstanceGuid), "Unloaded Overlay rendering survived.");
        gh.Context = GH_DocumentContext.Loaded;
        Require(solves == before && state.Current.SelectedKeys.SequenceEqual(saved) && OverlayManager.Snapshot(overlay.InstanceGuid).SelectedKeys.SequenceEqual(saved), "Tab reload lost selection or required a solve.");
        completed.Add("GH Unloaded/Loaded retains membership/selection and recreates Overlay rendering without solving");

        GH_Document.EnableSolutions = false;
        state.Activate("R04", BrushSelectMode.Toggle); Require(binding.Current.SelectedKeys.Contains("R04"), "Paused solver lost committed selection.");
        Require(solves == before, "Paused solver evaluated outputs.");
        GH_Document.EnableSolutions = true; gh.NewSolution(false);
        Require(Output(canvas, 0).Contains("R04"), "Paused output callback did not publish on resume.");
        completed.Add("Paused global solver keeps committed display state; output revisions publish on resume");

        // Real GH whole-tree validation, including a token from another document and a wired-empty tree.
        var probe = Add(new Param_GenericObject());
        canvas.Params.Input[1].RemoveAllSources(); canvas.Params.Input[1].AddSource(probe);
        overlay.Params.Input[3].RemoveAllSources(); overlay.Params.Input[3].AddSource(probe);
        foreach (var tokens in new IGH_Goo[][] { [], [new GH_String("bad")], [new ScopeGoo(new(ulong.MaxValue, scope.InstanceGuid))],
            [new ScopeGoo(binding.Lease.Token), new ScopeGoo(binding.Lease.Token)] })
        {
            probe.PersistentData.Clear(); probe.PersistentData.AppendRange(tokens); probe.ExpireSolution(false); canvas.ExpireSolution(false); overlay.ExpireSolution(false); gh.NewSolution(false);
            Require(overlay.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && Output(overlay, 0).Length == 0, "Overlay invalid Scope did not error/detach.");
            Require(canvas.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && !state.CanInteract, "Invalid/multiple/foreign/empty Scope did not disable brush.");
        }
        canvas.Params.Input[1].RemoveAllSources(); canvas.Params.Input[1].AddSource(scope.Params.Output[0]);
        overlay.Params.Input[3].RemoveAllSources(); overlay.Params.Input[3].AddSource(scope.Params.Output[0]);
        gh.RemoveObject(probe, false); canvas.ExpireSolution(false); overlay.ExpireSolution(false); gh.NewSolution(false);
        completed.Add("Both displays reject wired empty, text, foreign-document and multiple-token trees; valid rewiring recovers");

        // Delete/undo uses GH's real component/wire serialization, never saved transient selection.
        gh.UndoUtil.RecordRemoveObjectEvent("Delete shared Canvas", canvas);
        gh.RemoveObject(canvas, false);
        gh.UndoServer.PerformUndo();
        canvas = gh.Objects.OfType<CanvasCardComponent>().Single(); canvas.ExpireSolution(false); gh.NewSolution(false);
        state = (CanvasCardState)Get(canvas, "State");
        Require(state.Current.SelectedKeys.SequenceEqual(saved.Concat(["R04"]).Order()), "Delete/undo display lost surviving scope selection.");
        completed.Add("Participant delete/undo restores wires and rejoins surviving scope");

        var alternate = Add(new ScopeComponent());
        canvas.Params.Input[1].RemoveAllSources(); canvas.Params.Input[1].AddSource(alternate.Params.Output[0]);
        canvas.ExpireSolution(false); gh.NewSolution(false);
        Require(state.CanInteract && state.Current.SelectedKeys.Count == 0, "Changing Scope copied old selection or disabled valid new scope.");
        state.Activate("R05", BrushSelectMode.Toggle); Require(!OverlayManager.Snapshot(overlay.InstanceGuid).SelectedKeys.Contains("R05"), "Distinct Scope tokens coupled selection.");
        canvas.Params.Input[1].RemoveAllSources(); canvas.Params.Input[1].AddSource(scope.Params.Output[0]);
        canvas.ExpireSolution(false); gh.RemoveObject(alternate, false); gh.NewSolution(false);
        Require(state.Current.SelectedKeys.Contains("R04") && !state.Current.SelectedKeys.Contains("R05"), "Reattachment did not use original domain authority.");
        completed.Add("Scope changes attach atomically, preserve independent domains and never copy selection");

        var oldBinding = (ScopeBinding)canvas.GetType().GetField("_scope", Internal)!.GetValue(canvas)!;
        gh.UndoUtil.RecordRemoveObjectEvent("Delete Scope", scope); gh.RemoveObject(scope, false);
        Require(!oldBinding.IsValid && !state.CanInteract, "Deleting Scope retained live brush authority.");
        gh.UndoServer.PerformUndo(); scope = gh.Objects.OfType<ScopeComponent>().Single();
        canvas.ExpireSolution(false); overlay.ExpireSolution(false); second.ExpireSolution(false); gh.NewSolution(false);
        Require(state.Current.SelectedKeys.Count == 0 && state.CanInteract, "Undo Scope restored transient selection or failed to attach.");
        completed.Add("Scope delete invalidates all adapters immediately; undo restores wires with empty transient state");

        var temporaryView = doc.Views.Add("Scope lifecycle probe", DefinedViewportProjection.Top, new Rectangle(0, 0, 600, 500), true)
            ?? throw new InvalidOperationException("Cannot create lifecycle view.");
        doc.Views.ActiveView = temporaryView; overlay.ExpireSolution(false); gh.NewSolution(false);
        Require(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId == temporaryView.MainViewport.Id, "Normal solve did not bind test view.");
        temporaryView.Close();
        state.Activate("R01", BrushSelectMode.Toggle); gh.NewSolution(false);
        Require(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId is null, "Interaction rebound closed view to another active view.");
        doc.Views.ActiveView = view; overlay.ExpireSolution(false); gh.NewSolution(false);
        Require(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId == view.MainViewport.Id, "Normal data solve did not restore view binding.");
        completed.Add("Closing an owned Rhino view drops rendering; shared interaction cannot redirect it; normal data solve rebinds");
        gh.RemoveObject(second, false); gh.NewSolution(false);
        state.Activate(CardMeasure.ClearKey, BrushSelectMode.Toggle); state.Activate("R01", BrushSelectMode.Toggle); gh.NewSolution(false);
        ScopeFeedbackChecks.Run(output);
        if (capture)
        {
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(-18, -18, -1, 38, 34, 1)); view.Redraw();
            var session = Session(overlay);
            manager.GetMethod("MovePointerForChecks", Static)!.Invoke(null, [view, empty, true]);
            state.Hover("R01");
            Flush();
            CheckCanvasHover();
            var previousOutput = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS");
            try
            {
                Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", output);
                ExportChecks.Run(doc, view, session, Cards(session), output, true, 1);
                Flush();
                CheckCanvasHover();
                CheckLabelExports(session);
                Require(new FileInfo(Path.Combine(output, "captures", "overlay-vector.pdf")).Length > 0,
                    "Scope PDF export wrote no data.");
                Flush();
                CheckCanvasHover();
                ForgetPointer();
                state.CancelHover();
            }
            finally { Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", previousOutput); }
            File.Copy(Path.Combine(output, "captures", "raster-1x.png"), Path.Combine(output, "scope-viewport-1x.png"), true);
            using var control = new GH_Canvas(); control.Document = gh;
            canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
            var bounds = Rectangle.Ceiling(canvas.Attributes.Bounds); bounds.Inflate(40, 40);
            var viewport = new GH_Viewport(control.Viewport) { Width = bounds.Width, Height = bounds.Height, Zoom = 1, Tx = -bounds.X, Ty = -bounds.Y };
            using var image = control.GenerateHiResImageTile(viewport, Color.White);
            image.Save(Path.Combine(output, "scope-canvas-1x.png"), ImageFormat.Png);
            // Avoid disposing GH's definition through the temporary export control.
            control.Document = null;
            gh.Enabled = true; gh.Context = GH_DocumentContext.Loaded;
            completed.Add("1x GH export and viewport PNG of the same selected R01; Canvas hover survives PNG and PDF export");
        }
        File.WriteAllText(Path.Combine(output, "scope-checks.json"), CardJson.Serialize(completed));
        Console.WriteLine(string.Join(Environment.NewLine, completed));

        void CheckSlowExport(object session)
        {
            var occlusion = Get(session, "LabelScene");
            Call(occlusion, "Invalidate");
            Call(occlusion, "ScanSources");
            var sources = occlusion.GetType().GetField("_sources", Internal)!;
            var actual = (IEnumerator<KeyValuePair<object, Func<IEnumerable<GeometryBase>>>>)sources.GetValue(occlusion)!;
            sources.SetValue(occlusion, SlowSources(actual).GetEnumerator());
            var composeCount = 0;
            var command = typeof(ConduitPlusCapture).Assembly.GetType("ConduitPlus.Rhino.CommandRunner")!;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            command.GetMethod("RunExport", Static)!.Invoke(null, [doc, view, (Action)(() =>
            {
                ConduitPlusPdf.RunAutomated(doc, view);
                composeCount++;
            })]);
            Require(composeCount == 0, "Export composed synchronously during a slow harvest.");
            while (composeCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(45))
            {
                Thread.Sleep(15);
                Call(occlusion, "Continue", null, EventArgs.Empty);
            }
            Require(composeCount == 1 && stopwatch.Elapsed.TotalSeconds > 10, "Slow export did not compose once after harvest.");
            Console.WriteLine($"Slow harvest exported after {stopwatch.Elapsed.TotalSeconds:F1}s.");

            static IEnumerable<KeyValuePair<object, Func<IEnumerable<GeometryBase>>>> SlowSources(
                IEnumerator<KeyValuePair<object, Func<IEnumerable<GeometryBase>>>> originalSources)
            {
                using (originalSources)
                {
                    while (originalSources.MoveNext()) yield return originalSources.Current;
                    for (var i = 0; i < 560; i++)
                    {
                        Thread.Sleep(4);
                        yield return new(new object(), () => []);
                    }
                }
            }
        }

        void CheckLabelExports(object session)
        {
            const string sentinel = "VQ Label 479";
            const int swatch = unchecked((int)0xffd02060);
            var field = session.GetType().GetField("_labels", Internal)!;
            var originalLabels = (Array)field.GetValue(session)!;
            var type = originalLabels.GetType().GetElementType()!;
            var anchor = originalLabels.GetValue(0)!;
            var source = new LabelSource(Guid.NewGuid(), "export fixture");
            var testLabels = Array.CreateInstance(type, 1);
            testLabels.SetValue(Activator.CreateInstance(type, [source, null, 0, Get(anchor, "Point"),
                new LabelStyle(sentinel, null, sentinel, swatch, 100)]), 0);
            var captures = Path.Combine(output, "captures");
            var spare = Guid.NewGuid();
            try
            {
                Call(session, "SetLabels", testLabels);
                CheckSlowExport(session);
                var frameType = typeof(OverlayManager).Assembly.GetType("ConduitPlus.Rhino.LabelExportFrame")!;
                using var frame = (IDisposable)session.GetType().GetMethod("SnapshotLabels", Static)!
                    .Invoke(null, [doc, view, 1d, false])!;
                var glyphs = (List<(LabelRasterKey Key, Rect2 Bounds, string? Accent)>)frameType
                    .GetField("_glyphs", Internal)!.GetValue(frame)!;
                var pill = glyphs.Single(g => g.Key.Text == sentinel).Bounds;
                var dot = glyphs.Single(g => g.Key.Form == LabelForm.Dot).Bounds;
                File.WriteAllText(Path.Combine(captures, "label-check.json"), CardJson.Serialize(new { sentinel, dpi, pill, dot }));
                File.Move(Path.Combine(captures, "overlay-vector.pdf"), Path.Combine(captures, "label-on.pdf"), true);
                OverlayManager.Set(doc, view, spare, [original with { Title = "Preview off control" }]);
                session.GetType().GetProperty("Visible", Internal)!.SetValue(session, false);
                ConduitPlusPdf.RunAutomated(doc, view);
                File.Move(Path.Combine(captures, "overlay-vector.pdf"), Path.Combine(captures, "preview-off.pdf"), true);
                File.Move(Path.Combine(captures, "label-on.pdf"), Path.Combine(captures, "overlay-vector.pdf"), true);
                WriteOpacityFixture(frameType, captures, swatch);
            }
            finally
            {
                session.GetType().GetProperty("Visible", Internal)!.SetValue(session, true);
                Call(session, "SetLabels", originalLabels);
                OverlayManager.Remove(spare);
            }
        }

        void CheckCanvasHover()
        {
            var canvasOrigin = state.Current.HoverOrigin?.Surface;
            var overlayHovered = Cards(Session(overlay)).Single(c => c.Id == original.Id).HoveredKeys;
            Require(canvasOrigin == "canvas" && overlayHovered.Contains("R01"),
                $"Label frame or export displaced Canvas hover (origin {canvasOrigin}, Overlay keys {string.Join(", ", overlayHovered)}).");
        }
        void ForgetPointer() => manager.GetMethod("MovePointerForChecks", Static)!.Invoke(null, [view, empty, false]);
        void Flush() => typeof(OverlayManager).GetNestedType("InteractionPump", Static)!
            .GetMethod("Flush", Static)!.Invoke(null, [true]);
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        object[] Sessions() => ((Array)typeof(OverlayManager).GetMethod("InView", Static)!.Invoke(null, [doc, view])!).Cast<object>().ToArray();
        object Session(OverlayComponent owner) => Sessions().Single(s => (Guid)Get(s, "Owner") == owner.InstanceGuid);
    }
    private static void WriteOpacityFixture(Type frameType, string output, int swatch)
    {
        const double scale = 4;
        const string accent = "#2458A0";
        using var frame = (IDisposable)Activator.CreateInstance(frameType, [scale])!;
        using var image = new Bitmap(480, 160);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        var pdf = global::Rhino.FileIO.FilePdf.Create()!;
        try
        {
            var page = pdf.AddPage(image.Width, image.Height, (int)(96 * scale));
            Add(LabelForm.Dot, new(5, 5, 5, 5));
            Add(LabelForm.Full, new(20, 5, 75, 18));
            Call(frame, "DrawPdf", pdf, page);
            pdf.Write(Path.Combine(output, "label-opacity.pdf"));
            image.Save(Path.Combine(output, "label-opacity.png"), ImageFormat.Png);
        }
        finally { (pdf as IDisposable)?.Dispose(); }

        void Add(LabelForm form, Rect2 bounds)
        {
            var key = new LabelRasterKey(form == LabelForm.Dot ? "" : "Fade probe", swatch,
                LabelPalette.FromTheme(Theme.Paper), scale, form, bounds.Width, .45);
            Call(frame, "Add", key, bounds, accent);
            using var raster = ConduitPlus.Rendering.RasterDrawing.RenderLabel(key, accent);
            graphics.DrawImageUnscaled(raster, (int)((bounds.X - 1) * scale), (int)((bounds.Y - 1) * scale));
        }
    }

    private static string[] Output(GH_Component owner, int index) => owner.Params.Output[index].VolatileData.AllData(true).Select(g => g.ToString()!).ToArray();
    private static CardSpec[] Cards(object session) => (CardSpec[])Get(session, "Cards");
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Internal)?.GetValue(obj) ?? throw new InvalidOperationException("Missing property " + name);
    private static void Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Internal)!.Invoke(obj, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

