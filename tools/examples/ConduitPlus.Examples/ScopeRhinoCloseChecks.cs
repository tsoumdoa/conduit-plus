using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;

internal static class ScopeRhinoCloseChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(RhinoDoc original, string output)
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        var view = doc.Views.Add("Owned scope close probe", DefinedViewportProjection.Top, new Rectangle(0, 0, 500, 400), true);
        if (view is null) throw new InvalidOperationException("Cannot create headless scope-close view.");
        using var gh = new GH_Document(); Instances.DocumentServer.AddDocument(gh);
        try
        {
            RhinoDoc.ActiveDoc = doc; doc.Views.ActiveView = view;
            var scope = Add(new ScopeComponent()); var canvas = Add(new CanvasCardComponent()); var overlay = Add(new OverlayComponent());
            var canvasSource = Source("canvas-only"); var overlaySource = Source("overlay-only");
            canvas.Params.Input[0].AddSource(canvasSource); canvas.Params.Input[1].AddSource(scope.Params.Output[0]);
            overlay.Params.Input[0].AddSource(overlaySource); overlay.Params.Input[3].AddSource(scope.Params.Output[0]);
            gh.Enabled = true; gh.NewSolution(false);
            var canvasBinding = (ScopeBinding)canvas.GetType().GetField("_scope", Internal)!.GetValue(canvas)!;
            var overlayBinding = (ScopeBinding)overlay.GetType().GetField("_scope", Internal)!.GetValue(overlay)!;
            Require(OverlayManager.Binding(overlay.InstanceGuid)?.DocumentSerial == doc.RuntimeSerialNumber, "Overlay did not bind owned Rhino document.");
            overlayBinding.Select("overlay-only", BrushSelectMode.Toggle, BrushTargetKind.Member, new(overlay.InstanceGuid, "model", "model"));
            doc.Dispose(); // Own headless Rhino document only; never touches the user's Rhino process.
            RhinoDoc.ActiveDoc = original;
            gh.NewSolution(false);
            Require(!canvasBinding.Current.Membership.MemberKeys.Contains("overlay-only") && canvasBinding.Current.Membership.MemberKeys.Contains("canvas-only"), "Closed Rhino contribution was republished by an output refresh.");
            Require(OverlayManager.Binding(overlay.InstanceGuid) is null && !overlayBinding.IsActive, $"Closed Rhino owner retained runtime interaction: binding={OverlayManager.Binding(overlay.InstanceGuid)}, active={overlayBinding.IsActive}, suspended={overlay.GetType().GetField("_rhinoSuspended", Internal)!.GetValue(overlay)}, interactionSolve={overlay.GetType().GetField("_interactionSolve", Internal)!.GetValue(overlay)}.");
            canvasBinding.Select("canvas-only", BrushSelectMode.Toggle, BrushTargetKind.Member, new(canvas.InstanceGuid, "canvas-only", "canvas")); gh.NewSolution(false);
            Require(OverlayManager.Binding(overlay.InstanceGuid) is null, "Queued Canvas interaction rebound closed Rhino owner.");
            overlay.ExpireSolution(false); gh.NewSolution(false);
            Require(OverlayManager.Binding(overlay.InstanceGuid)?.DocumentSerial == original.RuntimeSerialNumber, "Normal external data solve did not rebind Overlay.");
            File.WriteAllText(Path.Combine(output, "scope-rhino-close.json"), CardJson.Serialize(new
            { ownedHeadlessRhinoDocumentClosed = true, closedContributionRemoved = true, canvasSurvives = true, interactionDoesNotRebind = true, normalDataSolveRebinds = true }));
            Console.WriteLine("Scope Rhino document close: owned headless doc disposed; contribution removed; Canvas survives; queued interactions stay detached; normal data solve rebinds.");

            T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
            Param_GenericObject Source(string key)
            {
                var source = Add(new Param_GenericObject());
                source.PersistentData.Append(new GH_ObjectWrapper(new CardSpec { Id = key, Kind = CardKind.Table, Headers = ["Key"], Rows = [new(key, [key])] }));
                return source;
            }
        }
        finally { RhinoDoc.ActiveDoc = original; Instances.DocumentServer.RemoveDocument(gh); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
