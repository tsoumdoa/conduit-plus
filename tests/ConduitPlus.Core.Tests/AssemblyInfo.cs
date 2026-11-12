using Xunit;

// Layout and GDI tests share TextMetrics.Measurer, including fallback-measurement tests.
// A measurement provider must stay fixed for the whole measure/paint/assert transaction.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
