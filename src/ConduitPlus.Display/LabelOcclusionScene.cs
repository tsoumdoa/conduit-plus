using System.Collections.Concurrent;
using System.Diagnostics;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

/// <summary>Experimental UI-thread scene collector. Advance between frames; workers own detached geometry.</summary>
internal sealed class LabelOcclusionScene : IDisposable
{
    private readonly RhinoDoc _document;
    internal static Func<RhinoDoc, ILabelOcclusionSources>? PreviewSources { get; set; }
    private readonly ILabelOcclusionSources? _preview;
    private int _previewRevision = -1;
    private readonly record struct PreviewKey(object Key);
    private IEnumerator<DetachedPart>? _items;
    private IEnumerator<KeyValuePair<object, Func<IEnumerable<GeometryBase>>>>? _sources;
    private object? _sourceKey;
    private readonly ConcurrentQueue<uint> _changedObjects = new();
    private int _membershipChanged;
    private readonly UiTimer _continuation = new(15);
    private bool _redrawPending;
    private bool _membershipRequested;
    private long _lastMembershipCheck;
    internal void RequestMembershipCheck() => _membershipRequested = true;
    private bool _checkBeforePublish;
    internal double LongestUiSliceMilliseconds { get; set; }
    private readonly RhinoViewport _view;
    private readonly Dictionary<object, OccluderIndex> _trees = [];
    private readonly Dictionary<object, List<DetachedPart>> _pending = [];
    private HashSet<object> _requestedKeys = [];
    private Task<BuildResult>? _worker;
    private long _generation;
    internal double LastWorkerMilliseconds { get; private set; }
    private string? _meshSettings;
    private bool _disposed;
    internal bool IsDisposed => _disposed;
    internal int HarvestCount { get; private set; }

    internal LabelOcclusionScene(RhinoDoc document, RhinoViewport view, ILabelOcclusionSources? preview = null)
    {
        _document = document;
        _view = view;
        _preview = preview ?? PreviewSources?.Invoke(document);

        RhinoDoc.AddRhinoObject += Changed;
        RhinoDoc.DeleteRhinoObject += Changed;
        RhinoDoc.UndeleteRhinoObject += Changed;
        RhinoDoc.ReplaceRhinoObject += Replaced;
        RhinoDoc.BeforeTransformObjects += Transforming;
        RhinoDoc.CloseDocument += Closing;
        RhinoDoc.LayerTableEvent += LayerChanged;
        RhinoDoc.ModifyObjectAttributes += AttributesChanged;
        RhinoApp.Idle += OnIdle;
        _continuation.Tick += Continue;
    }

    internal (OccluderIndex Index, long Generation)? Snapshot { get; private set; }

    private void Changed(object? sender, RhinoObjectEventArgs e) => ChangedObject(e.TheObject);

    private void ChangedObject(RhinoObject obj)
    {
        if (obj.Document?.RuntimeSerialNumber != _document.RuntimeSerialNumber) return;
        _changedObjects.Enqueue(obj.RuntimeSerialNumber);
    }

    private void LayerChanged(object? sender, global::Rhino.DocObjects.Tables.LayerTableEventArgs e)
    {
        if (e.Document.RuntimeSerialNumber == _document.RuntimeSerialNumber)
            Interlocked.Exchange(ref _membershipChanged, 1);
    }

    private void AttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        if (e.Document.RuntimeSerialNumber == _document.RuntimeSerialNumber)
            Interlocked.Exchange(ref _membershipChanged, 1);
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

    private void Invalidate()
    {
        _generation++;
        _items?.Dispose();
        _items = null;
        _sources?.Dispose();
        _sources = null;
        Snapshot = null;
        foreach (var part in _pending.Values.SelectMany(parts => parts)) part.Geometry?.Dispose();
        _pending.Clear();
    }

