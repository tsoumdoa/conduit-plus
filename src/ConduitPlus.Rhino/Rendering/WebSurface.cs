using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ConduitPlus.Core;
using Microsoft.Web.WebView2.Core;

namespace ConduitPlus.Rendering;

/// <summary>
/// Marshals async continuations back to the UI thread, even inside a host's custom message pump.
/// Never installed globally: <see cref="Enter"/> scopes it to our own call chains and restores the
/// host's context, so other plug-ins sharing Rhino's UI thread are unaffected.
/// </summary>
public sealed class CaptureUiContext : SynchronizationContext
{
    private readonly System.Windows.Forms.WindowsFormsSynchronizationContext _inner = new();

    /// <summary>Makes this context current until disposed. Awaits started inside the scope resume on the UI thread.</summary>
    public static IDisposable Enter()
    {
        var previous = Current;
        if (previous is CaptureUiContext) return new Restore(null, restore: false);
        SetSynchronizationContext(new CaptureUiContext());
        return new Restore(previous, restore: true);
    }

    public override SynchronizationContext CreateCopy() => this;
    public override void Post(SendOrPostCallback callback, object? state) => _inner.Post(_ => Run(callback, state), null);
    public override void Send(SendOrPostCallback callback, object? state) => _inner.Send(_ => Run(callback, state), null);

    private void Run(SendOrPostCallback callback, object? state)
    {
        var previous = Current; SetSynchronizationContext(this);
        try { callback(state); } finally { SetSynchronizationContext(previous); }
    }

    private sealed class Restore(SynchronizationContext? previous, bool restore) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done || !restore) return;
            _done = true; SetSynchronizationContext(previous);
        }
    }
}

public enum HostMode { Offscreen, Hidden, Minimized }
public sealed record CapturedCard(CardSpec Spec, CardLayout Layout, Bitmap Bitmap, double Scale) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>Rhino-free CoreWebView2 host. Construct, use and dispose on one pumping UI thread.</summary>
public sealed class WebSurface : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private CoreWebView2Environment? _environment;
    private bool _failed;
    private bool _disposed;
    private readonly SemaphoreSlim _serial = new(1);
    // One stable profile instead of a new folder per process, which would leak disk space every session.
    // Concurrent Rhino instances share it safely because they use identical environment options.
    private readonly string _profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConduitPlus", "WebView2");
    public HostMode Mode { get; }
    public uint BrowserProcessId => _controller?.CoreWebView2.BrowserProcessId ?? 0;
    public string RuntimeVersion => _environment?.BrowserVersionString ?? "not initialized";
    public event Action? BrowserFailed;

    public WebSurface(HostMode mode = HostMode.Offscreen)
    {
        if (SynchronizationContext.Current is null) throw new InvalidOperationException("A pumping UI SynchronizationContext is required.");
        Mode = mode;
        // Tool window without activation, entirely outside the virtual desktop. No Rhino focus changes.
        _hwnd = CreateWindowEx(0x08000080, "STATIC", "Conduit+ capture host", 0x80000000, -20000, -20000, 1600, 1600, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        ShowWindow(_hwnd, mode == HostMode.Hidden ? 0 : mode == HostMode.Minimized ? 7 : 4);
    }

    private void AssertUi()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("WebView2 accessed outside its UI thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async Task InitializeAsync()
    {
        AssertUi();
        if (_controller is not null && !_failed) return;
        _controller?.Close(); _controller = null; _failed = false;
        _environment = await CoreWebView2Environment.CreateAsync(null, _profile, new CoreWebView2EnvironmentOptions("--disable-background-timer-throttling --disable-renderer-backgrounding"));
        AssertUi();
        var controller = await _environment.CreateCoreWebView2ControllerAsync(_hwnd);
        AssertUi();
        if (_disposed) { controller.Close(); throw new ObjectDisposedException(nameof(WebSurface)); }
        _controller = controller;
        _controller.Bounds = new Rectangle(0, 0, 1600, 1600);
        _controller.IsVisible = true;
        _controller.DefaultBackgroundColor = Color.Transparent;
        var web = _controller.CoreWebView2;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.ProcessFailed += (_, _) => { _failed = true; BrowserFailed?.Invoke(); };
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                e.Response = _environment.CreateWebResourceResponse(Stream.Null, 403, "Offline runtime", "");
        };
        await web.CallDevToolsProtocolMethodAsync("Emulation.setDefaultBackgroundColorOverride", "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? _, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new InvalidOperationException($"WebView2 navigation: {e.WebErrorStatus}"));
        }
        web.NavigationCompleted += Completed;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ConduitPlus.cards.html") ?? throw new InvalidOperationException("Missing embedded card runtime.");
            using var reader = new StreamReader(stream);
            web.NavigateToString(await reader.ReadToEndAsync());
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { AssertUi(); web.NavigationCompleted -= Completed; }
    }

    public async Task<IReadOnlyList<CapturedCard>> CaptureAsync(IReadOnlyList<CardSpec> cards, Theme theme, double scale)
    {
        AssertUi();
        await _serial.WaitAsync();
        var output = new List<CapturedCard>();
        try
        {
            await InitializeAsync();
            if (cards.Count == 0) return output;
            var web = _controller!.CoreWebView2;
            var width = (int)Math.Ceiling(cards.Max(c => c.Width) + 2);
            var height = (int)Math.Ceiling(cards.Sum(c => CardMeasure.Measure(c, theme).Height + 8));
            await web.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", CardJson.Serialize(new { width, height, deviceScaleFactor = scale, mobile = false }));
            // Strings are encoded as JS string literals; no data is interpolated as executable code.
            var script = $"window.conduit.render({JsonSerializer.Serialize(CardJson.Serialize(cards))},{JsonSerializer.Serialize(CardJson.Serialize(theme))},{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            var metadata = await web.ExecuteScriptAsync(script);
            var layouts = CardJson.Deserialize<CardLayout[]>(metadata);
            if (layouts.Length != cards.Count) throw new InvalidOperationException("Runtime returned incomplete card metadata.");
            foreach (var layout in layouts)
            {
                var spec = cards.Single(c => c.Id == layout.Id);
                var measured = CardMeasure.Measure(spec, theme);
                if (layout.Rev != spec.Rev || layout.W != measured.Width || layout.H != measured.Height || CardJson.Serialize(layout.HitRegions) != CardJson.Serialize(CardMeasure.Hits(spec, theme)))
                    throw new InvalidOperationException($"Web/native layout contract mismatch for {spec.Id}.");
                var png = await web.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", CardJson.Serialize(new { format = "png", clip = new { x = layout.X, y = layout.Y, width = layout.W, height = layout.H, scale = 1 }, fromSurface = true, captureBeyondViewport = true }));
                using var result = JsonDocument.Parse(png);
                using var bytes = new MemoryStream(Convert.FromBase64String(result.RootElement.GetProperty("data").GetString()!));
                using var image = new Bitmap(bytes);
                if (Math.Abs(image.Width - layout.W * scale) > 1 || Math.Abs(image.Height - layout.H * scale) > 1)
                    throw new InvalidOperationException("WebView2 returned a bitmap at an unexpected device scale.");
                output.Add(new(spec, layout, new Bitmap(image), scale));
            }
            return output;
        }
        catch { foreach (var c in output) c.Dispose(); throw; }
        finally { _serial.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        AssertUi(); _disposed = true;
        _controller?.Close(); _controller = null;
        DestroyWindow(_hwnd);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
}
