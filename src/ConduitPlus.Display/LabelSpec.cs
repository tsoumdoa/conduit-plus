using ConduitPlus.Core;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal sealed record LabelSpec(LabelSource Source, Link? Link, LabelAnchor[] Anchors, string[] Remarks)
{
    internal static LabelSpec FromLink(Link link, LabelSource source, LabelStyle[]? styles = null)
    {
        styles ??= LabelAlignment.Align(source.Path, link.Keys, 0, [], [], [], [], []);
        var anchors = new List<LabelAnchor>();
        var remarks = new List<string>();
        var missing = 0;
        for (var i = 0; i < styles.Length; i++)
        {
            var key = link.Keys[i];
            if (!link.LabelAnchors.TryGetValue(key, out var result)) { missing++; continue; }
            if (result.Diagnostic is { } diagnostic) remarks.Add($"Key '{key}', {result.Location}: {diagnostic}");
            if (result.Point.IsValid) anchors.Add(new(source, key, null, result.Point, styles[i]));
        }
        if (missing > 0) remarks.Add($"Skipped {missing} Link keys without geometry.");
        return new(source, link, anchors.ToArray(), remarks.ToArray());
    }
}

internal sealed record LabelAnchor(LabelSource Source, string? LinkKey, int? PointIndex, Point3d Point, LabelStyle Style)
{
    internal LabelIdentity Identity(Guid owner) => new(owner, Source, LinkKey, PointIndex);
}
