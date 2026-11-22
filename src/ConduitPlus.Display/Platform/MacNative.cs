using System.Runtime.InteropServices;

namespace ConduitPlus.Platform;

/// <summary>
/// Minimal Objective-C runtime interop for Rhino for Mac (AppKit, main thread only). No Xamarin/Microsoft.macOS
/// dependency: every call goes through libobjc's objc_msgSend with explicit signatures. Nothing here is
/// reachable on Windows; callers check <see cref="OperatingSystem.IsMacOS"/> first.
/// </summary>
internal static class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)] internal struct CGPoint { public double X, Y; public CGPoint(double x, double y) { X = x; Y = y; } public override string ToString() => $"({X:0.##}, {Y:0.##})"; }
    [StructLayout(LayoutKind.Sequential)] internal struct CGSize { public double Width, Height; }
    [StructLayout(LayoutKind.Sequential)] internal struct CGRect { public CGPoint Origin; public CGSize Size; public override string ToString() => $"({Origin.X:0.##}, {Origin.Y:0.##}, {Size.Width:0.##}×{Size.Height:0.##})"; }

    [DllImport(Lib)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, ulong b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, CGPoint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern long SendLong(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern long SendLong(IntPtr receiver, IntPtr selector, CGPoint a, long b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern ulong SendULong(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern ulong SendULong(IntPtr receiver, IntPtr selector, ulong a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern ushort SendUShort(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern double SendDouble(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] internal static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint a, IntPtr b);
    // CGRect (32 bytes) is returned in registers on arm64 but through a hidden pointer on x86_64.
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern CGRect SendRectArm(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend_stret")] private static extern CGRect SendRectX64(IntPtr receiver, IntPtr selector);
    internal static CGRect SendRect(IntPtr receiver, IntPtr selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 ? SendRectX64(receiver, selector) : SendRectArm(receiver, selector);

    private static readonly Dictionary<string, IntPtr> Classes = [], Selectors = [];
    internal static IntPtr Class(string name) { if (!Classes.TryGetValue(name, out var c)) Classes[name] = c = objc_getClass(name); return c; }
    internal static IntPtr Sel(string name) { if (!Selectors.TryGetValue(name, out var s)) Selectors[name] = s = sel_registerName(name); return s; }

    internal static bool IsKind(IntPtr obj, string className) => obj != IntPtr.Zero && Class(className) is var c && c != IntPtr.Zero && SendBool(obj, Sel("isKindOfClass:"), c);
    internal static string ClassName(IntPtr obj) => obj == IntPtr.Zero ? "(nil)" : FromNSString(Send(obj, Sel("className"))) ?? "?";
    internal static string? FromNSString(IntPtr s) => s == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(s, Sel("UTF8String")));
    internal static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
    internal static IntPtr NSData(byte[] bytes)
    {
        var buffer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try { Marshal.Copy(bytes, 0, buffer, bytes.Length); return Send(Class("NSData"), Sel("dataWithBytes:length:"), buffer, (ulong)bytes.Length); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal static byte[] Bytes(IntPtr data)
    {
        if (data == IntPtr.Zero) return [];
        var length = (int)SendULong(data, Sel("length"));
        var result = new byte[length];
        if (length > 0) Marshal.Copy(Send(data, Sel("bytes")), result, 0, length);
        return result;
    }

    /// <summary>
    /// A global Objective-C block literal whose invoke function is a rooted managed delegate. Global blocks are
    /// never copied or freed by the runtime, so the native memory lives for the process (one per monitor).
    /// </summary>
    internal sealed class Block
    {
        internal delegate IntPtr EventHandler(IntPtr block, IntPtr nsEvent);
        private readonly EventHandler _invoke; // rooted: native code calls it for the block's lifetime
        public IntPtr Handle { get; }
        public Block(EventHandler invoke)
        {
            _invoke = invoke;
            var libSystem = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
            var isa = NativeLibrary.GetExport(libSystem, "_NSConcreteGlobalBlock");
            // struct Block_descriptor { unsigned long reserved; unsigned long size; const char *signature; }
            var signature = Marshal.StringToCoTaskMemUTF8("@16@?0@8");
            var descriptor = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteInt64(descriptor, 0, 0); Marshal.WriteInt64(descriptor, 8, 32); Marshal.WriteIntPtr(descriptor, 16, signature);
            // struct Block_literal { void *isa; int flags; int reserved; void *invoke; struct Block_descriptor *descriptor; }
            Handle = Marshal.AllocHGlobal(32);
            const int BlockIsGlobal = 1 << 28, BlockHasSignature = 1 << 30;
            Marshal.WriteIntPtr(Handle, 0, isa);
            Marshal.WriteInt32(Handle, 8, BlockIsGlobal | BlockHasSignature);
            Marshal.WriteInt32(Handle, 12, 0);
            Marshal.WriteIntPtr(Handle, 16, Marshal.GetFunctionPointerForDelegate(_invoke));
            Marshal.WriteIntPtr(Handle, 24, descriptor);
        }
    }
}

/// <summary>AppKit queries used by the Mac adapter. All values are in AppKit points unless stated.</summary>
internal static class AppKit
{
    // NSEventType
    internal const ulong LeftMouseDown = 1, LeftMouseUp = 2, RightMouseDown = 3, RightMouseUp = 4, MouseMoved = 5, LeftMouseDragged = 6,
        RightMouseDragged = 7, MouseEntered = 8, MouseExited = 9, KeyDown = 10, KeyUp = 11, FlagsChanged = 12, ScrollWheel = 22, OtherMouseDragged = 27;
    internal const ushort EscapeKeyCode = 53;
    internal static ulong Mask(params ulong[] types) => types.Aggregate(0UL, (m, t) => m | (1UL << (int)t));

    internal static ObjC.CGPoint MouseLocation => ObjC.SendPoint(ObjC.Class("NSEvent"), ObjC.Sel("mouseLocation"));
    internal static ulong PressedMouseButtons => ObjC.SendULong(ObjC.Class("NSEvent"), ObjC.Sel("pressedMouseButtons"));
    internal static ulong ModifierFlags => ObjC.SendULong(ObjC.Class("NSEvent"), ObjC.Sel("modifierFlags"));

    internal static IntPtr Window(IntPtr view) => view == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(view, ObjC.Sel("window"));
    internal static ObjC.CGRect Bounds(IntPtr view) => ObjC.SendRect(view, ObjC.Sel("bounds"));
    internal static bool IsFlipped(IntPtr view) => ObjC.SendBool(view, ObjC.Sel("isFlipped"));
    internal static double BackingScale(IntPtr window) => window == IntPtr.Zero ? 1 : ObjC.SendDouble(window, ObjC.Sel("backingScaleFactor"));
    internal static long WindowNumber(IntPtr window) => window == IntPtr.Zero ? -1 : ObjC.SendLong(window, ObjC.Sel("windowNumber"));
    internal static long WindowNumberAt(ObjC.CGPoint screen) => ObjC.SendLong(ObjC.Class("NSWindow"), ObjC.Sel("windowNumberAtPoint:belowWindowWithWindowNumber:"), screen, 0);
    internal static ObjC.CGPoint ScreenToWindow(IntPtr window, ObjC.CGPoint screen) => ObjC.SendPoint(window, ObjC.Sel("convertPointFromScreen:"), screen);
    internal static ObjC.CGPoint WindowToView(IntPtr view, ObjC.CGPoint point) => ObjC.SendPoint(view, ObjC.Sel("convertPoint:fromView:"), point, IntPtr.Zero);
    internal static bool IsDescendant(IntPtr view, IntPtr ancestor) => view != IntPtr.Zero && ObjC.SendBool(view, ObjC.Sel("isDescendantOf:"), ancestor);

    /// <summary>The deepest view of <paramref name="window"/> under a window-base point, or nil.</summary>
    internal static IntPtr HitTest(IntPtr window, ObjC.CGPoint windowPoint)
    {
        var content = ObjC.Send(window, ObjC.Sel("contentView"));
        if (content == IntPtr.Zero) return IntPtr.Zero;
        var superview = ObjC.Send(content, ObjC.Sel("superview"));
        var p = superview == IntPtr.Zero ? windowPoint : WindowToView(superview, windowPoint);
        return ObjC.Send(content, ObjC.Sel("hitTest:"), p);
    }

    private static readonly Dictionary<IntPtr, IntPtr> Resolved = [];
    private static readonly Dictionary<IntPtr, long> Unresolved = [];

    /// <summary>
    /// Resolve an NSView from a Rhino/WinForms handle: the view itself, or an NSWindow's content view.
    /// A handle is never messaged unless it is found, by pointer comparison only, among the app's windows and
    /// their view trees: messaging a pointer that isn't an Objective-C object would crash Rhino.
    /// </summary>
    internal static IntPtr ViewOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        if (Resolved.TryGetValue(handle, out var view)) return view;
        if (Unresolved.TryGetValue(handle, out var since) && Environment.TickCount64 - since < 1000) return IntPtr.Zero;
        view = Find(handle);
        if (view != IntPtr.Zero) { Resolved[handle] = view; Unresolved.Remove(handle); }
        else Unresolved[handle] = Environment.TickCount64;
        return view;
    }
    internal static void Forget(IntPtr handle) { Resolved.Remove(handle); Unresolved.Remove(handle); }

    private static IntPtr Find(IntPtr handle)
    {
        var app = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
        var windows = app == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(app, ObjC.Sel("windows"));
        if (windows == IntPtr.Zero) return IntPtr.Zero;
        var count = (long)ObjC.SendULong(windows, ObjC.Sel("count"));
        for (long i = 0; i < count; i++)
        {
            var window = ObjC.Send(windows, ObjC.Sel("objectAtIndex:"), (IntPtr)i);
            var content = ObjC.Send(window, ObjC.Sel("contentView"));
            if (window == handle) return content;
            if (content != IntPtr.Zero && Contains(content, handle, 0)) return handle;
        }
        return IntPtr.Zero;
    }
    private static bool Contains(IntPtr view, IntPtr target, int depth)
    {
        if (view == target) return true;
        if (depth > 64) return false;
        var subviews = ObjC.Send(view, ObjC.Sel("subviews"));
        if (subviews == IntPtr.Zero) return false;
        var count = (long)ObjC.SendULong(subviews, ObjC.Sel("count"));
        for (long i = 0; i < count; i++)
            if (Contains(ObjC.Send(subviews, ObjC.Sel("objectAtIndex:"), (IntPtr)i), target, depth + 1)) return true;
        return false;
    }

    internal static double MainScreenBackingScale()
    {
        var screen = ObjC.Send(ObjC.Class("NSScreen"), ObjC.Sel("mainScreen"));
        return screen == IntPtr.Zero ? 1 : ObjC.SendDouble(screen, ObjC.Sel("backingScaleFactor"));
    }
    internal static IntPtr KeyWindow()
    {
        var app = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
        return app == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(app, ObjC.Sel("keyWindow"));
    }

    // NSEvent instance accessors
    internal static ulong EventType(IntPtr e) => ObjC.SendULong(e, ObjC.Sel("type"));
    internal static IntPtr EventWindow(IntPtr e) => ObjC.Send(e, ObjC.Sel("window"));
    internal static ObjC.CGPoint EventLocationInWindow(IntPtr e) => ObjC.SendPoint(e, ObjC.Sel("locationInWindow"));
    internal static double ScrollingDeltaY(IntPtr e) => ObjC.SendDouble(e, ObjC.Sel("scrollingDeltaY"));
    internal static double ScrollingDeltaX(IntPtr e) => ObjC.SendDouble(e, ObjC.Sel("scrollingDeltaX"));
    internal static bool HasPreciseScrollingDeltas(IntPtr e) => ObjC.SendBool(e, ObjC.Sel("hasPreciseScrollingDeltas"));
    internal static ulong Phase(IntPtr e) => ObjC.SendULong(e, ObjC.Sel("phase"));
    internal static ulong MomentumPhase(IntPtr e) => ObjC.SendULong(e, ObjC.Sel("momentumPhase"));
    internal static ushort KeyCode(IntPtr e) => ObjC.SendUShort(e, ObjC.Sel("keyCode"));
}