    // Membership is checked at refresh, so preview and attribute edits can reuse valid source trees.
    internal void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Advance(checkMembership: true);
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        if (_disposed || _continuation.Enabled) return;
        var checkMembership = _membershipRequested && Stopwatch.GetElapsedTime(_lastMembershipCheck).TotalMilliseconds >= 100;
        if (checkMembership)
        {
            _membershipRequested = false;
            _lastMembershipCheck = Stopwatch.GetTimestamp();
        }
        Advance(checkMembership);
    }

    private void Continue(object? sender, EventArgs e)
    {
        if (!_disposed) Advance(checkMembership: false);
    }

    private void Advance(bool checkMembership)
    {
        var timer = Stopwatch.StartNew();
        try { AdvanceSlice(checkMembership, timer); }
        finally { LongestUiSliceMilliseconds = Math.Max(LongestUiSliceMilliseconds, timer.Elapsed.TotalMilliseconds); }
        if (_redrawPending && !_disposed)
        {
            _redrawPending = false;
            _view.ParentView?.Redraw();
        }
    }

    private void AdvanceSlice(bool checkMembership, Stopwatch timer)
    {
        if (!_changedObjects.IsEmpty)
        {
            while (_changedObjects.TryDequeue(out var serial))
            {
                _trees.Remove((serial, MeshType.Render));
                _trees.Remove((serial, MeshType.Analysis));
            }
            Invalidate();
        }
        if (Interlocked.Exchange(ref _membershipChanged, 0) != 0)
        {
            checkMembership = true;
            if (!Snapshot.HasValue) Invalidate();
        }
        var previewRevision = _preview?.Revision ?? -1;
        if (previewRevision != _previewRevision)
        {
            _previewRevision = previewRevision;
            foreach (var key in _trees.Keys.Where(key => key is PreviewKey).ToArray()) _trees.Remove(key);
            Invalidate();
        }

        _checkBeforePublish |= checkMembership;
        if ((checkMembership && Snapshot.HasValue) || (_sources is null && _worker is null && !Snapshot.HasValue))
        {
            ScanSources();
            _checkBeforePublish = false;
        }
        if (_worker is { IsCompleted: true })
        {
            if (_checkBeforePublish)
            {
                ScanSources();
                _checkBeforePublish = false;
            }
            var finished = _worker;
            _worker = null;
            var built = finished.GetAwaiter().GetResult();
            if (built.Generation == _generation)
            {
                foreach (var (key, tree) in built.Trees) _trees[key] = tree;
                Snapshot = (built.Index, built.Generation);
                LastWorkerMilliseconds = built.WorkerMilliseconds;
                _continuation.Stop();
                _redrawPending = true;
                return;
            }
        }
        if (Snapshot.HasValue) return;
        _continuation.Start();
        if (_worker is not null || _sources is null) return;

        while (timer.Elapsed.TotalMilliseconds < 3)
        {
            if (_items is not null)
            {
                if (_items.MoveNext())
                {
                    _pending[_sourceKey!].Add(_items.Current);
                    continue;
                }
                _items.Dispose();
                _items = null;
            }
            if (!_sources.MoveNext())
            {
                _sources.Dispose();
                _sources = null;
                using var settings = MeshingParameters.DocumentCurrentSetting(_document);
                StartWorker(settings);
                return;
            }
            var (key, read) = _sources.Current;
            if (_trees.ContainsKey(key)) continue;
            _sourceKey = key;
            _pending.Add(key, []);
            _items = read().SelectMany(DetachGeometry).GetEnumerator();
        }
    }

    private void ScanSources()
    {
        using var settings = MeshingParameters.DocumentCurrentSetting(_document);
        var signature = settings.ToEncodedString();
        if (_meshSettings != signature)
        {
            _trees.Clear();
            _meshSettings = signature;
            Invalidate();
        }
        var sources = new Dictionary<object, Func<IEnumerable<GeometryBase>>>();
        foreach (var obj in _document.Objects.GetObjectList(ObjectType.Mesh | ObjectType.Brep | ObjectType.Surface | ObjectType.Extrusion | ObjectType.SubD))
        {
            if (obj.IsHidden || obj.IsDeleted) continue;
            if (!LayerVisible(obj.Attributes.LayerIndex, out var layerLocked)) continue;
            var mode = DisplayModeDescription.GetDisplayMode(obj.Attributes.GetDisplayModeOverride(_view.Id)) ?? _view.DisplayMode;
            if (mode?.DisplayAttributes.ShadingEnabled != true) continue;
            var locked = obj.IsLocked || layerLocked;
            var showLocked = !mode.DisplayAttributes.GhostLockedObjects || mode.DisplayAttributes.LockedObjectTransparency < 1;
            if (locked && !showLocked) continue;
            if (obj.GetMaterial(true)?.Transparency > 0) continue;
            var meshType = (obj.GetActiveVisualAnalysisModes() ?? []).Any(m => m.Style != VisualAnalysisMode.AnalysisStyle.Wireframe)
                ? MeshType.Analysis : MeshType.Render;
            sources[(obj.RuntimeSerialNumber, meshType)] = () => ReadRhino(obj, meshType);
        }
        if (_preview is not null)
            foreach (var source in _preview.GetSources(_view)) sources[new PreviewKey(source.Key)] = source.Read;
        if (!_requestedKeys.SetEquals(sources.Keys))
        {
            Invalidate();
            _requestedKeys = [.. sources.Keys];
            foreach (var key in _trees.Keys.Where(key => !_requestedKeys.Contains(key)).ToArray())
                _trees.Remove(key);
        }
        if (!Snapshot.HasValue && _worker is null && _sources is null)
        {
            HarvestCount++;
            _sources = sources.GetEnumerator();
        }
    }

    private void StartWorker(MeshingParameters settings)
    {
        var cached = _requestedKeys.Where(_trees.ContainsKey).Select(key => _trees[key]).ToArray();
        var pending = _pending.ToArray();
        _pending.Clear();
        var workerSettings = new MeshingParameters(settings);
        var generation = _generation;
        _worker = Task.Run(() =>
        {
            var workerTimer = Stopwatch.StartNew();
            try
            {
                var trees = pending.ToDictionary(entry => entry.Key, entry =>
                    OccluderIndex.Build(entry.Value.SelectMany(part => part.Triangles ?? Triangulate(Meshes(part.Geometry, workerSettings)))));
                var index = OccluderIndex.Combine(cached.Concat(trees.Values));
                return new BuildResult(generation, trees, index, workerTimer.Elapsed.TotalMilliseconds);
            }
            finally
            {
                workerSettings.Dispose();
                foreach (var entry in pending)
                    foreach (var part in entry.Value) part.Geometry?.Dispose();
            }
        });
    }

    private bool LayerVisible(int index, out bool locked)
    {
        locked = false;
        var layer = _document.Layers[index];
        while (layer is not null)
        {
            if (!layer.IsVisible || !layer.PerViewportIsVisible(_view.Id)) return false;
            locked |= layer.IsLocked;
            layer = layer.ParentLayerId == Guid.Empty ? null : _document.Layers.FindId(layer.ParentLayerId);
        }
        return true;
    }

    private static IEnumerable<GeometryBase> ReadRhino(RhinoObject obj, MeshType meshType)
    {
        if (obj.Geometry is Mesh mesh) return [mesh];
        var existing = obj.GetMeshes(meshType);
        if (meshType == MeshType.Analysis && existing is not { Length: > 0 }) existing = obj.GetMeshes(MeshType.Render);
        return existing is { Length: > 0 } ? existing : [obj.Geometry];
    }

    private readonly record struct DetachedPart(GeometryBase? Geometry, OcclusionTriangle[]? Triangles);

    private static IEnumerable<DetachedPart> DetachGeometry(GeometryBase geometry)
    {
        if (geometry is not Mesh mesh || mesh.Faces.Count <= 8192)
        {
            yield return new(geometry.Duplicate(), null);
            yield break;
        }

        // Copy only face geometry for large meshes, without a monolithic native duplicate.
        const int facesPerChunk = 512;
        var count = mesh.Faces.Count;
        var vertices = mesh.Vertices;
        var vertexCount = vertices.Count;
        for (var start = 0; start < count; start += facesPerChunk)
        {
            var chunk = new List<OcclusionTriangle>(facesPerChunk);
            for (var i = start; i < Math.Min(start + facesPerChunk, count); i++)
            {
                var face = mesh.Faces[i];
                if (!face.IsValid(vertexCount)) continue;
                var a = Point(vertices.Point3dAt(face.A));
                var b = Point(vertices.Point3dAt(face.B));
                var c = Point(vertices.Point3dAt(face.C));
                chunk.Add(new(a, b, c));
                if (face.IsQuad) chunk.Add(new(a, c, Point(vertices.Point3dAt(face.D))));
            }
            yield return new(null, chunk.ToArray());
        }
    }

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
        _preview?.Dispose();
        RhinoApp.Idle -= OnIdle;
        _continuation.Tick -= Continue;
        _continuation.Dispose();
        RhinoDoc.LayerTableEvent -= LayerChanged;
        RhinoDoc.ModifyObjectAttributes -= AttributesChanged;
        _trees.Clear();
        Invalidate();
    }

    private sealed record BuildResult(long Generation, Dictionary<object, OccluderIndex> Trees, OccluderIndex Index, double WorkerMilliseconds);
}
