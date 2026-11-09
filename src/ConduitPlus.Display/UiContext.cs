using System.Runtime.InteropServices;
using Rhino;
using Rhino.Display;

namespace ConduitPlus.Rhino;

internal static class UiContext
{
    public static void Ensure()
    {
        if (RhinoApp.InvokeRequired) throw new InvalidOperationException("Overlay must be updated on Rhino's UI thread.");
    }
    public static double Dpi(RhinoView view) => Math.Max(1, GetDpiForWindow(view.Handle) / 96d);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
