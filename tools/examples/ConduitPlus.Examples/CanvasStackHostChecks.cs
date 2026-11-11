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
            PixelsEqual(expected, (Bitmap)Get(frame, "Bitmap"));
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
            Require(restored.Params.Input[0].Name == "Cards" && restored.Params.Input[0].InstanceGuid == inputId &&
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
        state.Hover(table.Id, region.Key);
        Export("canvas-stack-3cards-1x.png");
        Export("canvas-stack-3cards-2x.png", 2);
        completed.Add("Three-card stack: tree order, own widths, selection union, per-ID sort/scroll, lower-card hit routing/drag cancellation and real GH 1x/2x exports");

        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(bar), new GH_ObjectWrapper(bar));
        Require(state.Specs.Length == 2 && Messages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Duplicate card ID", StringComparison.Ordinal)), "Duplicate must warn and show once.");
        Set(new GH_ObjectWrapper(metric), new GH_ObjectWrapper(null), new GH_ObjectWrapper(table));
        Require(state.Specs.Select(c => c.Id).SequenceEqual(["metric", "table"]) && Messages(GH_RuntimeMessageLevel.Warning).Contains("Skipped 1 empty Card items; showing 2. Check upstream components."), "Null must warn and keep valid siblings.");
        Set(new GH_ObjectWrapper(null)); Require(state.Specs.Length == 0 && Messages(GH_RuntimeMessageLevel.Warning).Length > 0, "All-null input retained stale content.");
        Set(new GH_ObjectWrapper(metric), new GH_String("foreign"), new GH_ObjectWrapper(table));
        Require(state.Specs.Length == 2 && Messages(GH_RuntimeMessageLevel.Error).Length == 1, "Foreign item must error and keep valid siblings.");
        Set(Enumerable.Range(0, 15).Select(i => (IGH_Goo)new GH_ObjectWrapper(metric with { Id = "metric" + i, Title = "Metric " + i })).ToArray());
        Require(state.Specs.Length == 12 && state.Specs.Select(c => c.Id).SequenceEqual(Enumerable.Range(0, 12).Select(i => "metric" + i)) && Messages(GH_RuntimeMessageLevel.Warning).Contains("Showing 12 of 15 cards; use another Canvas Card for the rest."), "Cap contract.");
        completed.Add("Duplicate Warning/first occurrence; null Warning/valid siblings; foreign Error/valid siblings; all-null clears; exact 12-of-15 cap Warning");

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

        void Export(string name, int scale = 1)
        {
            using var control = new GH_Canvas(); control.Document = gh;
            try
            {
                canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
                var bounds = Rectangle.Ceiling(canvas.Attributes.Bounds); bounds.Inflate(40, 40);
                var viewport = new GH_Viewport(control.Viewport) { Width = bounds.Width * scale, Height = bounds.Height * scale, Zoom = scale, Tx = -bounds.X * scale, Ty = -bounds.Y * scale };
                using var image = control.GenerateHiResImageTile(viewport, Color.White);
                image.Save(Path.Combine(output, name), ImageFormat.Png);
            }
            finally { control.Document = null; gh.Enabled = true; gh.Context = GH_DocumentContext.Loaded; }
        }
    }
    private static string[] Output(GH_Component c) => c.Params.Output[0].VolatileData.AllData(true).Select(g => g.ToString()!).ToArray();
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Internal)?.GetValue(obj) ?? throw new InvalidOperationException("Missing " + name);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Internal)!.Invoke(obj, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void PixelsEqual(Bitmap a, Bitmap b)
    {
        Require(a.Size == b.Size, "Single-card bitmap size changed.");
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++) Require(a.GetPixel(x, y) == b.GetPixel(x, y), "Single-card pixels changed.");
    }
}
