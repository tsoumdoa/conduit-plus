using System.Reflection;
using System.Runtime.CompilerServices;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Commands;
using Rhino.Display;

internal static class ExportInputChecks
{
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
