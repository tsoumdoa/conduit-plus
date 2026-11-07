using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using Point = Rhino.Geometry.Point;

namespace ConduitPlus.Rhino;

/// <summary>Shared, privately owned geometry snapshot. Sessions borrow it and never dispose it.</summary>
public sealed partial class Link
{
    private sealed record Wires(Curve[] Curves, Line[] Lines, Point3d[] Points, Point3d[] Markers);
    private readonly GeometryBase[] _geometry;
    private readonly Dictionary<string, Wires[]> _wires;
    public IReadOnlyList<string> Keys { get; }
    public BoundingBox Bounds { get; }
    /// <summary>Draw every linked wire faintly (for commands without a Grasshopper preview, e.g. the demo).</summary>
    public bool ShowBase { get; init; }
    internal IEnumerable<string> LinkedKeys => _wires.Keys;
    public Link(IEnumerable<string> keys, IEnumerable<(string Key, object Geometry)> items)
        : this(keys, items.Select((p, i) => (p.Key, p.Geometry, $"Geometry item [{i}]"))) { }
    public Link(IEnumerable<string> keys, IEnumerable<(string Key, object Geometry, string Location)> items)
    {
        var declared = keys.Distinct(StringComparer.Ordinal).ToArray();
        if (declared.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Link keys must be nonempty.");
        Keys = Array.AsReadOnly(declared);
        var geometry = new List<GeometryBase>();
        var wires = new List<(string Key, Wires Wire)>();
        var picks = new List<(string Key, PickProxy Proxy)>();
        var bounds = BoundingBox.Empty;
        try
        {
            foreach (var (key, value, location) in items)
            {
                try
                {
                    if (!declared.Contains(key, StringComparer.Ordinal)) throw new ArgumentException($"Undeclared Link key '{key}'.");
                    var copy = Duplicate(value);
                    geometry.Add(copy);
                    if (!copy.IsValid) throw new ArgumentException("Invalid geometry.");
                    bounds.Union(copy.GetBoundingBox(true));
                    var wire = Prepare(copy);
                    wires.Add((key, wire));
                    picks.Add((key, PreparePick(copy, wire, geometry)));
                }
                catch (ArgumentException e) { throw new ArgumentException($"{location}: {e.Message}", e); }
            }
            _geometry = geometry.ToArray();
            _wires = wires.GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Wire).ToArray(), StringComparer.Ordinal);
            _picks = picks.GroupBy(p => p.Key, StringComparer.Ordinal).Select(g =>
            {
                var box = BoundingBox.Empty;
                foreach (var p in g) box.Union(p.Proxy.Bounds);
                return new PickGroup(g.Key, box.GetCorners(), g.Select(p => p.Proxy).ToArray());
            }).ToArray();
            Bounds = bounds;
        }
        catch
        {
            foreach (var wire in wires) foreach (var curve in wire.Wire.Curves) curve.Dispose();
            foreach (var g in geometry) g.Dispose();
            throw;
        }
    }
    private static GeometryBase Duplicate(object value) => value switch
    {
        Point3d p when p.IsValid => new Point(p),
        Line l when l.IsValid => new LineCurve(l),
        Polyline p when p.IsValid => new PolylineCurve(p),
        Point or Curve or Brep or Mesh or Surface or Extrusion => ((GeometryBase)value).Duplicate(),
        _ => throw new ArgumentException($"Unsupported or invalid Link geometry: {value?.GetType().Name ?? "null"}.")
    };
    private static Wires Prepare(GeometryBase geometry)
    {
        switch (geometry)
        {
            case Point p: return new([], [], [p.Location], [p.Location]);
            case Curve c:
                var curve = c.DuplicateCurve();
                return new([curve], [], [], [curve.PointAtStart, curve.PointAtEnd]);
            case Mesh m:
                var edges = m.TopologyEdges;
                var lines = Enumerable.Range(0, edges.Count).Select(edges.EdgeLine).ToArray();
                return new([], lines, [], lines.SelectMany(l => new[] { l.From, l.To }).Distinct().ToArray());
            case Extrusion e:
                using (var brep = e.ToBrep()) return BrepWires(brep);
            case Surface s:
                using (var brep = s.ToBrep()) return BrepWires(brep);
            case Brep b: return BrepWires(b);
            default: throw new ArgumentException("Unsupported Link geometry.");
        }
    }
    private static Wires BrepWires(Brep? brep)
    {
        var curves = brep?.DuplicateEdgeCurves() ?? throw new ArgumentException("Cannot extract brep edge curves.");
        return new(curves, [], [], curves.SelectMany(c => new[] { c.PointAtStart, c.PointAtEnd }).Distinct().ToArray());
    }
    internal void Draw(DisplayPipeline display, string key, Color color, int width, bool selected)
    {
        if (!_wires.TryGetValue(key, out var wires)) return;
        foreach (var w in wires)
        {
            foreach (var c in w.Curves) display.DrawCurve(c, color, width);
            foreach (var line in w.Lines) display.DrawLine(line, color, width);
            foreach (var p in selected ? w.Markers : w.Points) display.DrawPoint(p, PointStyle.RoundSimple, selected ? 5 : 3, color);
        }
        GC.KeepAlive(_geometry);
    }
}
