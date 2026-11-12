using System.Runtime.CompilerServices;
using ConduitPlus.Rendering;
using Xunit;

// TextMetrics.Measurer is process-wide; raster initialization and measurement probes
// must not switch it while another test is laying out or painting a card.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestTextMetrics
{
    // Geometry assertions use the production CSS metrics from the start, independent
    // of whether a raster test or CoreTests happens to run first.
    [ModuleInitializer]
    internal static void Initialize() => GdiCanvas.RegisterTextMetrics();
}
