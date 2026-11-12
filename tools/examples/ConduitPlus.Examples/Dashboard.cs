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
using Rhino.Geometry;

internal static class Dashboard
{
    private static readonly Type Manager = typeof(OverlayManager);
    private static readonly BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal static void Stop() => Manager.GetMethod("Stop", Internal)!.Invoke(null, null);
    internal static void Run(RhinoDoc doc, RhinoView view, string[] args)
    {
        Console.WriteLine("Registering built-in Grasshopper objects.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ConduitPlus.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository not found.");
        var path = Path.Combine(root.FullName, "examples", "structural-dashboard.gh");
        // Explicit registration only: never scan the user's installed GH libraries.
        // The default getter calls LoadExternalFiles and can open modal add-on dialogs.
        typeof(Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, new GH_ComponentServer());
        Register(typeof(GH_NumberSlider).Assembly);
        Console.WriteLine("Registering checkout components.");
        var gha = typeof(ConduitPlus.Grasshopper.DemoTrussComponent).Assembly;
        Register(gha);
        TableHostChecks.Run();
        var example = args.Contains("--example") ? args[Array.IndexOf(args, "--example") + 1] : "structural";
        if (example is "rooms" or "massing") { GroupInputChecks.Run(); ArchitectureDashboard.Run(doc, view, root.FullName, args, example); return; }
        InputTrapChecks.Run();
        Require(example == "structural", "Run Build-Examples.ps1 for all examples (one RhinoCore host per definition).");
        using (var legacy = new GH_Document())
        {
            var frozen = new GH_Archive();
            var frozenPath = Path.Combine(root.FullName, "tools", "examples", "fixtures", "structural-dashboard.gh");
            Require(frozen.ReadFromFile(frozenPath) && frozen.ExtractObject(legacy, "Definition"), "Cannot read frozen fixture.");
            DisplayLifecycleChecks.ArchiveClean(frozen, frozenPath);
            CheckLegacyBar(legacy);
            var oldOverlay = legacy.Objects.OfType<ConduitPlus.Grasshopper.OverlayComponent>().Single();
            Require(oldOverlay.Params.Input.Count == 6 && oldOverlay.Params.Input[4] is ConduitPlus.Grasshopper.Param_TextOption { HasValue: false } && oldOverlay.Params.Input[5] is Param_Number { PersistentData.DataCount: 0 }, "Legacy View and Size must be appended and unset.");
            Require(oldOverlay.Params.Input[0].SourceCount > 0, "Legacy card wires lost.");
            var clean = new GH_Archive().CreateTopLevelNode("Overlay"); oldOverlay.Write(clean);
            Require(!((GH_IReader)clean).ItemExists("ConduitPlus.Layout"), "Legacy layout must be ignored.");
            var saved = new GH_Archive(); Require(saved.AppendObject(legacy, "Definition"), "Cannot save legacy fixture.");
            using var reopened = new GH_Document(); Require(saved.ExtractObject(reopened, "Definition"), "Cannot reopen legacy fixture.");
            DisplayLifecycleChecks.ArchiveClean(saved, "legacy roundtrip");
            CheckLegacyBar(reopened);
            Console.WriteLine("Frozen fixture: clean archive and roundtrip, six inputs, unset View and Size and card wires passed; 2 legacy Bar assertions passed (Threshold 1 and M01… keys before/after resave).");
        }
        Console.WriteLine("Constructing definition.");
        if (!args.Contains("--verify-only")) Build(path);
        using var definition = new GH_Document();
        var archive = new GH_Archive();
        Require(archive.ReadFromFile(path) && archive.ExtractObject(definition, "Definition"), "Cannot reopen definition.");
        DisplayLifecycleChecks.ArchiveClean(archive, path);
        Instances.DocumentServer.AddDocument(definition); definition.Enabled = true; GH_Document.EnableSolutions = true;
        try
        {
            definition.NewSolution(false);
            DisplayLifecycleChecks.DefinitionSolved();
            var messages = definition.Objects.OfType<IGH_ActiveObject>().SelectMany(o =>
                new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }.SelectMany(level => o.RuntimeMessages(level).Select(message => $"{o.Name}: {level}: {message}"))).ToArray();
            foreach (var message in messages) Console.WriteLine(message);
            // The small compact window can't hold all ten cards: Overlay must say so, and nothing else may warn.
            var overflow = messages.Where(m => m.StartsWith("Overlay: Warning: ", StringComparison.Ordinal) && (m.Contains(" cards hidden in ", StringComparison.Ordinal) || m.Contains(" items hidden in ", StringComparison.Ordinal))).ToArray();
            Require(messages.Length == overflow.Length, "Definition has runtime warnings/errors.");
            Require(args.Contains("--compact") ? overflow.Length == 1 : overflow.Length == 0,
                args.Contains("--compact") ? "The compact viewport must report its hidden cards." : "The full-size viewport must not hide cards.");
            var overlay = definition.Objects.OfType<ConduitPlus.Grasshopper.OverlayComponent>().Single();
            var sessions = (Array)Manager.GetMethod("InView", Internal)!.Invoke(null, [doc, view])!;
            var session = sessions.Cast<object>().SingleOrDefault(s => (Guid)Property(s, "Owner") == overlay.InstanceGuid);
            Require(session is not null, "Overlay did not publish a session for its InstanceGuid.");
            var theme = args.Contains("--theme") ? Theme.Presets.Single(t => string.Equals(t.Name, args[Array.IndexOf(args, "--theme") + 1], StringComparison.OrdinalIgnoreCase)) : Theme.Paper;
            OverlayManager.ReplaceTheme(overlay.InstanceGuid, theme);
            var cards = (CardSpec[])Property(session!, "Cards");
            Require(cards.Length == 10, $"Expected ten dashboard cards; got {cards.Length}: {string.Join(", ", cards.Select(c => c.Title))}.");
            Console.WriteLine($"Reopened {path}: {definition.ObjectCount} objects, 0 warnings/errors; owner {overlay.InstanceGuid}, {cards.Length} cards.");
            var truss = DemoTruss.Create(bays: 4);
            // GH preview normally draws the members. Reuse the command demo's base-wire mode headlessly.
            var links = (Link[])session!.GetType().GetField("_links", Instance)!.GetValue(session)!;
            Require(links.Length == 1 && links[0].Keys.Count == truss.Members.Length, "Expected a keyed Link from the GH definition.");
            foreach (var link in links) typeof(Link).GetProperty("ShowBase")!.SetValue(link, true);
            definition.PreviewMode = GH_PreviewMode.Disabled;
            view.ActiveViewport.SetCameraLocations(new(12, 0, 1.5), new(31, -32, 17));
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(new Point3d(-8, -10, -8), new Point3d(32, 10, 11)));
            view.Redraw();
            var result = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS") ?? throw new InvalidOperationException("Set CONDUITPLUS_RESULTS.");
            Directory.CreateDirectory(result);
            File.WriteAllText(Path.Combine(result, "example.json"), CardJson.Serialize(new { path, objects = definition.ObjectCount, warnings = messages, owner = overlay.InstanceGuid, cards = cards.Select(c => c.Title), view = view.ActiveViewport.Size, truss.MaxUtilisation, truss.MaxDeflection, truss.Mass, truss.Passing, members = truss.Members.Length }));
            ExportInputChecks.Run(doc, view);
            if (args.Contains("--exports"))
            {
                var scale = int.Parse(args[Array.IndexOf(args, "--scale") + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (args.Contains("--compact")) ExportChecks.RunCompact(doc, view, session!, cards, result, scale, args[Array.IndexOf(args, "--state") + 1]);
                else ExportChecks.Run(doc, view, session!, cards, result, args.Contains("--safe"), scale);
            }
        }
        finally { Instances.DocumentServer.RemoveDocument(definition); }
    }
    private static void CheckLegacyBar(GH_Document definition)
    {
        definition.Enabled = true;
        definition.NewSolution(false);
        var bar = definition.Objects.OfType<ConduitPlus.Grasshopper.BarComponent>().Single();
        var card = bar.Params.Output[0].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<CardSpec>().Single();
        Require(card.Title == "Member utilisation" && card.HasThreshold && card.Threshold == 1 && card.Keys.SequenceEqual(Enumerable.Range(1, 17).Select(i => $"M{i:00}")), $"Frozen legacy Bar: title={card.Title}, threshold={card.Threshold}, hasThreshold={card.HasThreshold}, keys={string.Join(",", card.Keys)}.");
    }
    private static void Build(string path)
    {
        using var gh = new GH_Document();
        var span = Slider("Span (m)", 24, 12, 36, 50, 200); var load = Slider("Load (kN/m)", 12, 0, 24, 50, 260);
        var truss = Component(209, 360, 240); Wire(truss, 0, span); Wire(truss, 3, load);
        // Four bays keep every card visible even in a 200% DPI viewport. The component default is eight.
        ((Param_Integer)truss.Params.Input[1]).PersistentData.Clear();
        ((Param_Integer)truss.Params.Input[1]).PersistentData.Append(new GH_Integer(4));
        var bar = Component(202, 740, 120); Wire(bar, 0, truss.Params.Output[3]); Wire(bar, 1, truss.Params.Output[2]); Wire(bar, 2, truss.Params.Output[1]);
        var table = Component(203, 740, 310); Text(table, 0, "Member", "Type", "Util.", "Length (m)"); Wire(table, 1, truss.Params.Output[6]); Wire(table, 2, truss.Params.Output[1]); Text(table, 3, "Member results");
        var metrics = new List<GH_Component>();
        string[] titles = ["Max utilisation", "Max deflection", "Total mass", "Members passing"]; string[] units = ["ratio", "mm", "t", ""];
        int[] outputs = [7, 8, 5, 9];
        for (var i = 0; i < 4; i++)
        {
            var metric = Component(201, 740, 490 + i * 110); Wire(metric, 0, truss.Params.Output[outputs[i]]); Text(metric, 1, titles[i]); Text(metric, 2, units[i]); metrics.Add(metric);
        }
        var line = Component(206, 740, 980); Wire(line, 0, truss.Params.Output[4]); Wire(line, 1, truss.Params.Output[10]); Text(line, 3, "Deflection along span (mm)");
        var legend = Component(208, 740, 1190); Text(legend, 6, "Utilisation scale");
        ((Param_Colour)legend.Params.Input[0]).PersistentData.AppendRange(new[] { ColorTranslator.FromHtml("#1F8A5B"), ColorTranslator.FromHtml("#E5B63D"), ColorTranslator.FromHtml("#C2334D") }.Select(c => new GH_Colour(c)));
        var min = Slider("Scale minimum", 0, 0, 0, 360, 1130); var max = Slider("Scale maximum", 1.2m, 1.2m, 1.2m, 360, 1190);
        Wire(legend, 2, min); Wire(legend, 3, max); Text(legend, 4, "ratio");
        var note = Component(207, 740, 1410); Text(note, 0, "Illustrative example"); Text(note, 1, "Illustrative results. Click a bar or a member.\nChange Span or Load in Grasshopper.\nNot structural analysis or design verification.");
        var link = Component(205, 1020, 1680); Wire(link, 0, truss.Params.Output[0]); Wire(link, 1, truss.Params.Output[1]);
        var overlay = Component(204, 1280, 760);
        GH_Component[] cards = [.. metrics, bar, table, line, legend, note];
        // Set slots explicitly for the example arrangement.
        Slot[] slots = [Slot.Tl, Slot.Tl, Slot.Tl, Slot.Tl, Slot.Tr, Slot.Tr, Slot.Tl, Slot.Tr, Slot.Tl];
        for (var i = 0; i < cards.Length; i++) Text(cards[i], cards[i].Params.Input.FindIndex(p => p.Name == "Slot"), slots[i].ToString().ToLowerInvariant());
        var title = new ConduitPlus.Grasshopper.TitleComponent(); title.CreateAttributes(); title.Attributes.Pivot = new PointF(740, 1550); gh.AddObject(title, false);
        Text(title, 0, "Truss utilisation review"); Text(title, 1, "4 bays · 24 m span"); Text(title, 2, "Illustrative results; not structural analysis or design verification."); Text(title, 3, "tc");
        cards = [.. cards, title];
        overlay.Params.Input[0].DataMapping = GH_DataMapping.Flatten;
        overlay.Params.Input[1].DataMapping = GH_DataMapping.Flatten;
        foreach (var card in cards) Wire(overlay, 0, card.Params.Output[0]); Wire(overlay, 1, link.Params.Output[0]);
        Group("1  Explore span and load · illustrative data only", [span, load, truss]);
        Group("2  Result cards · double-click inputs to inspect constants", cards);
        Group("3  Keyed geometry → viewport", [link, overlay]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var archive = new GH_Archive(); Require(archive.AppendObject(gh, "Definition") && archive.WriteToFile(path, true, false), "Cannot save example.");
        Console.WriteLine("Generated " + path);

        T Add<T>(T obj, float x, float y) where T : IGH_DocumentObject
        { obj.CreateAttributes(); obj.Attributes.Pivot = new(x, y); gh.AddObject(obj, false); return obj; }
        GH_Component Component(int suffix, float x, float y) => Add((GH_Component)(Instances.ComponentServer.EmitObject(new Guid($"a7f32adb-ce5f-49c8-a33f-e3834a0ae{suffix}")) ?? throw new InvalidOperationException("Component not registered.")), x, y);
        GH_NumberSlider Slider(string title, decimal value, decimal minimum, decimal maximum, float x, float y)
        {
            var obj = (GH_NumberSlider)(Instances.ComponentServer.EmitObject(new Guid("57da07bd-ecab-415d-9d86-af36d7073abc")) ?? throw new InvalidOperationException("Slider not registered."));
            obj.NickName = title; obj.Slider.Minimum = minimum; obj.Slider.Maximum = maximum; obj.Slider.DecimalPlaces = 1; obj.SetSliderValue(value); return Add(obj, x, y);
        }
        void Group(string title, IEnumerable<IGH_DocumentObject> objects)
        {
            var group = Add(new GH_Group { NickName = title, Colour = Color.FromArgb(35, 100, 130, 160) }, 0, 0);
            foreach (var obj in objects) group.AddObject(obj.InstanceGuid);
        }
    }
    private static void Wire(GH_Component target, int input, IGH_Param source) => target.Params.Input[input].AddSource(source);
    private static void Register(Assembly assembly) => typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(Assembly), typeof(string), typeof(GH_LoadingMechanism)], null)!
        .Invoke(Instances.ComponentServer, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
    private static void Text(GH_Component target, int input, params string[] values)
    {
        var parameter = (Param_String)target.Params.Input[input]; parameter.PersistentData.Clear();
        parameter.PersistentData.AppendRange(values.Select(v => new GH_String(v)));
    }
    private static object Property(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

