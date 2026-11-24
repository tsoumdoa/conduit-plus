using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal static class LabelsBench
{
    internal static void Run(RhinoDoc doc, RhinoView view, Action pump, bool benchmark = true)
    {
        CheckAnchors();
        var server = new GH_ComponentServer();
        typeof(Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, server);
        var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(Assembly), typeof(string), typeof(GH_LoadingMechanism)], null)!;
        foreach (var assembly in new[] { typeof(Instances).Assembly, typeof(OverlayComponent).Assembly })
            parse.Invoke(server, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
        var components = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "Plug-ins", "Grasshopper", "Components");
        foreach (var name in new[] { "SurfaceComponents", "XformComponents", "MathComponents", "VectorComponents" })
        {
            var assembly = Assembly.LoadFrom(Path.Combine(components, name + ".gha"));
            parse.Invoke(server, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
        }
        GH_Document.EnableSolutions = true;

        using var gh = new GH_Document { Enabled = true };
        var points = new Param_Point();
        points.PersistentData.Append(new GH_Point(view.MainViewport.CameraTarget));
        var link = new LinkComponent();
        var labels = new LabelsComponent();
        var overlay = new OverlayComponent();
        foreach (var obj in new IGH_DocumentObject[] { points, link, labels, overlay }) gh.AddObject(obj, false);
        link.Params.Input[0].AddSource(points);
        labels.Params.Input[0].AddSource(link.Params.Output[0]);
        ((Param_String)labels.Params.Input[2]).PersistentData.Append(new GH_String("Explicit caption"));
        overlay.Params.Input[1].AddSource(link.Params.Output[0]);
        Check(!overlay.ShowLinkKeys, "Show Link keys starts off");
        overlay.ToggleLinkKeys();
        Check(overlay.ShowLinkKeys, "Show Link keys toggles on");
        gh.UndoServer.PerformUndo();
        overlay = gh.Objects.OfType<OverlayComponent>().Single();
        Check(!overlay.ShowLinkKeys, "Show Link keys is undoable");
        gh.UndoServer.PerformRedo();
        overlay = gh.Objects.OfType<OverlayComponent>().Single();
        Check(overlay.ShowLinkKeys, "Show Link keys is redoable");
        gh.NewSolution(false);
        var session = OverlayManager.Get(doc, view, overlay.InstanceGuid);
        Check(session.Cards.Length == 0 && session.InspectLabels().Single().Anchor.Style.Text == "1", "Labels-only automatic publication");

        overlay.Params.Input[6].AddSource(link.Params.Output[0]);
        overlay.Params.Input[6].AddSource(labels.Params.Output[0]);
        overlay.ExpireSolution(false);
        gh.NewSolution(false);
        var second = new OverlayComponent();
        gh.AddObject(second, false);
        second.Params.Input[6].AddSource(link.Params.Output[0]);
        gh.NewSolution(false);
        var otherSession = OverlayManager.Get(doc, view, second.InstanceGuid);
        var scene = session.LabelScene ?? throw new InvalidOperationException("Missing label scene");
        Check(otherSession.Cards.Length == 0 && ReferenceEquals(scene, otherSession.LabelScene), "Labels-only owners share a viewport scene");

        for (var i = 0; i < 100; i++)
        {
            pump();
            view.Redraw();
            if (session.InspectLabels().Single().Answer.Current && otherSession.InspectLabels().Single().Answer.Current)
                break;
            Thread.Sleep(10);
        }
        var resolved = session.InspectLabels().Single();
        Check(resolved.Anchor.Style.Text == "Explicit caption" && resolved.Answer is { Current: true, Visible: true }, "Explicit supersession and classified anchor");

        Check(otherSession.InspectLabels().Single().Answer is { Current: true, Visible: true } &&
            scene.HarvestCount == 1, "One harvest and combined classification through the viewport conduit");
        var frame = session.InspectLabelFrame();
        Check(frame.Length == 1 && frame[0].Form == ConduitPlus.Core.LabelForm.Dot,
            "Real foreground redraw places one dot for coincident owners");
        var dot = frame[0].Dot;
        var dpiForInput = UiContext.Dpi(view);
        var pointer = new System.Drawing.Point((int)Math.Round((dot.X + dot.Width / 2) * dpiForInput),
            (int)Math.Round((dot.Y + dot.Height / 2) * dpiForInput));
        var firstStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var hit = OverlaySession.PickLabel(view, pointer) ?? throw new InvalidOperationException("Missing displayed dot hit.");
        var hoverWork = OverlayManager.MovePointerForChecks(view, pointer);
        var firstInteraction = System.Diagnostics.Stopwatch.GetElapsedTime(firstStarted);
        Console.WriteLine(FormattableString.Invariant($"Label ApplyPending-only (first): {hoverWork.TotalMilliseconds:F2} ms"));

        Console.WriteLine(FormattableString.Invariant($"Label first interaction (pick + move + flush): {firstInteraction.TotalMilliseconds:F2} ms"));
        // Warm samples alternate between the dot and an empty point so every move changes hover.
        var away = new System.Drawing.Point(pointer.X + (int)Math.Round(60 * dpiForInput), pointer.Y + (int)Math.Round(60 * dpiForInput));
        var samples = new List<double>();
        for (var i = 0; i < 120; i++)
        {
            var work = OverlayManager.MovePointerForChecks(view, i % 2 == 0 ? away : pointer);
            if (i >= 20) samples.Add(work.TotalMilliseconds);
        }
        samples.Sort();
        double P(double q) => samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(q * samples.Count) - 1)];
        Console.WriteLine(FormattableString.Invariant($"Label ApplyPending-only warm: p50 {P(.5):F3} ms, p95 {P(.95):F3} ms, p99 {P(.99):F3} ms (frame budget {InteractionTiming.FrameMilliseconds} ms)"));
        Check(P(.95) < 2, "Warm label ApplyPending-only p95 under 2 ms");
        OverlayManager.MovePointerForChecks(view, pointer);
        Check(OverlayManager.Snapshot(hit.Owner.Owner).HoverKey == "1", "Paced dot hover brushes its member");
        view.Redraw();
        for (var i = 0; i < 10 && hit.Owner.InspectLabelPopover() is null; i++)
        {
            pump();
            view.Redraw();
        }
        var popover = hit.Owner.InspectLabelPopover() ?? throw new InvalidOperationException("Missing dot popover.");
        var expanded = new System.Drawing.Point((int)Math.Round((popover.X + 10) * dpiForInput), pointer.Y);
        Check(OverlaySession.PickLabel(view, expanded)?.Placement.Form == LabelForm.Dot, "Dot hover publishes a popover hit");
        var primary = PointerModifiers.Resolve(false, true, false, false);
        Check(ModelPicking.Pick(view, [hit.Owner], pointer) is not null, "Model geometry is pickable beneath the label");
        Check(OverlayManager.ClickForChecks(view, pointer, primary) == (true, true), "Label over model consumes mouse down and up");
        Check(OverlayManager.ClickForChecks(view, pointer, PointerModifiers.Resolve(true, true, false, true, physicalControl: true)) == (false, false),
            "Mac Control-click passes through the label");
        var component = hit.Owner.Owner == overlay.InstanceGuid ? overlay : second;
        component.ExpireSolution(false);
        gh.NewSolution(false);
        Check(component.Params.Output[0].VolatileData.AllData(false).OfType<GH_String>().Single().Value == "1" &&
            component.Params.Output[1].VolatileDataCount == 1 && OverlayManager.DomainSnapshot(hit.Owner)?.Events is [{ SourceSurface: "label" }], "Label click publishes selection and a single event");
        OverlayManager.SetInteractionEnabled(hit.Owner.Owner, false);
        Check(OverlaySession.PickLabel(view, pointer) is null &&
            OverlayManager.Snapshot(hit.Owner.Owner).HoverKey is null, "Disabled interaction clears label hover and hits");
        OverlayManager.SetInteractionEnabled(hit.Owner.Owner, true);
        OverlayManager.ClearSelection(hit.Owner.Owner);
        gh.RemoveObject(second, false);
        for (var i = 0; i < 50; i++)
        {
            view.Redraw();
            if (session.InspectLabelFrame() is [{ Form: LabelForm.Full } remaining] && remaining.Identity.Owner == session.Owner) break;
            pump();
            Thread.Sleep(10);
        }
        Check(session.InspectLabelFrame() is [{ Form: LabelForm.Full } remainingLabel] && remainingLabel.Identity.Owner == session.Owner,
            "Real foreground redraw expands the remaining owner to a pill");
        var expected = session.InspectLabelFrame().Select(label => (label.Identity, label.Form)).ToArray();
        var settleTicks = session.LabelSettleTicks;
        session.SetLabelPaintBudgetForChecks(fixedLimit: 0);
        var beforeMotion = session.LabelFrameTiming.Sequence;
        view.Redraw();
        if (session.LabelFrameTiming.Sequence == beforeMotion) pump();
        Check(session.LabelFrameTiming.Sequence > beforeMotion && session.LabelFrameTiming.InMotion &&
            session.InspectLabelFrame() is [{ Form: LabelForm.Dot }],
            $"Motion allowance leaves the expected label as a dot (sequence {beforeMotion} to {session.LabelFrameTiming.Sequence}, " +
            $"motion {session.LabelFrameTiming.InMotion}, forms {string.Join(", ", session.InspectLabelFrame().Select(label => label.Form))})");
        for (var i = 0; i < 100 && (session.LabelSettleTicks == settleTicks ||
            !session.InspectLabelFrame().Select(label => (label.Identity, label.Form)).SequenceEqual(expected)); i++)
        {
            Thread.Sleep(10);
            pump();
        }
        Check(session.LabelSettleTicks > settleTicks &&
            session.InspectLabelFrame().Select(label => (label.Identity, label.Form)).SequenceEqual(expected),
            $"Production settle timer restores the expected label identity and pill form without an explicit redraw " +
            $"(ticks {settleTicks} to {session.LabelSettleTicks}, frame {session.LabelFrameTiming.Sequence}, " +
            $"motion {session.LabelFrameTiming.InMotion}, forms {string.Join(", ", session.InspectLabelFrame().Select(label => label.Form))})");
        session.SetLabelPaintBudgetForChecks(fixedLimit: int.MaxValue);
        foreach (var dpi in new[] { 1, 2 })
        {
            session.SetLabelDpiForChecks(dpi);
            foreach (var theme in ConduitPlus.Core.Theme.Presets)
            {
                OverlayManager.ReplaceTheme(overlay.InstanceGuid, theme);
                using var target = new Bitmap(view.MainViewport.Size.Width, view.MainViewport.Size.Height);
                using var graphics = Graphics.FromImage(target);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                session.ObserveLabelPillForChecks((bitmap, x, y) => graphics.DrawImageUnscaled(bitmap, x, y));
                try { view.Redraw(); pump(); }
                finally { session.ObserveLabelPillForChecks(null); }
                var painted = session.InspectLabelFrame();
                Check(painted is [{ Form: LabelForm.Full, Dot.Width: 5, Pill.Height: 18 }],
                    $"Real foreground redraw at simulated {dpi}x with {theme.Name}");
                var pill = painted[0].Pill;
                var px = (int)Math.Round(pill.X * dpi) + (int)Math.Ceiling(pill.Width * dpi) - 2 * dpi;
                var py = (int)Math.Round(pill.Y * dpi) + 9 * dpi;
                var surface = ColorRgba.Parse(LabelPalette.FromTheme(session.Theme).Surface);
                var pixel = target.GetPixel(px, py);
                Check(pixel.A == surface.A && Math.Abs(pixel.R - surface.R) <= 1 &&
                    Math.Abs(pixel.G - surface.G) <= 1 && Math.Abs(pixel.B - surface.B) <= 1,
                    $"Foreground pill surface pixels at {dpi}x with {theme.Name}");
            }
        }
        session.SetLabelDpiForChecks(null);

        var output = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!;
        session.SetLabelPaintBudgetForChecks(fixedLimit: int.MaxValue);
        view.Redraw();
        foreach (var scale in new[] { 1, 2 })
        {
            var size = view.MainViewport.Size;
            var sequence = session.LabelFrameTiming.Sequence;
            using var capture = new ViewCapture { Width = size.Width * scale, Height = size.Height * scale }.CaptureToBitmap(view);
            Check(capture is not null && session.LabelFrameTiming.Sequence == sequence,
                $"Export exclusion at {scale}x leaves the live foreground label frame untouched");
            capture!.Save(Path.Combine(output, $"labels-scene-{scale}x.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        Check(otherSession.LabelScene is null && ReferenceEquals(session.LabelScene, scene) &&
            !scene.IsDisposed, "Removing one owner keeps the shared scene alive");

        Check(session.InspectLabelFrame().Length > 0, "Snapshot invalidation starts with a populated frame");
        var invalidated = false;
        var viewportId = view.MainViewport.Id;
        void InvalidateBeforeForeground(object? sender, DrawEventArgs args)
        {
            if (args.Viewport.Id != viewportId || args.Display.IsInViewCapture) return;
            scene.Invalidate();
            invalidated = true;
        }
        DisplayPipeline.PreDrawObjects += InvalidateBeforeForeground;
        try { view.Redraw(); pump(); }
        finally { DisplayPipeline.PreDrawObjects -= InvalidateBeforeForeground; }
        Check(invalidated && session.InspectLabelFrame().Length == 0,
            "An unavailable snapshot clears the published foreground label frame");
        for (var i = 0; i < 100 && session.InspectLabelFrame().Length == 0; i++)
        {
            scene.Refresh();
            Thread.Sleep(10);
            pump();
            view.Redraw();
        }

        var previousSize = view.MainViewport.Size;
        view.MainViewport.Size = new System.Drawing.Size(920, 700);
        view.Redraw();
        pump();
        Check(view.MainViewport.Size.Width < previousSize.Width && session.InspectLabelFrame().Length == 1,
            "Label frame stays valid after a viewport resize");
        view.MainViewport.Size = previousSize;

        var nextView = doc.Views.Add("Label target", DefinedViewportProjection.Perspective,
            new System.Drawing.Rectangle(0, 0, 600, 500), true)
            ?? throw new InvalidOperationException("Could not create second label viewport.");
        try
        {
            var viewInput = (Param_String)overlay.Params.Input[4];
            viewInput.PersistentData.Append(new GH_String(nextView.MainViewport.Name));
            viewInput.ExpireSolution(false);
            overlay.ExpireSolution(false);
            gh.NewSolution(false);
            Check(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId == nextView.MainViewport.Id &&
                session.LabelScene is null, "Named viewport moves label ownership");
            doc.Views.ActiveView = view;
            pump();
            Check(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId == nextView.MainViewport.Id,
                "Named viewport keeps labels when the active viewport changes");
            viewInput.PersistentData.Clear();
            viewInput.ExpireSolution(false);
            overlay.ExpireSolution(false);
            gh.NewSolution(false);
            doc.Views.ActiveView = nextView;
            pump();
            // The hidden host does not reliably raise RhinoApp.Idle after changing active views.
            typeof(OverlayComponent).GetMethod("FollowActiveView", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(overlay, null);

            Check(OverlayManager.Binding(overlay.InstanceGuid)?.ViewId == nextView.MainViewport.Id,
                "Follow Active moves labels without a Grasshopper solution");
        }
        finally
        {
            doc.Views.ActiveView = view;
            nextView.Close();
            pump();
        }
        overlay.ExpireSolution(false);
        gh.NewSolution(false);
        session = OverlayManager.Get(doc, view, overlay.InstanceGuid);
        scene = session.LabelScene ?? throw new InvalidOperationException("Label view did not rebind.");

        var saved = new GH_Archive();
        Check(saved.AppendObject(gh, "Definition"), "Save Labels definition");
        using var reopened = new GH_Document();
        Check(saved.ExtractObject(reopened, "Definition"), "Reopen Labels definition");
        var restored = reopened.Objects.OfType<OverlayComponent>().Single();
        Check(restored.ShowLinkKeys && restored.Params.Input[6].SourceCount == 2, "Restore toggle and Labels wires");

        overlay.Params.Input[6].RemoveAllSources();
        overlay.ToggleLinkKeys();
        overlay.ExpireSolution(false);
        gh.NewSolution(false);
        Check(session.InspectLabels().Length == 0 && session.LabelScene is null && scene.IsDisposed &&
            OverlayManager.Binding(overlay.InstanceGuid) is null,
            "Publication without labels releases the last owner and disposes the viewport scene");

        var pointLabels = new LabelsComponent();
        gh.AddObject(pointLabels, false);
        var explicitPoints = (Param_Point)pointLabels.Params.Input[1];
        explicitPoints.PersistentData.Append(new GH_Point(new Point3d(1, 2, 3)), new global::Grasshopper.Kernel.Data.GH_Path(3));
        explicitPoints.PersistentData.Append(new GH_Point(new Point3d(4, 5, 6)), new global::Grasshopper.Kernel.Data.GH_Path(3));
        ((Param_String)pointLabels.Params.Input[2]).PersistentData.Append(new GH_String("Point caption"));
        ((Param_Number)pointLabels.Params.Input[6]).PersistentData.Append(new GH_Number(4));
        gh.NewSolution(false);
        var explicitSpec = (LabelSpec)pointLabels.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Check(explicitSpec.Anchors.Length == 2 && explicitSpec.Anchors.All(a =>
            a.Style is { Key: null, Text: "Point caption", Priority: 4 }), "Explicit points broadcast text and priority");
        Check(explicitSpec.Anchors.Select(a => a.Identity(overlay.InstanceGuid)).Distinct().Count() == 2, "Explicit points retain distinct identities");

        var scope = new ScopeComponent();
        var scopedLabels = new LabelsComponent();
        var sourceOverlay = new OverlayComponent();
        var mirrorOverlay = new OverlayComponent();
        foreach (var obj in new IGH_DocumentObject[] { scope, scopedLabels, sourceOverlay, mirrorOverlay })
            gh.AddObject(obj, false);
        ((Param_Point)scopedLabels.Params.Input[1]).PersistentData.Append(new GH_Point(view.MainViewport.CameraTarget));
        ((Param_String)scopedLabels.Params.Input[2]).PersistentData.Append(new GH_String("Scoped point"));
        ((Param_String)scopedLabels.Params.Input[3]).PersistentData.Append(new GH_String("point-member"));
        foreach (var display in new[] { sourceOverlay, mirrorOverlay })
        {
            display.Params.Input[3].AddSource(scope.Params.Output[0]);
            display.Params.Input[6].AddSource(scopedLabels.Params.Output[0]);
        }
        gh.NewSolution(false);
        var scopedSession = OverlayManager.Get(doc, view, sourceOverlay.InstanceGuid);
        for (var i = 0; i < 100 && scopedSession.InspectLabelFrame().Length == 0; i++)
        {
            view.Redraw();
            pump();
            Thread.Sleep(10);
        }
        var scopedDot = scopedSession.InspectLabelFrame().Single().Dot;
        var scopedPointer = new System.Drawing.Point((int)Math.Round((scopedDot.X + 2.5) * dpiForInput),
            (int)Math.Round((scopedDot.Y + 2.5) * dpiForInput));
        var scopedHit = OverlaySession.PickLabel(view, scopedPointer) ?? throw new InvalidOperationException("Missing Scope label.");
        var viewportForPointer = view.MainViewport;
        var targetBefore = viewportForPointer.CameraTarget;
        var locationBefore = viewportForPointer.CameraLocation;
        var pan = viewportForPointer.CameraX * (viewportForPointer.CameraDirection.Length * .1 +
            locationBefore.DistanceTo(targetBefore) * .2);
        viewportForPointer.SetCameraLocations(targetBefore + pan, locationBefore + pan);
        view.Redraw();
        Check(OverlaySession.PickLabel(view, scopedPointer) is null, "Panned label leaves the pointer over empty space");
        OverlayManager.MovePointerForChecks(view, scopedPointer, retainPointer: true);
        Check(OverlayManager.Snapshot(sourceOverlay.InstanceGuid).HoverKey is null, "Empty pointer starts without a hover owner");
        viewportForPointer.SetCameraLocations(targetBefore, locationBefore);
        view.Redraw();
        OverlayManager.RecheckPointerForChecks();
        Check(OverlayManager.Snapshot(sourceOverlay.InstanceGuid).HoverKey == "point-member" &&
            OverlayManager.Snapshot(mirrorOverlay.InstanceGuid).HoverKey == "point-member", "Label hover shares Scope across displays");
        Check(OverlayManager.ClickForChecks(view, scopedPointer, PointerModifiers.Resolve(true, true, true, false, command: true)) == (true, true), "Scope label click routes to its owner");
        Check(OverlayManager.Snapshot(sourceOverlay.InstanceGuid).SelectedKeys.SequenceEqual(["point-member"]) &&
            OverlayManager.Snapshot(mirrorOverlay.InstanceGuid).SelectedKeys.SequenceEqual(["point-member"]) &&
            OverlayManager.DomainSnapshot(scopedHit.Owner)?.Events.Length == 1, "Scope shares one member selection and one event");
        using (var switched = RhinoDoc.CreateHeadless(null))
        {
            // The Windows in-process host has no modeless document switcher. Deliver its notification
            // through the production handler with another document in the host.
            OverlayManager.ActiveDocumentChanged(switched, EventArgs.Empty);
            Check(OverlayManager.Snapshot(scopedHit.Owner.Owner).HoverKey is null &&
                OverlayManager.Snapshot(scopedHit.Owner.Owner).SelectedKeys.SequenceEqual(["point-member"]),
                "Document-change notification clears pointer hover and retains the bound Scope selection");
        }
        OverlayManager.ClearSelection(scopedHit.Owner.Owner);
        Check(OverlayManager.Snapshot(mirrorOverlay.InstanceGuid).SelectedKeys.Length == 0, "Clear selection shares Scope");
        gh.RemoveObject(sourceOverlay, false);
        gh.RemoveObject(mirrorOverlay, false);
        gh.RemoveObject(scopedLabels, false);
        gh.RemoveObject(scope, false);

        var frozen = new GH_Archive();
        using var legacy = new GH_Document();
        Check(frozen.ReadFromFile(Path.Combine("examples", "hello.gh")) && frozen.ExtractObject(legacy, "Definition"), "Open pre-label fixture");
        var old = legacy.Objects.OfType<OverlayComponent>().Single();
        Check(!old.ShowLinkKeys && old.Params.Input.Count == 7 && old.Params.Input[6].Optional, "Append optional Labels when reopening");
        // Ordered Source entries from the frozen archive's param_input chunks.
        Guid[][] wires =
        [
            [new("aafb7038-ca42-4e52-99ec-37f2d0291665")],
            [new("aca4dea7-6a40-4468-b57f-775271abc82e")],
            [],
            [new("3ea64dc0-fc4f-47e7-9332-a64b77dbfa48")],
            [],
            []
        ];
        Check(MatchesFrozenWires(old), "First read preserves all six frozen input source lists");
        var roundTrip = new GH_Archive();
        using var legacyReopened = new GH_Document();
        Check(roundTrip.AppendObject(legacy, "Definition") && roundTrip.ExtractObject(legacyReopened, "Definition"), "Round-trip pre-label fixture");
        Check(MatchesFrozenWires(legacyReopened.Objects.OfType<OverlayComponent>().Single()),
            "Resave preserves all six frozen input source lists");

        bool MatchesFrozenWires(OverlayComponent component) => component.Params.Input.Take(6)
            .Select((p, i) => p.Sources.Select(s => s.InstanceGuid).SequenceEqual(wires[i])).All(same => same);
        CheckUnkeyedTooltip();
        CheckFallbackDotOrder();
        CheckCardHover();
        if (benchmark)
        {
            LabelPaintBench.RunViewport(doc, view, pump);
            LabelPaintBench.Run();
        }

        void CheckUnkeyedTooltip()
        {
            var viewport = view.MainViewport;
            var origin = viewport.WorldToClient(viewport.CameraTarget);
            if (!viewport.GetFrustumLine(origin.X + 35 * dpiForInput, origin.Y, out var ray))
                throw new InvalidOperationException("Missing frustum line for the neighboring label.");
            var owner = Guid.NewGuid();
            var caption = new LabelAnchor(new(owner, "{0}"), null, 0, viewport.CameraTarget,
                new("Full text of an unkeyed label", null, "Short", null, 1));
            var neighbor = new LabelAnchor(new(owner, "{0}"), null, 1, ray.ClosestPoint(viewport.CameraTarget, true),
                new("Neighbor", null, null, null, 0));
            OverlayManager.SetWithLabels(doc, view, owner, [], null, Theme.Paper, null, true, 1, "Unkeyed", true, [caption, neighbor]);
            try
            {
                var fixture = OverlayManager.Get(doc, view, owner);
                for (var i = 0; i < 100 && fixture.InspectLabelFrame().All(p => p.Form != LabelForm.Short); i++)
                {
                    pump();
                    view.Redraw();
                    Thread.Sleep(10);
                }
                var shortLabel = fixture.InspectLabelFrame().Single(p => p.Identity == caption.Identity(owner));
                Check(shortLabel.Form == LabelForm.Short, "Explicit annotation is painted with short text");
                var point = new System.Drawing.Point((int)Math.Round((shortLabel.Pill.X + shortLabel.Pill.Width / 2) * dpiForInput),
                    (int)Math.Round((shortLabel.Pill.Y + shortLabel.Pill.Height / 2) * dpiForInput));
                OverlayManager.MovePointerForChecks(view, point);
                Check(fixture.InspectTooltipLines()?.SequenceEqual([caption.Style.Text]) == true &&
                    OverlayManager.Snapshot(owner).HoverKey is null, "Short unkeyed annotation has a local full-text tooltip");
            }
            finally { OverlayManager.Remove(owner); }
        }

        static void CheckFallbackDotOrder()
        {
            var faded = new LabelPlacement(new(Guid.NewGuid(), default, null, 0), new(7.5, 17.5, 5, 5),
                default, LabelForm.Dot, false, .45);
            var opaque = faded with { Identity = new(Guid.NewGuid(), default, null, 0), Dot = new(13.5, 17.5, 5, 5), Opacity = 1 };
            Check(LabelPicking.Hit([faded, opaque], new(13, 20), true, false,
                id => OverlaySession.FallbackLabelDotPaintOrder(id == faded.Identity)) == faded,
                "Equidistant fallback dots pick the last paint pass despite reversed frame order");
        }

        void CheckCardHover()
        {
            var viewport = view.MainViewport;
            var previousMode = viewport.DisplayMode;
            var target = viewport.CameraTarget;
            var offset = viewport.CameraLocation - target;
            var center = target + offset * .2;
            var radius = offset.Length * .025;
            var extent = new Vector3d(radius, radius, radius);
            using var blocker = new BoundingBox(center - extent, center + extent).ToBrep();
            var blockerId = doc.Objects.AddBrep(blocker);
            var clipId = doc.Objects.AddPoint(target - offset * .1);
            var owner = Guid.NewGuid();
            try
            {
                viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
                const string key = "occluded-member";
                var card = new CardSpec { Id = "metric", Title = "Occluded member", Kind = CardKind.Metric, Keys = [key] };
                var anchor = new LabelAnchor(new(owner, "{0}"), null, 0, target, new("Occluded member", key, null, null, 0));
                OverlayManager.SetWithLabels(doc, view, owner, [card], null, Theme.Paper, null, true, 1, "Occluded", true, [anchor]);
                var fixture = OverlayManager.Get(doc, view, owner);
                for (var i = 0; i < 100 && fixture.InspectLabels().Single().Answer is not { Current: true, Occluded: true }; i++)
                {
                    pump();
                    view.Redraw();
                    Thread.Sleep(10);
                }
                Check(fixture.InspectLabels().Single().Answer is { Current: true, Occluded: true } &&
                    fixture.InspectLabelFrame().Length == 0, "Occluded inactive label has no painted hit");
                var placed = OverlayManager.Placements(fixture).Single();
                var region = CardMeasure.Hits(fixture.Cards.Single(), fixture.Theme).Single(h => h.Key == key);
                var cardPointer = new System.Drawing.Point((int)Math.Round(placed.X + (region.X + region.W / 2) * fixture.Unit),
                    (int)Math.Round(placed.Y + (region.Y + region.H / 2) * fixture.Unit));
                OverlayManager.MovePointerForChecks(view, cardPointer);
                for (var i = 0; i < 20 && fixture.InspectLabelFrame().Length == 0; i++)
                {
                    pump();
                    view.Redraw();
                }
                Check(fixture.InspectLabelFrame() is [{ Opacity: .45, Form: LabelForm.Full }], "Card hover paints occluded member pill at 45 percent");
                if (!viewport.GetFrustumLine(cardPointer.X, cardPointer.Y, out var underCardRay))
                    throw new InvalidOperationException("Missing frustum line under the card.");
                var underCard = anchor with { Point = underCardRay.ClosestPoint(target, true),
                    Style = anchor.Style with { Key = "under-card" } };
                OverlayManager.SetWithLabels(doc, view, owner, [card], null, Theme.Paper, null, true, 1, "Occluded", true, [underCard]);
                view.Redraw();
                var projected = viewport.WorldToClient(underCard.Point);
                Check(Math.Abs(projected.X - cardPointer.X) < 1 && Math.Abs(projected.Y - cardPointer.Y) < 1 &&
                    OverlaySession.PickLabel(view, cardPointer) is null, "Card reserves the pointer over the label anchor");
                Check(OverlayManager.ClickForChecks(view, cardPointer, primary) == (true, true) &&
                    OverlayManager.DomainSnapshot(fixture)?.Events is [{ CardId: "metric" }] &&
                    OverlayManager.Snapshot(owner).SelectedKeys.SequenceEqual([key]), "Card click consumes both messages and publishes one event");
                OverlayManager.ClearSelection(owner);
                OverlayManager.SetPresentationVisible(owner, false);
                Check(fixture.InspectLabelFrame().Length == 0 && OverlayManager.Snapshot(owner).HoverKey is null,
                    "Hiding overlay retires the label frame and hover");
            }
            finally
            {
                OverlayManager.Remove(owner);
                doc.Objects.Delete(blockerId, true);
                doc.Objects.Delete(clipId, true);
                viewport.DisplayMode = previousMode;
            }
        }
    }

    private static void CheckAnchors()
    {
        using var outer = new Circle(10).ToNurbsCurve();
        using var inner = new Circle(3).ToNurbsCurve();
        var planar = Brep.CreatePlanarBreps(new[] { outer, inner }, 0.001);
        using var holed = planar.Single();
        var holeAnchor = Link.FindAnchor(holed, "holed face").Point;
        Check(holeAnchor.IsValid && Math.Abs(holeAnchor.DistanceTo(Point3d.Origin) - 3) < 1e-5, "Holed trimmed face projects to inner rim");
        using var box = new BoundingBox(new Point3d(-2, -3, -4), new Point3d(2, 3, 4)).ToBrep();
        var solid = Link.FindAnchor(box, "closed box").Point;
        Check(solid.DistanceTo(new Point3d(2, 0, 0)) < 1e-5, "Closed solid projects to the first nearest face (+X)");
        using var mesh = new Mesh();
        // Equal parallel triangles put the centroid midway between the two faces.
        foreach (var z in new[] { 2, -2 })
        {
            var first = mesh.Vertices.Count;
            mesh.Vertices.Add(0, 0, z);
            mesh.Vertices.Add(6, 0, z);
            mesh.Vertices.Add(0, 3, z);
            mesh.Faces.AddFace(first, first + 1, first + 2);
        }
        Check(Link.FindAnchor(mesh, "parallel triangles").Point.DistanceTo(new Point3d(2, 1, 2)) < 1e-5,
            "Off-surface mesh centroid projects to the first nearest triangle");
        var link = new Link(["empty", "multi"], [("multi", (object)new Point3d(4, 3, 2)), ("multi", new Point3d(9, 9, 9))]);
        var spec = LabelSpec.FromLink(link, default);
        Check(spec.Anchors.Single().Point == new Point3d(4, 3, 2) && spec.Remarks.Single().Contains("1 Link"), "Multipart first anchor and empty-key diagnostic");
        using var degenerate = new LineCurve(Point3d.Origin, Point3d.Origin);
        var fallback = Link.FindAnchor(degenerate, "collapsed curve");
        Check(fallback.Point == Point3d.Origin && fallback.Diagnostic is not null, "Degenerate curve falls back to endpoint");
        using var bent = new PolylineCurve(new[] { Point3d.Origin, new Point3d(2, 0, 0), new Point3d(2, 8, 0) });
        Check(Link.FindAnchor(bent, "bent curve").Point.DistanceTo(new Point3d(2, 3, 0)) < 1e-6, "Curve uses half arc length");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
