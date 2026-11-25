using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ConduitPlus.Core;
using ConduitPlus.Rendering;

namespace ConduitPlus.Rhino;

internal static class DistributionMotion
{
    public static bool Enabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled;
            if (!OperatingSystem.IsMacOS()) return false;
            var type = objc_getClass("NSWorkspace");
            var workspace = Send(type, sel_registerName("sharedWorkspace"));
            return workspace != IntPtr.Zero && !ReadBool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
        }
    }

    public static Bitmap Blend(CardSpec from, CapturedCard to, Theme theme, double progress)
    {
        using var previous = RasterDrawing.Render(from, theme, to.Scale);
        var result = new Bitmap(to.Bitmap.Width, to.Bitmap.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(previous, 0, 0);
        using var attributes = new ImageAttributes();
        var mix = (float)(progress * progress * (3 - 2 * progress));
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = mix });
        var target = new Rectangle(0, 0, result.Width, result.Height);
        graphics.DrawImage(to.Bitmap, target, 0, 0, to.Bitmap.Width, to.Bitmap.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool enabled, uint flags);
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ReadBool(IntPtr receiver, IntPtr selector);
}
