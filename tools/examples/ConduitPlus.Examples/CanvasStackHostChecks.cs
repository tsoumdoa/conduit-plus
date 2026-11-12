using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rendering;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class CanvasStackHostChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        var completed = new List<string>();
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        var source = Add(new Param_GenericObject());
        var canvas = Add(new CanvasCardComponent());
        canvas.Attributes.Pivot = new(100, 100);
        canvas.Params.Input[0].AddSource(source);
        // Keep one real, handled canvas attached throughout the checks. Exporting must not
        // simulate a tab unload (which deliberately disposes document display resources).
        using var control = new GH_Canvas { Size = new(1000, 800) };
        _ = control.Handle;
        control.Document = gh;
        // Make captures deterministic regardless of the host's UI palette.
        Call(canvas, "ApplyTheme", "Paper");
        var state = (CanvasCardState)Get(canvas, "State");
        void Set(params IGH_Goo[] items)
        {
            source.PersistentData.Clear();
            for (var i = 0; i < items.Length; i++) source.PersistentData.Append(items[i], new GH_Path(i / 2, i % 2));
            source.ExpireSolution(false); canvas.ExpireSolution(false); gh.NewSolution(false);
        }
        string[] Messages(GH_RuntimeMessageLevel level) => canvas.RuntimeMessages(level).ToArray();
        var metric = new CardSpec { Id = "metric", Kind = CardKind.Metric, Title = "Floor area", Value = "320", Unit = "m²", Keys = ["area"], Width = 240 };
        var bar = new CardSpec { Id = "bar", Kind = CardKind.Bar, Title = "Utilisation", Values = [.4, .9, 1.2, .7], Labels = ["Beam A", "Beam B", "Beam C", "Beam D"], Keys = ["A", "B", "C", "D"], Width = 300, MaxRows = 3 };
        var table = new CardSpec { Id = "table", Kind = CardKind.Table, Title = "Room schedule", Width = 420, MaxRows = 3, Headers = ["Room", "Area"], Rows = Enumerable.Range(1, 8).Select(i => new TableRow("room" + i, ["Room " + i, (20 + i).ToString()])).ToArray() };
        Set(new GH_ObjectWrapper(metric));
        Require(canvas.Params.Input[0].Name == "Cards" && canvas.Params.Input[0].Access == GH_ParamAccess.tree && canvas.ComponentGuid == new Guid("b28d203e-2254-4b6f-94c0-d4db7e251c5a"), "Stable Canvas input/component contract.");
        Require((Size2)Get(canvas, "BodySize") == CardMeasure.Measure(metric, Theme.Paper), "Single card body size changed.");
        Call(canvas.Attributes, "Regenerate");
        var frame = Get(canvas.Attributes, "Frame");
        using (var expected = RasterDrawing.Render(state.Spec!, Theme.Paper, 1))
            PixelsEqual(expected, ((Bitmap?[])Get(frame, "Bitmaps"))[0]!);
        state.Activate("area"); gh.NewSolution(false);
        Require(Output(canvas).SequenceEqual(["area"]), "Single card selection changed.");
        Export("canvas-single-1x.png");
        completed.Add("Single card: original pixels, measured size, selection and stable input GUID/index/tree access");

        // Simulate an archive predating the rename. Its parameter ID and data wire must survive.
        using (var legacy = new GH_Document { Enabled = true })
        {
            var producer = new MetricComponent(); producer.CreateAttributes(); legacy.AddObject(producer, false);
            ((Param_String)producer.Params.Input[0]).PersistentData.Append(new GH_String("42"));
            var savedCanvas = new CanvasCardComponent(); savedCanvas.CreateAttributes(); legacy.AddObject(savedCanvas, false);
            savedCanvas.Params.Input[0].Name = "Card"; savedCanvas.Params.Input[0].AddSource(producer.Params.Output[0]);
            var inputId = savedCanvas.Params.Input[0].InstanceGuid;
            var archive = new GH_Archive(); Require(archive.AppendObject(legacy, "Definition"), "Cannot archive Canvas definition.");
            using var reopened = new GH_Document();
            Require(archive.ExtractObject(reopened, "Definition"), "Cannot restore Canvas definition.");
            reopened.Enabled = true; reopened.NewSolution(false);
            var restored = reopened.Objects.OfType<CanvasCardComponent>().Single();
            var restoredState = (CanvasCardState)Get(restored, "State");
            Require(restored.Params.Input[0].Description == canvas.Params.Input[0].Description && restored.Params.Input[0].Name == "Cards" && restored.Params.Input[0].InstanceGuid == inputId &&
                restored.Params.Input[0].Sources.Single().InstanceGuid == producer.Params.Output[0].InstanceGuid && restoredState.Spec?.Value == "42",
                "Old Canvas archive lost input identity, wire or card.");
        }
        completed.Add("Saved Card input roundtrip: renamed in place, original parameter ID/wire preserved");

        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(table));
        Require(state.Specs.Select(c => c.Id).SequenceEqual(["metric", "bar", "table"]) && Messages(GH_RuntimeMessageLevel.Error).Length == 0, "Three-card tree order.");
        state.Activate("metric", "area"); state.Activate("bar", "C", BrushSelectMode.Add); state.Activate("table", "room5", BrushSelectMode.Add);
        gh.NewSolution(false);
        Require(Output(canvas).SequenceEqual(new[] { "C", "area", "room5" }.Order(StringComparer.Ordinal)), "Stack Selection output did not union all three cards.");
        state.Activate("table", TableSort.Key(1)); state.Scroll("table", 3); state.Scroll("bar", 1);
        Set(new GH_ObjectWrapper(table), new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar));
        Require(state.Specs[0].SortColumn == 1 && state.Specs[0].ScrollOffset == 3 && state.Specs[2].ScrollOffset == 1, "Sort/scroll did not follow card ID across reorder.");
        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(table));
        Call(canvas.Attributes, "Regenerate");
        frame = Get(canvas.Attributes, "Frame");
        var layout = (CanvasStackLayout)Get(frame, "Layout");
        var lower = layout.Cards[2]; var region = lower.HitRegions.First(h => !CardMeasure.IsReserved(h.Key));
        canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
        var body = (RectangleF)canvas.Attributes.GetType().GetField("_body", Internal)!.GetValue(canvas.Attributes)!;
        var point = new PointF(body.X + (float)region.X + 1, body.Y + (float)(lower.Y + region.Y) + 1);
        var routed = (CanvasStackHit)Call(canvas.Attributes, "Hit", point)!;
        Require(routed.CardId == table.Id && routed.Key == region.Key, "Host hit map routed a lower-card row incorrectly.");
        // A cancelled lower-card scrollbar drag restores only its own offset.
        state.Scroll(table.Id, 5);
        canvas.Attributes.GetType().GetField("_pressedCard", Internal)!.SetValue(canvas.Attributes, table.Id);
        canvas.Attributes.GetType().GetField("_originalOffset", Internal)!.SetValue(canvas.Attributes, 3);
        canvas.Attributes.GetType().GetField("_dragScroll", Internal)!.SetValue(canvas.Attributes, true);
        Call(canvas.Attributes, "CancelGesture");
        Require(state.Specs.Single(c => c.Id == table.Id).ScrollOffset == 3 && state.Specs.Single(c => c.Id == bar.Id).ScrollOffset == 1,
            "Cancelled stack drag restored the wrong card's scroll offset.");
        Call(canvas.Attributes, "Attach", control);
        control.Viewport.Width = 1000; control.Viewport.Height = 200;
        control.Viewport.Zoom = 1; control.Viewport.Tx = -(int)body.X; control.Viewport.Ty = -(int)body.Y;
        control.Viewport.ComputeProjection();
        Call(canvas.Attributes, "Regenerate"); // Publish the cancelled drag's restored state before isolating pan.
        var beforePan = (CanvasStackFrameKey)Call(canvas.Attributes, "DesiredKey")!;
        var beforePanFrame = Get(canvas.Attributes, "Frame");
        control.Viewport.Ty -= (int)lower.Y;
        control.Viewport.ComputeProjection();
        Require(beforePan == (CanvasStackFrameKey)Call(canvas.Attributes, "DesiredKey")!, "Visibility changed a fully retained stack key.");
        Call(canvas.Attributes, "Regenerate");
        Require(ReferenceEquals(beforePanFrame, Get(canvas.Attributes, "Frame")), "Panning repainted a fully retained stack.");
        var unchanged = ((Bitmap?[])Get(beforePanFrame, "Bitmaps")).ToArray();
        Set(new GH_ObjectWrapper(metric with { Title = "Updated floor area" }), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(table));
        Call(canvas.Attributes, "Regenerate");
        var updated = (Bitmap?[])Get(Get(canvas.Attributes, "Frame"), "Bitmaps");
        Require(!ReferenceEquals(unchanged[0], updated[0]) && ReferenceEquals(unchanged[1], updated[1]) && ReferenceEquals(unchanged[2], updated[2]),
            "Changing one card repainted unchanged sibling bitmaps.");
        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(table));
        Call(canvas.Attributes, "Regenerate");
        completed.Add("Viewport mask changes preserve fully retained frames; one-card content changes transfer unchanged sibling bitmaps");
        state.Hover(table.Id, region.Key);
        Export("canvas-stack-3cards-1x.png");
        Export("canvas-stack-3cards-2x.png", 2);
        canvas.Locked = true;
        Export("canvas-locked-stack-1x.png");
        Export("canvas-locked-stack-2x.png", 2);
        canvas.Locked = false;
        completed.Add("Three-card stack: tree order, own widths, selection union, per-ID sort/scroll, lower-card hit routing/drag cancellation and real GH 1x/2x exports");

        state.Activate(metric.Id, CardMeasure.ClearKey);
        Set(new GH_ObjectWrapper(metric));
        Export("canvas-metric-alone-2x.png", 2, true);
        var tall = table with { MaxRows = 200, Rows = Enumerable.Range(0, 200).Select(i => new TableRow("tall" + i, ["Room " + i, "42"])).ToArray() };
        Set([new GH_ObjectWrapper(metric), .. Enumerable.Range(0, 11).Select(i => (IGH_Goo)new GH_ObjectWrapper(tall with { Id = "tall" + i }))]);
        canvas.Attributes.GetType().GetField("_scale", Internal)!.SetValue(canvas.Attributes, 2d);
        Call(canvas.Attributes, "Regenerate");
        frame = Get(canvas.Attributes, "Frame");
        var bitmaps = (Bitmap?[])Get(frame, "Bitmaps");
        var rasters = (CanvasRasterPlan[])Get(frame, "Rasters");
        Require(bitmaps.Length == 12 && rasters[0].Scale == 2, "Tall siblings reduced small card density at zoom 2.");
        using (var expected = RasterDrawing.Render(state.Specs[0], Theme.Paper, 2)) PixelsEqual(expected, bitmaps[0]!);
        Require(bitmaps.OfType<Bitmap>().All(b => b.Width <= 4096 && b.Height <= 4096 && (long)b.Width * b.Height * 4 <= CanvasRasterPlan.BitmapBudget) &&
            (long)Get(frame, "Bytes") == bitmaps.OfType<Bitmap>().Sum(b => (long)b.Width * b.Height * 4) && (long)Get(frame, "Bytes") <= CanvasRasterPlan.DocumentBudget / 2,
            "Per-card surfaces or document accounting exceed bounds.");
        Export("canvas-tall-stack-metric-1x.png", 1, true);
        Export("canvas-tall-stack-metric-2x.png", 2, true);
        Export("canvas-tall-stack-1x.png", maxHeight: 800);
        Export("canvas-tall-stack-2x.png", 2, maxHeight: 800);
        // Rounded transparent corners reveal different component capsule sizes; compare the painted content.
        using (var alone = new Bitmap(Path.Combine(output, "canvas-metric-alone-2x.png")))
        using (var stacked = new Bitmap(Path.Combine(output, "canvas-tall-stack-metric-2x.png")))
            PixelsEqual(alone, stacked, 8);
        completed.Add("12-card tall stack at zoom 2: independent bounded bitmaps, actual byte accounting, full-density Metric pixels; real GH 2x Metric export content equals standalone export");

        // Three near-32 MiB owners reproduce the review's real document cache pressure.
        var heavy = Enumerable.Range(0, 3).Select(i => tall with { Id = "pressure" + i, Title = "Frozen schedule " + i, Width = 1200 }).ToArray();
        Set(heavy.Select(c => (IGH_Goo)new GH_ObjectWrapper(c)).ToArray());
        control.Viewport.Ty = -(int)body.Y;
        control.Viewport.ComputeProjection();
        Call(canvas.Attributes, "Regenerate");
        var previousHeavy = Get(canvas.Attributes, "Frame");
        var reusedHeavy = ((Bitmap?[])Get(previousHeavy, "Bitmaps"))[0];
        var heavyLayout = (CanvasStackLayout)Get(previousHeavy, "Layout");
        control.Viewport.Ty = -(int)(body.Y + heavyLayout.Cards[2].Y);
        control.Viewport.ComputeProjection();
        Call(canvas.Attributes, "Regenerate");
        frame = Get(canvas.Attributes, "Frame");
        Require(ReferenceEquals(reusedHeavy, ((Bitmap?[])Get(frame, "Bitmaps"))[0]) && ((Bitmap?[])Get(frame, "Bitmaps"))[2] is not null,
            "Changing retained set failed to carry over the unchanged raster.");
        Require((long)Get(frame, "Bytes") > CanvasRasterPlan.DocumentBudget / 2 - 1024 * 1024, "Pressure fixture did not retain nearly 32 MiB.");
        canvas.Locked = true;
        Screen("canvas-locked-pressure-before-screen-2x.png", 2);
        Export("canvas-locked-pressure-before-export-2x.png", 2, maxHeight: 300);
        // Deliberately make live state/theme differ from the frozen publication.
        state.ReplaceCards([metric with { Value = "999", Title = "Live state must not leak" }]);
        Call(canvas, "ApplyTheme", "Night");
        canvas.Attributes.GetType().GetProperty("LastVisible", Internal)!.SetValue(canvas.Attributes, 0L);
        var pressureOwners = new List<CanvasCardComponent> { canvas };
        for (var i = 0; i < 2; i++)
        {
            var owner = Add(new CanvasCardComponent()); pressureOwners.Add(owner);
            owner.Attributes.Pivot = new(3000 * (i + 1), 100); // Keep pressure owners outside the exported region.
            ((CanvasCardState)Get(owner, "State")).ReplaceCards(heavy);
            Call(owner, "ApplyTheme", "Paper");
            owner.Attributes.GetType().GetField("_scale", Internal)!.SetValue(owner.Attributes, 2d);
            owner.Attributes.GetType().GetProperty("LastVisible", Internal)!.SetValue(owner.Attributes, (long)i + 1);
            Call(owner.Attributes, "Regenerate");
            Require(pressureOwners.Sum(c => (long)Get(Get(c.Attributes, "Frame"), "Bytes")) <= CanvasRasterPlan.DocumentBudget,
                "Real document cache pressure exceeded 64 MiB.");
        }
        Require(ReferenceEquals(frame, Get(canvas.Attributes, "Frame")) && (long)Get(frame, "Bytes") == 0,
            "Cache eviction removed the frozen snapshot or did not evict its bitmap surfaces.");
        Call(canvas.Attributes, "Regenerate");
        Screen("canvas-locked-pressure-after-screen-2x.png", 2);
        Export("canvas-locked-pressure-after-export-2x.png", 2, maxHeight: 300);
        CompareFiles("canvas-locked-pressure-before-screen-2x.png", "canvas-locked-pressure-after-screen-2x.png");
        CompareFiles("canvas-locked-pressure-before-export-2x.png", "canvas-locked-pressure-after-export-2x.png");
        Require((long)Get(frame, "Bytes") == 0, "Frozen sequential painting retained unbudgeted bitmaps.");
        foreach (var owner in pressureOwners.Skip(1)) gh.RemoveObject(owner, false);
        canvas.Locked = false;
        Set(heavy.Select(c => (IGH_Goo)new GH_ObjectWrapper(c)).ToArray()); Call(canvas, "ApplyTheme", "Paper");
        Call(canvas.Attributes, "Regenerate");
        gh.Enabled = false;
        Screen("canvas-disabled-before-screen-2x.png", 2);
        Export("canvas-disabled-before-export-2x.png", 2, maxHeight: 300);
        Call(canvas.Attributes, "EvictRasters");
        state.ReplaceCards([metric with { Value = "999" }]); Call(canvas, "ApplyTheme", "Night");
        Call(canvas.Attributes, "Regenerate");
        Screen("canvas-disabled-after-screen-2x.png", 2);
        Export("canvas-disabled-after-export-2x.png", 2, maxHeight: 300);
        CompareFiles("canvas-disabled-before-screen-2x.png", "canvas-disabled-after-screen-2x.png");
        CompareFiles("canvas-disabled-before-export-2x.png", "canvas-disabled-after-export-2x.png");
        gh.Enabled = true; Call(canvas, "ApplyTheme", "Paper");
        completed.Add("Real three-owner cache pressure evicts locked rasters within 64 MiB while frozen screen/GH export pixels survive live data/theme changes; disabled-document eviction is also pixel-identical");

        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(bar));
        Require(state.Specs.Length == 2 && Messages(GH_RuntimeMessageLevel.Warning).Contains("Card 'Utilisation' is connected twice; showing it once."), "Duplicate must warn and show once.");
        foreach (var untitled in new[] { bar with { Title = " " }, new CardSpec { Id = "untitled-text", Kind = CardKind.Text, Text = "Note" }, new CardSpec { Id = "untitled-legend", Kind = CardKind.Legend, Colors = ["#123456"], Labels = ["Category"] } })
        {
            Set(new GH_ObjectWrapper(untitled), new GH_ObjectWrapper(untitled));
            Require(Messages(GH_RuntimeMessageLevel.Warning).Contains($"Card '{untitled.Kind}' is connected twice; showing it once."), "Blank-title duplicate did not fall back to kind.");
        }
        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(null), new GH_ObjectWrapper(table));
        Require(state.Specs.Select(c => c.Id).SequenceEqual(["metric", "table"]) && Messages(GH_RuntimeMessageLevel.Warning).Contains("Showing 2 of 3 cards. 1 is empty because its component has an error."), "Null must warn and keep valid siblings.");
        Set(new GH_ObjectWrapper(null)); Require(state.Specs.Length == 0 && Messages(GH_RuntimeMessageLevel.Warning).Length > 0, "All-null input retained stale content.");
        Set(new GH_String("foreign"));
        Require(Messages(GH_RuntimeMessageLevel.Error).Contains("Cards item 0 is text, not a Conduit+ card.") &&
            (string)Get(canvas, "Placeholder") == "Cards item 0 is text, not a Conduit+ card.", "Wrong-type placeholder must match the error.");
        Export("canvas-wrong-type-placeholder-1x.png");
        Set(new GH_ObjectWrapper(metric), new GH_String("foreign"), new GH_ObjectWrapper(table));
        Require(state.Specs.Length == 2 && Messages(GH_RuntimeMessageLevel.Error).Length == 1, "Foreign item must error and keep valid siblings.");
        Set(Enumerable.Range(0, 15).Select(i => (IGH_Goo)new GH_ObjectWrapper(metric with { Id = "metric" + i, Title = "Metric " + i })).ToArray());
        Require(state.Specs.Length == 12 && state.Specs.Select(c => c.Id).SequenceEqual(Enumerable.Range(0, 12).Select(i => "metric" + i)) && Messages(GH_RuntimeMessageLevel.Warning).Contains("Showing 12 of 15 cards; use another Canvas Card for the rest."), "Cap contract.");
        completed.Add("Duplicate Warning/first occurrence and blank Bar/Text/Legend kind fallback; null Warning/valid siblings; foreign Error/valid siblings and wrapped placeholder capture; all-null clears; exact 12-of-15 cap Warning");

        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(table));
        var scope = Add(new ScopeComponent());
        canvas.Params.Input[1].AddSource(scope.Params.Output[0]);
        var peer = Add(new CanvasCardComponent());
        ((Param_GenericObject)peer.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(metric with { Id = "peer", Keys = ["peer-key"] }));
        peer.Params.Input[1].AddSource(scope.Params.Output[0]); canvas.ExpireSolution(false); gh.NewSolution(false);
        var peerState = (CanvasCardState)Get(peer, "State");
        state.Activate("bar", "B"); state.Activate("table", "room2", BrushSelectMode.Add); peerState.Activate("peer-key", BrushSelectMode.Add); gh.NewSolution(false);
        Require(Output(canvas).SequenceEqual(new[] { "B", "peer-key", "room2" }) && Output(peer).SequenceEqual(Output(canvas)), "Scoped stack must output entire domain union.");
        completed.Add("Wired Scope: stack and peer share selection and whole-domain output");
        File.WriteAllText(Path.Combine(output, "canvas-stack-checks.json"), CardJson.Serialize(completed));
        Console.WriteLine(string.Join(Environment.NewLine, completed));
        Console.WriteLine("PR F Canvas stack host checks passed.");

        void Export(string name, int scale = 1, bool metricOnly = false, int? maxHeight = null)
        {
            canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
            var bounds = Rectangle.Ceiling(canvas.Attributes.Bounds); bounds.Inflate(40, 40);
            if (maxHeight is { } height) bounds.Height = Math.Min(bounds.Height, height);
            if (metricOnly)
            {
                var body = (RectangleF)canvas.Attributes.GetType().GetField("_body", Internal)!.GetValue(canvas.Attributes)!;
                var size = CardMeasure.Measure(metric, Theme.Paper);
                bounds = Rectangle.Ceiling(new RectangleF(body.X, body.Y, (float)size.Width, (float)size.Height));
            }
            var viewport = new GH_Viewport(control.Viewport) { Width = bounds.Width * scale, Height = bounds.Height * scale, Zoom = scale, Tx = -bounds.X * scale, Ty = -bounds.Y * scale };
            using var image = control.GenerateHiResImageTile(viewport, Color.White);
            image.Save(Path.Combine(output, name), ImageFormat.Png);
        }
        void Screen(string name, int scale)
        {
            canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
            var bounds = Rectangle.Ceiling(canvas.Attributes.Bounds); bounds.Inflate(40, 40); bounds.Height = 300;
            control.Viewport.Width = bounds.Width * scale; control.Viewport.Height = bounds.Height * scale;
            control.Viewport.Zoom = scale; control.Viewport.Tx = -bounds.X * scale; control.Viewport.Ty = -bounds.Y * scale;
            control.Viewport.ComputeProjection();
            using var image = new Bitmap(bounds.Width * scale, bounds.Height * scale);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White); control.Viewport.ApplyProjection(graphics);
                Call(canvas.Attributes, "Render", control, graphics, GH_CanvasChannel.Objects);
            }
            image.Save(Path.Combine(output, name), ImageFormat.Png);
        }
        void CompareFiles(string before, string after)
        {
            using var a = new Bitmap(Path.Combine(output, before)); using var b = new Bitmap(Path.Combine(output, after));
            PixelsEqual(a, b);
        }
    }
    private static string[] Output(GH_Component c) => c.Params.Output[0].VolatileData.AllData(true).Select(g => g.ToString()!).ToArray();
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Internal)?.GetValue(obj) ?? throw new InvalidOperationException("Missing " + name);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Internal)!.Invoke(obj, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void PixelsEqual(Bitmap a, Bitmap b, int inset = 0)
    {
        Require(a.Size == b.Size, "Single-card bitmap size changed.");
        for (var y = inset; y < a.Height - inset; y++)
            for (var x = inset; x < a.Width - inset; x++) Require(a.GetPixel(x, y) == b.GetPixel(x, y), "Card pixels changed.");
    }
}
