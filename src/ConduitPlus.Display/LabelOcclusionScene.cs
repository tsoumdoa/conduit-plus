using System.Diagnostics;
using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

/// <summary>Experimental UI-thread scene collector. Advance between frames; workers own detached geometry.</summary>
internal sealed class LabelOcclusionScene : IDisposable
{
    private readonly RhinoDoc _document;
    private readonly GH_Document[] _definitions;
    private readonly IReadOnlyDictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?> _adapters;
    private readonly RhinoViewport _view;
    private readonly Dictionary<object, OccluderIndex> _trees = [];
    private readonly Dictionary<object, GeometryBase[]> _pending = [];
    private HashSet<object> _requestedKeys = [];
    private Task<BuildResult>? _worker;
    private long _generation;
    internal double LastWorkerMilliseconds { get; private set; }
    internal static readonly Guid CustomPreviewGuid = new("537b0419-bbc2-4ff4-bf08-afe526367b2c");
    private string? _meshSettings;
    private bool _disposed;

    internal LabelOcclusionScene(RhinoDoc document, RhinoViewport view, IEnumerable<GH_Document> definitions,
        IReadOnlyDictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?>? adapters = null)
    {
        _document = document;
        _view = view;
        _definitions = definitions.Distinct().ToArray();
        _adapters = adapters ?? new Dictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?>();

        RhinoDoc.AddRhinoObject += Changed;
        RhinoDoc.DeleteRhinoObject += Changed;
        RhinoDoc.UndeleteRhinoObject += Changed;
        RhinoDoc.ReplaceRhinoObject += Replaced;
        RhinoDoc.BeforeTransformObjects += Transforming;
        RhinoDoc.CloseDocument += Closing;
        foreach (var definition in _definitions) definition.SolutionEnd += Solved;
    }

    internal (OccluderIndex Index, long Generation)? Snapshot { get; private set; }

    private void Changed(object? sender, RhinoObjectEventArgs e) => ChangedObject(e.TheObject);

    private void ChangedObject(RhinoObject obj)
    {
        if (obj.Document?.RuntimeSerialNumber != _document.RuntimeSerialNumber) return;
        _trees.Remove((obj.RuntimeSerialNumber, MeshType.Render));
        _trees.Remove((obj.RuntimeSerialNumber, MeshType.Analysis));
        Invalidate();
    }

    private void Replaced(object? sender, RhinoReplaceObjectEventArgs e) => ChangedObject(e.OldRhinoObject);

    private void Transforming(object? sender, RhinoTransformObjectsEventArgs e)
    {
        foreach (var obj in e.Objects) ChangedObject(obj);
    }

    private void Closing(object? sender, DocumentEventArgs e)
    {
        if (e.Document.RuntimeSerialNumber == _document.RuntimeSerialNumber) Dispose();
    }

    private void Solved(object? sender, GH_SolutionEventArgs e)
    {
        foreach (var key in _trees.Keys.OfType<Guid>().ToArray()) _trees.Remove(key);
        Invalidate();
    }

    private void Invalidate()
    {
        _generation++;
        Snapshot = null;
        foreach (var geometry in _pending.Values.SelectMany(parts => parts)) geometry.Dispose();
        _pending.Clear();
    }

