using ConduitPlus.Core;
using ConduitPlus.Rendering;

namespace ConduitPlus.Rhino;

/// <summary>UI-thread cache: one bitmap/DisplayBitmap pair per live card, revision and current scale.</summary>
internal sealed class RasterCardRenderer : IDisposable
{
    private Dictionary<string, BitmapCard> _prepared = new(StringComparer.Ordinal);
    private Theme? _theme;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public double Scale { get; private set; }
    public IReadOnlyDictionary<string, BitmapCard> Prepared => _prepared;
    public long RenderCount { get; private set; }
    public void Prepare(CardSpec[] cards, Theme theme, double scale)
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Raster cache accessed outside its owning draw/UI thread.");
        var next = new Dictionary<string, BitmapCard>(StringComparer.Ordinal);
        var created = new List<BitmapCard>();
        try
        {
            foreach (var spec in cards)
            {
                if (_theme == theme && _prepared.TryGetValue(spec.Id, out var old) && old.Capture.Spec.Rev == spec.Rev && old.Capture.Scale == scale)
                { next.Add(spec.Id, old); continue; }
                var size = CardMeasure.Measure(spec, theme);
                var capture = new CapturedCard(spec, new(spec.Id, spec.Rev, 0, 0, size.Width, size.Height, CardMeasure.Hits(spec, theme)), RasterDrawing.Render(spec, theme, scale), scale, RasterDrawing.Margin(theme, scale));
                BitmapCard replacement;
                try { replacement = new(capture); } catch { capture.Dispose(); throw; }
                created.Add(replacement); next.Add(spec.Id, replacement); RenderCount++;
            }
        }
        catch { foreach (var card in created) card.Dispose(); throw; }
        var previous = _prepared;
        _prepared = next; _theme = theme; Scale = scale; // Pixels, revision, spec and hits publish as a unit.
        foreach (var pair in previous)
            if (!next.TryGetValue(pair.Key, out var keep) || !ReferenceEquals(keep, pair.Value)) pair.Value.Dispose();
    }
    public void Dispose() { foreach (var card in _prepared.Values) card.Dispose(); _prepared.Clear(); }
}
