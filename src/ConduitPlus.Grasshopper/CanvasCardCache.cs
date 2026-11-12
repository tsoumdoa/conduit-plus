using System.Drawing;
using ConduitPlus.Core;

namespace ConduitPlus.Grasshopper;

/// <summary>One UI-thread timer for all Canvas Cards. No solves, Rhino textures or per-card timers.</summary>
internal static class CanvasCardCache
{
    private static readonly HashSet<CanvasCardAttributes> Entries = [];
    private static readonly LinkedList<CanvasCardAttributes> QueueEntries = new();
    private static readonly Dictionary<CanvasCardAttributes, LinkedListNode<CanvasCardAttributes>> Queued = [];
    private static System.Windows.Forms.Timer? _timer;
    internal static void Register(CanvasCardAttributes entry)
    {
        Entries.Add(entry);
    }
    internal static void Queue(CanvasCardAttributes entry)
    {
        Register(entry);
        if (entry.Pending && !Queued.ContainsKey(entry)) Queued.Add(entry, QueueEntries.AddLast(entry));
        if (_timer is null) { _timer = new() { Interval = 16 }; _timer.Tick += Tick; }
        _timer.Start();
    }
    internal static void Remove(CanvasCardAttributes entry)
    {
        Entries.Remove(entry);
        if (Queued.Remove(entry, out var node)) QueueEntries.Remove(node);
        if (Entries.Count != 0 || _timer is null) return;
        _timer.Stop(); _timer.Tick -= Tick; _timer.Dispose(); _timer = null;
    }
    private static void Tick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        foreach (var entry in Entries.ToArray())
            entry.CheckCapture();
        // FIFO fairness also matters when an eviction makes an earlier component dirty again.
        var count = QueueEntries.Count;
        for (var i = 0; i < count; i++)
        {
            var entry = QueueEntries.First!.Value;
            QueueEntries.RemoveFirst(); Queued.Remove(entry);
            if (!entry.Pending) continue;
            if (!entry.Visible) { entry.Pending = false; continue; }
            if (now < entry.SettleAt) { Queued.Add(entry, QueueEntries.AddLast(entry)); continue; }
            // One component per tick bounds repaint work without starving the UI during large documents.
            entry.Regenerate();
            return;
        }
        if (QueueEntries.Count == 0 && !Entries.Any(e => e.HasGesture)) _timer?.Stop();
    }
    internal static void Reserve(CanvasCardAttributes owner, long bytes)
    {
        Register(owner);
        var document = owner.Component.OnPingDocument();
        var entries = Entries.Where(e => ReferenceEquals(e.Component.OnPingDocument(), document)).ToArray();
        var evict = CanvasCacheBudget.Evictions(entries.Where(e => e.Frame is not null).Select(e =>
            new CanvasCacheEntry(e.Component.InstanceGuid.ToString("N"), e.Frame!.Bytes, e.LastVisible)).ToArray(), owner.Component.InstanceGuid.ToString("N"), bytes);
        foreach (var entry in entries.Where(e => evict.Contains(e.Component.InstanceGuid.ToString("N"), StringComparer.Ordinal))) entry.ReleaseFrame();
    }
}

internal sealed class CanvasCardFrame : IDisposable
{
    internal Bitmap?[] Bitmaps { get; }
    internal CanvasRasterPlan[] Rasters { get; }
    internal CardSpec[] Specs { get; }
    internal CanvasStackLayout Layout { get; }
    internal Theme Theme => Key.Theme;
    internal CanvasStackFrameKey Key { get; }
    internal long Bytes => Bitmaps.Sum(b => b is null ? 0 : (long)b.Width * b.Height * 4);
    internal CanvasCardFrame(CardSpec[] specs, CanvasStackFrameKey key)
    {
        Specs = specs; Key = key; Layout = new(specs, key.Theme);
        Rasters = CanvasStackRaster.Plan(Layout, key.Theme, key.Scale);
        Bitmaps = new Bitmap?[specs.Length];
        try
        {
            foreach (var i in CanvasStackRaster.RetainedCards(Rasters, key.VisibleCards, CanvasRasterPlan.DocumentBudget / 2))
                Bitmaps[i] = ConduitPlus.Rendering.RasterDrawing.Render(specs[i], key.Theme, Rasters[i].Scale);
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var bitmap in Bitmaps) bitmap?.Dispose(); }
}
