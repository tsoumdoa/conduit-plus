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
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ConduitPlus.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository not found.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var outputs = new[] { "ConduitPlus.Display", "ConduitPlus.Grasshopper", "ConduitPlus.Rhino" }.Select(name => Path.Combine(root.FullName, "src", name, "bin", configuration, "net8.0-windows")).ToArray();
        SetDllDirectory(RhinoSystem);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (var folder in outputs.Concat([AppContext.BaseDirectory, Path.Combine(RhinoSystem, "netcore"), RhinoSystem, Path.Combine(RhinoSystem, "..", "Plug-ins", "Grasshopper")]))
            foreach (var extension in new[] { ".dll", ".rhp", ".gha" })
            {
                var path = Path.GetFullPath(Path.Combine(folder, name.Name + extension));
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        string[] options = ["/nosplash", "/notemplate", "/scheme=ConduitPlusExamples", .. args.Contains("--safe") ? new[] { "/safemode" } : []];
        using var core = new Rhino.Runtime.InProcess.RhinoCore(options, Rhino.Runtime.InProcess.WindowStyle.Hidden);
        Console.WriteLine($"Rhino {Rhino.RhinoApp.Version}, .NET {Environment.Version}");
        var doc = Rhino.RhinoDoc.ActiveDoc ?? Rhino.RhinoDoc.Create(null);
        var bounds = args.Contains("--compact") ? new System.Drawing.Rectangle(0, 0, 500, 400) : new System.Drawing.Rectangle(0, 0, 1900, 1800);
        var view = doc.Views.Add("Conduit+ structural dashboard", Rhino.Display.DefinedViewportProjection.Perspective, bounds, true)
            ?? throw new InvalidOperationException("Could not create viewport.");
        doc.Views.ActiveView = view;
        Console.WriteLine("Perspective viewport created.");
        try
        {
            core.InvokeInHostContext(() => { DisplayLifecycleChecks.Run(doc, view); Dashboard.Run(doc, view, args); return true; });
            Console.WriteLine("Example workload completed successfully.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            Dashboard.Stop(); view.Close(); core.DoEvents();
            Console.WriteLine("Overlay and floating viewport disposed; shutting down RhinoCore.");
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
}
