using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class InputTrapChecks
{
    internal static void Run()
    {
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        var count = 0;
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        void Solve(GH_Component component) { component.ExpireSolution(false); gh.NewSolution(false); }
        CardSpec[] Cards(GH_Component component) => component.Params.Output[0].VolatileData.AllData(true).Select(g => g?.ScriptVariable()).OfType<CardSpec>().ToArray();
        CardSpec Card(GH_Component component) => Cards(component).Single();
        bool Has(GH_Component c, GH_RuntimeMessageLevel level, string message) => c.RuntimeMessages(level).Any(m => m.Contains(message, StringComparison.Ordinal));
        void Text(GH_Component c, int index, params string[] values)
        { var p = (Param_String)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); p.ExpireSolution(false); }
        void Numbers(GH_Component c, int index, params double[] values)
        { var p = (Param_Number)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_Number(v))); p.ExpireSolution(false); }
        var bar = Add(new BarComponent()); Numbers(bar, 0, 4, 8); Solve(bar);
        Check(Card(bar).Title == "Bar" && Card(bar).Labels.SequenceEqual(new[] { "1", "2" }) && !Card(bar).HasThreshold, "New Bar defaults.");
        Text(bar, 1, "Bath", "Bath"); Solve(bar);
        Check(Card(bar).Keys.SequenceEqual(new[] { "1", "2" }) && !bar.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Repeated labels without Keys.");
        Text(bar, 2, "Bath", "Bath"); Solve(bar);
        Check(Has(bar, GH_RuntimeMessageLevel.Error, "Keys repeat ('Bath' ×2)"), "Repeated explicit keys.");
        Text(bar, 1); Text(bar, 2); Numbers(bar, 4, 1); Text(bar, 3, "Stored title"); Solve(bar);
        var archive = new GH_Archive(); var chunk = archive.CreateTopLevelNode("Bar"); bar.Write(chunk);
        var restored = Add(new BarComponent()); restored.Read((GH_IReader)chunk); Solve(restored);
        Check(Card(restored).Title == "Stored title" && Card(restored).Threshold == 1 && Card(restored).HasThreshold && Card(restored).Keys[0] == "1", "New defaults marker and persistent values roundtrip.");
        chunk.RemoveItem("ConduitPlus.DefaultsVersion");
        var legacy = Add(new BarComponent()); legacy.Read((GH_IReader)chunk); Solve(legacy);
        Check(Card(legacy).Keys.SequenceEqual(new[] { "M01", "M02" }) && Card(legacy).Title == "Stored title" && Card(legacy).Threshold == 1, "Unmarked Bar retains legacy generated keys and saved values.");
        var legacyChunk = new GH_Archive().CreateTopLevelNode("Legacy"); legacy.Write(legacyChunk);
        var legacyAgain = Add(new BarComponent()); legacyAgain.Read((GH_IReader)legacyChunk); Solve(legacyAgain);
        Check(Card(legacyAgain).Keys[0] == "M01", "Legacy defaults survive resaving.");
        Check(archive.Messages is null || !archive.Messages.Any(m => m.Type == GH_Message_Type.error), "Default migration archive clean.");
        Text(bar, 5, "top-left"); Solve(bar);
        Check(Has(bar, GH_RuntimeMessageLevel.Warning, "Unknown slot 'top-left'; use tl, tr, bl, br, lc or rc."), "Unknown Slot warning.");
        Text(bar, 5);
        var line = Add(new LineComponent()); Numbers(line, 0, 1, 2); Numbers(line, 4, 1.5); Solve(line);
        Check(Card(line).Title == "Line" && Card(line).Threshold == 1.5, "Internalised Line threshold.");
        Text(line, 1, "A", "A"); Solve(line);
        Check(Card(line).Keys.SequenceEqual(new[] { "1", "2" }), "Repeated Line labels.");
        var title = Add(new TitleComponent()); Solve(title);
        Check(Card(title).Title == "Title" && !title.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Fresh Title is valid.");
        var legend = Add(new LegendComponent());
        ((Param_Colour)legend.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Colour(Color.Blue), new GH_Colour(Color.Red) });
        Numbers(legend, 2, 0); Numbers(legend, 3, 10); Solve(legend);
        Check(Card(legend).Min == 0 && Card(legend).Max == 10, "Internalised Legend gradient bounds.");
        Numbers(legend, 3); Text(legend, 1, "Low", "High"); Solve(legend);
        Check(Card(legend).Min is null && Has(legend, GH_RuntimeMessageLevel.Warning, "Min is set but Max isn't"), "One-bound swatch fallback.");
        Text(legend, 1); Solve(legend);
        Check(Has(legend, GH_RuntimeMessageLevel.Error, "Legend has 2 colours and 0 labels."), "One-bound mismatched labels error.");
        var metric = Add(new MetricComponent()); Text(metric, 0, "1", "2", "3"); Text(metric, 1, "One", "Two", "Three"); Text(metric, 2, "m", "mm", "kg"); Solve(metric);
        var metrics = Cards(metric);
        Check(metrics.Length == 3 && metrics.Select(c => c.Id).Distinct().Count() == 3 && metrics[0].Id == metric.InstanceGuid.ToString("N") && metrics[2].Id.EndsWith(":2", StringComparison.Ordinal), "Metric list positional identities.");
        Check(metrics.Select(c => c.Title).SequenceEqual(new[] { "One", "Two", "Three" }) && metrics.Select(c => c.Unit).SequenceEqual(new[] { "m", "mm", "kg" }), "Native Metric Title/Unit matching.");
        Text(metric, 5, "group"); ((Param_String)metric.Params.Input[6]).PersistentData.Append(new GH_String("a"), new GH_Path(0)); metric.Params.Input[6].ExpireSolution(false); Solve(metric);
        Check(Cards(metric).Length == 0 && Has(metric, GH_RuntimeMessageLevel.Error, "Members needs a single Value; use one Metric per group."), "Multi-run grouped Metric rejects all outputs: " + Cards(metric).Length + " cards, " + string.Join("; ", metric.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var branched = Add(new BarComponent()); var values = (Param_Number)branched.Params.Input[0];
        values.PersistentData.Append(new GH_Number(1), new GH_Path(0)); values.PersistentData.Append(new GH_Number(2), new GH_Path(1)); Solve(branched);
        Check(Cards(branched).Length == 2 && Has(branched, GH_RuntimeMessageLevel.Warning, "Flatten Values for one chart."), "One-item branch warning and unique Bar cards.");
        var branchedLine = Add(new LineComponent());
        var lineValues = (Param_Number)branchedLine.Params.Input[0];
        lineValues.PersistentData.Append(new GH_Number(1), new GH_Path(0)); lineValues.PersistentData.Append(new GH_Number(2), new GH_Path(1)); Solve(branchedLine);
        Check(Has(branchedLine, GH_RuntimeMessageLevel.Warning, "Flatten Values for one chart.") && Has(branchedLine, GH_RuntimeMessageLevel.Error, "at least two samples"), "One-item Line branches warn with a flattening remedy and retain minimum sample validation.");
        var link = Add(new LinkComponent()); var geometry = (Param_GenericObject)link.Params.Input[0];
        geometry.PersistentData.AppendRange(new[] { new GH_ObjectWrapper(new Point3d(0, 0, 0)), new GH_ObjectWrapper(new Point3d(1, 0, 0)) }); Solve(link);
        var linked = (Link)link.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Check(linked.Keys.SequenceEqual(new[] { "1", "2" }) && linked.ToString() == "Link · 2 keys · 2 objects", "Link per-item defaults and readable summary.");
        geometry.PersistentData.Clear(); geometry.PersistentData.Append(new GH_ObjectWrapper(new Point3d(0, 0, 0)), new GH_Path(0)); geometry.PersistentData.Append(new GH_ObjectWrapper(new Point3d(1, 0, 0)), new GH_Path(1)); geometry.ExpireSolution(false); Solve(link);
        Check(((Link)link.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).Keys.SequenceEqual(new[] { "1", "2" }), "Link per-branch defaults.");
        var input = Add(new Param_GenericObject());
        input.PersistentData.AppendRange(new[] { new GH_ObjectWrapper(metrics[0]), new GH_ObjectWrapper(null), new GH_ObjectWrapper(5), new GH_ObjectWrapper(metrics[0]), new GH_ObjectWrapper(metrics[1]) });
        var overlay = Add(new OverlayComponent()); overlay.Params.Input[0].AddSource(input); Solve(overlay);
        Check(Has(overlay, GH_RuntimeMessageLevel.Warning, "are empty because their components have errors") && Has(overlay, GH_RuntimeMessageLevel.Error, "Cards item 2") && Has(overlay, GH_RuntimeMessageLevel.Error, "Cards item [3]"), "Overlay diagnoses null, foreign and duplicate siblings.");
        var originals = (CardSpec[])typeof(OverlayComponent).GetField("_originals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
        Check(originals.Length == 2, "Overlay keeps valid siblings and first duplicate.");
        var canvas = Add(new CanvasCardComponent()); canvas.Params.Input[0].AddSource(legend.Params.Output[0]); Solve(canvas);
        Check(Has(canvas, GH_RuntimeMessageLevel.Warning, "Showing 0 of 1 cards. 1 is empty because its component has an error (Legend).") && (string)typeof(CanvasCardComponent).GetProperty("Placeholder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)! == "No card received. Check upstream components.", "Canvas upstream error warning/placeholder: " + string.Join("; ", canvas.RuntimeMessages(GH_RuntimeMessageLevel.Warning)) + " / " + typeof(CanvasCardComponent).GetProperty("Placeholder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas));
        input.PersistentData.Clear(); input.ExpireSolution(false); Solve(overlay);
        Check(Has(overlay, GH_RuntimeMessageLevel.Warning, "Showing 0 of 1 cards"), "Connected-empty Overlay solves and removes stale cards.");
        Console.WriteLine($"PR A input trap checks passed: {count} assertions.");
    }
}
