using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Shared UI-thread accounting for inline and pinned raster ownership, including replacement allocations.</summary>
public interface ICanvasRasterOwner
{
    object? RasterDocument { get; }
    string RasterOwner { get; }
    long RasterBytes { get; }
    long RasterLastVisible { get; }
    void EvictCanvasRasters();
}
public static class CanvasRasterCache
{
    private static readonly HashSet<ICanvasRasterOwner> Owners = [];
    public static void Register(ICanvasRasterOwner owner) => Owners.Add(owner);
    public static void Remove(ICanvasRasterOwner owner) => Owners.Remove(owner);
    public static void Reserve(ICanvasRasterOwner owner, long bytes)
    {
        Register(owner);
        var document = owner.RasterDocument;
        var entries = Owners.Where(o => ReferenceEquals(o.RasterDocument, document)).ToArray();
        var evictions = CanvasCacheBudget.Evictions(entries.Select(o => new CanvasCacheEntry(o.RasterOwner, o.RasterBytes, o.RasterLastVisible)).ToArray(), owner.RasterOwner, bytes);
        foreach (var entry in entries.Where(o => evictions.Contains(o.RasterOwner, StringComparer.Ordinal))) entry.EvictCanvasRasters();
    }
}
