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
/// One definition that uses every Conduit+ component: a viewport dashboard for the room
/// demo, Canvas Cards for every card kind from the truss demo, a massing strip and a Scope.
/// Writes examples/showcase.gh, a readable definition dump and review screenshots.
/// </summary>
internal static class Showcase
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run(RhinoDoc doc, RhinoView view, string root, string[] args)
    {
        var path = Path.Combine(root, "examples", "showcase.gh");
        if (!args.Contains("--verify-only")) Build(path);
        var output = Path.Combine(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS") ?? throw new InvalidOperationException("Set CONDUITPLUS_RESULTS."), "showcase");
        Directory.CreateDirectory(output);
        using var gh = new GH_Document(); var archive = new GH_Archive();
        Require(archive.ReadFromFile(path) && archive.ExtractObject(gh, "Definition"), "Cannot reopen " + path);
        DisplayLifecycleChecks.ArchiveClean(archive, path);
        File.WriteAllText(Path.Combine(output, "showcase.ghx"), archive.Serialize_Xml());
        Instances.DocumentServer.AddDocument(gh); gh.Enabled = true; GH_Document.EnableSolutions = true;
        try
        {
            gh.NewSolution(false);
            DisplayLifecycleChecks.DefinitionSolved();
            var messages = gh.Objects.OfType<IGH_ActiveObject>().SelectMany(o => new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }
                .SelectMany(level => o.RuntimeMessages(level).Select(m => $"{o.NickName} ({o.Name}): {level}: {m}"))).ToArray();
            foreach (var m in messages) Console.WriteLine(m);
            if (!args.Contains("--verify-only")) Relayout(gh, path);
            File.WriteAllText(Path.Combine(output, "definition.md"), Dump(gh, messages));

            var overlay = gh.Objects.OfType<ConduitPlus.Grasshopper.OverlayComponent>().Single();
            var session = Session(doc, view, overlay.InstanceGuid);
            // Put the rooms in the Rhino document, coloured by type, as a user's own preview would be.
            // GH preview is off headlessly, and conduit base wires would draw over the cards.
            gh.PreviewMode = GH_PreviewMode.Disabled;
            var roomData = DemoRooms.Create(); var typeNames = Volatile(gh, "Demo Rooms", 10); var typeColours = gh.Objects.OfType<GH_Component>().First(c => c.Name == "Demo Rooms").Params.Output[11].VolatileData.AllData(true).OfType<GH_Colour>().Select(c => c.Value).ToArray();
            foreach (var r in roomData.Rooms)
            {
                var face = Brep.CreatePlanarBreps(new Rectangle3d(Plane.WorldXY, new Interval(r.X, r.X + r.Width), new Interval(r.Y, r.Y + r.Depth)).ToNurbsCurve(), 1e-6).Single();
                var colour = typeColours[Math.Max(0, Array.IndexOf(typeNames, r.Type))];
                var attributes = new Rhino.DocObjects.ObjectAttributes { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = colour };
                doc.Objects.AddBrep(face, attributes);
                doc.Objects.AddCurve(new Rectangle3d(Plane.WorldXY, new Interval(r.X, r.X + r.Width), new Interval(r.Y, r.Y + r.Depth)).ToNurbsCurve(), new Rhino.DocObjects.ObjectAttributes { ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = Color.FromArgb(60, 60, 60) });
            }
            view.ActiveViewport.DisplayMode = DisplayModeDescription.FindByName("Shaded") ?? view.ActiveViewport.DisplayMode;
            view.ActiveViewport.SetProjection(DefinedViewportProjection.Top, "Top", false);
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(-9, -4, -1, 29, 20, 1));
            view.Redraw();
            var cards = (CardSpec[])Get(session, "Cards");
            Console.WriteLine($"Showcase overlay: {cards.Length} cards: {string.Join(", ", cards.Select(c => c.Kind + " " + c.Title))}");

            // RhinoCore can fail natively on a second full-scene capture, so each process takes one shot.
            var shot = args.Contains("--shot") ? args[Array.IndexOf(args, "--shot") + 1] : "paper";
            var themeName = shot.Split('-')[0];
            OverlayManager.ReplaceTheme(overlay.InstanceGuid, Theme.Presets.Single(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase)));
            session = Session(doc, view, overlay.InstanceGuid);
            if (shot.EndsWith("-selected", StringComparison.Ordinal)) { Select(session, "model", "R05", BrushSelectMode.Replace); Select(session, "model", "R09", BrushSelectMode.Add); }
            if (shot.EndsWith("-group", StringComparison.Ordinal) || shot.EndsWith("-partial", StringComparison.Ordinal))
            {
                var legend = ((CardSpec[])Get(session, "Cards")).First(c => c.Kind == CardKind.Legend && c.Groups.Count > 0);
                Select(session, legend.Id, Volatile(gh, "Demo Rooms", 15).First(), BrushSelectMode.Replace);
                if (shot.EndsWith("-partial", StringComparison.Ordinal)) Select(session, "model", "R01", BrushSelectMode.Remove);
            }
            Capture(doc, view, output, "viewport-" + shot);
            if (args.Contains("--verify-only")) return;

            // Each overlay card alone at 2x in every theme: the cards as a reviewer would zoom in on them.
            var cardsDir = Path.Combine(output, "cards"); Directory.CreateDirectory(cardsDir);
            Select(session, "model", "R05", BrushSelectMode.Replace);
            cards = (CardSpec[])Get(session, "Cards");
            foreach (var theme in Theme.Presets.Where(t => t.Name != "Auto"))
            for (var i = 0; i < cards.Length; i++)
            {
                using var bitmap = RasterDrawing.Render(cards[i], theme, 2);
                bitmap.Save(Path.Combine(cardsDir, $"{i:00}-{cards[i].Kind.ToString().ToLowerInvariant()}-{theme.Name.ToLowerInvariant()}.png"), ImageFormat.Png);
            }

            // The Grasshopper canvas itself: the whole definition, then each group up close.
            var canvasDir = Path.Combine(output, "canvas"); Directory.CreateDirectory(canvasDir);
            using (var control = new GH_Canvas())
            {
                control.Document = gh;
                foreach (var obj in gh.Objects) { obj.Attributes.ExpireLayout(); obj.Attributes.PerformLayout(); }
                var all = Rectangle.Ceiling(gh.Objects.Select(o => o.Attributes.Bounds).Aggregate(RectangleF.Union)); all.Inflate(60, 60);
                CanvasImage(control, all, 1, Path.Combine(canvasDir, "00-whole-definition.png"));
                var n = 1;
                foreach (var group in gh.Objects.OfType<GH_Group>().OrderBy(g => g.Attributes.Bounds.Y).ThenBy(g => g.Attributes.Bounds.X))
                {
                    var bounds = Rectangle.Ceiling(group.Attributes.Bounds); bounds.Inflate(30, 30);
                    var slug = new string(group.NickName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                    CanvasImage(control, bounds, 1.5f, Path.Combine(canvasDir, $"{n++:00}-{(slug.Length > 40 ? slug[..40] : slug)}.png"));
                }
                control.Document = null;
            }
            gh.Enabled = true; gh.Context = GH_DocumentContext.Loaded;
            Console.WriteLine($"Showcase review outputs written to {output}");
        }
        finally { Instances.DocumentServer.RemoveDocument(gh); }
    }

    private static void Build(string path)
    {
        using var gh = new GH_Document();
        var groups = new List<(string Title, Color Colour, IGH_DocumentObject[] Objects)>();

        // ── 1. Room data → viewport dashboard ──────────────────────────────────────────────
        var scale = Slider("Plan scale", 1, .5m, 2, 2, 40, 120);
        var rooms = Component(210, 260, 240); Wire(rooms, 0, scale);
        groups.Add(("1  Room data (Demo Rooms) · metres, fictitious minima", Blue, [scale, rooms]));

        var cards = new List<GH_Component>(); var extra = new List<IGH_DocumentObject>();
        var title = Add(new ConduitPlus.Grasshopper.TitleComponent(), 700, 40);
        Text(title, 0, "Conduit+ showcase"); Text(title, 1, "Room area check · 17 rooms"); Text(title, 2, "Every card kind on one viewport.\nClick a room, row, bar or swatch."); Text(title, 3, "tc");
        cards.Add(title);
        var gia = Metric(700, 200, rooms.Params.Output[5], "Schematic GIA", "m²", 0); cards.Add(gia);
        var ng = Metric(700, 300, rooms.Params.Output[6], "Net / gross", "%", 1); cards.Add(ng);
        var failing = Metric(700, 400, rooms.Params.Output[8], "Rooms below minimum", "", 0);
        Text(failing, 5, "below-minimum"); Wire(failing, 6, rooms.Params.Output[17]); cards.Add(failing);
        var bar = Component(202, 700, 520); Wire(bar, 0, rooms.Params.Output[3]); Wire(bar, 1, rooms.Params.Output[2]); Wire(bar, 2, rooms.Params.Output[1]);
        Text(bar, 3, "Area − minimum (m²)"); Number(bar, 4, 0); Wire(bar, 6, rooms.Params.Output[9]); cards.Add(bar);
        var table = Component(203, 700, 720); Text(table, 0, "Room", "Type", "m²", "Min m²", "Δ m²", "Status");
        Wire(table, 1, rooms.Params.Output[4]); Wire(table, 2, rooms.Params.Output[1]); Text(table, 3, "Room schedule"); Integer(table, 5, 6); Number(table, 6, 520); cards.Add(table);
        var types = Component(208, 700, 940); Wire(types, 0, rooms.Params.Output[11]); Wire(types, 1, rooms.Params.Output[10]);
        Wire(types, 5, rooms.Params.Output[15]); Text(types, 6, "Room types"); Wire(types, 8, rooms.Params.Output[16]); cards.Add(types);
        var gradient = Component(208, 700, 1160);
        Colours(gradient, 0, "#2C7BB6", "#FFFFBF", "#D7191C");
        // Legend only enters gradient mode when Min and Max are wired; internalised values are ignored (review finding).
        var gMin = Slider("Area min", 0, 0, 0, 0, 470, 1160); var gMax = Slider("Area max", 40, 40, 40, 0, 470, 1220); Wire(gradient, 2, gMin); Wire(gradient, 3, gMax); extra.Add(gMin); extra.Add(gMax); Text(gradient, 4, "m²"); Text(gradient, 6, "Room area"); cards.Add(gradient);
        var line = Component(206, 700, 1360); Wire(line, 0, rooms.Params.Output[12]); Wire(line, 1, rooms.Params.Output[2]); Wire(line, 2, rooms.Params.Output[1]);
        Text(line, 3, "Area per room (m²)"); Number(line, 4, 12); cards.Add(line);
        var status = Component(int.MinValue, 700, 1560, new Guid("f1deb262-9f13-4293-bc5f-cb98b926dc3c"));
        Text(status, 0, "Meets minimum", "Below minimum"); Colours(status, 1, "#1F8A5B", "#C2334D"); Text(status, 2, "9 rooms", "8 rooms"); Text(status, 3, "Status"); cards.Add(status);
        var note = Component(207, 700, 1740); Text(note, 0, "How to read this");
        Text(note, 1, "Bars show area minus the type minimum; red is a deficit.\nShift adds to the selection, Ctrl removes; Clear or an empty click resets.\nDemo data: not a regulatory check."); cards.Add(note);
        Slot(gia, "tl"); Slot(ng, "tl"); Slot(failing, "tl"); Slot(bar, "tl"); Slot(table, "tr"); Slot(types, "tr"); Slot(gradient, "br"); Slot(line, "bl"); Slot(status, "br"); Slot(note, "bl");
        groups.Add(("2  Viewport cards · Title, Metric ×3, Bar, Table, Legend ×2, Line, Key Legend, Text", Green, [.. cards, .. extra]));

        var link = Component(205, 1120, 260); Wire(link, 0, rooms.Params.Output[0]); Wire(link, 1, rooms.Params.Output[1]);
        var scope = Add(new ConduitPlus.Grasshopper.ScopeComponent(), 1120, 1060); Text(scope, 0, "Room review");
        var overlay = Component(204, 1400, 760); overlay.Params.Input[0].DataMapping = GH_DataMapping.Flatten; overlay.Params.Input[1].DataMapping = GH_DataMapping.Flatten;
        foreach (var card in cards) Wire(overlay, 0, card.Params.Output[0]);
        Wire(overlay, 1, link.Params.Output[0]); Wire(overlay, 3, scope.Params.Output[0]);
        var size = (Param_Number)overlay.Params.Input[5]; size.PersistentData.Clear(); size.PersistentData.Append(new GH_Number(1));
        var roomCanvas = Add(new ConduitPlus.Grasshopper.CanvasCardComponent(), 1700, 640); Wire(roomCanvas, 0, table.Params.Output[0]); Wire(roomCanvas, 1, scope.Params.Output[0]);
        var selection = Add(new GH_Panel(), 1700, 1100); selection.NickName = "Selection"; selection.Properties.Multiline = true; selection.Attributes.Bounds = new RectangleF(1700, 1100, 160, 140); selection.AddSource(overlay.Params.Output[0]);
        groups.Add(("3  Link geometry → Overlay · Scope shares selection with a Canvas Card", Amber, [link, scope, overlay, roomCanvas, selection]));

        // ── 2. Truss data → one Canvas Card per card kind ─────────────────────────────────
        var span = Slider("Span (m)", 24, 12, 36, 1, 40, 2160); var load = Slider("Load (kN/m)", 12, 0, 24, 1, 40, 2220);
        var truss = Component(209, 260, 2260); Wire(truss, 0, span); Wire(truss, 3, load); Integer(truss, 1, 6);
        groups.Add(("4  Truss data (Demo Truss) · illustrative, not analysis", Blue, [span, load, truss]));
        var x = 700f; var y = 2080f; var canvasObjects = new List<IGH_DocumentObject>();
        var util = Metric(x, y, truss.Params.Output[7], "Max utilisation", "ratio", 2);
        var tBar = Component(202, x, y + 110); Wire(tBar, 0, truss.Params.Output[3]); Wire(tBar, 1, truss.Params.Output[2]); Wire(tBar, 2, truss.Params.Output[1]); Text(tBar, 3, "Member utilisation");
        var tLine = Component(206, x, y + 330); Wire(tLine, 0, truss.Params.Output[4]); Wire(tLine, 1, truss.Params.Output[10]); Text(tLine, 3, "Deflection along span (mm)");
        var tTable = Component(203, x, y + 520); Text(tTable, 0, "Member", "Type", "Util.", "Length (m)"); Wire(tTable, 1, truss.Params.Output[6]); Wire(tTable, 2, truss.Params.Output[1]); Text(tTable, 3, "Member results");
        var tLegend = Component(208, x, y + 740); Colours(tLegend, 0, "#1F8A5B", "#E5B63D", "#C2334D");
        var uMin = Slider("Scale min", 0, 0, 0, 1, 470, y + 740); var uMax = Slider("Scale max", 1.2m, 1.2m, 1.2m, 1, 470, y + 800); Wire(tLegend, 2, uMin); Wire(tLegend, 3, uMax); Text(tLegend, 4, "ratio"); Text(tLegend, 6, "Utilisation scale");
        var tTitle = Add(new ConduitPlus.Grasshopper.TitleComponent(), x, y + 950); Text(tTitle, 0, "Truss review"); Text(tTitle, 1, "6 bays"); Text(tTitle, 4, "Card");
        var tText = Component(207, x, y + 1120); Text(tText, 0, "Assumptions"); Text(tText, 1, "Pin supports at both ends.\nUniform load on the top chord.\nIllustrative numbers only."); Number(tText, 2, 240);
        GH_Component[] trussCards = [util, tBar, tLine, tTable, tLegend, tTitle, tText];
        var cx = 1060f; var cy = 2040f;
        foreach (var card in trussCards)
        {
            var canvas = Add(new ConduitPlus.Grasshopper.CanvasCardComponent(), cx, cy); Wire(canvas, 0, card.Params.Output[0]);
            canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout();
            canvasObjects.Add(canvas);
            cy += Math.Max(120, canvas.Attributes.Bounds.Height + 30);
            if (cy > 3000) { cy = 2040; cx += 560; }
        }
        groups.Add(("5  Same card kinds on the GH canvas · one Canvas Card each", Green, [.. trussCards, uMin, uMax]));
        groups.Add(("6  Canvas Cards (unscoped: private selection)", Amber, [.. canvasObjects]));

        // ── 3. Massing data → grouped bar + key legend on the canvas ──────────────────────
        var floors = Slider("Extra floors", 0, -1, 5, 0, 40, 3400);
        var massing = Component(211, 260, 3440); Wire(massing, 0, floors);
        var useBar = Component(202, 700, 3360); Wire(useBar, 0, massing.Params.Output[8]); Wire(useBar, 1, massing.Params.Output[7]); Wire(useBar, 2, massing.Params.Output[17]);
        Wire(useBar, 6, massing.Params.Output[9]); Wire(useBar, 7, massing.Params.Output[18]); Text(useBar, 3, "GFA by use (m²)"); Number(useBar, 4, 16000);
        var useKey = Component(int.MinValue, 700, 3580, new Guid("f1deb262-9f13-4293-bc5f-cb98b926dc3c")); Wire(useKey, 0, massing.Params.Output[7]); Wire(useKey, 1, massing.Params.Output[9]); Text(useKey, 3, "Uses");
        var far = Metric(700, 3760, massing.Params.Output[6], "FAR (allowed 2.0)", "", 2);
        var mc = new List<IGH_DocumentObject>(); var my = 3340f;
        foreach (var card in new[] { useBar, useKey, far })
        {
            var canvas = Add(new ConduitPlus.Grasshopper.CanvasCardComponent(), 1060, my); Wire(canvas, 0, card.Params.Output[0]);
            canvas.Attributes.ExpireLayout(); canvas.Attributes.PerformLayout(); my += Math.Max(120, canvas.Attributes.Bounds.Height + 30); mc.Add(canvas);
        }
        groups.Add(("7  Massing (Demo Massing) · grouped Bar, Key Legend, Metric on canvas", Blue, [floors, massing, useBar, useKey, far, .. mc]));

        foreach (var (t, c, objects) in groups)
        {
            var group = Add(new GH_Group { NickName = t, Colour = c }, 0, 0);
            foreach (var obj in objects) group.AddObject(obj.InstanceGuid);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var archive = new GH_Archive(); Require(archive.AppendObject(gh, "Definition") && archive.WriteToFile(path, true, false), "Cannot save " + path);
        Console.WriteLine("Generated " + path);

        T Add<T>(T obj, float px, float py) where T : IGH_DocumentObject { obj.CreateAttributes(); obj.Attributes.Pivot = new(px, py); gh.AddObject(obj, false); return obj; }
        GH_Component Component(int suffix, float px, float py, Guid? id = null) => Add((GH_Component)(Instances.ComponentServer.EmitObject(id ?? new Guid($"a7f32adb-ce5f-49c8-a33f-e3834a0ae{suffix}")) ?? throw new InvalidOperationException("Component not registered.")), px, py);
        GH_Component Metric(float px, float py, IGH_Param value, string t, string unit, int decimals)
        { var m = Component(201, px, py); Wire(m, 0, value); Text(m, 1, t); Text(m, 2, unit); Integer(m, 4, decimals); return m; }
        GH_NumberSlider Slider(string name, decimal value, decimal min, decimal max, int decimals, float px, float py)
        {
            var s = (GH_NumberSlider)(Instances.ComponentServer.EmitObject(new Guid("57da07bd-ecab-415d-9d86-af36d7073abc")) ?? throw new InvalidOperationException("Slider not registered."));
            s.NickName = name; s.Slider.Minimum = min; s.Slider.Maximum = max; s.Slider.DecimalPlaces = decimals; s.SetSliderValue(value); return Add(s, px, py);
        }
        void Slot(GH_Component card, string slot) => Text(card, card.Params.Input.FindIndex(p => p.Name == "Slot"), slot);
    }

    /// <summary>Canvas Cards only know their size after a solve: stack them by measured height, then move the massing section below.</summary>
    private static void Relayout(GH_Document gh, string path)
    {
        void Layout() { foreach (var o in gh.Objects) { o.Attributes.ExpireLayout(); o.Attributes.PerformLayout(); } }
        Layout();
        var groups = gh.Objects.OfType<GH_Group>().ToArray();
        IGH_DocumentObject[] Members(string prefix) => groups.Single(g => g.NickName.StartsWith(prefix, StringComparison.Ordinal)).ObjectIDs.Select(id => gh.FindObject(id, true)).OfType<IGH_DocumentObject>().ToArray();
        void Stack(IEnumerable<IGH_DocumentObject> cards, float x, float top, float maxBottom)
        {
            var y = top; var column = 0f;
            foreach (var card in cards.OrderBy(c => c.Attributes.Pivot.Y))
            {
                var b = card.Attributes.Bounds;
                if (y + b.Height > maxBottom && y > top) { y = top; x += column + 60; column = 0; }
                card.Attributes.Pivot = new PointF(x + (card.Attributes.Pivot.X - b.X), y + (card.Attributes.Pivot.Y - b.Y));
                card.Attributes.ExpireLayout(); card.Attributes.PerformLayout();
                y += b.Height + 40; column = Math.Max(column, b.Width);
            }
        }
        Stack(Members("6 "), 1060, 2040, 3000);
        Layout();
        var above = gh.Objects.Where(o => o is not GH_Group && !Members("7 ").Contains(o)).Max(o => o.Attributes.Bounds.Bottom);
        var massing = Members("7 ");
        var dy = above + 160 - massing.Min(o => o.Attributes.Bounds.Top);
        foreach (var o in massing) { var p = o.Attributes.Pivot; o.Attributes.Pivot = new PointF(p.X, p.Y + dy); }
        Layout();
        Stack(massing.OfType<ConduitPlus.Grasshopper.CanvasCardComponent>(), 1060, massing.Min(o => o.Attributes.Bounds.Top), float.MaxValue);
        Layout();
        var archive = new GH_Archive(); Require(archive.AppendObject(gh, "Definition") && archive.WriteToFile(path, true, false), "Cannot re-save " + path);
        Console.WriteLine("Re-laid out Canvas Cards and saved " + path);
    }

    private static readonly Color Blue = Color.FromArgb(40, 90, 130, 190), Green = Color.FromArgb(40, 80, 160, 110), Amber = Color.FromArgb(40, 210, 160, 60);

    private static void Capture(RhinoDoc doc, RhinoView view, string output, string name)
    {
        view.Redraw();
        typeof(ConduitPlusCapture).GetMethod("Capture", Static)!.Invoke(null, [doc, view, 1]);
        var source = Path.Combine(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!, "captures", "raster-1x.png");
        var target = Path.Combine(output, name + ".png");
        // The capture is transparent outside cards/geometry: flatten onto a neutral viewport grey for review.
        using (var png = new Bitmap(source))
        using (var flat = new Bitmap(png.Width, png.Height))
        {
            using (var g = Graphics.FromImage(flat)) { g.Clear(Color.FromArgb(214, 214, 214)); g.DrawImage(png, 0, 0, png.Width, png.Height); }
            flat.Save(target, ImageFormat.Png);
        }
        Console.WriteLine("Captured " + target);
    }

    private static void CanvasImage(GH_Canvas control, Rectangle bounds, float zoom, string file)
    {
        var viewport = new GH_Viewport(control.Viewport) { Width = (int)(bounds.Width * zoom), Height = (int)(bounds.Height * zoom), Zoom = zoom, Tx = -bounds.X, Ty = -bounds.Y };
        using var image = control.GenerateHiResImageTile(viewport, Color.FromArgb(212, 212, 212));
        image.Save(file, ImageFormat.Png);
        Console.WriteLine("Canvas image " + file);
    }

    private static string Dump(GH_Document gh, string[] messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# showcase.gh — definition dump").AppendLine();
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

    private static string[] Volatile(GH_Document gh, string component, int output) =>
        gh.Objects.OfType<GH_Component>().First(c => c.Name == component).Params.Output[output].VolatileData.AllData(true).Select(g => g.ToString()!).ToArray();
    private static object Session(RhinoDoc doc, RhinoView view, Guid owner) =>
        ((Array)typeof(OverlayManager).GetMethod("InView", Static)!.Invoke(null, [doc, view])!).Cast<object>().Single(s => (Guid)Get(s, "Owner") == owner);
    private static void Select(object session, string card, string key, BrushSelectMode mode) => session.GetType().GetMethod("Select", Instance)!.Invoke(session, [card, key, mode]);
    private static void Wire(GH_Component target, int input, IGH_Param source) => target.Params.Input[input].AddSource(source);
    private static void Text(GH_Component target, int input, params string[] values) { var p = (Param_String)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); }
    private static void Number(GH_Component target, int input, double value) { var p = (Param_Number)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); }
    private static void Integer(GH_Component target, int input, int value) { var p = (Param_Integer)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); }
    private static void Colours(GH_Component target, int input, params string[] hex) { var p = (Param_Colour)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.AppendRange(hex.Select(h => new GH_Colour(ColorTranslator.FromHtml(h)))); }
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
