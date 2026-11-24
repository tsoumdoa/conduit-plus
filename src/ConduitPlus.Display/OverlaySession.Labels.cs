using System.Diagnostics;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    private LabelAnchor[] _labels = [];
    private LabelOcclusionScene? _labelScene;
    private OcclusionSchedule? _labelSchedule;
    private OcclusionSegment?[] _labelSegments = [];
    private (Transform Screen, Size2 Size, double Dpi, double Near, double Far)? _labelProjection;
    private long _labelProjectionRevision;
    private UiTimer? _labelRedraw;

    internal void SetLabels(LabelAnchor[] labels)
    {
        if (!_labels.SequenceEqual(labels))
        {
            _labels = labels;
            _labelSchedule = labels.Length == 0 ? null : new(labels.Length);
            _labelSegments = new OcclusionSegment?[labels.Length];
            _labelProjection = null;
        }
        RefreshLabelScene();
    }

    internal void RefreshLabelScene()
    {
        if (!Visible || _labels.Length == 0)
        {
            _labelRedraw?.Dispose();
            _labelRedraw = null;
            _labelScene?.Dispose();
            _labelScene = null;
            _labelSchedule = _labels.Length == 0 ? null : new(_labels.Length);
            return;
        }
        _labelScene ??= new(Document, View.MainViewport);
        _labelScene.Refresh();
    }

    internal void ClassifyLabels()
    {
        _labelScene?.RequestMembershipCheck();
        if (_labelScene?.Snapshot is not { } snapshot || _labelSchedule is not { } schedule) return;
        var started = Stopwatch.GetTimestamp();
        var viewport = View.MainViewport;
        var dpi = UiContext.Dpi(View);
        viewport.GetFrustum(out _, out _, out _, out _, out var near, out var far);
        var projection = (viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen), ViewSize, dpi, near, far);
        if (_labelProjection != projection)
        {
            _labelProjection = projection;
            _labelProjectionRevision++;
            var projector = new LabelOcclusionScene.Projection(viewport, dpi);
            for (var i = 0; i < _labels.Length; i++) _labelSegments[i] = projector.Project(_labels[i].Point);
        }
        var active = Enumerable.Range(0, _labels.Length).Where(i => _labels[i].Style.Key is { } key &&
            (_selectedMembers.Contains(key) || _hoveredMembers.Contains(key))).ToArray();
        schedule.Classify(snapshot.Index, snapshot.Generation, _labelProjectionRevision, _labelSegments, active,
            () => Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2);
        if (Enumerable.Range(0, _labels.Length).Any(i => !schedule.Answer(i, snapshot.Generation, _labelProjectionRevision).Current))
        {
            if (_labelRedraw is null)
            {
                _labelRedraw = new(15);
                _labelRedraw.Tick += (_, _) =>
                {
                    _labelRedraw.Stop();
                    if (Visible) View.Redraw();
                };
            }
            _labelRedraw.Start();
        }
    }

    internal (LabelAnchor Anchor, OcclusionAnswer Answer)[] InspectLabels() =>
        _labels.Select((anchor, i) => (anchor,
            _labelScene?.Snapshot is { } snapshot && _labelSchedule is { } schedule
                ? schedule.Answer(i, snapshot.Generation, _labelProjectionRevision)
                : default)).ToArray();
}
