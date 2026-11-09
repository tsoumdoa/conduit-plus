using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    private static readonly string RhinoSystem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "System");
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
    [STAThread]
    private static int Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ConduitPlus.sln"))) root = root.Parent;
        if (root is null) { Console.Error.WriteLine("Could not locate the repository root."); return 1; }
        var pluginDirectory = Path.Combine(root.FullName, "src", "ConduitPlus.Rhino", "bin", Configuration, "net8.0-windows");
        SetDllDirectory(RhinoSystem);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (var folder in new[] { pluginDirectory, Path.Combine(RhinoSystem, "netcore"), RhinoSystem })
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { return Run(pluginDirectory, args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string pluginDirectory, string[] args)
    {
        Console.WriteLine("Starting RhinoCore under .NET " + Environment.Version);
        using var core = new Rhino.Runtime.InProcess.RhinoCore(["/nosplash", "/notemplate", "/safemode", "/scheme=ConduitPlusBench"], Rhino.Runtime.InProcess.WindowStyle.Hidden);
        Console.WriteLine("RhinoCore initialized.");
        var doc = Rhino.RhinoDoc.ActiveDoc ?? Rhino.RhinoDoc.Create(null);
        var bounds = args.Contains("--compact") ? new System.Drawing.Rectangle(0, 0, 500, 400) : new System.Drawing.Rectangle(0, 0, 1900, 1800);
        var view = doc.Views.Add("Conduit+ benchmark", Rhino.Display.DefinedViewportProjection.Perspective, bounds, true)
            ?? throw new InvalidOperationException("Could not create benchmark viewport.");
        doc.Views.ActiveView = view;
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(pluginDirectory, "ConduitPlus.Rhino.rhp"));
        var captureIndex = Array.IndexOf(args, "--capture");
        var type = captureIndex >= 0 ? "ConduitPlusCapture" : args.Contains("--pdf") ? "ConduitPlusPdf" : "ConduitPlusBench";
        var entry = assembly.GetType("ConduitPlus.Rhino." + type)!.GetMethod("RunAutomated", BindingFlags.Public | BindingFlags.Static)!;
        var arguments = captureIndex >= 0
            ? new object[] { doc, view, int.Parse(args[captureIndex + 1], System.Globalization.CultureInfo.InvariantCulture), args.Contains("--compact") }
            : [doc, view];
        try
        {
            core.InvokeInHostContext(() => { entry.Invoke(null, arguments); return true; });
            Console.WriteLine("Benchmark workload completed successfully.");
            return 0;
        }
        finally
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(pluginDirectory, "ConduitPlus.Display.dll")).GetType("ConduitPlus.Rhino.OverlayManager")!.GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            view.Close();
            core.DoEvents();
            Console.WriteLine("Overlay and floating viewport disposed; shutting down RhinoCore.");
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
}
