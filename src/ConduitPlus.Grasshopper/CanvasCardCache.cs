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
            // One card per tick bounds repaint work without starving the UI during large documents.
            entry.Regenerate();
            return;
        }
        if (QueueEntries.Count == 0 && !Entries.Any(e => e.HasGesture)) _timer?.Stop();
    }
    internal static long BitmapBudget(CanvasCardAttributes owner)
    {
        var document = owner.Component.OnPingDocument();
        return CanvasCacheBudget.PerVisibleCard(Entries.Count(e => ReferenceEquals(e.Component.OnPingDocument(), document) && e.Visible));
    }
    internal static void Reserve(CanvasCardAttributes owner, long bytes)
    {
        var document = owner.Component.OnPingDocument();
        var entries = Entries.Where(e => ReferenceEquals(e.Component.OnPingDocument(), document)).ToArray();
        var evict = CanvasCacheBudget.Evictions(entries.Where(e => e.Frame is not null).Select(e =>
            new CanvasCacheEntry(e.Component.InstanceGuid.ToString("N"), e.Frame!.Bytes, e.LastVisible)).ToArray(), owner.Component.InstanceGuid.ToString("N"), bytes);
        foreach (var entry in entries.Where(e => evict.Contains(e.Component.InstanceGuid.ToString("N"), StringComparer.Ordinal))) entry.ReleaseFrame();
    }
}

internal sealed class CanvasCardFrame(Bitmap bitmap, CardSpec[] specs, Theme theme, CanvasFrameKey key) : IDisposable
{
    internal Bitmap Bitmap { get; } = bitmap;
    internal CardSpec[] Specs { get; } = specs;
    internal CanvasStackLayout Layout { get; } = new(specs, theme);
    internal Theme Theme { get; } = theme;
    internal CanvasFrameKey Key { get; } = key;
    internal long Bytes => (long)Bitmap.Width * Bitmap.Height * 4;
    public void Dispose() => Bitmap.Dispose();
}
