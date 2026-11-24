using Rhino.Geometry;
using Rhino.Display;

namespace ConduitPlus.Rhino;

internal readonly record struct LabelOcclusionSource(object Key, Func<IEnumerable<GeometryBase>> Read);

internal interface ILabelOcclusionSources : IDisposable
{
    int Revision { get; }
    IEnumerable<LabelOcclusionSource> GetSources(RhinoViewport viewport);
}
