using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

internal static class ScopeFeedbackChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(string output)
    {
        using var gh = new GH_Document(); Instances.DocumentServer.AddDocument(gh);
        gh.Enabled = true; GH_Document.EnableSolutions = true;
        var scope = Add(new ScopeComponent()); var canvas = Add(new CanvasCardComponent());
        var filter = Add(new SelectionFilter(() => ((CanvasCardState)typeof(CanvasCardComponent).GetProperty("State", Internal)!.GetValue(canvas)!).Current.SelectedKeys.ToArray()));
        canvas.Params.Input[0].AddSource(filter.Params.Output[0]); canvas.Params.Input[1].AddSource(scope.Params.Output[0]);
        filter.Params.Input[0].AddSource(canvas.Params.Output[0]);
        var solutions = 0; gh.SolutionEnd += (_, _) => solutions++;
        gh.NewSolution(false);
        var state = (CanvasCardState)typeof(CanvasCardComponent).GetProperty("State", Internal)!.GetValue(canvas)!;
        Require(state.Spec is not null, "Feedback fixture did not produce a card.");
        state.Activate("a", BrushSelectMode.Toggle); state.Activate("b", BrushSelectMode.Toggle);
        var before = solutions;
        gh.NewSolution(false); // Committed user outputs feed the filter; first union prune leaves b.
        Require(state.Current.SelectedKeys.SequenceEqual(["b"]), "First feedback reconciliation should retain b.");
        gh.NewSolution(false); // One allowed reconciliation refresh; filter removes b too.
        Require(state.Current.SelectedKeys.Count == 0, "Second feedback reconciliation should clear b.");
        var binding = (ScopeBinding)typeof(CanvasCardComponent).GetField("_scope", Internal)!.GetValue(canvas)!;
        Require(binding.OutputRefreshWarning, "Second feedback prune did not warn.");
        Require(gh.ScheduleDelay < 0, "Feedback scheduled an unbounded successor.");
        Require(solutions == before + 2, "Feedback exceeded action solve plus one reconciliation refresh.");
        Require(canvas.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("next external solve", StringComparison.Ordinal)), "Feedback warning not visible on Canvas.");
        var record = new { actionAndRefreshSolutions = solutions - before, selection = state.Current.SelectedKeys,
            outputsAwaitExternalSolve = binding.OutputRefreshWarning, filterInputWiredToCanvasSelection = true, harnessSnapshotParameterBreaksGHEvaluationRecursion = true };
        File.WriteAllText(Path.Combine(output, "scope-feedback.json"), CardJson.Serialize(record));

        // A queued action callback becomes inert on close; serialized wires reopen with empty state.
        using var persisted = new GH_Document();
        var savedScope = new ScopeComponent(); savedScope.CreateAttributes(); persisted.AddObject(savedScope, false);
        var savedCanvas = new CanvasCardComponent(); savedCanvas.CreateAttributes(); persisted.AddObject(savedCanvas, false);
        savedCanvas.Params.Input[1].AddSource(savedScope.Params.Output[0]);
        var archive = new GH_Archive(); Require(archive.AppendObject(persisted, "Definition"), "Cannot archive scope wires.");
        binding.Publish([new CardSpec { Id = "late", Kind = CardKind.Text, Text = "pending" }], []);
        var oldToken = binding.Lease.Token; var broker = ScopeBroker.ForDocument(gh);
        gh.Context = GH_DocumentContext.Close;
        Require(broker.IsDisposed && !binding.IsValid, "Close retained a live Scope authority.");
        typeof(GH_Document).GetMethod("ScheduleCallDelegates", Internal)?.Invoke(gh, null);
        using var reopened = new GH_Document(); Require(archive.ExtractObject(reopened, "Definition"), "Cannot reopen feedback fixture.");
        Require(reopened.RuntimeID != oldToken.DocumentId, "Reopened document reused runtime identity.");
        Require(!ScopeBroker.ForDocument(reopened).IsValid(oldToken), "Old token survived document close/reopen.");
        reopened.Enabled = true; reopened.NewSolution(false);
        var reopenedCanvas = reopened.Objects.OfType<CanvasCardComponent>().Single();
        Require(reopenedCanvas.Params.Input[1].SourceCount == 1 && ((CanvasCardState)typeof(CanvasCardComponent).GetProperty("State", Internal)!.GetValue(reopenedCanvas)!).Current.SelectedKeys.Count == 0,
            "Reopened Scope wires did not start with empty selection.");
        Instances.DocumentServer.RemoveDocument(gh);
        Console.WriteLine("Scope feedback: Selection wired into card filter; two solutions maximum, second prune warns; close cancels queued authority; reopen rejects old token.");
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // GH rejects a direct recursive dependency before evaluating any display. This harness-only
    // snapshot parameter breaks evaluation recursion while preserving the actual Selection wire:
    // it samples the same authoritative snapshot that Canvas Selection will publish in this solution.
    // It does not schedule anything; the production broker alone controls refreshes.
    private sealed class SnapshotSelection(Func<string[]> read) : GH_Param<GH_String>("Selection", "S", "Snapshot feedback", "Tests", "Tests", GH_ParamAccess.list)
    {
        public override Guid ComponentGuid => new("e37dcaae-a0f7-43d0-89e8-6e58ef68b8f6");
        public override bool DependsOn(IGH_ActiveObject potentialSource) => false;
        protected override void CollectVolatileData_FromSources() => m_data.AppendRange(read().Select(k => new GH_String(k)), new GH_Path(0));
    }
    private sealed class SelectionFilter(Func<string[]> read) : GH_Component("Scope feedback filter", "Filter", "Harness selection feedback", "Tests", "Tests")
    {
        public override Guid ComponentGuid => new("b8a08c22-89c2-42eb-8588-02ed59162a10");
        protected override void RegisterInputParams(GH_InputParamManager p)
        { p.AddParameter(new SnapshotSelection(read), "Selection", "S", "Selected keys (snapshot feedback)", GH_ParamAccess.list); p[0].Optional = true; }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Card", "C", "Filtered card", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var selected = new List<string>(); da.GetDataList(0, selected);
            var keys = selected.Count == 0 ? new[] { "a", "b", "c" } : selected.Skip(1).ToArray();
            da.SetData(0, new GH_ObjectWrapper(new CardSpec { Id = "feedback", Kind = CardKind.Table, Headers = ["Key"], Rows = keys.Select(k => new TableRow(k, [k])).ToArray() }));
        }
    }
}
