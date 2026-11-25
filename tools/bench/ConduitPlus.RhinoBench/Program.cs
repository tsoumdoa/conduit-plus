using System.Reflection;
using ConduitPlus.Core;
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
            foreach (var folder in new[] { pluginDirectory, Path.Combine(RhinoSystem, "netcore"), RhinoSystem, Path.GetFullPath(Path.Combine(RhinoSystem, "..", "Plug-ins", "Grasshopper")) })
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            var gha = Path.Combine(root.FullName, "src", "ConduitPlus.Grasshopper", "bin", Configuration, "net8.0-windows", name.Name + ".gha");
            return File.Exists(gha) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(gha) : null;
        };
        try { return Run(pluginDirectory, args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddThresholdLegend(Rhino.RhinoDoc doc, Rhino.Display.RhinoView view)
    {
        var scale = ConduitPlus.Core.ColourScale.Create([2, 6, 12, 24, 25, 28, 43], ["Viridis"], 0, 40,
            title: "Thresholds", slot: ConduitPlus.Core.Slot.Tr, thresholds: [6, 24, 25, 42],
            thresholdColours: ["#622E94", "#E9A028", "#B93238", "#852733"],
            thresholdSides: ["Below", "Above", "Above", "Above"]);
        ConduitPlus.Rhino.OverlayManager.Set(doc, view, new Guid("98a9906b-30dd-425f-bd56-78ec86378a61"), [scale.Legend]);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string pluginDirectory, string[] args)
    {
        if (args.Contains("--distribution") && !args.Contains("--capture") && !args.Contains("--pdf"))
        {
            DistributionBench.Run(Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!);
            Console.WriteLine("Benchmark workload completed successfully.");
            return 0;
        }
        Console.WriteLine("Starting RhinoCore under .NET " + Environment.Version);
        // FilePdf.Create needs the Rhino PDF writer, which does not load in safe mode.
        var options = new List<string> { "/nosplash", "/notemplate", "/scheme=ConduitPlusBench" };
        if (!args.Contains("--pdf")) options.Add("/safemode");
        using var core = new Rhino.Runtime.InProcess.RhinoCore(options.ToArray(), Rhino.Runtime.InProcess.WindowStyle.Hidden);
        Console.WriteLine("RhinoCore initialized.");
        var doc = Rhino.RhinoDoc.ActiveDoc ?? Rhino.RhinoDoc.Create(null);
        var bounds = args.Contains("--compact") ? new System.Drawing.Rectangle(0, 0, 500, 400) : new System.Drawing.Rectangle(0, 0, 1900, 1800);
        var view = doc.Views.Add("Conduit+ benchmark", Rhino.Display.DefinedViewportProjection.Perspective, bounds, true)
            ?? throw new InvalidOperationException("Could not create benchmark viewport.");
        doc.Views.ActiveView = view;
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(pluginDirectory, "ConduitPlus.Rhino.rhp"));
        if (args.Contains("--label-occlusion"))
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "Grasshopper" or "GH_IO") ||
                assembly.GetReferencedAssemblies().Any(a => a.Name is "Grasshopper" or "GH_IO"))
                throw new InvalidOperationException("Loading the Rhino plug-in resolved Grasshopper.");
        }
        var captureIndex = Array.IndexOf(args, "--capture");
        var type = captureIndex >= 0 ? "ConduitPlusCapture" : args.Contains("--pdf") ? "ConduitPlusPdf" : args.Contains("--platform-check") ? "ConduitPlusPlatformCheck" : "ConduitPlusBench";

        var arguments = captureIndex >= 0
            ? new object[] { doc, view, int.Parse(args[captureIndex + 1], System.Globalization.CultureInfo.InvariantCulture), args.Contains("--compact") }
            : [doc, view];
        try
        {
            core.InvokeInHostContext(() =>
            {
                if (args.Contains("--label-occlusion")) RunLabels(doc, view, () => { core.DoEvents(); System.Windows.Forms.Application.DoEvents(); });
                else if (args.Contains("--labels") || args.Contains("--label-input"))
                    RunLabelWiring(doc, view, () => { core.DoEvents(); System.Windows.Forms.Application.DoEvents(); Rhino.RhinoApp.Wait(); },
                        benchmark: args.Contains("--labels"));
                else
                {
                    if (args.Contains("--thresholds")) AddThresholdLegend(doc, view);
                    var entry = assembly.GetType("ConduitPlus.Rhino." + type)!
                        .GetMethod("RunAutomated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                    try
                    {
                        if (args.Contains("--distribution"))
                        {
                            var theme = args.Contains("--distribution-night") ? Theme.Night : Theme.Paper;
                            if (captureIndex >= 0)
                            {
                                var capture = assembly.GetType("ConduitPlus.Rhino.ConduitPlusCapture")!
                                    .GetMethod("CaptureToFile", BindingFlags.NonPublic | BindingFlags.Static)!;
                                DistributionBench.Export(doc, view, theme, () => capture.Invoke(null, [doc, view, arguments[2], false]));
                            }
                            else DistributionBench.Export(doc, view, theme, () => entry.Invoke(null, arguments));
                        }
                        else entry.Invoke(null, arguments);
                    }
                    catch (TargetInvocationException e) { Console.Error.WriteLine(e.InnerException); throw; }
                }
                return true;
            });
            Console.WriteLine("Benchmark workload completed successfully.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(pluginDirectory, "ConduitPlus.Display.dll")).GetType("ConduitPlus.Rhino.OverlayManager")!.GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            view.Close();
            core.DoEvents();
            Console.WriteLine("Overlay and floating viewport disposed; shutting down RhinoCore.");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunLabels(Rhino.RhinoDoc doc, Rhino.Display.RhinoView view, Action pump) =>
        ConduitPlus.Rhino.LabelOcclusionBench.RunAutomated(doc, view, pump);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunLabelWiring(Rhino.RhinoDoc doc, Rhino.Display.RhinoView view, Action pump, bool benchmark) =>
        ConduitPlus.Rhino.LabelsBench.Run(doc, view, pump, benchmark);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
}
