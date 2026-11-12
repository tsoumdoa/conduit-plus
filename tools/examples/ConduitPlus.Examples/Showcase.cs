using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using ConduitPlus.Rhino;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

/// <summary>
/// Build/reopen the room showcase or native-data hello definition, assert clean solves,
/// and capture one viewport per RhinoCore process plus card and canvas review images.
/// </summary>
internal static class Showcase
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run(RhinoDoc doc, RhinoView view, string root, string[] args)
    {
        var hello = args.Contains("hello");
        var example = hello ? "hello" : "showcase";
        var path = Path.Combine(root, "examples", example + ".gh");
        if (!args.Contains("--verify-only")) ReviewDefinition.Build(path, hello);
        var output = Path.Combine(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS") ?? throw new InvalidOperationException("Set CONDUITPLUS_RESULTS."), example);
        Directory.CreateDirectory(output);
        using var gh = new GH_Document(); var archive = new GH_Archive();
        Require(archive.ReadFromFile(path) && archive.ExtractObject(gh, "Definition"), "Cannot reopen " + path);
        DisplayLifecycleChecks.ArchiveClean(archive, path);
        File.WriteAllText(Path.Combine(output, example + ".ghx"), archive.Serialize_Xml());
        Instances.DocumentServer.AddDocument(gh); gh.Enabled = true; GH_Document.EnableSolutions = true;
        try
        {
            if (!hello && args.Contains("--plan-scale"))
                gh.Objects.OfType<GH_NumberSlider>().Single(s => s.NickName == "Plan scale").SetSliderValue(decimal.Parse(args[Array.IndexOf(args, "--plan-scale") + 1], System.Globalization.CultureInfo.InvariantCulture));
            gh.NewSolution(false);
            DisplayLifecycleChecks.DefinitionSolved();
            if (hello) CheckHelloEdits(gh);
            else if (!args.Contains("--verify-only")) CheckScaleEndpoints(gh);
            if (!hello) CheckPlanSeparation(gh);
            var messages = gh.Objects.OfType<IGH_ActiveObject>().SelectMany(o => new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }
                .SelectMany(level => o.RuntimeMessages(level).Select(m => $"{o.NickName} ({o.Name}): {level}: {m}"))).ToArray();
            foreach (var m in messages) Console.WriteLine(m);
            Require(messages.Length == 0, example + " must solve with zero warnings/errors.");
            Require(!hello || !gh.Objects.OfType<GH_Component>().Any(c => c.Name.StartsWith("Demo ", StringComparison.Ordinal)), "Hello must use native data.");
            if (!args.Contains("--verify-only")) Relayout(gh, path);
            if (hello) CheckHelloWires(gh);
            var diagnostic = !hello && args.Contains("--plan-scale") && args[Array.IndexOf(args, "--plan-scale") + 1] != "1"
                ? "definition-scale-" + args[Array.IndexOf(args, "--plan-scale") + 1] + ".md" : "definition.md";
            File.WriteAllText(Path.Combine(output, diagnostic), Dump(gh, messages));

            var overlay = gh.Objects.OfType<ConduitPlus.Grasshopper.OverlayComponent>().Single();
            var session = Session(doc, view, overlay.InstanceGuid);
            // Headless GH does not draw previews. Mirror the actual Custom Preview inputs into
            // a temporary conduit rather than substituting demo colours or geometry.
            gh.PreviewMode = GH_PreviewMode.Disabled;
            using var previewConduit = new ReviewPreview();
            foreach (var preview in gh.Objects.OfType<GH_Component>().Where(c => c.Name == "Custom Preview"))
            {
                var shapes = preview.Params.Input[0].VolatileData.AllData(true).OfType<IGH_GeometricGoo>().ToArray();
                var colours = preview.Params.Input[1].Sources.Single().VolatileData.AllData(true).OfType<GH_Colour>().Select(c => c.Value).ToArray();
                Require(shapes.Length == (hello ? 6 : 17) && shapes.Length == colours.Length, "Custom Preview must have one colour per room face.");
                for (var i = 0; i < shapes.Length; i++)
                {
                    var brep = new GH_Brep(); Require(brep.CastFrom(shapes[i]), "Preview must contain room faces.");
                    previewConduit.Add(brep.Value, colours[i]);
                }
            }
            previewConduit.Enabled = true;
            view.ActiveViewport.SetProjection(DefinedViewportProjection.Top, "Top", false);
            var scene = gh.Objects.OfType<GH_Component>().Where(c => c.Name == "Custom Preview")
                .SelectMany(c => c.Params.Input[0].VolatileData.AllData(true)).OfType<IGH_GeometricGoo>()
                .Select(g => g.Boundingbox).Aggregate((a, b) => { a.Union(b); return a; });
            view.ActiveViewport.ZoomBoundingBox(hello ? new BoundingBox(-15, -8, -1, 40, 17, 1)
                : new BoundingBox(-scene.Max.X * .57, -scene.Max.Y * .8125, -1, scene.Max.X * 1.57, scene.Max.Y * 1.8125, 1));
            view.Redraw();
            var cards = (CardSpec[])Get(session, "Cards");
            Console.WriteLine($"{example} overlay: {cards.Length} cards: {string.Join(", ", cards.Select(c => c.Kind + " " + c.Title))}");
            if (hello)
            {
                var metrics = cards.Where(c => c.Kind == CardKind.Metric).ToArray();
                Require(metrics.Select(c => (c.Title, c.Value ?? "", c.Unit)).SequenceEqual(new[] { ("Total area", "90", "m²"), ("Rooms below 15 m²", "3", "") }),
                    "Hello must show the two summary KPIs in Metric list order.");
            }
            else
            {
                Require(cards.Length == 9 && cards.Count(c => c.Title == "01 Programme") == 1 && !cards.Any(c => c.Title.StartsWith("Programme palette", StringComparison.Ordinal)),
                    "Showcase viewport must have one programme legend and no canvas palette reference.");
                var palette = gh.Objects.OfType<ConduitPlus.Grasshopper.KeyLegendComponent>().Single(c => c.Params.Output[0].VolatileData.AllData(true)
                    .OfType<GH_ObjectWrapper>().Any(w => w.Value is CardSpec card && card.Title == "Programme palette · canvas reference"));
                Require(palette.Params.Output[0].Recipients.Count == 1 && palette.Params.Output[0].Recipients[0].Attributes.GetTopLevel.DocObject is GH_Component merge
                    && merge.Params.Output[0].Recipients.Single().Attributes.GetTopLevel.DocObject is ConduitPlus.Grasshopper.CanvasCardComponent,
                    "Programme palette reference must feed the group 4 Canvas Card only.");
            }

            // RhinoCore can fail natively on a second full-scene capture, so each process takes one shot.
            var shot = args.Contains("--shot") ? args[Array.IndexOf(args, "--shot") + 1] : "paper";
            var themeName = shot.Split('-')[0];
            OverlayManager.ReplaceTheme(overlay.InstanceGuid, Theme.Presets.Single(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase)));
            session = Session(doc, view, overlay.InstanceGuid);
            if (shot.EndsWith("-selected", StringComparison.Ordinal)) { Select(session, "model", "R05", BrushSelectMode.Replace); Select(session, "model", "R09", BrushSelectMode.Add); }
            if (shot.EndsWith("-group", StringComparison.Ordinal) || shot.EndsWith("-partial", StringComparison.Ordinal))
            {
                var legend = ((CardSpec[])Get(session, "Cards")).First(c => c.Kind == CardKind.Legend && c.Groups.Count > 0);
                Select(session, legend.Id, gh.Objects.OfType<ConduitPlus.Grasshopper.ColourCategoriesComponent>().First().Params.Output[4].VolatileData.AllData(true).First().ToString()!, BrushSelectMode.Replace);
                if (shot.EndsWith("-partial", StringComparison.Ordinal)) Select(session, "model", "R01", BrushSelectMode.Remove);
            }
            Capture(doc, view, output, "viewport-" + shot, previewConduit, args.Contains("--review-capture-checks"));
            if (args.Contains("--verify-only")) return;

            // Every generated card, including the canvas reference stacks, at 2x in every theme: the cards as a reviewer would zoom in on them.
            var cardsDir = Path.Combine(output, "cards"); Directory.CreateDirectory(cardsDir);
            foreach (var old in Directory.EnumerateFiles(cardsDir, "*.png")) File.Delete(old);
            var cardIndex = new StringBuilder("File\tDescription\n");
            cards = gh.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Output)
                .SelectMany(p => p.VolatileData.AllData(true)).OfType<GH_ObjectWrapper>()
                .Select(w => w.Value).OfType<CardSpec>().DistinctBy(c => c.Id).ToArray();
            foreach (var theme in Theme.Presets.Where(t => t.Name != "Auto"))
            for (var i = 0; i < cards.Length; i++)
            {
                using var bitmap = RasterDrawing.Render(cards[i], theme, 2);
                var file = $"{i:00}-{cards[i].Kind.ToString().ToLowerInvariant()}-{theme.Name.ToLowerInvariant()}.png";
                bitmap.Save(Path.Combine(cardsDir, file), ImageFormat.Png);
                cardIndex.AppendLine($"{file}\t{cards[i].Kind}: {cards[i].Title}, {theme.Name}, 2× raster.");
            }

            File.WriteAllText(Path.Combine(cardsDir, "index.tsv"), cardIndex.ToString());

            // The Grasshopper canvas itself: the whole definition, then each group up close.
            var canvasDir = Path.Combine(output, "canvas"); Directory.CreateDirectory(canvasDir);
            foreach (var old in Directory.EnumerateFiles(canvasDir, "*.png")) File.Delete(old);
            var canvasIndex = new StringBuilder("File\tDescription\n00-whole-definition.png\tWhole editable Grasshopper definition, with every group and card stack.\n");
            using (var control = new GH_Canvas())
            {
                _ = control.Handle;
                control.Document = gh;
                foreach (var obj in gh.Objects) { obj.Attributes.ExpireLayout(); obj.Attributes.PerformLayout(); }
                var all = Rectangle.Ceiling(gh.Objects.Select(o => o.Attributes.Bounds).Aggregate(RectangleF.Union)); all.Inflate(60, 60);
                CanvasImage(control, all, 1, Path.Combine(canvasDir, "00-whole-definition.png"));
                var n = 1;
                foreach (var group in gh.Objects.OfType<GH_Group>().OrderBy(g => g.NickName, StringComparer.Ordinal))
                {
                    var bounds = Rectangle.Ceiling(group.Attributes.Bounds); bounds.Inflate(30, 30);
                    var slug = new string(group.NickName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                    var file = $"{n++:00}-{(slug.Length > 40 ? slug[..40] : slug)}.png";
                    CanvasImage(control, bounds, 1.5f, Path.Combine(canvasDir, file));
                    canvasIndex.AppendLine($"{file}\tGrasshopper group {group.NickName}, 150% detail.");
                }
                control.Document = null;
            }
            File.WriteAllText(Path.Combine(canvasDir, "index.tsv"), canvasIndex.ToString());
            gh.Enabled = true; gh.Context = GH_DocumentContext.Loaded;
            Console.WriteLine($"{example} review outputs written to {output}");
        }
        finally { Instances.DocumentServer.RemoveDocument(gh); }
    }

    private static CardSpec[] ComponentCards(GH_Document gh) => gh.Objects.OfType<GH_Component>()
        .Where(c => c is ConduitPlus.Grasshopper.MetricComponent or ConduitPlus.Grasshopper.BarComponent or ConduitPlus.Grasshopper.TableComponent)
        .SelectMany(c => c.Params.Output[0].VolatileData.AllData(true)).OfType<GH_ObjectWrapper>().Select(w => (CardSpec)w.Value).ToArray();

    private static void RequireClean(GH_Document gh)
    {
        var messages = gh.Objects.OfType<IGH_ActiveObject>().SelectMany(o => new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }
            .SelectMany(level => o.RuntimeMessages(level).Select(m => $"{o.NickName} ({o.Name}): {level}: {m}"))).ToArray();
        Require(messages.Length == 0, "Edited definition must solve without warnings/errors: " + string.Join("; ", messages));
    }

    private static void CheckHelloEdits(GH_Document gh)
    {
        var metric = gh.Objects.OfType<ConduitPlus.Grasshopper.MetricComponent>().Single();
        Require(metric.Params.Input[0].SourceCount > 0, "Hello Metrics must be computed from wired values.");
        var panel = gh.Objects.OfType<GH_Panel>().Single(p => p.NickName == "Area (m²)");
        var original = panel.UserText;
        try
        {
            panel.UserText = original.Replace("9", "16", StringComparison.Ordinal); panel.ExpireSolution(false); gh.NewSolution(false);
            RequireClean(gh);
            var cards = ComponentCards(gh);
            Require(cards.Where(c => c.Kind == CardKind.Metric).Select(c => (c.Title, c.Value ?? "")).SequenceEqual(new[] { ("Total area", "97"), ("Rooms below 15 m²", "2") }),
                "Editing Store 01 from 9 to 16 must update Hello Metrics to 97 and 2.");
            Require(cards.Single(c => c.Kind == CardKind.Bar).Values.Contains(16), "Hello Bar must update with the edited area.");
            Require(cards.Single(c => c.Kind == CardKind.Table).Rows.Any(r => r.Cells.Contains("Store 01") && r.Cells.Contains("16")), "Hello Table must update with the edited area.");
        }
        finally { panel.UserText = original; panel.ExpireSolution(false); gh.NewSolution(false); }
        Console.WriteLine("Hello edit check passed: Store 01 9 → 16 updates Metric 97 m² / 2, Bar and Table; original data restored.");
    }

    private static void CheckHelloWires(GH_Document gh)
    {
        foreach (var target in gh.Objects.OfType<GH_Component>())
        foreach (var input in target.Params.Input)
        foreach (var source in input.Sources)
            Require(source.Attributes.OutputGrip.X < input.Attributes.InputGrip.X,
                $"Hello wires must flow left to right: {source.Attributes.GetTopLevel.DocObject.NickName} → {target.NickName}.{input.NickName}.");
        Console.WriteLine("Hello canvas check passed: every wire flows left to right.");
    }

    private static void CheckScaleEndpoints(GH_Document gh)
    {
        var slider = gh.Objects.OfType<GH_NumberSlider>().Single(s => s.NickName == "Plan scale");
        var original = slider.CurrentValue;
        try
        {
            foreach (var value in new[] { .5m, 2m })
            {
                slider.SetSliderValue(value); slider.ExpireSolution(false); gh.NewSolution(false);
                RequireClean(gh); CheckPlanSeparation(gh);
                var scale = gh.Objects.OfType<ConduitPlus.Grasshopper.ColourScaleComponent>().Single();
                Require(scale.Params.Output[0].VolatileData.AllData(true).OfType<GH_Colour>().Select(c => c.Value).Distinct().Count() > 1,
                    "Area colours must retain their range at both plan-scale endpoints.");
            }
        }
        finally { slider.SetSliderValue(original); slider.ExpireSolution(false); gh.NewSolution(false); }
        var bar = ComponentCards(gh).Single(c => c.Title == "Area − minimum (m²)");
        Require(bar.Values.Length == 16 && !bar.Keys.Contains("R17"), "Headline Bar must exclude the hall.");
        Require(BarPresentation.Order(bar).Take(bar.RowLimit).Any(i => bar.Values[i] > 0), "Headline Bar must include a visible surplus.");
        var within = ComponentCards(gh).Single(c => c.GoodWhen == BarGoodWhen.Within);
        Require(within.MaxRows == 0 && within.Values.Any(v => BarPresentation.IsGood(v!.Value, 2, BarGoodWhen.Within))
            && within.Values.Any(v => !BarPresentation.IsGood(v!.Value, 2, BarGoodWhen.Within)), "Within study must show pass and fail rows.");
        Console.WriteLine("Showcase scale checks passed at 0.5 and 2: 4 m plan gap, distinct area colours and zero warnings; default scale restored.");
    }

    private static void CheckPlanSeparation(GH_Document gh)
    {
        BoundingBox Bounds(string name) => gh.Objects.OfType<GH_Component>().Single(c => c.NickName == name)
            .Params.Input[0].VolatileData.AllData(true).OfType<IGH_GeometricGoo>().Select(g => g.Boundingbox)
            .Aggregate((a, b) => { a.Union(b); return a; });
        var first = Bounds("Programme preview"); var second = Bounds("Area preview");
        Require(Math.Abs(second.Min.X - first.Max.X - 4) < 1e-8, "Plans must retain a 4 m gap from the first plan's bounding box.");
    }

    private static void Relayout(GH_Document gh, string path)
    {
        void Layout()
        {
            foreach (var obj in gh.Objects.Where(o => o is not GH_Group)) { obj.Attributes.ExpireLayout(); obj.Attributes.PerformLayout(); }
            foreach (var group in gh.Objects.OfType<GH_Group>()) { group.Attributes.ExpireLayout(); group.Attributes.PerformLayout(); }
        }
        Layout();
        var groups = gh.Objects.OfType<GH_Group>().ToArray();
        var cursorX = 40f; var lowerY = 0f;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i]; var bounds = group.Attributes.Bounds;
            var target = i < 3 ? new PointF(cursorX, 80) : new PointF(900, lowerY);
            foreach (var id in group.ObjectIDs)
            {
                var obj = gh.FindObject(id, true)!; var pivot = obj.Attributes.Pivot;
                obj.Attributes.Pivot = new(pivot.X + target.X - bounds.X, pivot.Y + target.Y - bounds.Y);
            }
            Layout();
            if (i < 3)
            {
                cursorX = group.Attributes.Bounds.Right + 120;
                lowerY = Math.Max(lowerY, group.Attributes.Bounds.Bottom + 160);
            }
            else lowerY = group.Attributes.Bounds.Bottom + 160;
        }
        // Save the editable preview-enabled definition, before headless rendering disables it.
        var archive = new GH_Archive(); Require(archive.AppendObject(gh, "Definition") && archive.WriteToFile(path, true, false), "Cannot re-save " + path);
    }

    private sealed class ReviewPreview : DisplayConduit, IDisposable
    {
        private readonly List<(Mesh Mesh, Curve[] Edges, Color Colour)> faces = [];
        internal void Add(Brep brep, Color colour)
        {
            var mesh = new Mesh(); foreach (var piece in Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh)) { mesh.Append(piece); piece.Dispose(); }
            mesh.Normals.ComputeNormals(); mesh.VertexColors.CreateMonotoneMesh(colour); faces.Add((mesh, brep.DuplicateNakedEdgeCurves(true, false), colour));
        }
        internal void RequireCapturedFaces(Bitmap png, RhinoViewport viewport)
        {
            var matches = 0;
            foreach (var (mesh, _, colour) in faces)
            {
                var point = viewport.WorldToClient(mesh.GetBoundingBox(true).Center);
                var x = (int)Math.Round(point.X); var y = (int)Math.Round(point.Y);
                if (x < 0 || y < 0 || x >= png.Width || y >= png.Height) continue;
                var pixel = png.GetPixel(x, y);
                // Selection may tint a minority of rooms; the remaining faces must match their preview colours.
                if (pixel.A > 200 && Math.Abs(pixel.R - colour.R) + Math.Abs(pixel.G - colour.G) + Math.Abs(pixel.B - colour.B) < 40) matches++;
            }
            Require(matches >= (faces.Count + 1) / 2, $"Incomplete review capture: only {matches}/{faces.Count} room faces match the solved preview colours.");
            Console.WriteLine($"Review image check passed: {matches}/{faces.Count} room faces match solved colours.");
        }
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            foreach (var (mesh, _, _) in faces) e.IncludeBoundingBox(mesh.GetBoundingBox(true));
        }
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            foreach (var (mesh, edges, colour) in faces)
            { using var material = new DisplayMaterial(colour); e.Display.EnableLighting(false); e.Display.DrawMeshShaded(mesh, material); e.Display.EnableLighting(true); foreach (var edge in edges) e.Display.DrawCurve(edge, Color.FromArgb(102, 110, 120), 1); }
        }
        public void Dispose() { Enabled = false; foreach (var (mesh, edges, _) in faces) { mesh.Dispose(); foreach (var edge in edges) edge.Dispose(); } }
    }

    private static void Capture(RhinoDoc doc, RhinoView view, string output, string name, ReviewPreview preview, bool checkScene)
    {
        view.Redraw(); RhinoApp.Wait(); view.Redraw();
        typeof(ConduitPlusCapture).GetMethod("Capture", Static)!.Invoke(null, [doc, view, 1]);
        var source = Path.Combine(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!, "captures", "raster-1x.png");
        var target = Path.Combine(output, name + ".png");
        // The capture is transparent outside cards/geometry: flatten onto a neutral viewport grey for review.
        using (var png = new Bitmap(source))
        using (var flat = new Bitmap(png.Width, png.Height))
        {
            using (var g = Graphics.FromImage(flat)) { g.Clear(Color.FromArgb(214, 214, 214)); g.DrawImage(png, 0, 0, png.Width, png.Height); }
            if (checkScene) preview.RequireCapturedFaces(flat, view.ActiveViewport);
            flat.Save(target, ImageFormat.Png);
        }
        File.Move(Path.ChangeExtension(source, ".json"), Path.ChangeExtension(target, ".json"), true);
        File.Delete(source);
        Console.WriteLine("Captured " + target);
    }

    private static void CanvasImage(GH_Canvas control, Rectangle bounds, float zoom, string file)
    {
        var viewport = new GH_Viewport(control.Viewport) { Width = (int)(bounds.Width * zoom), Height = (int)(bounds.Height * zoom), Zoom = zoom, Tx = (int)(-bounds.X * zoom), Ty = (int)(-bounds.Y * zoom) };
        using var image = control.GenerateHiResImageTile(viewport, Color.FromArgb(212, 212, 212));
        image.Save(file, ImageFormat.Png);
        Console.WriteLine("Canvas image " + file);
    }

    private static string Dump(GH_Document gh, string[] messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Room review — definition dump").AppendLine();
        sb.AppendLine("Generated by tools/examples (Showcase.cs). Every object, its inputs (wired sources or persistent values) and outputs. Positions are GH canvas pivots.").AppendLine();
        var names = gh.Objects.ToDictionary(o => o.InstanceGuid, o => o);
        string Owner(IGH_Param p) => p.Attributes.GetTopLevel.DocObject is { } top && top != p ? $"{top.NickName}.{p.NickName}" : p.NickName;
        foreach (var group in gh.Objects.OfType<GH_Group>().OrderBy(g => g.Attributes.Bounds.Y))
        {
            sb.AppendLine($"## Group: {group.NickName}").AppendLine();
            foreach (var id in group.ObjectIDs)
            {
                if (!names.TryGetValue(id, out var obj)) continue;
                var p = obj.Attributes.Pivot;
                sb.AppendLine($"### {obj.NickName} — `{obj.Name}` at ({p.X:0}, {p.Y:0})");
                if (obj is IGH_Component c)
                {
                    foreach (var input in c.Params.Input)
                    {
                        var src = input.Sources.Count > 0 ? "← " + string.Join(", ", input.Sources.Select(Owner))
                            : input.GetType().GetProperty("PersistentData")?.GetValue(input) is IGH_Structure { DataCount: > 0 } pd ? "= " + string.Join(" | ", pd.AllData(true).Select(d => d.ToString()!.Replace("\n", "\\n"))) : "(unset)";
                        sb.AppendLine($"- in  **{input.Name}** ({input.NickName}{(input.Optional ? ", optional" : "")}, {input.Access}{(input.DataMapping != GH_DataMapping.None ? ", " + input.DataMapping : "")}) {src}");
                    }
                    foreach (var o in c.Params.Output)
                    {
                        var data = o.VolatileData.AllData(true).Take(6).Select(d => d?.ToString() ?? "null").ToArray();
                        sb.AppendLine($"- out **{o.Name}** ({o.NickName}) → {o.Recipients.Count} recipient(s); sample: {string.Join(" | ", data).Replace("\n", "\\n")}");
                    }
                }
                else if (obj is IGH_Param param)
                {
                    var data = param.VolatileData.AllData(true).Take(6).Select(d => d?.ToString() ?? "null");
                    sb.AppendLine($"- value: {string.Join(" | ", data)}{(param.Sources.Count > 0 ? " ← " + string.Join(", ", param.Sources.Select(Owner)) : "")}");
                }
                if (obj is IGH_ActiveObject active)
                    foreach (var level in new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error })
                    foreach (var m in active.RuntimeMessages(level)) sb.AppendLine($"- **{level}:** {m}");
                sb.AppendLine();
            }
        }
        sb.AppendLine("## Runtime messages").AppendLine();
        sb.AppendLine(messages.Length == 0 ? "None." : string.Join("\n", messages.Select(m => "- " + m)));
        return sb.ToString();
    }

    private static object Session(RhinoDoc doc, RhinoView view, Guid owner) =>
        ((Array)typeof(OverlayManager).GetMethod("InView", Static)!.Invoke(null, [doc, view])!).Cast<object>().Single(s => (Guid)Get(s, "Owner") == owner);
    private static void Select(object session, string card, string key, BrushSelectMode mode) => session.GetType().GetMethod("Select", Instance)!.Invoke(session, [card, key, mode]);
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
