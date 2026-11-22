using System.Runtime.InteropServices;
using ConduitPlus.Core;

namespace ConduitPlus.Platform;

/// <summary>
/// Host input state shared by the viewport and Grasshopper adapters. On Windows every member returns exactly
/// what the code used before this layer existed (WinForms statics and GetAsyncKeyState). On macOS it reads
/// AppKit directly, so behaviour doesn't depend on how complete Rhino's WinForms shim is.
/// Windows-only and Mac-only types live in separate classes so neither platform loads the other's assemblies.
/// </summary>
public static class HostInput
{
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>
    /// WinForms-style modifiers. macOS: Command → Control (toggle), Option → Alt, Shift → Shift; physical
    /// Control (secondary click) → <c>Keys.LWin</c>, a non-modifier bit that adapters treat as "other".
    /// </summary>
    public static System.Windows.Forms.Keys ModifierKeys => IsMac ? Mac.ModifierKeys() : Win.ModifierKeys();

    public static CardModifiers CardModifiers => IsMac ? MacModifierMapping.ToCard((MacModifierFlags)Mac.Flags())
        : Win.CardModifiers();

    public static System.Windows.Forms.MouseButtons MouseButtons => IsMac ? Mac.Buttons() : Win.Buttons();

    public static bool LeftButtonDown => IsMac ? (Mac.Pressed() & 1) != 0 : Win.Down(0x01);
    public static bool AnyButtonDown => IsMac ? (Mac.Pressed() & 7) != 0 : Win.Down(0x01) || Win.Down(0x02) || Win.Down(0x04);
    /// <summary>Windows: polled key state. macOS: tracked from keyDown/keyUp in the app's event monitor.</summary>
    public static bool EscapeDown => IsMac ? MacEscape.Down : Win.Down(0x1B);

    /// <summary>macOS modifier state for the viewport (Command toggles, Control-click is secondary).</summary>
    internal static (bool Shift, bool Command, bool Control) MacModifiers()
    {
        var flags = (MacModifierFlags)Mac.Flags();
        return ((flags & MacModifierFlags.Shift) != 0, (flags & MacModifierFlags.Command) != 0, (flags & MacModifierFlags.Control) != 0);
    }

    private static class Win
    {
        internal static System.Windows.Forms.Keys ModifierKeys() => System.Windows.Forms.Control.ModifierKeys;
        internal static CardModifiers CardModifiers()
        {
            var keys = System.Windows.Forms.Control.ModifierKeys;
            return ((keys & System.Windows.Forms.Keys.Shift) != 0 ? Core.CardModifiers.Shift : 0) | ((keys & System.Windows.Forms.Keys.Control) != 0 ? Core.CardModifiers.Control : 0) | ((keys & System.Windows.Forms.Keys.Alt) != 0 ? Core.CardModifiers.Alt : 0);
        }
        internal static System.Windows.Forms.MouseButtons Buttons() => System.Windows.Forms.Control.MouseButtons;
        internal static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    }

    private static class Mac
    {
        internal static ulong Flags() { try { return AppKit.ModifierFlags; } catch (Exception e) { MacDiagnostics.Fault("modifierFlags", e); return 0; } }
        internal static ulong Pressed() { try { return AppKit.PressedMouseButtons; } catch (Exception e) { MacDiagnostics.Fault("pressedMouseButtons", e); return 0; } }
        internal static System.Windows.Forms.Keys ModifierKeys()
        {
            var flags = (MacModifierFlags)Flags();
            var keys = System.Windows.Forms.Keys.None;
            if ((flags & MacModifierFlags.Shift) != 0) keys |= System.Windows.Forms.Keys.Shift;
            if ((flags & MacModifierFlags.Command) != 0) keys |= System.Windows.Forms.Keys.Control;
            if ((flags & MacModifierFlags.Option) != 0) keys |= System.Windows.Forms.Keys.Alt;
            if ((flags & MacModifierFlags.Control) != 0) keys |= System.Windows.Forms.Keys.LWin;
            return keys;
        }
        internal static System.Windows.Forms.MouseButtons Buttons()
        {
            var pressed = Pressed();
            var buttons = System.Windows.Forms.MouseButtons.None;
            if ((pressed & 1) != 0) buttons |= System.Windows.Forms.MouseButtons.Left;
            if ((pressed & 2) != 0) buttons |= System.Windows.Forms.MouseButtons.Right;
            if ((pressed & 4) != 0) buttons |= System.Windows.Forms.MouseButtons.Middle;
            return buttons;
        }
    }
}

