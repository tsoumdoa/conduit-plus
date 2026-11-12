using System.Drawing;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Saved, editable GH wiring. Native previews use exactly the colours shown by the legends.</summary>
internal sealed class ReviewDefinition
{
    private readonly GH_Document gh = new();
    private readonly List<IGH_DocumentObject> section = [];
    private readonly List<GH_Component> cards = [];
    private readonly Dictionary<GH_Component, (GH_Component Merge, int Count)> displayMerges = [];
    private float top = 100;
    private float row = 100;
    private readonly bool hello;
    private ReviewDefinition(bool hello) => this.hello = hello;

    internal static void Build(string path, bool hello)
    {
        var b = new ReviewDefinition(hello);
        try { b.Create(); b.Save(path); }
        finally { b.gh.Dispose(); }
    }
    private T Add<T>(T obj, float x, float y) where T : IGH_DocumentObject
    { obj.CreateAttributes(); obj.Attributes.Pivot = new(x, y); gh.AddObject(obj, false); section.Add(obj); return obj; }
    private T Card<T>(T obj) where T : GH_Component
    { Add(obj, 740, row); row += 235; cards.Add(obj); return obj; }
    private GH_Component Native(string name, float x, float y)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == name)
            ?? throw new InvalidOperationException("Native GH component unavailable: " + name);
        var component = Add((GH_Component)Instances.ComponentServer.EmitObject(proxy.Guid), x, y);
        component.Hidden = name != "Custom Preview"; return component;
    }
    private GH_Panel Panel(string name, IEnumerable<string> values, float y)
    {
        var p = Add(new GH_Panel { NickName = name, UserText = string.Join("\n", values) }, 70, y);
        p.Properties.Multiline = false; p.Attributes.Bounds = new RectangleF(70, y, 190, 140); return p;
    }
    private void Finish(string title)
    {
        var group = new GH_Group { NickName = title, Colour = Color.FromArgb(32, 105, 128, 146) };
        group.CreateAttributes(); gh.AddObject(group, false);
        foreach (var o in section) group.AddObject(o.InstanceGuid);
        section.Clear(); cards.Clear();
    }
    private CanvasCardComponent Stack(ScopeComponent? scope = null)
    {
        var stack = Add(new CanvasCardComponent(), 1180, top);
        typeof(CanvasCardComponent).GetMethod("ApplyTheme", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(stack, ["Paper"]);
        foreach (var card in cards) DisplayCard(stack, card.Params.Output[0]);
        if (scope is not null) Wire(stack, 1, scope.Params.Output[0]);
        return stack;
    }
    // Flatten each source before native Merge so GH branch paths cannot reorder cards.
    private void DisplayCard(GH_Component display, IGH_Param source)
    {
        if (!displayMerges.TryGetValue(display, out var entry))
        {
            var merge = Native("Merge", display.Attributes.Pivot.X - 200, display.Attributes.Pivot.Y + 140);
            Wire(display, 0, merge.Params.Output[0]); entry = (merge, 0);
        }
        while (entry.Merge.Params.Input.Count <= entry.Count)
        {
            var variable = (IGH_VariableParameterComponent)entry.Merge;
            entry.Merge.Params.RegisterInputParam(variable.CreateParameter(GH_ParameterSide.Input, entry.Merge.Params.Input.Count));
            entry.Merge.Params.OnParametersChanged();
        }
        entry.Merge.Params.Input[entry.Count].DataMapping = GH_DataMapping.Flatten;
        Wire(entry.Merge, entry.Count, source); displayMerges[display] = (entry.Merge, entry.Count + 1);
    }
    private void Create()
    {
        GH_Component? rooms = null;
        IGH_Param geometry, names, types, values, keys;
        if (hello)
        {
            names = Panel("Room names", ["Studio 01", "Studio 02", "Meeting 01", "Meeting 02", "Store 01", "Store 02"], top);
            types = Panel("Programme", ["Studio", "Studio", "Meeting", "Meeting", "Store", "Store"], top + 185);
            values = Panel("Area (m²)", ["12", "18", "15", "24", "9", "12"], top + 370);
            var rects = Add(new Param_Rectangle { NickName = "Six room faces · metres", Hidden = true }, 380, top);
            double[] widths = [4, 6, 5, 8, 3, 4];
            for (var i = 0; i < widths.Length; i++) rects.PersistentData.Append(new GH_Rectangle(new Rectangle3d(
                new Plane(new Point3d(i % 3 * 10, i / 3 * 5, 0), Vector3d.ZAxis), widths[i], 3)));
            var surface = Native("Boundary Surfaces", 540, top); Wire(surface, 0, rects);
            geometry = surface.Params.Output[0]; geometry.DataMapping = GH_DataMapping.Flatten;
            keys = Panel("Keys · match Link defaults", ["1", "2", "3", "4", "5", "6"], top + 555);
        }
        else
        {
            var scale = Add(new GH_NumberSlider { NickName = "Plan scale" }, 70, top);
            scale.Slider.Minimum = .5m; scale.Slider.Maximum = 2; scale.Slider.DecimalPlaces = 2; scale.SetSliderValue(1);
            rooms = Add(new DemoRoomsComponent { Hidden = true }, 380, top + 100); Wire(rooms, 0, scale);
            geometry = rooms.Params.Output[0]; names = rooms.Params.Output[2]; types = rooms.Params.Output[14]; values = rooms.Params.Output[12]; keys = rooms.Params.Output[1];
        }
        var linkedGeometry = geometry;
        Finish(hello ? "1  Your data · six rooms, metres" : "1  Room data · schematic minima, not regulations");

        top = 1050; row = top;
        var categories = Add(new ColourCategoriesComponent(), 380, top); Wire(categories, 0, types); Wire(categories, 1, keys);
        Text(categories, 2, "Programme"); Text(categories, 4, hello ? "Programme" : "01 Programme"); Text(categories, 5, "tr");
        if (rooms is not null) Wire(categories, 3, rooms.Params.Output[10]);
        var preview = Native("Custom Preview", 740, top); preview.NickName = "Programme preview";
        Wire(preview, 0, geometry); Wire(preview, 1, categories.Params.Output[0]);
        ColourScaleComponent? scaleColours = null;
        if (!hello)
        {
            scaleColours = Add(new ColourScaleComponent(), 380, top + 300); Wire(scaleColours, 0, values);
            Text(scaleColours, 1, "Blue"); Number(scaleColours, 2, 0); Number(scaleColours, 3, 40); Text(scaleColours, 5, "m²"); Text(scaleColours, 6, "02 Area"); Text(scaleColours, 7, "br");
            var vector = Add(new Param_Vector { NickName = "Second plan offset" }, 70, top + 330); vector.PersistentData.Append(new GH_Vector(new Vector3d(24, 0, 0)));
            var move = Native("Move", 740, top + 300); Wire(move, 0, geometry); Wire(move, 1, vector);
            var both = Native("Entwine", 1180, top + 300);
            while (both.Params.Input.Count > 2) both.Params.UnregisterInputParameter(both.Params.Input[^1], true);
            both.Params.OnParametersChanged(); Wire(both, 0, geometry); Wire(both, 1, move.Params.Output[0]);
            both.Params.Input[0].DataMapping = GH_DataMapping.Flatten; both.Params.Input[1].DataMapping = GH_DataMapping.Flatten;
            var pair = Native("Flip Matrix", 1380, top + 300); Wire(pair, 0, both.Params.Output[0]); linkedGeometry = pair.Params.Output[0];
            var areaPreview = Native("Custom Preview", 1000, top + 300); areaPreview.NickName = "Area preview"; Wire(areaPreview, 0, move.Params.Output[0]); Wire(areaPreview, 1, scaleColours.Params.Output[0]);
        }
        Finish(hello ? "2  Cards · programme colours + preview" : "2  Matching colours · helper → Custom Preview + Legend");

        top = 1800; row = top;
        if (!hello)
        {
            var title = Card(new TitleComponent()); Text(title, 0, "ROOM / AREA REVIEW"); Text(title, 1, "01 Programme    /    02 Area"); Text(title, 3, "tc");
            var summary = Card(new MetricComponent()); Wire(summary, 0, rooms!.Params.Output[5]); Wire(summary, 0, rooms.Params.Output[6]); summary.Params.Input[0].DataMapping = GH_DataMapping.Flatten;
            Text(summary, 1, "Schematic GIA", "Net / gross"); Text(summary, 2, "m²", "%"); Integer(summary, 4, 0); ((Param_Integer)summary.Params.Input[4]).PersistentData.Append(new GH_Integer(1)); Text(summary, 3, "tl");
            var fail = Card(new MetricComponent()); Wire(fail, 0, rooms.Params.Output[8]); Text(fail, 1, "Rooms below minimum"); Text(fail, 5, "below-minimum"); Wire(fail, 6, rooms.Params.Output[17]); Text(fail, 3, "tl"); Integer(fail, 4, 0);
        }
        else
        {
            // A two-item list demonstrates native Metric matching without six room cards.
            // These fixed summary values correspond to the editable room-area list above.
            var metric = Card(new MetricComponent()); Text(metric, 0, "90", "3"); Text(metric, 1, "Total area", "Rooms below 15 m²"); Text(metric, 2, "m²", ""); Integer(metric, 4, 0); Text(metric, 3, "tl");
        }
        var bar = Card(new BarComponent()); Wire(bar, 0, hello ? values : rooms!.Params.Output[3]); Wire(bar, 1, names);
        Wire(bar, 2, keys);
        Text(bar, 3, hello ? "Area ≥ 15 m²" : "Area − minimum (m²)"); Number(bar, 4, hello ? 15 : 0); Text(bar, 8, "Above"); Text(bar, 9, "Worst first"); Integer(bar, 10, 12); Text(bar, 5, "tl");
        var table = Card(new TableComponent()); Text(table, 0, hello ? ["Room", "Type", "Area m²"] : ["Room", "Type", "Area m²", "Min m²", "Δ m²", "Status"]);
        if (hello)
        {
            // Separate native lists, one branch per column: no row-string assembly.
            var entwine = Native("Entwine", 530, top + 480);
            while (entwine.Params.Input.Count < 3)
            {
                var variable = (IGH_VariableParameterComponent)entwine; var i = entwine.Params.Input.Count;
                entwine.Params.RegisterInputParam(variable.CreateParameter(GH_ParameterSide.Input, i)); entwine.Params.OnParametersChanged();
            }
            Wire(entwine, 0, names); Wire(entwine, 1, types); Wire(entwine, 2, values); Wire(table, 7, entwine.Params.Output[0]);
        }
        else
        {
            var flip = Native("Flip Matrix", 380, row - 235); Wire(flip, 0, rooms!.Params.Output[4]); Wire(table, 7, flip.Params.Output[0]); Wire(table, 2, keys);
        }
        if (hello) Wire(table, 2, keys);
        Text(table, 3, "Room schedule"); Integer(table, 5, hello ? 6 : 7); Number(table, 6, hello ? 360 : 460); Integer(table, 8, 1); Text(table, 4, "tr");
        var scope = Add(new ScopeComponent(), 980, top - 120); Text(scope, 0, "Room review");
        Stack(scope);
        // The same cards serve both displays; Scope shares their keyed selection.
        var overlay = Add(new OverlayComponent(), 980, row + 80);
        foreach (var card in cards) DisplayCard(overlay, card.Params.Output[0]);
        DisplayCard(overlay, categories.Params.Output[1]); if (scaleColours is not null) DisplayCard(overlay, scaleColours.Params.Output[1]);
        var link = Add(new LinkComponent(), 380, row + 80); Wire(link, 0, linkedGeometry); if (!hello) Wire(link, 1, keys);
        Wire(overlay, 1, link.Params.Output[0]); Wire(overlay, 3, scope.Params.Output[0]);
        Finish(hello ? "3  Show it · Overlay + stacked Canvas Card" : "3  Room review · stacked cards, shared Scope + Overlay");

        if (!hello)
        {
            top = 4400; row = top;
            var within = Card(new BarComponent());
            foreach (var (source, i) in new[] { (rooms!.Params.Output[3], 0), (names, 1), (keys, 2) })
            {
                var cull = Native("Cull Index", 380, top + i * 160); Wire(cull, 0, source); Integer(cull, 1, 16); Wire(within, i, cull.Params.Output[0]);
            }
            Text(within, 3, "Minimum allowance ±2 m²"); Number(within, 4, 2); Text(within, 8, "Within"); Text(within, 9, "Worst first"); Integer(within, 10, 6);
            var areaBar = Card(new BarComponent()); Wire(areaBar, 0, values); Wire(areaBar, 1, names); Wire(areaBar, 2, keys); Text(areaBar, 3, "Room area (m²)"); Integer(areaBar, 10, 6);
            var note = Card(new TextComponent()); Text(note, 0, "Reading the review"); Text(note, 1, "Left plan: programme. Right plan: area.\nArea − minimum ≥ 0 passes; ±2 is a separate allowance study (hall excluded).\nClick replaces; Shift adds; Ctrl removes; Ctrl+Shift toggles.\nClear or plain empty viewport click clears. Escape cancels gestures.\nCanvas wheel zooms; use card scrollbars.\nIllustrative data, not a regulatory check."); Number(note, 2, 340);
            var palette = Add(new ColourPaletteComponent(), 380, top + 700); Text(palette, 0, "Programme"); Integer(palette, 1, 6);
            var key = Card(new KeyLegendComponent()); Wire(key, 0, rooms.Params.Output[10]); Wire(key, 1, palette.Params.Output[0]); Text(key, 3, "Programme palette · canvas reference");
            // The programme legend already comes from Colour Categories in the viewport.
            DisplayCard(overlay, note.Params.Output[0]);
            Stack(scope); Finish("4  Design allowance · Within ±2 + canvas-only palette reference");

            top = 5800; row = top;
            var truss = Add(new DemoTrussComponent { Hidden = true }, 180, top); Integer(truss, 1, 4);
            var tm = Card(new MetricComponent()); Wire(tm, 0, truss.Params.Output[7]); Text(tm, 1, "Max utilisation"); Integer(tm, 4, 2);
            var tb = Card(new BarComponent()); Wire(tb, 0, truss.Params.Output[3]); Wire(tb, 1, truss.Params.Output[2]); Text(tb, 3, "Utilisation ≤ 1"); Number(tb, 4, 1); Integer(tb, 10, 5);
            var tl = Card(new LineComponent()); Wire(tl, 0, truss.Params.Output[4]); Wire(tl, 1, truss.Params.Output[10]); Text(tl, 3, "Deflection (mm)"); Number(tl, 4, 12);
            var tt = Card(new TableComponent()); Text(tt, 0, "Member", "Type", "Util.", "Length m"); Wire(tt, 1, truss.Params.Output[6]); Text(tt, 3, "Member results"); Integer(tt, 5, 4); Number(tt, 6, 360);
            var legend = Card(new LegendComponent()); Colours(legend, 0, "#E8EFF4", "#356687"); Number(legend, 2, 0); Number(legend, 3, 12); Text(legend, 4, "mm"); Text(legend, 6, "Deflection reference");
            var title = Card(new TitleComponent()); Text(title, 0, "STRUCTURE / REFERENCE"); Text(title, 1, "Independent canvas study"); Text(title, 4, "Card");
            var text = Card(new TextComponent()); Text(text, 0, "Illustrative truss"); Text(text, 1, "Four bays, simply supported.\nFictitious stiffness and capacities.\nNot structural verification.");
            Stack(); Finish("5  Structure reference · Demo Truss, all card kinds in one stack");

            top = 8400; row = top;
            var massing = Add(new DemoMassingComponent { Hidden = true }, 180, top);
            var cats = Add(new ColourCategoriesComponent(), 380, top); Wire(cats, 0, massing.Params.Output[16]); Wire(cats, 1, massing.Params.Output[1]); Wire(cats, 3, massing.Params.Output[7]); Text(cats, 2, "Programme");
            var mb = Card(new BarComponent()); Wire(mb, 0, massing.Params.Output[8]); Wire(mb, 1, cats.Params.Output[2]); Wire(mb, 2, cats.Params.Output[4]); Wire(mb, 6, cats.Params.Output[3]); Wire(mb, 7, cats.Params.Output[5]); Text(mb, 3, "GFA by use (m²)");
            var mk = Card(new KeyLegendComponent()); Wire(mk, 0, cats.Params.Output[2]); Wire(mk, 1, cats.Params.Output[3]); Text(mk, 3, "Uses");
            var mm = Card(new MetricComponent()); Wire(mm, 0, massing.Params.Output[6]); Text(mm, 1, "FAR / allowed 2.0"); Integer(mm, 4, 2);
            Stack(); Finish("6  Massing reference · grouped Bar from Colour Categories");
        }
    }
    private void Save(string path)
    {
        foreach (var (merge, count) in displayMerges.Values)
        {
            while (merge.Params.Input.Count > count) merge.Params.UnregisterInputParameter(merge.Params.Input[^1], true);
            merge.Params.OnParametersChanged();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var archive = new GH_Archive();
        if (!archive.AppendObject(gh, "Definition") || !archive.WriteToFile(path, true, false)) throw new InvalidOperationException("Cannot save " + path);
    }
    private static void Wire(GH_Component target, int input, IGH_Param source) => target.Params.Input[input].AddSource(source);
    private static void Text(GH_Component target, int input, params string[] values)
    { var p = (Param_String)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); }
    private static void Number(GH_Component target, int input, double value)
    { var p = (Param_Number)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); }
    private static void Integer(GH_Component target, int input, int value)
    { var p = (Param_Integer)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); }
    private static void Colours(GH_Component target, int input, params string[] values)
    { var p = (Param_Colour)target.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_Colour(ColorTranslator.FromHtml(v)))); }
}
