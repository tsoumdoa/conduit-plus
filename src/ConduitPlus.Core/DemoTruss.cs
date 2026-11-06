namespace ConduitPlus.Core;

public sealed record DemoMember(string Key, string Type, Point2 Start, Point2 End, double Utilisation)
{
    public double Length => Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
    public string Label => Key;
}
public sealed record DemoTrussResult(DemoMember[] Members, double[] Positions, double[] Deflection, double Mass)
{
    public double MaxUtilisation => Members.Max(m => m.Utilisation);
    public double MaxDeflection => Deflection.Max();
    public int Passing => Members.Count(m => m.Utilisation <= 1);
}

/// <summary>Illustrative deterministic dashboard data, NOT structural analysis or design verification.
/// Coordinates are metres; load kN/m; deflections mm; mass tonnes. Fixed fictitious capacities,
/// EI and areas deliberately put four default chord members over 1.0.</summary>
public static class DemoTruss
{
    public static DemoTrussResult Create(double span = 24, int bays = 8, double depth = 3, double load = 12)
    {
        if (!double.IsFinite(span) || span is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(span), "Span must be > 0 and <= 1000 m.");
        if (bays is < 2 or > 40) throw new ArgumentOutOfRangeException(nameof(bays), "Bays must be 2–40.");
        if (!double.IsFinite(depth) || depth is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be > 0 and <= 100 m.");
        if (!double.IsFinite(load) || load is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(load), "Load must be 0–10000 kN/m.");
        var step = span / bays; var members = new List<DemoMember>();
        for (var chord = 0; chord < 2; chord++)
        for (var i = 0; i < bays; i++)
        {
            var x = (i + .5) * step;
            var moment = load * x * (span - x) / 2;
            Add(chord == 0 ? "Lower" : "Upper", new(i * step, chord * depth), new((i + 1) * step, chord * depth), moment / depth / 260);
        }
        for (var i = 0; i <= bays; i++)
            Add("Post", new(i * step, 0), new(i * step, depth), load * step / 120);
        for (var i = 0; i < bays; i++)
        {
            var shear = Math.Abs(load * (span / 2 - (i + .5) * step));
            var force = shear * Math.Sqrt(step * step + depth * depth) / depth;
            Add("Diagonal", new(i * step, i < bays / 2 ? depth : 0), new((i + 1) * step, i < bays / 2 ? 0 : depth), force / 230);
        }
        var positions = Enumerable.Range(0, bays + 1).Select(i => i * step).ToArray();
        // Simply-supported uniform-load curve with fictitious E=200 GPa, I proportional to depth².
        var ei = 200e6 * .012 * Math.Pow(depth / 3, 2);
        var deflection = positions.Select(x => 1000 * load * x * (Math.Pow(span, 3) - 2 * span * x * x + x * x * x) / (24 * ei)).ToArray();
        var mass = members.Sum(m => m.Length * (m.Type is "Lower" or "Upper" ? .004 : .0025) * 7.85);
        return new(members.ToArray(), positions, deflection, mass);
        void Add(string type, Point2 a, Point2 b, double utilisation) => members.Add(new($"M{members.Count + 1:00}", type, a, b, utilisation));
    }
}