/// <summary>Escape state seen by the Mac event monitor (main thread only).</summary>
internal static class MacEscape
{
    internal static bool Down { get; set; }
}

/// <summary>
/// UI-thread timer. Windows: the same System.Windows.Forms.Timer as before. macOS: Eto's UITimer, which is
/// backed by an NSTimer on the main run loop. Start on a running timer is a no-op on both, like WinForms.
/// </summary>
public sealed class UiTimer : IDisposable
{
    private interface IImpl : IDisposable { void Start(); void Stop(); int Interval { get; set; } }
    private readonly IImpl _impl;
    public event EventHandler? Tick;
    public UiTimer(int intervalMilliseconds)
    {
        _impl = HostInput.IsMac ? new EtoImpl(this, intervalMilliseconds) : new WinImpl(this, intervalMilliseconds);
    }
    public bool Enabled { get; private set; }
    public int Interval { get => _impl.Interval; set => _impl.Interval = value; }
    public void Start() { if (Enabled) return; Enabled = true; _impl.Start(); }
    public void Stop() { if (!Enabled) return; Enabled = false; _impl.Stop(); }
    public void Dispose() { Enabled = false; _impl.Dispose(); }
    private void Raise() { if (Enabled) Tick?.Invoke(this, EventArgs.Empty); }

    private sealed class WinImpl : IImpl
    {
        private readonly System.Windows.Forms.Timer _timer;
        public WinImpl(UiTimer owner, int interval) { _timer = new() { Interval = interval }; _timer.Tick += (_, _) => owner.Raise(); }
        public int Interval { get => _timer.Interval; set => _timer.Interval = value; }
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() => _timer.Dispose();
    }
    private sealed class EtoImpl : IImpl
    {
        private readonly Eto.Forms.UITimer _timer;
        private int _interval;
        public EtoImpl(UiTimer owner, int interval)
        {
            _interval = interval;
            _timer = new() { Interval = interval / 1000d };
            _timer.Elapsed += (_, _) => { MacDiagnostics.TimerTicks++; owner.Raise(); };
        }
        public int Interval { get => _interval; set { _interval = value; _timer.Interval = value / 1000d; } }
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() { _timer.Stop(); _timer.Dispose(); }
    }
}

/// <summary>Counters and first faults from the Mac adapter, reported by ConduitPlusPlatformCheck.</summary>
public static class MacDiagnostics
{
    public static long TimerTicks { get; internal set; }
    public static long MonitorEvents { get; internal set; }
    public static long ScrollEvents { get; internal set; }
    public static long ScrollSwallowed { get; internal set; }
    public static long MoveEvents { get; internal set; }
    public static long MouseCallbackMoves { get; internal set; }
    public static string? MonitorStatus { get; internal set; }
    public static string? LastScroll { get; internal set; }
    private static readonly List<string> Faults = [];
    public static IReadOnlyList<string> FaultLog => Faults;
    internal static void Fault(string where, Exception e)
    {
        if (Faults.Count >= 20) return;
        var line = $"{where}: {e.GetType().Name}: {e.Message}";
        if (!Faults.Contains(line)) { Faults.Add(line); try { global::Rhino.RhinoApp.WriteLine("Conduit+ (macOS) " + line); } catch { } }
    }
}
