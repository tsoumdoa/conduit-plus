using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class ColourHostChecks
{
    internal static void Run()
    {
        using var doc = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); doc.AddObject(obj, false); return obj; }
        var scale = Add(new ColourScaleComponent());
        var cat = Add(new ColourCategoriesComponent());
        var colPal = Add(new ColourPaletteComponent());
        doc.NewSolution(false);
        var expected = Palettes.Styles[0].Take(8);
        string[] Hex() => colPal.Params.Output[1].VolatileData.AllData(true).Cast<GH_String>().Select(s => s.Value).ToArray();
        Require(Hex().SequenceEqual(expected) && colPal.Params.Output[0].VolatileData.DataCount == 8,
            "Colour Palette must default to eight Programme colours.");
        Text(colPal, 0); doc.NewSolution(false);
        Require(colPal.Params.Input[0].Optional && Hex().SequenceEqual(expected) &&
            !colPal.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && !colPal.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(),
            "Cleared Style must still produce eight Programme colours without a generic collection error.");
        var count = (Param_Integer)colPal.Params.Input[1];
        foreach (var n in new[] { 0, -1, 257, int.MaxValue, 256, 8 })
        {
            count.PersistentData.Clear(); count.PersistentData.Append(new GH_Integer(n)); count.ExpireSolution(false);
            doc.NewSolution(false);
            var errors = colPal.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if (n is < 0 or > 256)
                Require(errors.SequenceEqual(new[] { "Count must be 0–256." }) && Hex().Length == 0,
                    "Invalid Count must report its range before producing outputs.");
            else
                Require(!errors.Any() && Hex().Length == n && colPal.Params.Output[0].VolatileData.DataCount == n &&
                    (n != 0 || !colPal.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any()),
                    "Valid Counts must produce matching Colours/Hex outputs; zero must be empty without an error or warning.");
        }
        var wiredScale = Add(new ColourScaleComponent());
        ((Param_Number)wiredScale.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Number(0), new GH_Number(1) });
        var styleSource = Add(new Param_String());
        wiredScale.Params.Input[1].AddSource(styleSource);
        foreach (var name in new[] { "Programme", "Categorical reversed", "Accent reversed" })
        {
            styleSource.PersistentData.Clear(); styleSource.PersistentData.Append(new GH_String(name)); styleSource.ExpireSolution(false);
            doc.NewSolution(false);
            Require(wiredScale.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(
                $"'{name}' is a category palette; using Blue. Choose a sequential, diverging or Status palette.") &&
                !wiredScale.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(),
                "A wired category palette must warn and fall back to Blue.");
        }
        Require(scale.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains("Connect Values (or set Min and Max).") &&
            cat.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains("Connect Categories (or set Order)."), "Unwired helpers must warn.");
        Require(!scale.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && !cat.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Unwired helpers must not error.");
        Require(!((Param_TextOption)scale.Params.Input[1]).Options.Any(o => o.Label.StartsWith("Categorical")), "Scale must not offer Categorical.");
        var numbers = (Param_Number)scale.Params.Input[0];
        numbers.PersistentData.AppendRange(new[] { new GH_Number(1), new GH_Number(2) }, new GH_Path(2, 3));
        numbers.PersistentData.EnsurePath(new GH_Path(7)); numbers.ExpireSolution(false);
        Text(scale, 6, "first", "second"); doc.NewSolution(false);
        Require(scale.Params.Output[0].VolatileData.DataCount == 2 && scale.Params.Output[1].VolatileData.DataCount == 1,
            $"Multiple scalar values must not duplicate colours or legends: {scale.Params.Output[0].VolatileData.DataCount} colours, {scale.Params.Output[1].VolatileData.DataCount} legends; {string.Join("; ", scale.RuntimeMessages(GH_RuntimeMessageLevel.Error))}");
        Require(scale.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(s => s.Contains("makes one legend per component")), "Scale must diagnose extra iterations.");
        Text(scale, 6, "single");
        var palette = (Param_String)scale.Params.Input[1]; palette.PersistentData.Clear();
        palette.PersistentData.Append(new GH_String("#000000"), new GH_Path(0));
        palette.PersistentData.Append(new GH_String("#FFFFFF"), new GH_Path(1)); palette.ExpireSolution(false);
        Text(cat, 0, "A", "B"); Text(cat, 1, "a", "b");
        var order = (Param_String)cat.Params.Input[3];
        order.PersistentData.Append(new GH_String("B"), new GH_Path(0));
        order.PersistentData.Append(new GH_String("A"), new GH_Path(1)); order.ExpireSolution(false);
        var categoryPalette = (Param_String)cat.Params.Input[2];
        categoryPalette.PersistentData.Append(new GH_String("#123456"), new GH_Path(0));
        categoryPalette.PersistentData.Append(new GH_String("#ABCDEF"), new GH_Path(1)); categoryPalette.ExpireSolution(false);
        doc.NewSolution(false);
        Require(scale.Params.Output[0].VolatileData.DataCount == 2 && scale.Params.Output[1].VolatileData.DataCount == 1,
            "A multi-branch custom Palette must be flattened into a single scale.");
        Require(!scale.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Flattened Palette must not error.");
        Require(cat.Params.Output[0].VolatileData.DataCount == 2 && cat.Params.Output[1].VolatileData.DataCount == 1,
            "Multi-branch Order must produce one categorical legend.");
        Require(Card(cat, 1).Colors.SequenceEqual(new[] { "#123456", "#ABCDEF" }) &&
            !cat.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Categories must flatten multi-branch Palette.");
        var bar = Add(new BarComponent());
        ((Param_Number)bar.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Number(10), new GH_Number(20) });
        foreach (var (input, output) in new[] { (1, 2), (2, 4), (6, 3), (7, 5) }) bar.Params.Input[input].AddSource(cat.Params.Output[output]);
        doc.NewSolution(false);
        var card = Card(bar, 0);
        Require(card.Groups["Legend:A@" + cat.InstanceGuid.ToString("N")[..8]].Members.SequenceEqual(new[] { "a" }), "Grouped Bar A must select only a.");
        Require(card.Groups["Legend:B@" + cat.InstanceGuid.ToString("N")[..8]].Members.SequenceEqual(new[] { "b" }), "Grouped Bar B must select only b.");
        var originalKeys = Card(cat, 1).Keys;
        var archive = new GH_IO.Serialization.GH_LooseChunk("Colour");
        Require(cat.Write(archive), "Categories must archive.");
        var reopened = Add(new ColourCategoriesComponent());
        Require(reopened.Read(archive), "Categories must reopen.");
        reopened.ExpireSolution(false); doc.NewSolution(false);
        Require(Card(reopened, 1).Keys.SequenceEqual(originalKeys), "Reopen must preserve generated group keys.");
        reopened.NewInstanceGuid(); reopened.ExpireSolution(false); doc.NewSolution(false);
        Require(!Card(reopened, 1).Keys.Intersect(originalKeys).Any(), "A pasted component identity must produce independent group keys.");
        Text(cat, 4, "first", "second"); doc.NewSolution(false);
        Require(cat.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(s => s.Contains("makes one legend per component")) &&
            cat.Params.Output[0].VolatileData.DataCount == 2 && cat.Params.Output[1].VolatileData.DataCount == 1 && cat.Params.Output[5].VolatileData.DataCount == 2,
            "Categories must diagnose extra scalar iterations without merging Members.");
        Console.WriteLine("Colour GH checks passed: default/cleared Style, Count boundaries, wired categorical scale fallback, unwired warnings, menu, multi-item scalar guards, flattened Palette/Order, grouped Bar membership, archive/reopen and copy identity.");
    }
    private static CardSpec Card(GH_Component component, int output) => component.Params.Output[output].VolatileData.AllData(true).Single().ScriptVariable() as CardSpec
        ?? throw new InvalidOperationException("Missing Card from " + component.Name);
    private static void Text(GH_Component component, int input, params string[] values)
    {
        var p = (Param_String)component.Params.Input[input]; p.PersistentData.Clear();
        p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); p.ExpireSolution(false);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
