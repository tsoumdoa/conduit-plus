using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Components;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Grasshopper;

internal sealed class GhLabelOcclusionSources(
    Func<IEnumerable<GH_Document>> definitions,
    IReadOnlyDictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?>? adapters = null) : ILabelOcclusionSources
{
    internal static readonly Guid CustomPreviewGuid = new("537b0419-bbc2-4ff4-bf08-afe526367b2c");
    private readonly HashSet<GH_Document> _subscribed = [];
    private readonly IReadOnlyDictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?> _adapters =
        adapters ?? new Dictionary<Guid, Func<GH_Component, IEnumerable<Mesh>>?>();
    private int _revision;
    private int _documentsChanged = 1;
    private GH_DocumentServer? _server;
    private RhinoDoc? _document;
    private bool _wasActive;
    public int Revision
    {
        get
        {
            var active = _document is null ||
                global::Grasshopper.Instances.ActiveRhinoDoc?.RuntimeSerialNumber == _document.RuntimeSerialNumber;
            if (_server is not null && Interlocked.Exchange(ref _documentsChanged, 0) == 0 &&
                _wasActive == active) return Volatile.Read(ref _revision);
            _wasActive = active;
            var current = active ? definitions().ToHashSet() : [];
            if (!_subscribed.SetEquals(current))
            {
                foreach (var definition in _subscribed.Except(current)) definition.SolutionEnd -= Solved;
                foreach (var definition in current.Except(_subscribed)) definition.SolutionEnd += Solved;
                _subscribed.Clear();
                _subscribed.UnionWith(current);
                Interlocked.Increment(ref _revision);
            }
            return Volatile.Read(ref _revision);
        }
    }

    private void Solved(object? sender, GH_SolutionEventArgs e) => Interlocked.Increment(ref _revision);

    internal static ILabelOcclusionSources ForDocument(RhinoDoc document)
    {
        var server = global::Grasshopper.Instances.DocumentServer;
        var provider = new GhLabelOcclusionSources(() => server.OfType<GH_Document>())
        {
            _server = server,
            _document = document
        };
        server.DocumentAdded += provider.DocumentsChanged;
        server.DocumentRemoved += provider.DocumentsChanged;
        return provider;
    }

    private void DocumentsChanged(GH_DocumentServer server, GH_Document document) =>
        Interlocked.Exchange(ref _documentsChanged, 1);

    public IEnumerable<LabelOcclusionSource> GetSources(RhinoViewport viewport)
    {
        foreach (var definition in _subscribed)
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
                            yield return new(component.InstanceGuid, () => adapter(component));
                    }
                    else if (component.ComponentGuid == CustomPreviewGuid && component is GH_CustomPreviewComponent preview)
                    {
                        var filter = preview.ViewportFilter;
                        if (!string.IsNullOrEmpty(filter) && !LikeOperator.LikeString(viewport.Name, filter, CompareMethod.Binary))
                            continue;
                        var selected = owner.Attributes.Selected;
                        yield return new((component.InstanceGuid, selected), () => ReadCustomPreview(preview, selected));
                    }
                    else
                    {
                        foreach (var output in component.Params.Output)
                            if (output is IGH_PreviewObject { Hidden: false, IsPreviewCapable: true })
                                yield return new(output.InstanceGuid, () => ReadParam(output));
                    }
                }
                else if (owner is IGH_Param parameter)
                    yield return new(owner.InstanceGuid, () => ReadParam(parameter));
            }
        }
    }

    private static IEnumerable<GeometryBase> ReadParam(IGH_Param param) =>
        param.VolatileData.AllData(true).Select(Geometry).OfType<GeometryBase>();

    private static GeometryBase? Geometry(IGH_Goo? goo) =>
        goo is IGH_GeometricGoo geometric && geometric.ScriptVariable() is GeometryBase value &&
        value is Mesh or Brep or Surface or Extrusion or SubD ? value : null;

    private static IEnumerable<GeometryBase> ReadCustomPreview(GH_CustomPreviewComponent preview, bool selected)
    {
        // Grasshopper has already matched paths, items and materials during SolveInstance.
        foreach (var item in preview.DisplayItems ?? [])
            if ((selected || item.Shader.Transparency <= 0) && Geometry(item.Geometry as IGH_Goo) is { } geometry)
                yield return geometry;
    }

    public void Dispose()
    {
        if (_server is not null)
        {
            _server.DocumentAdded -= DocumentsChanged;
            _server.DocumentRemoved -= DocumentsChanged;
        }
        foreach (var definition in _subscribed) definition.SolutionEnd -= Solved;
        _subscribed.Clear();
    }
}
