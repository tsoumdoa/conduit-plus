namespace ConduitPlus.Core;

/// <summary>Authoritative piecewise interpolation in sRGB bytes, for model and renderers.</summary>
public sealed class ColourGradient
{
    private readonly ColorRgba[] _colours;
    private readonly double[] _positions;

    public ColourGradient(IReadOnlyList<string> colours, IReadOnlyList<double> positions)
    {
        _colours = colours.Select(ColorRgba.Parse).ToArray();
        _positions = positions.Count == 0
            ? Enumerable.Range(0, colours.Count).Select(i => (double)i / (colours.Count - 1)).ToArray()
            : positions.ToArray();
    }

    public ColorRgba ColourAt(double position)
    {
        var p = Math.Clamp(position, 0, 1);
        var i = 0;
        while (i < _positions.Length - 2 && p > _positions[i + 1]) i++;
        return Interpolate(_colours[i], _colours[i + 1], (p - _positions[i]) / (_positions[i + 1] - _positions[i]));
    }

    /// <summary>PDF bands follow every exact stop interval; adjacent bands share boundaries.</summary>
    public static IEnumerable<(Rect2 Rect, ColorRgba Colour)> Bands(Rect2 rect, IReadOnlyList<string> colours,
        IReadOnlyList<double> positions, int totalBands = 256)
    {
        var gradient = new ColourGradient(colours, positions);
        for (var knot = 0; knot < gradient._positions.Length - 1; knot++)
        {
            var start = gradient._positions[knot]; var end = gradient._positions[knot + 1];
            var bandsPerInterval = Math.Max(4, (int)Math.Ceiling(totalBands * (end - start)));
            for (var i = 0; i < bandsPerInterval; i++)
            {
                var a = start + (end - start) * i / bandsPerInterval;
                var b = start + (end - start) * (i + 1) / bandsPerInterval;
                var left = rect.X + a * rect.Width; var right = rect.X + b * rect.Width;
                yield return (new(left, rect.Y, right - left, rect.Height),
                    Interpolate(gradient._colours[knot], gradient._colours[knot + 1], (i + .5) / bandsPerInterval));
            }
        }
    }

    public static ColorRgba Interpolate(ColorRgba a, ColorRgba b, double fraction)
    {
        var t = Math.Clamp(fraction, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * t, MidpointRounding.AwayFromZero);
        return new(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), Mix(a.A, b.A));
    }
}
