using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class BarHostChecks
{
    internal static void Run()
    {
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        BarComponent Add() { var obj = new BarComponent(); obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        void Solve(BarComponent c) { c.ExpireSolution(false); gh.NewSolution(false); }
        CardSpec Card(BarComponent c) => c.Params.Output[0].VolatileData.AllData(true).Select(g => g?.ScriptVariable()).OfType<CardSpec>().Single();
        void Text(BarComponent c, int index, string? value)
        { var p = (Param_String)c.Params.Input[index]; p.PersistentData.Clear(); if (value is not null) p.PersistentData.Append(new GH_String(value)); p.ExpireSolution(false); }
        void Rows(BarComponent c, int value)
        { var p = (Param_Integer)c.Params.Input[10]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); p.ExpireSolution(false); }
        bool Error(BarComponent c, string message) => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Contains(message);
        var bar = Add();
        ((Param_Number)bar.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Number(-4), new GH_Number(0), new GH_Number(12.5) });
        Solve(bar);
        Check(bar.Params.Input.Count == 11 && bar.Params.Input[8] is Param_TextOption && bar.Params.Input[8].Name == "Good when" &&
            bar.Params.Input[8].NickName == "Gw" && bar.Params.Input[9] is Param_TextOption && bar.Params.Input[9].Name == "Sort" &&
            bar.Params.Input[9].NickName == "O" && bar.Params.Input[10].Name == "Visible rows" && bar.Params.Input[10].NickName == "N", "Bar appended input contract.");
        Check(Card(bar).MaxRows == 12 && Card(bar).GoodWhen == BarGoodWhen.Below && Card(bar).BarSort == BarOrder.Original && !Card(bar).HasThreshold, "Fresh Bar neutral defaults.");
        ((Param_TextOption)bar.Params.Input[8]).ApplyOption(InputOptions.BarGoodWhen[1]);
        ((Param_TextOption)bar.Params.Input[9]).ApplyOption(InputOptions.BarSort[1]); Rows(bar, 2);
        ((Param_Number)bar.Params.Input[4]).PersistentData.Append(new GH_Number(0)); bar.Params.Input[4].ExpireSolution(false);
        Text(bar, 2, null);
        ((Param_String)bar.Params.Input[2]).PersistentData.AppendRange(new[] { new GH_String("delta"), new GH_String("exact"), new GH_String("positive") });
        var members = (Param_String)bar.Params.Input[7];
        for (var i = 0; i < 3; i++) members.PersistentData.Append(new GH_String($"member{i}"), new GH_Path(i));
        members.ExpireSolution(false); bar.Params.Input[2].ExpireSolution(false); Solve(bar);
        Check(Card(bar).GoodWhen == BarGoodWhen.Above && Card(bar).BarSort == BarOrder.WorstFirst && Card(bar).MaxRows == 2 && Card(bar).HasThreshold, "Right-click options and persistent Visible rows apply.");
        Check(Card(bar).Groups["delta"].Members.SequenceEqual(new[] { "member0" }) && Card(bar).Keys[0] == "delta", "Group identity remains attached to sorted Bar rows.");
        var archive = new GH_Archive(); var chunk = archive.CreateTopLevelNode("Bar"); bar.Write(chunk);
        var restored = Add(); restored.Read((GH_IReader)chunk); Solve(restored);
        Check(Card(restored).GoodWhen == BarGoodWhen.Above && Card(restored).BarSort == BarOrder.WorstFirst && Card(restored).MaxRows == 2 && Card(restored).Keys[0] == "delta", "Bar options and persistent keys roundtrip.");
        Check(InputOptions.BarGoodWhen.Select(o => o.Label).SequenceEqual(new[] { "Below", "Above", "Within" }), "Good when menu order.");
        ((Param_TextOption)bar.Params.Input[8]).ApplyOption(InputOptions.BarGoodWhen[2]); Solve(bar);
        Check(Card(bar).GoodWhen == BarGoodWhen.Within && bar.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains("Within needs a positive Threshold (the tolerance)."), "Within menu choice and zero tolerance warning.");
        foreach (var tolerance in new[] { -5d, 5d })
        {
            var parameter = (Param_Number)bar.Params.Input[4]; parameter.PersistentData.Clear(); parameter.PersistentData.Append(new GH_Number(tolerance)); parameter.ExpireSolution(false);
            Text(bar, 8, " wItHiN "); Solve(bar);
            Check(Card(bar).GoodWhen == BarGoodWhen.Within && Card(bar).Threshold == tolerance && !bar.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Within accepts typed text and nonpositive tolerance without errors.");
            Check(bar.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains("Within needs a positive Threshold (the tolerance).") == (tolerance <= 0), "Within tolerance warning level and recovery.");
        }
        var withinArchive = new GH_Archive(); var withinChunk = withinArchive.CreateTopLevelNode("WithinBar"); bar.Write(withinChunk);
        var withinRestored = Add(); withinRestored.Read((GH_IReader)withinChunk); Solve(withinRestored);
        Check(Card(withinRestored).GoodWhen == BarGoodWhen.Within && Card(withinRestored).Threshold == 5, "Within options roundtrip.");
        Text(bar, 8, "higher"); Solve(bar); Check(Error(bar, "Good when must be Below, Above or Within."), "Good when error text.");
        Text(bar, 8, " above "); Text(bar, 9, "descending"); Solve(bar); Check(Error(bar, "Sort must be Original, Worst first or Best first."), "Sort error text.");
        Text(bar, 9, " best FIRST "); Rows(bar, 0); Solve(bar);
        Check(Card(bar).GoodWhen == BarGoodWhen.Above && Card(bar).BarSort == BarOrder.BestFirst && TableScroll.Visible(Card(bar)) == 3, "Case-insensitive trimmed inputs and zero means all.");
        Rows(bar, -1); Solve(bar); Check(Error(bar, "Visible rows must be 0–200."), "Visible rows lower-bound error text.");
        Rows(bar, 201); Solve(bar); Check(Error(bar, "Visible rows must be 0–200."), "Visible rows upper-bound error text.");
        // Simulate a saved Bar predating PR D, including a stored threshold and legacy generated keys.
        var old = Add(); ((Param_Number)old.Params.Input[0]).PersistentData.Append(new GH_Number(4));
        ((Param_Number)old.Params.Input[4]).PersistentData.Append(new GH_Number(1)); Text(old, 3, "Stored Bar");
        for (var i = 10; i >= 8; i--) old.Params.UnregisterInputParameter(old.Params.Input[i], false);
        var legacyArchive = new GH_Archive(); var legacyChunk = legacyArchive.CreateTopLevelNode("LegacyBar"); old.Write(legacyChunk);
        legacyChunk.RemoveItem("ConduitPlus.DefaultsVersion"); gh.RemoveObject(old, false);
        var legacy = Add(); legacy.Read((GH_IReader)legacyChunk); Solve(legacy);
        Check(legacy.Params.Input.Count == 11 && Card(legacy).MaxRows == 12 && Card(legacy).GoodWhen == BarGoodWhen.Below && Card(legacy).BarSort == BarOrder.Original, "Legacy archive restores appended Bar defaults, including new 12-row limit.");
        Check(Card(legacy).Keys[0] == "M01" && Card(legacy).Title == "Stored Bar" && Card(legacy).Threshold == 1 && Card(legacy).HasThreshold, "Legacy Bar persistent values and generated keys preserved.");
        var resaved = new GH_Archive(); var resavedChunk = resaved.CreateTopLevelNode("ResavedBar"); legacy.Write(resavedChunk);
        var again = Add(); again.Read((GH_IReader)resavedChunk); Solve(again);
        Check(Card(again).MaxRows == 12 && Card(again).Keys[0] == "M01", "Legacy Bar resave retains defaults.");
        Check(new[] { archive, legacyArchive, resaved }.All(a => a.Messages is null || !a.Messages.Any(m => m.Type == GH_Message_Type.error)), "Bar archives clean without missing-input messages.");
        Console.WriteLine($"PR D Bar host checks passed: {count} assertions.");
    }
}
