using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using CoordinateSystem = Rhino.DocObjects.CoordinateSystem;
using Rhino.Geometry;

internal static class ArchitectureDashboard
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(RhinoDoc doc, RhinoView view, string root, string[] args, string example)
    {
        foreach (var rooms in new[] { true, false })
        {
            var name = rooms ? "room-areas" : "massing";
            if (example != "all" && example != (rooms ? "rooms" : "massing")) continue;
            var path = Path.Combine(root, "examples", name + ".gh");
            if (!args.Contains("--verify-only")) Build(path, rooms);
            using var gh = new GH_Document(); var archive = new GH_Archive();
            Require(archive.ReadFromFile(path) && archive.ExtractObject(gh, "Definition"), "Cannot reopen " + path);
            Instances.DocumentServer.AddDocument(gh); gh.Enabled = true; GH_Document.EnableSolutions = true;
            try
            {
                gh.NewSolution(false);
                var messages = gh.Objects.OfType<IGH_ActiveObject>().SelectMany(o => new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }.SelectMany(level => o.RuntimeMessages(level).Select(m => o.Name + ": " + m))).ToArray();
                Require(messages.Length == 0, string.Join("\n", messages));
                var overlay = gh.Objects.OfType<ConduitPlus.Grasshopper.OverlayComponent>().Single();
                var manager = typeof(OverlayManager);
                var sessions = (Array)manager.GetMethod("InView", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [doc, view])!;
                var session = sessions.Cast<object>().Single(s => (Guid)Get(s, "Owner") == overlay.InstanceGuid);
                var cards = (CardSpec[])Get(session, "Cards");
                Require(cards.Length == (rooms ? 7 : 9), "Unexpected architectural card count.");
                var metrics = cards.Where(c => c.Kind == CardKind.Metric).ToArray();
                Require(metrics.Select(c => c.Value).SequenceEqual(rooms ? ["320", "87.5", "12.5", "8"] : new[] { "14976", "16000", "1.87", "2" }), "Unexpected architectural metric precision.");
                var theme = args.Contains("--theme") ? Theme.Presets.Single(t => string.Equals(t.Name, args[Array.IndexOf(args, "--theme") + 1], StringComparison.OrdinalIgnoreCase)) : Theme.Paper;
                OverlayManager.ReplaceTheme(overlay.InstanceGuid, theme);
                var links = (Link[])session.GetType().GetField("_links", Instance)!.GetValue(session)!;
                Require(links.Length == 1 && links[0].Keys.Count == (rooms ? 17 : 6), "Missing keyed architecture geometry.");
                foreach (var link in links) typeof(Link).GetProperty("ShowBase")!.SetValue(link, true);
                gh.PreviewMode = GH_PreviewMode.Disabled;
                view.ActiveViewport.SetProjection(DefinedViewportProjection.Top, "Top", false);
                view.ActiveViewport.ZoomBoundingBox(rooms ? new BoundingBox(-2, -2, -1, 22, 18, 1) : new BoundingBox(-5, -5, -1, 105, 85, 45));
                view.Redraw();
                // Pick every roof/room centre, well away from edges, through the production ModelPicking path.
                var centres = rooms ? DemoRooms.Create().Rooms.Select(r => (r.Key, Point: new Point3d(r.X + r.Width / 2, r.Y + r.Depth / 2, 0))).ToArray()
                    : DemoMassing.Create().Blocks.Select(b => (b.Key, Point: new Point3d(b.X + b.Width / 2, b.Y + b.Depth / 2, b.Height))).ToArray();
                var screen = view.ActiveViewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
                var picker = manager.Assembly.GetType("ConduitPlus.Rhino.ModelPicking")!.GetMethod("Pick", BindingFlags.Static | BindingFlags.NonPublic)!;
                var onlySession = Array.CreateInstance(session.GetType(), 1); onlySession.SetValue(session, 0);
                foreach (var centre in centres)
                {
                    var p = centre.Point; p.Transform(screen);
                    var picked = picker.Invoke(null, [view, onlySession, new System.Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))]);
                    Require(picked is not null, "No face-interior hit for " + centre.Key);
                    var hit = (PickCandidate)picked!.GetType().GetField("Item2")!.GetValue(picked)!;
                    Require(hit.IsMesh && hit.Key == centre.Key, "Wrong face-interior hit for " + centre.Key + ": " + hit);
                }
                if (!rooms)
                {
                    var b = DemoMassing.Create().Blocks[0];
                    using var outline = new Rectangle3d(Plane.WorldXY, new Interval(b.X, b.X + b.Width), new Interval(b.Y, b.Y + b.Depth)).ToNurbsCurve();
                    using var extrusion = Extrusion.Create(outline, b.Height, true);
                    using var mesh = Mesh.CreateFromBox(new BoundingBox(b.X, b.Y, 0, b.X + b.Width, b.Y + b.Depth, b.Height), 1, 1, 1);
                    try
                    {
                        foreach (var geometry in new GeometryBase[] { extrusion, mesh })
                        {
                            var probe = new Link([b.Key], [(b.Key, (object)geometry)]);
                            session.GetType().GetMethod("SetLinks", Instance)!.Invoke(session, [new[] { probe }]);
                            var p = centres[0].Point; p.Transform(screen);
                            var picked = picker.Invoke(null, [view, onlySession, new System.Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))]);
                            Require(picked is not null && ((PickCandidate)picked.GetType().GetField("Item2")!.GetValue(picked)!).IsMesh, "No face-interior hit for " + geometry.GetType().Name);
                        }
                    }
                    finally { session.GetType().GetMethod("SetLinks", Instance)!.Invoke(session, [links]); }
                }
                // Last row starts outside the room table's visible range. Selection must reveal it after a sort too.
                var table = cards.Single(c => c.Kind == CardKind.Table);
                var key = centres[^1].Key;
                session.GetType().GetMethod("Select", Instance)!.Invoke(session, ["model", key, BrushSelectMode.Toggle]);
                var selectedTable = ((CardSpec[])Get(session, "Cards")).Single(c => c.Id == table.Id);
                Require(TableSort.Rows(selectedTable).Skip(TableScroll.Offset(selectedTable)).Take(TableScroll.Visible(selectedTable)).Any(r => r.Key == key), "Model selection did not reveal table row.");
                session.GetType().GetMethod("Sort", Instance)!.Invoke(session, [table.Id, 0]);
                session.GetType().GetMethod("Select", Instance)!.Invoke(session, ["model", key, BrushSelectMode.Toggle]); // deselect
                session.GetType().GetMethod("ScrollTo", Instance)!.Invoke(session, [table.Id, 0]);
                session.GetType().GetMethod("Select", Instance)!.Invoke(session, ["model", key, BrushSelectMode.Toggle]);
                selectedTable = ((CardSpec[])Get(session, "Cards")).Single(c => c.Id == table.Id);
                Require(TableSort.Rows(selectedTable).Skip(TableScroll.Offset(selectedTable)).Take(TableScroll.Visible(selectedTable)).Any(r => r.Key == key), "Sorted table did not reveal selection.");
                // Exercise the reverse direction too: a table row toggles the same model key.
                session.GetType().GetMethod("Select", Instance)!.Invoke(session, [table.Id, key, BrushSelectMode.Toggle]);
                Require(!OverlayManager.Snapshot(overlay.InstanceGuid).SelectedKeys.Contains(key), "Table row did not toggle model selection off.");
                session.GetType().GetMethod("Select", Instance)!.Invoke(session, [table.Id, key, BrushSelectMode.Toggle]);
                Require(OverlayManager.Snapshot(overlay.InstanceGuid).SelectedKeys.Contains(key), "Table row did not select model key.");
                if (!rooms)
                {
                    view.ActiveViewport.SetProjection(DefinedViewportProjection.Perspective, "Perspective", false);
                    view.ActiveViewport.SetCameraLocations(new(50, 40, 12), new(160, -170, 170));
                    view.ActiveViewport.ZoomBoundingBox(new BoundingBox(-10, -10, 0, 110, 90, 45));
                }
                var result = Path.Combine(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!, name); Directory.CreateDirectory(result);
                File.WriteAllText(Path.Combine(result, "example.json"), CardJson.Serialize(new { path, objects = gh.ObjectCount, warnings = messages, theme = theme.Name, cards = cards.Select(c => c.Title), metrics = metrics.Select(c => new { c.Title, c.Value }), faceInteriorHits = centres.Length, proxyFaceInteriorHits = rooms ? 0 : 2, selectionRevealed = key, tableToModelSelection = true }));
                var groupState = args[Array.IndexOf(args, "--state") + 1];
                if (args.Contains("--exports") && groupState != "scope" && !groupState.StartsWith("group", StringComparison.Ordinal))
                {
                    Require(!args.Contains("--compact"), "Compact export fixture is structural only; use -Example Structural.");
                    var scale = int.Parse(args[Array.IndexOf(args, "--scale") + 1], System.Globalization.CultureInfo.InvariantCulture);
                    var geometry = rooms ? DemoRooms.Create().Rooms.Select(r => (GeometryBase)Brep.CreatePlanarBreps(new Rectangle3d(Plane.WorldXY, new Interval(r.X, r.X + r.Width), new Interval(r.Y, r.Y + r.Depth)).ToNurbsCurve(), 1e-6).Single()).ToArray()
                        : DemoMassing.Create().Blocks.Select(b => (GeometryBase)new BoundingBox(b.X, b.Y, 0, b.X + b.Width, b.Y + b.Depth, b.Height).ToBrep()).ToArray();
                    var previous = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS");
                    try
                    {
                        Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", result);
                        ExportChecks.Run(doc, view, session, (CardSpec[])Get(session, "Cards"), result, args.Contains("--safe"), scale, geometry);
                        // The selected face centre is far from any edge: blue here proves shaded fill, not just wires.
                        var point = centres[^1].Point; point.Transform(view.ActiveViewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen));
                        using var png = new Bitmap(Path.Combine(result, "captures", $"raster-{scale}x.png"));
                        var pixel = png.GetPixel((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale));
                        Require(pixel.A >= 140 && pixel.A < 240 && pixel.B > pixel.R + 20 && pixel.B > pixel.G + 10, "Selected face interior needs readable, translucent blue shading.");
                        File.WriteAllText(Path.Combine(result, "surface-check.json"), CardJson.Serialize(new { key, x = point.X, y = point.Y, pixel.R, pixel.G, pixel.B, pixel.A }));
                    }
                    finally { Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", previous); foreach (var g in geometry) g.Dispose(); }
                }
                if (rooms) ScopeHostChecks.Run(gh, doc, view, result, args.Contains("--exports") && groupState == "scope");
                session = ((Array)manager.GetMethod("InView", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [doc, view])!).Cast<object>().Single(s => (Guid)Get(s, "Owner") == overlay.InstanceGuid);
                ArchitectureGroupChecks.Run(gh, doc, view, session, rooms, result, args.Contains("--exports") ? groupState : "none");
                if (rooms) ScopeRhinoCloseChecks.Run(doc, result);
                Console.WriteLine($"Reopened {path}: {gh.ObjectCount} objects, 0 warnings/errors; {cards.Length} cards; {centres.Length} face-interior hits; sorted selection reveal passed.");
            }
            finally { Instances.DocumentServer.RemoveDocument(gh); }
        }
    }
    private static void Build(string path, bool rooms)
    {
        using var gh = new GH_Document();
        var slider = Add(new GH_NumberSlider(), 50, 160);
        slider.NickName = rooms ? "Plan scale (minima fixed)" : "Extra floors per block";
        slider.Slider.Minimum = rooms ? .5m : -1; slider.Slider.Maximum = rooms ? 2 : 5; slider.Slider.DecimalPlaces = rooms ? 2 : 0; slider.SetSliderValue(rooms ? 1 : 0);
        var demo = Component(rooms ? 210 : 211, 340, 280); Wire(demo, 0, slider);
        var cards = new List<GH_Component>();
        Metric(rooms ? 5 : 4, rooms ? "Schematic GIA" : "Total GFA", "m²", 0);
        Metric(rooms ? 6 : 5, rooms ? "Net / gross" : "Allowed GFA", rooms ? "%" : "m²", rooms ? 1 : 0);
        Metric(rooms ? 7 : 6, rooms ? "Circulation" : "FAR (allowed 2.0)", rooms ? "%" : "ratio", rooms ? 1 : 2);
        Metric(rooms ? 8 : 11, rooms ? "Rooms below minimum" : "Blocks above 30 m", "", 0);
        Text(cards[^1], 5, rooms ? "below-minimum" : "above-height-limit");
        Wire(cards[^1], 6, demo.Params.Output[rooms ? 17 : 19]);
        var bar = Component(202, 740, 600); Wire(bar, 0, demo.Params.Output[rooms ? 3 : 2]); Wire(bar, 1, demo.Params.Output[rooms ? 2 : 1]); Wire(bar, 2, demo.Params.Output[1]); Text(bar, 3, rooms ? "Area − minimum (m²)" : "Height / 30 m limit"); Number(bar, 4, rooms ? 0 : 1);
        if (rooms) Wire(bar, 6, demo.Params.Output[9]); cards.Add(bar);
        var table = Component(203, 740, 820); Text(table, 0, rooms ? ["Room", "Type", "m²", "Min m²", "Δ m²", "Status"] : ["Block", "Use", "Floors", "Height m", "Limit m", "GFA m²"]);
        Wire(table, 1, demo.Params.Output[rooms ? 4 : 3]); Wire(table, 2, demo.Params.Output[1]); Text(table, 3, rooms ? "Room area check" : "Block schedule"); Number(table, 6, 600); cards.Add(table);
        var legend = Component(208, 740, 1050); Wire(legend, 0, demo.Params.Output[rooms ? 11 : 9]); Wire(legend, 1, demo.Params.Output[rooms ? 10 : 7]); Text(legend, 6, rooms ? "Room types" : "Building uses");
        Wire(legend, 5, demo.Params.Output[rooms ? 15 : 17]); Wire(legend, 8, demo.Params.Output[rooms ? 16 : 18]); cards.Add(legend);
        if (!rooms)
        {
            var useBar = Component(202, 740, 1240); Wire(useBar, 0, demo.Params.Output[8]); Wire(useBar, 1, demo.Params.Output[7]); Wire(useBar, 6, demo.Params.Output[9]); Text(useBar, 3, "GFA by use (m²)"); Number(useBar, 4, 16000);
            Wire(useBar, 2, demo.Params.Output[17]); Wire(useBar, 7, demo.Params.Output[18]); cards.Add(useBar);
            var note = Component(207, 740, 1450); Text(note, 0, "Schematic zoning study"); Text(note, 1, "100 × 80 m site · FAR limit 2.0 · height limit 30 m.\nGFA counts whole floorplates. Limits are fictitious.\nClick a face or row; change Extra floors."); cards.Add(note);
        }
        else
        {
            // Keep seven cards: put the assumptions in a canvas group rather than another viewport card.
            Group("Schematic GIA includes Hall; walls omitted. Net excludes Hall. Fictitious minima, not regulations.", [demo, slider]);
        }
        var link = Component(205, 1050, 400); Wire(link, 0, demo.Params.Output[0]); Wire(link, 1, demo.Params.Output[1]);
        var overlay = Component(204, 1340, 750); overlay.Params.Input[0].DataMapping = GH_DataMapping.Flatten; overlay.Params.Input[1].DataMapping = GH_DataMapping.Flatten;
        var layout = cards.Select((c, i) => (c, i)).ToDictionary(p => p.c.InstanceGuid.ToString("N"), p => new LayoutOverride { ManualSlot = p.i == 4 ? Slot.Tr : p.i == 5 ? Slot.Br : Slot.Tl, OrderSlot = p.i == 4 ? Slot.Tr : p.i == 5 ? Slot.Br : Slot.Tl, Order = p.i });
        var chunk = new GH_Archive().CreateTopLevelNode("Overlay"); overlay.Write(chunk); chunk.RemoveItem("ConduitPlus.Layout"); chunk.SetString("ConduitPlus.Layout", LayoutEditing.Serialize(layout)); overlay.Read((GH_IReader)chunk);
        foreach (var card in cards) Wire(overlay, 0, card.Params.Output[0]); Wire(overlay, 1, link.Params.Output[0]);

        if (rooms)
        {
            var scope = Add(new ConduitPlus.Grasshopper.ScopeComponent(), 1340, 1030);
            Text(scope, 0, "Room review"); Wire(overlay, 3, scope.Params.Output[0]);
            var canvas = Add(new ConduitPlus.Grasshopper.CanvasCardComponent(), 1700, 750);
            Wire(canvas, 0, table.Params.Output[0]); Wire(canvas, 1, scope.Params.Output[0]);
            Group("4  Shared room selection · Canvas ↔ viewport", [scope, canvas]);
        }
        Group("1  Explore schematic geometry (metres)", [slider, demo]); Group("2  Cards · explicit room/block keys", cards); Group("3  Click geometry ↔ row", [link, overlay]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var archive = new GH_Archive(); Require(archive.AppendObject(gh, "Definition") && archive.WriteToFile(path, true, false), "Cannot save " + path); Console.WriteLine("Generated " + path);

        T Add<T>(T obj, float x, float y) where T : IGH_DocumentObject { obj.CreateAttributes(); obj.Attributes.Pivot = new(x, y); gh.AddObject(obj, false); return obj; }
        GH_Component Component(int suffix, float x, float y) => Add((GH_Component)(Instances.ComponentServer.EmitObject(new Guid($"a7f32adb-ce5f-49c8-a33f-e3834a0ae{suffix}")) ?? throw new InvalidOperationException("Component not registered.")), x, y);
        void Metric(int output, string title, string unit, int decimals) { var m = Component(201, 740, 90 + cards.Count * 110); Wire(m, 0, demo.Params.Output[output]); Text(m, 1, title); Text(m, 2, unit); ((Param_Integer)m.Params.Input[4]).PersistentData.Append(new GH_Integer(decimals)); cards.Add(m); }
        void Group(string title, IEnumerable<IGH_DocumentObject> objects) { var g = Add(new GH_Group { NickName = title, Colour = Color.FromArgb(35, 100, 130, 160) }, 0, 0); foreach (var obj in objects) g.AddObject(obj.InstanceGuid); }
    }
    private static void Wire(GH_Component target, int input, IGH_Param source) => target.Params.Input[input].AddSource(source);
    private static void Text(GH_Component target, int input, params string[] values) { var p = (Param_String)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); }
    private static void Number(GH_Component target, int input, double value) { var p = (Param_Number)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); }
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
