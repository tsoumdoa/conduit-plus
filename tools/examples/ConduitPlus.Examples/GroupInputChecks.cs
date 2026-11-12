using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class GroupInputChecks
{
    internal static void Run()
    {
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        var bar = Add(new BarComponent());
        ((Param_Number)bar.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Number(1), new GH_Number(2) });
        Text(bar, 2, "first", "second");
        var members = Add(new Param_String());
        members.PersistentData.AppendRange(new[] { new GH_String("b"), new GH_String("a"), new GH_String("b") }, new GH_Path(4, 2));
        members.PersistentData.EnsurePath(new GH_Path(9));
        bar.Params.Input[7].AddSource(members); gh.NewSolution(false);
        var card = Card(bar);
        Require(card.Groups.Count == 2 && card.Groups["first"].Members.SequenceEqual(new[] { "a", "b" }) && card.Groups["second"].Members.IsEmpty,
            "GH must preserve empty Members branches and map sorted path order.");
        var metric = Add(new MetricComponent()); Text(metric, 5, "one"); gh.NewSolution(false);
        Require(Card(metric).Keys.SequenceEqual(new[] { "one" }) && Card(metric).Groups.Count == 0, "Metric Key alone must target a member.");
        metric.Params.Input[6].AddSource(members); SolveError(metric, "2 branches");
        ((Param_String)metric.Params.Input[5]).PersistentData.Clear(); metric.Params.Input[5].ExpireSolution(false); SolveError(metric, "requires");
        Text(metric, 5, "one");
        members.PersistentData.Clear(); members.PersistentData.EnsurePath(new GH_Path(7, 3));
        members.ExpireSolution(false); gh.NewSolution(false);
        Require(Card(metric).Groups["one"].Members.IsEmpty, "Metric empty branch must remain a zero-member group.");
        Require(bar.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m => m.Contains("1 branches", StringComparison.Ordinal)), "Bar must reject a branch mismatch.");
        members.PersistentData.Append(new GH_String(" "), new GH_Path(7, 3)); members.ExpireSolution(false); SolveError(metric, "path {7;3}, item [0]");
        members.PersistentData.Clear(); members.ExpireSolution(false); SolveError(bar, "0 branches");
        members.PersistentData.EnsurePath(new GH_Path(0)); members.PersistentData.EnsurePath(new GH_Path(1));
        members.ExpireSolution(false); ((Param_String)bar.Params.Input[2]).PersistentData.Clear(); bar.Params.Input[2].ExpireSolution(false); SolveError(bar, "explicit Keys");
        var legend = Add(new LegendComponent());
        ((Param_Colour)legend.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Colour(Color.Blue), new GH_Colour(Color.Red) });
        var min = Add(new Param_Number()); min.PersistentData.Append(new GH_Number(0));
        var max = Add(new Param_Number()); max.PersistentData.Append(new GH_Number(1));
        legend.Params.Input[2].AddSource(min); legend.Params.Input[3].AddSource(max); legend.Params.Input[8].AddSource(members);
        SolveError(legend, "Gradient Legend does not accept Members");
        Require(bar.Params.Input.Select(p => p.Name).SequenceEqual(new[] { "Values", "Labels", "Keys", "Title", "Threshold", "Slot", "Colors", "Members", "Good when", "Sort", "Visible rows" }), "Bar inputs must retain their original order with PR D appended.");
        Require(metric.Params.Input.Select(p => p.Name).SequenceEqual(new[] { "Value", "Title", "Unit", "Slot", "Decimals", "Key", "Members" }), "Metric input order changed.");
        Require(legend.Params.Input.Select(p => p.Name).SequenceEqual(new[] { "Colors", "Labels", "Min", "Max", "Unit", "Keys", "Title", "Slot", "Members" }), "Legend input order changed.");
        Console.WriteLine("GH group input checks passed: empty/path-ordered branches, wired empty tree, explicit Keys, Metric Key, diagnostics and gradient rejection.");

        void SolveError(GH_Component component, string text)
        {
            component.ExpireSolution(false); gh.NewSolution(false);
            Require(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m => m.Contains(text, StringComparison.Ordinal)),
                component.Name + " must report " + text + ": " + string.Join("; ", component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        }
    }
    private static CardSpec Card(GH_Component component) => component.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable() as CardSpec
        ?? throw new InvalidOperationException("Missing Card output from " + component.Name);
    private static void Text(GH_Component target, int input, params string[] values)
    {
        var param = (Param_String)target.Params.Input[input]; param.PersistentData.Clear();
        param.PersistentData.AppendRange(values.Select(v => new GH_String(v))); param.ExpireSolution(false);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
