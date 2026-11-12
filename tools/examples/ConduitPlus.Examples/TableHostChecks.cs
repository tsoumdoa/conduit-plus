using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class TableHostChecks
{
    internal static void Run()
    {
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        void Solve(GH_Component component) { component.ExpireSolution(false); gh.NewSolution(false); }
        CardSpec Card(TableComponent component) => component.Params.Output[0].VolatileData.AllData(true).Select(g => g?.ScriptVariable()).OfType<CardSpec>().Single();
        bool Has(TableComponent c, GH_RuntimeMessageLevel level, string message) => c.RuntimeMessages(level).Any(m => m.Contains(message, StringComparison.Ordinal));
        void Text(TableComponent c, int index, params string[] values) => Tree(c, index, [values]);
        void Tree(TableComponent c, int index, params string[][] branches)
        {
            var p = (Param_String)c.Params.Input[index]; p.PersistentData.Clear();
            for (var i = 0; i < branches.Length; i++)
            {
                var path = new GH_Path(i); p.PersistentData.EnsurePath(path);
                p.PersistentData.AppendRange(branches[i].Select(v => new GH_String(v)), path);
            }
            p.ExpireSolution(false);
        }
        var table = Add(new TableComponent()); Text(table, 0, "Room", "Area"); Solve(table);
        Check(table.Params.Input.Count == 9 && table.Params.Input[7].Name == "Columns" && table.Params.Input[7].NickName == "Co" &&
            table.Params.Input[7].Access == GH_ParamAccess.tree && table.Params.Input[8].Name == "Decimals" && table.Params.Input[8].NickName == "D", "Table appended input contract.");
        Check(table.Params.Input[1].Optional && Has(table, GH_RuntimeMessageLevel.Warning, "Set Rows or Columns."), "Optional Rows / neither warning.");
        // Appending branches out of order must still follow GH path order.
        var columns = (Param_String)table.Params.Input[7];
        columns.PersistentData.AppendRange(new[] { new GH_String("1.234"), new GH_String("1.231") }, new GH_Path(8));
        columns.PersistentData.AppendRange(new[] { new GH_String("Office"), new GH_String("Living") }, new GH_Path(2));
        columns.ExpireSolution(false); Solve(table);
        Check(Card(table).Rows.Select(r => r.Key).SequenceEqual(new[] { "Office", "Living" }) && Card(table).Rows[0].Cells[1] == "1.234", "Columns path-order transpose and first-column keys.");
        ((Param_Integer)table.Params.Input[8]).PersistentData.Append(new GH_Integer(2)); table.Params.Input[8].ExpireSolution(false);
        Text(table, 3, "Stored table"); Solve(table);
        Check(Card(table).Decimals == 2 && Card(table).Rows[0].Cells[1] == "1.234", "Decimals preserves raw data.");
        var saved = new GH_Archive(); var chunk = saved.CreateTopLevelNode("Table"); table.Write(chunk);
        var restored = Add(new TableComponent()); restored.Read((GH_IReader)chunk); Solve(restored);
        Check(restored.Params.Input[1].Optional && Card(restored).Decimals == 2 && Card(restored).Title == "Stored table" && Card(restored).Rows[0].Key == "Office", "New Table persistent Columns/Decimals roundtrip.");
        Check(saved.Messages is null || !saved.Messages.Any(m => m.Type == GH_Message_Type.error), "New Table archive clean.");
        Tree(table, 1, Array.Empty<string>()); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Use Rows or Columns, not both."), "Persistent empty Rows branch counts as supplied.");
        Tree(table, 1); Tree(table, 7, ["a", "b"]); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Columns has 1 branch but there are 2 Headers.") &&
            Has(table, GH_RuntimeMessageLevel.Error, "Entwine the column lists"), "Column count diagnostics and remedy.");
        Tree(table, 7, ["a", "b"], ["1"]); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "'Room' has 2 items, 'Area' has 1 item"), "Ragged Columns counts name headers.");
        Tree(table, 7); Tree(table, 1, ["Office", "1"], ["Office", "2"]); Solve(table);
        Check(Card(table).Rows.Select(r => r.Key).SequenceEqual(new[] { "1", "2" }) &&
            Has(table, GH_RuntimeMessageLevel.Remark, "First column repeats ('Office' ×2), so rows are keyed 1…n.") &&
            !table.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "Rows fallback remark.");
        Text(table, 2, "a"); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Key count must match row count."), "Explicit Keys count mismatch.");
        Text(table, 2, "a", "a"); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Keys repeat ('a' ×2)"), "Explicit Keys remain unique.");
        Tree(table, 2); var emptyKeys = Add(new Param_String()); table.Params.Input[2].AddSource(emptyKeys); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Key count must match row count."), "Connected-empty Keys must match rows.");
        table.Params.Input[2].RemoveAllSources();
        ((Param_Integer)table.Params.Input[8]).PersistentData.Clear(); ((Param_Integer)table.Params.Input[8]).PersistentData.Append(new GH_Integer(13));
        table.Params.Input[8].ExpireSolution(false); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Decimals must be 0–12."), "Decimals range diagnostic.");
        ((Param_Integer)table.Params.Input[8]).PersistentData.Clear(); table.Params.Input[8].ExpireSolution(false);
        Tree(table, 1, ["Office"]); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Rows must each have 2 cells"), "Ragged Rows counts.");
        Tree(table, 1); var empty = Add(new Param_String()); table.Params.Input[7].AddSource(empty); Solve(table);
        Check(Has(table, GH_RuntimeMessageLevel.Error, "Columns has 0 branches"), "Connected-empty Columns is supplied.");
        table.Params.Input[7].RemoveAllSources(); Tree(table, 7, [], []); Solve(table);
        Check(Card(table).Rows.Length == 0, "Empty column branches produce a valid empty table.");

        var old = Add(new TableComponent()); Text(old, 0, "Room", "Area"); Tree(old, 1, ["Office", "1.234"], ["Living", "2.345"]);
        Text(old, 2, "saved-a", "saved-b"); Text(old, 3, "Legacy title");
        ((Param_TextOption)old.Params.Input[4]).PersistentData.Append(new GH_String("br"));
        ((Param_Integer)old.Params.Input[5]).PersistentData.Clear(); ((Param_Integer)old.Params.Input[5]).PersistentData.Append(new GH_Integer(3));
        ((Param_Number)old.Params.Input[6]).PersistentData.Clear(); ((Param_Number)old.Params.Input[6]).PersistentData.Append(new GH_Number(512));
        old.Params.Input[1].Optional = false;
        old.Params.UnregisterInputParameter(old.Params.Input[8], false); old.Params.UnregisterInputParameter(old.Params.Input[7], false);
        var legacyArchive = new GH_Archive(); var legacyChunk = legacyArchive.CreateTopLevelNode("LegacyTable"); old.Write(legacyChunk);
        gh.RemoveObject(old, false);
        var legacy = Add(new TableComponent()); legacy.Read((GH_IReader)legacyChunk); Solve(legacy);
        var legacyCard = Card(legacy);
        Check(legacy.Params.Input.Count == 9 && legacy.Params.Input[1].Optional && legacyCard.Decimals is null, "Legacy Table restores optional Rows and unset appended inputs.");
        Check(legacyCard.Title == "Legacy title" && legacyCard.WiredSlot == Slot.Br && legacyCard.Width == 512 && legacyCard.MaxRows == 3 &&
            legacyCard.Rows.Select(r => r.Key).SequenceEqual(new[] { "saved-a", "saved-b" }) && legacyCard.Rows[0].Cells[1] == "1.234", "Legacy Table preserves all persistent values.");
        var resaved = new GH_Archive(); var resavedChunk = resaved.CreateTopLevelNode("ResavedTable"); legacy.Write(resavedChunk);
        var again = Add(new TableComponent()); again.Read((GH_IReader)resavedChunk); Solve(again);
        Check(Card(again).Rows[1].Key == "saved-b" && again.Params.Input[1].Optional, "Legacy Table resave roundtrip.");
        Check(new[] { legacyArchive, resaved }.All(a => a.Messages is null || !a.Messages.Any(m => m.Type == GH_Message_Type.error)), "Legacy Table archive clean.");
        Tree(legacy, 2); Solve(legacy);
        Check(Card(legacy).Rows[0].Key == "Office", "Legacy Rows without explicit Keys adopt first-column keys.");
        Console.WriteLine($"PR C Table host checks passed: {count} assertions.");
    }
}
