using System.Reflection;
using System.Runtime.CompilerServices;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Commands;
using Rhino.Display;

internal static class ExportInputChecks
{
    internal static void CheckFailedLabelCapture(RhinoDoc doc, RhinoView view, object session)
    {
        var original = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS");
        var invalidDirectory = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", invalidDirectory);
            var capture = typeof(ConduitPlusCapture).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                capture.Invoke(null, [doc, view, 2]);
                throw new InvalidOperationException("Capture should fail when its output root is a file.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
            var type = session.GetType();
            if (type.GetProperty("CaptureLabels", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session) is not null ||
                (double)type.GetProperty("CaptureMultiplier")!.GetValue(session)! != 1 ||
                !(bool)type.GetProperty("Visible")!.GetValue(session)!)
                throw new InvalidOperationException("A failed label capture leaked export state.");
            Console.WriteLine("Failed label PNG restored live visibility, capture scale and label frame.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", original);
            File.Delete(invalidDirectory);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(RhinoDoc doc, RhinoView view)
    {
        const BindingFlags internalStatic = BindingFlags.NonPublic | BindingFlags.Static;
        var sessions = (Array)typeof(OverlayManager).GetMethod("InView", internalStatic)!.Invoke(null, [doc, view])!;
        var visible = sessions.Cast<object>().Select(s => (Session: s, Property: s.GetType().GetProperty("Visible")!, Value: (bool)s.GetType().GetProperty("Visible")!.GetValue(s)!)).ToArray();
        var output = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS")!;
        var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        try
        {
            foreach (var entry in visible) entry.Property.SetValue(entry.Session, false);
            var commands = new Command[] { new ConduitPlusCapture(), new ConduitPlusPdf() };
            foreach (var command in commands)
            {
                var result = (Result)command.GetType().GetMethod("RunCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(command, [doc, RunMode.Scripted])!;
                if (result != Result.Cancel) throw new InvalidOperationException(command.EnglishName + " must cancel in an empty view.");
            }
            var after = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
            if (!before.SequenceEqual(after)) throw new InvalidOperationException("Empty-view exports wrote files.");
            var remaining = (Array)typeof(OverlayManager).GetMethod("InView", internalStatic)!.Invoke(null, [doc, view])!;
            if (remaining.Length != sessions.Length) throw new InvalidOperationException("Empty-view exports inserted demo cards.");
            Console.WriteLine("PR A export checks passed: 4 assertions (PNG/PDF Cancel, no files, no demo sessions).");
        }
        finally { foreach (var entry in visible) entry.Property.SetValue(entry.Session, entry.Value); }
    }
}
