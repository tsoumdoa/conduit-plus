using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CanvasRasterAccountingTests
{
    private sealed class Owner(object document, string id, long bytes, long last) : ICanvasRasterOwner
    {
        public object RasterDocument => document;
        public string RasterOwner => id;
        public long RasterBytes { get; set; } = bytes;
        public long RasterLastVisible => last;
        public bool Evicted { get; private set; }
        public void EvictCanvasRasters() { Evicted = true; RasterBytes = 0; }
    }
    [Fact]
    public void InlineAndPinnedSharePeakBudgetAndDocumentsStayIsolated()
    {
        var document = new object(); var unit = CanvasRasterPlan.BitmapBudget;
        var inline = new Owner(document, "inline", unit, 20);
        var pinned = new Owner(document, "pinned", unit * 2, 1);
        var other = new Owner(new object(), "other", unit * 4, 0);
        try
        {
            CanvasRasterCache.Register(inline); CanvasRasterCache.Register(pinned); CanvasRasterCache.Register(other);
            // Old inline + old pinned + new inline would exceed 64 MiB. Evict only the other owner in this document.
            CanvasRasterCache.Reserve(inline, unit * 2);
            Assert.True(pinned.Evicted); Assert.False(inline.Evicted); Assert.False(other.Evicted);
            inline.RasterBytes = unit * 4;
            Assert.Throws<InvalidOperationException>(() => CanvasRasterCache.Reserve(inline, 4));
        }
        finally { CanvasRasterCache.Remove(inline); CanvasRasterCache.Remove(pinned); CanvasRasterCache.Remove(other); }
    }
}