    // Membership is checked at refresh, so preview and attribute edits can reuse valid source trees.
    internal void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var settings = MeshingParameters.DocumentCurrentSetting(_document);
        var signature = settings.ToEncodedString();
        if (_meshSettings != signature)
        {
            _trees.Clear();
            _meshSettings = signature;
            Invalidate();
        }
        var sources = new Dictionary<object, Func<GeometryBase[]>>();
        foreach (var obj in _document.Objects.GetObjectList(ObjectType.Mesh | ObjectType.Brep | ObjectType.Surface | ObjectType.Extrusion | ObjectType.SubD))
        {
            if (obj.IsHidden || obj.IsDeleted || !LayerVisible(obj.Attributes.LayerIndex)) continue;
            var mode = DisplayModeDescription.GetDisplayMode(obj.Attributes.GetDisplayModeOverride(_view.Id)) ?? _view.DisplayMode;
            if (mode?.DisplayAttributes.ShadingEnabled != true) continue;
            if (obj.GetMaterial(true)?.Transparency > 0) continue;
            var meshType = (obj.GetActiveVisualAnalysisModes() ?? []).Any(m => m.Style != VisualAnalysisMode.AnalysisStyle.Wireframe)
                ? MeshType.Analysis : MeshType.Render;
            sources[(obj.RuntimeSerialNumber, meshType)] = () => ReadRhino(obj, meshType);
        }
        foreach (var definition in _definitions)
        {
            if (!definition.Enabled || definition.PreviewMode != GH_PreviewMode.Shaded) continue;
            foreach (var owner in definition.Objects)
            {
                if (owner is not IGH_PreviewObject { Hidden: false, IsPreviewCapable: true } ||
                    owner is not IGH_ActiveObject { Locked: false } ||
                    (definition.PreviewFilter == GH_PreviewFilter.Selected && !owner.Attributes.Selected)) continue;
                if (owner is GH_Component component)
                {
                    if (_adapters.TryGetValue(component.ComponentGuid, out var adapter))
                    {
                        if (adapter is not null)
                            sources[component.InstanceGuid] = () => adapter(component).Select(m => m.Duplicate()).ToArray();
                    }
                    else if (component.ComponentGuid == CustomPreviewGuid)
                    {
                        if (component.Params.Input[1].VolatileData.AllData(true).Any(g =>
                            g.ScriptVariable() is System.Drawing.Color { A: < 255 } ||
                            g.ScriptVariable() is DisplayMaterial { Transparency: > 0 } ||
                            g is GH_Material m && m.MaterialBestGuess()?.ToMaterial(global::Rhino.Render.RenderTexture.TextureGeneration.Disallow)?.Transparency > 0)) continue;
                        var geometry = component.Params.Input[0];
                        sources[geometry.InstanceGuid] = () => ReadParam(geometry);
                    }
                    else
                    {
                        foreach (var output in component.Params.Output)
                            if (output is IGH_PreviewObject { Hidden: false, IsPreviewCapable: true })
                                sources[output.InstanceGuid] = () => ReadParam(output);
                    }
                }
                else if (owner is IGH_Param parameter)
                    sources[owner.InstanceGuid] = () => ReadParam(parameter);
            }
        }
        if (!_requestedKeys.SetEquals(sources.Keys))
        {
            Invalidate();
            _requestedKeys = [.. sources.Keys];
        }

        if (_worker is { IsCompleted: true })
        {
            var finished = _worker;
            _worker = null;
            var built = finished.GetAwaiter().GetResult();
            if (built.Generation == _generation)
            {
                foreach (var (key, tree) in built.Trees) _trees[key] = tree;
                Snapshot = (built.Index, built.Generation);
                LastWorkerMilliseconds = built.WorkerMilliseconds;
            }
        }
        if (_worker is not null || Snapshot.HasValue) return;

