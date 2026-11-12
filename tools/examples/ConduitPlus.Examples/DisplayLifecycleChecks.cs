using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Display;

internal static class DisplayLifecycleChecks
{
    private const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type Manager = typeof(OverlayManager);
    private static void Start() => Manager.GetMethod("Start", Internal)!.Invoke(null, null);
    private static void Stop() => Manager.GetMethod("Stop", Internal)!.Invoke(null, null);
    private static bool Started => (bool)Manager.GetField("_started", Internal)!.GetValue(null)!;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    /// <summary>Grasshopper shows these in its Archive Messages dialog, e.g. when a saved component predates an input.</summary>
    internal static void ArchiveClean(GH_IO.Serialization.GH_Archive archive, string path)
    {
        var errors = archive.Messages.Where(m => m.Type == GH_IO.Serialization.GH_Message_Type.error).Select(m => m.Message).ToArray();
        Require(errors.Length == 0, $"{path} opens with archive errors; regenerate it with Build-Examples.ps1:\n{string.Join("\n", errors)}");
    }
    internal static void DefinitionSolved()
    {
        // Run before the harness's capture/PDF command checks, which legitimately load the RHP.
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ConduitPlus.Rhino"),
            "Definition unexpectedly loaded the RHP assembly; the harness resolver must not mask this dependency.");
        Console.WriteLine("Definition solved with the RHP assembly never loaded.");
    }

    internal static void Run(RhinoDoc doc, RhinoView view)
    {
        TitleBackgroundChecks.Run(doc);
        var references = typeof(CanvasCardComponent).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Require(references.Contains("ConduitPlus.Display") && references.Contains("ConduitPlus.Core") && !references.Contains("ConduitPlus.Rhino"),
            "GHA dependencies must use Display/Core and must not reference the RHP.");
        Require(!Started, "Display started before first use.");
        Stop(); Stop(); // Safe even before first use.
        _ = new CanvasCardComponent();
        _ = new OverlayComponent(); // Grasshopper does this for every component while scanning plug-ins.
        Require(!Started && TextMetrics.Measurer.Method.DeclaringType?.Assembly == Manager.Assembly,
            "Constructing a component must register GDI text metrics without starting Display's global hooks.");
        Start(); Start();
        var owner = Guid.NewGuid();
        OverlayManager.Set(doc, view, owner, [new CardSpec { Id = "lifecycle", Kind = CardKind.Text, Text = "First use" }]);
        Require(OverlayManager.Binding(owner)?.DocumentSerial == doc.RuntimeSerialNumber, "First-use document binding failed.");
        OverlayManager.Detach(doc, owner);
        Require(OverlayManager.Binding(owner) == (doc.RuntimeSerialNumber, (Guid?)null), "Detach lost binding intent.");
        Stop(); Stop();
        Require(!(bool)Manager.GetField("_viewsPending", Internal)!.GetValue(null)!, "Stop retained a pending rebind.");
        Require(!Started, "Stop did not stop Display.");
        // Snapshot is itself a first-use path and must restart without retaining old bindings.
        Require(OverlayManager.Snapshot(owner).SelectedKeys.Length == 0 && OverlayManager.Binding(owner) is null && Started,
            "Restart retained a stale document binding.");
        using (var closed = RhinoDoc.CreateHeadless(null))
        {
            closed.Dispose();
            OverlayManager.Set(closed, view, owner, []);
            Require(OverlayManager.Binding(owner) is null, "First use retained a closed document.");
        }
        Stop();
        // Race two off-thread first uses while pumping Rhino's UI, as a background host would.
        var starts = Task.WhenAll(Task.Run(Start), Task.Run(Start));
        var deadline = Environment.TickCount64 + 10000;
        while (!starts.IsCompleted && Environment.TickCount64 < deadline) RhinoApp.Wait();
        Require(starts.IsCompleted, "UI marshalled Start did not finish.");
        starts.GetAwaiter().GetResult();
        Require(Started, "Racing first uses did not start Display.");
        Stop();
        Console.WriteLine("Display lifecycle: component construction without hooks, GDI metrics, idempotent start/stop, snapshot restart, closed-document rejection and racing UI-marshalled starts passed.");
    }
}
