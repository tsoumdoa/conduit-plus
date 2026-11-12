using System.Drawing;
using ConduitPlus.Core;
using Rhino.Display;
using Rhino.Geometry;
using Point = Rhino.Geometry.Point;

namespace ConduitPlus.Rhino;

/// <summary>Shared, privately owned geometry snapshot. Sessions borrow it and never dispose it.</summary>
public sealed partial class Link
{
    private sealed record Wires(Curve[] Curves, Line[] Lines, Point3d[] Points);
    private readonly GeometryBase[] _geometry;
    private readonly Dictionary<string, Wires[]> _wires;
    private readonly Dictionary<string, Mesh[]> _faces;
    // A neutral 20% wash preserves the original model/category preview underneath.
    private readonly DisplayMaterial _selectedWash = new(Color.White, 1 - GeometryHighlight.Selected.WhiteWashOpacity);
    public IReadOnlyList<string> Keys { get; }
    public BoundingBox Bounds { get; }
    private readonly int _objectCount;
    public IEnumerable<string> LinkedGeometryKeys => _wires.Keys;
    public override string ToString() => $"Link · {Keys.Count} keys · {_objectCount} objects";
    /// <summary>Draw secondary linked wires (for commands without a Grasshopper preview, e.g. the demo).</summary>
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
                    // Drawing boundary/feature mesh edges must not remove the existing edge-picking tolerance.
                    var pickWire = copy is Mesh mesh ? wire with
                    { Lines = Enumerable.Range(0, mesh.TopologyEdges.Count).Select(mesh.TopologyEdges.EdgeLine).ToArray() } : wire;
                    picks.Add((key, PreparePick(copy, pickWire, geometry)));
                }
                catch (ArgumentException e) { throw new ArgumentException($"{location}: {e.Message}", e); }
            }
            _objectCount = wires.Count;
            _geometry = geometry.ToArray();
            _wires = wires.GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Wire).ToArray(), StringComparer.Ordinal);
            // Reuse privately owned pick meshes; no meshing/material allocation during a frame.
            _faces = picks.GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.SelectMany(p => p.Proxy.Meshes).ToArray(), StringComparer.Ordinal);
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
            case Point p: return new([], [], [p.Location]);
            case Curve c:
                var curve = c.DuplicateCurve();
                return new([curve], [], []);
            case Mesh m:
                return new([], MeshOutline(m), []);
            case Extrusion e:
                using (var brep = e.ToBrep()) return BrepWires(brep);
            case Surface s:
                using (var brep = s.ToBrep()) return BrepWires(brep);
            case Brep b: return BrepWires(b);
            default: throw new ArgumentException("Unsupported Link geometry.");
        }
    }
    // Prepare once for this privately owned mesh. Each connected topology component needs
    // feedback, even when another component supplies sharp edges (e.g. a joined box + sphere).
    private static Line[] MeshOutline(Mesh mesh)
    {
        var edges = mesh.TopologyEdges;
        var parents = Enumerable.Range(0, mesh.TopologyVertices.Count).ToArray();
        int Root(int vertex)
        {
            while (parents[vertex] != vertex)
            {
                parents[vertex] = parents[parents[vertex]];
                vertex = parents[vertex];
            }
            return vertex;
        }
        for (var i = 0; i < edges.Count; i++)
        {
            var pair = edges.GetTopologyVertices(i);
            var a = Root(pair.I); var b = Root(pair.J);
            // Deterministic union; path compression keeps subsequent lookups cheap.
            parents[Math.Max(a, b)] = Math.Min(a, b);
        }
        mesh.FaceNormals.ComputeFaceNormals();
        var features = new bool[edges.Count];
        var hasFeatures = new bool[parents.Length];
        var creaseCosine = Math.Cos(Math.PI / 6);
        for (var i = 0; i < edges.Count; i++)
        {
            var faces = edges.GetConnectedFaces(i);
            features[i] = faces.Length != 2 || Vector3d.Multiply(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]) < creaseCosine;
            if (features[i]) hasFeatures[Root(edges.GetTopologyVertices(i).I)] = true;
        }
        var lines = new List<Line>();
        for (var i = 0; i < edges.Count; i++)
            if (features[i] || !hasFeatures[Root(edges.GetTopologyVertices(i).I)]) lines.Add(edges.EdgeLine(i));
        return lines.ToArray();
    }
    private static Wires BrepWires(Brep? brep)
    {
        var curves = brep?.DuplicateEdgeCurves() ?? throw new ArgumentException("Cannot extract brep edge curves.");
        return new(curves, [], []);
    }
    internal void DrawFaces(DisplayPipeline display, string key)
    {
        if (_faces.TryGetValue(key, out var meshes))
            foreach (var mesh in meshes) display.DrawMeshShaded(mesh, _selectedWash);
    }
    internal void Draw(DisplayPipeline display, string key, Color color, int width, bool selected, bool shade = true)
    {
        if (!_wires.TryGetValue(key, out var wires)) return;
        if (shade && selected) DrawFaces(display, key);
        foreach (var w in wires)
        {
            foreach (var c in w.Curves) display.DrawCurve(c, color, width);
            foreach (var line in w.Lines) display.DrawLine(line, color, width);
            foreach (var p in w.Points) display.DrawPoint(p, PointStyle.RoundSimple, width + 1, color);
        }
        GC.KeepAlive(_geometry);
    }
}