        // Limit copying to one changed producer per refresh.
        foreach (var (key, read) in sources)
        {
            if (_trees.ContainsKey(key) || _pending.ContainsKey(key)) continue;
            _pending.Add(key, read());
            return;
        }
        var cached = sources.Keys.Where(_trees.ContainsKey).Select(key => _trees[key]).ToArray();
        var pending = _pending.ToArray();
        _pending.Clear();
        var workerSettings = new MeshingParameters(settings);
        var generation = _generation;
        _worker = Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var trees = pending.ToDictionary(entry => entry.Key, entry =>
                    OccluderIndex.Build(Triangulate(entry.Value.SelectMany(g => Meshes(g, workerSettings)))));
                var index = OccluderIndex.Combine(cached.Concat(trees.Values));
                return new BuildResult(generation, trees, index, timer.Elapsed.TotalMilliseconds);
            }
            finally
            {
                workerSettings.Dispose();
                foreach (var entry in pending)
                    foreach (var geometry in entry.Value) geometry.Dispose();
            }
        });
    }

    private bool LayerVisible(int index)
    {
        var layer = _document.Layers[index];
        while (layer is not null)
        {
            if (!layer.IsVisible || !layer.PerViewportIsVisible(_view.Id)) return false;
            layer = layer.ParentLayerId == Guid.Empty ? null : _document.Layers.FindId(layer.ParentLayerId);
        }
        return true;
    }

    private static GeometryBase[] ReadRhino(RhinoObject obj, MeshType meshType)
    {
        if (obj.Geometry is Mesh mesh) return [mesh.DuplicateMesh()];
        var existing = obj.GetMeshes(meshType);
        if (meshType == MeshType.Analysis && existing is not { Length: > 0 }) existing = obj.GetMeshes(MeshType.Render);
        if (existing is { Length: > 0 }) return existing.Select(m => m.DuplicateMesh()).ToArray();
        return [obj.Geometry.Duplicate()];
    }

    private static GeometryBase[] ReadParam(IGH_Param param) =>
        param.VolatileData.AllData(true).OfType<IGH_GeometricGoo>()
            .Select(goo => goo.ScriptVariable())
            .Where(geometry => geometry is Mesh or Brep or Surface or Extrusion or SubD)
            .Cast<GeometryBase>().Select(geometry => geometry.Duplicate()).ToArray();

    private static IEnumerable<Mesh> Meshes(GeometryBase? geometry, MeshingParameters settings)
    {
        if (geometry is Mesh mesh)
        {
            yield return mesh;
            yield break;
        }
        if (geometry is SubD subd)
        {
            using var generated = Mesh.CreateFromSubD(subd, (int)settings.SubDDisplayParameters().DisplayDensity(subd));
            if (generated is not null) yield return generated;
            yield break;
        }
        if (geometry is not (Brep or Surface or Extrusion)) yield break;
        using var converted = geometry is Brep ? null : Brep.TryConvertBrep(geometry);
        var brep = geometry as Brep ?? converted;
        if (brep is null) yield break;
        foreach (var part in Mesh.CreateFromBrep(brep, settings) ?? [])
        {
            using (part) yield return part;
        }
    }

    internal static OcclusionTriangle[] Triangulate(IEnumerable<Mesh> meshes)
    {
        var result = new List<OcclusionTriangle>();
        foreach (var mesh in meshes)
        {
            foreach (var face in mesh.Faces)
            {
                if (!face.IsValid(mesh.Vertices.Count)) continue;
                var a = Point(mesh.Vertices.Point3dAt(face.A));
                var b = Point(mesh.Vertices.Point3dAt(face.B));
                var c = Point(mesh.Vertices.Point3dAt(face.C));
                result.Add(new(a, b, c));
                if (face.IsQuad) result.Add(new(a, c, Point(mesh.Vertices.Point3dAt(face.D))));
            }
        }
        return [.. result];
    }

    internal sealed class Projection
    {
        private readonly Transform _screen;
        private readonly Point3d _camera;
        private readonly Vector3d _direction;
        private readonly Point3d _near;
        private readonly Vector3d _right;
        private readonly Vector3d _down;
        private readonly double _nearDepth;
        private readonly double _farDepth;
        private readonly bool _parallel;
        private readonly double _worldUnitsPerPixel;
        private readonly System.Drawing.Size _size;

        internal Projection(RhinoViewport view, double deviceScale)
        {
            _screen = view.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
            _size = view.Size;
            _camera = view.CameraLocation;
            _direction = view.CameraDirection;
            _parallel = view.IsParallelProjection;
            var origin = LineAt(0, 0);
            _near = origin.From;
            _right = (LineAt(_size.Width, 0).From - _near) / _size.Width;
            _down = (LineAt(0, _size.Height).From - _near) / _size.Height;
            _nearDepth = Vector3d.Multiply(_near - _camera, _direction);
            _farDepth = Vector3d.Multiply(origin.To - _camera, _direction);
            _worldUnitsPerPixel = _right.Length / deviceScale;

            Line LineAt(double x, double y)
            {
                view.GetFrustumLine(x, y, out var line);
                if (Vector3d.Multiply(line.Direction, _direction) < 0) line.Flip();
                return line;
            }
        }

        internal OcclusionSegment? Project(Point3d anchor)
        {
            var depth = Vector3d.Multiply(anchor - _camera, _direction);
            if (depth <= 0 || depth < _nearDepth || depth > _farDepth) return null;
            var pixel = anchor;
            pixel.Transform(_screen);
            if (!(pixel.X >= 0 && pixel.Y >= 0 && pixel.X < _size.Width && pixel.Y < _size.Height)) return null;
            var factor = _parallel ? 1 : depth / _nearDepth;
            var start = _near + _right * pixel.X + _down * pixel.Y;
            return new(Point(start), Point(anchor), _worldUnitsPerPixel * factor);
        }
    }

    private static OcclusionPoint Point(Point3d p) => new(p.X, p.Y, p.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RhinoDoc.AddRhinoObject -= Changed;
        RhinoDoc.DeleteRhinoObject -= Changed;
        RhinoDoc.UndeleteRhinoObject -= Changed;
        RhinoDoc.ReplaceRhinoObject -= Replaced;
        RhinoDoc.BeforeTransformObjects -= Transforming;
        RhinoDoc.CloseDocument -= Closing;
        foreach (var definition in _definitions) definition.SolutionEnd -= Solved;
        _trees.Clear();
        Invalidate();
    }

    private sealed record BuildResult(long Generation, Dictionary<object, OccluderIndex> Trees, OccluderIndex Index, double WorkerMilliseconds);
}

