using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    private static readonly string RhinoSystem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "System");
    [STAThread]
    private static int Main(string[] args)
    {
        SetDllDirectory(RhinoSystem);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (var folder in new[] { Path.Combine(Directory.GetCurrentDirectory(), "src", "ConduitPlus.Rhino", "bin", "Debug", "net8.0-windows"), Path.Combine(RhinoSystem, "netcore"), RhinoSystem })
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { return Run(args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory(), args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string root, string[] args)
    {
        Console.WriteLine("Starting RhinoCore under .NET " + Environment.Version);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var core = new Rhino.Runtime.InProcess.RhinoCore(["/nosplash", "/notemplate", "/safemode", "/scheme=ConduitPlusSpike"], Rhino.Runtime.InProcess.WindowStyle.Hidden);
        Console.WriteLine("RhinoCore initialized.");
        var doc = Rhino.RhinoDoc.ActiveDoc ?? Rhino.RhinoDoc.Create(null);
        var view = doc.Views.Add("Conduit+ spike", Rhino.Display.DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(0, 0, 1900, 1800), true);
        if (view is null) throw new InvalidOperationException("Could not create test viewport.");
        doc.Views.ActiveView = view;
        Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", Path.Combine(root, "docs", "spike"));
        var plugin = Path.Combine(root, "src", "ConduitPlus.Rhino", "bin", "Debug", "net8.0-windows", "ConduitPlus.Rhino.rhp");
        // Safe mode suppresses third-party startup plug-ins and command registration.
        // Invoke our benchmark directly against the real Rhino DisplayPipeline/ViewCapture.
        Environment.SetEnvironmentVariable("CONDUITPLUS_RUN_CONTEXT", "RhinoCore Hidden /safemode; ViewCapture fallback if redraw is suppressed; third-party startup plug-ins disabled");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(plugin);
        var captureIndex = Array.IndexOf(args, "--capture");
        if (captureIndex < 0) Environment.SetEnvironmentVariable("CONDUITPLUS_SKIP_CAPTURE", "1");
        var type = captureIndex >= 0 ? "ConduitPlus.Rhino.ConduitPlusCapture" : "ConduitPlus.Rhino.ConduitPlusBench";
        var entry = assembly.GetType(type)!.GetMethod("RunAutomatedAsync", BindingFlags.Public | BindingFlags.Static)!;
        Console.WriteLine("Running real Rhino ViewCapture benchmark.");
        var arguments = captureIndex >= 0 ? new object[] { doc, view, args[captureIndex + 1], int.Parse(args[captureIndex + 2], System.Globalization.CultureInfo.InvariantCulture), args[captureIndex + 3] == "prepared" } : [doc, view];
        // Mirror CommandRunner: async work starts inside the plug-in's scoped UI context so WebView2 continuations
        // resume on this thread even inside RhinoCore's message pump.
        var enter = assembly.GetType("ConduitPlus.Rendering.CaptureUiContext")!.GetMethod("Enter", BindingFlags.Public | BindingFlags.Static)!;
        var task = core.InvokeInHostContext(() => { using var scope = (IDisposable)enter.Invoke(null, null)!; return (Task)entry.Invoke(null, arguments)!; });
        try
        {
            while (!task.IsCompleted) { core.DoEvents(); Application.DoEvents(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult(); return 0;
        }
        finally
        {
            assembly.GetType("ConduitPlus.Rhino.OverlayManager")!.GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            view.Close();
            core.DoEvents();
            Console.WriteLine("Overlay and floating viewport disposed; shutting down RhinoCore.");
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
}
