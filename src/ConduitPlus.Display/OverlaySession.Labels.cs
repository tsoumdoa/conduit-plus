using System.Diagnostics;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    private LabelAnchor[] _labels = [];
    private LabelView? _labelView;
    internal LabelOcclusionScene? LabelScene => _labelView?.Scene;

    internal void SetLabels(LabelAnchor[] labels)
    {
        if (!_labels.SequenceEqual(labels))
        {
            _labels = labels;
            _labelView?.InvalidateAnchors();
        }
        RefreshLabelScene();
    }

    internal void RefreshLabelScene()
    {
        if (_disposed || !Visible || _labels.Length == 0)
        {
            _labelView?.Remove(this);
            _labelView = null;
            return;
        }

        if (_labelView is null)
        {
            _labelView = LabelView.Acquire(this, out var created);
            if (created) _labelView.Scene.Refresh();
        }
        _labelView.Scene.RequestMembershipCheck();
    }

    internal void SetLabelDpiForChecks(double? dpi)
    {
        if (_labelView is { } view) view.DpiOverride = dpi;
    }

    internal void DrawViewLabels(DrawEventArgs e, IReadOnlyList<Rect2> reserved) => _labelView?.Draw(e, reserved);

    internal (LabelAnchor Anchor, OcclusionAnswer Answer)[] InspectLabels() =>
        _labels.Select((anchor, i) => (anchor, _labelView?.Answer(this, i) ?? default)).ToArray();

    private sealed partial class LabelView : IDisposable
    {
        private static readonly Dictionary<(uint Document, Guid Viewport), LabelView> Views = [];
        private readonly (uint Document, Guid Viewport) _key;
        private readonly RhinoView _view;
        private readonly HashSet<OverlaySession> _owners = [];
        private readonly UiTimer _redraw = new(15);
        private readonly List<int> _active = [];
        private readonly Dictionary<OverlaySession, int> _offsets = [];
        private (OverlaySession Owner, LabelAnchor Anchor)[] _anchors = [];
        private OcclusionSegment?[] _segments = [];
        private Point2[] _pixels = [];
        private OcclusionSchedule? _schedule;
        private (Transform Screen, Size2 Size, double Dpi, double Near, double Far)? _projection;
        private long _projectionRevision;

        internal LabelOcclusionScene Scene { get; }
        internal double? DpiOverride { get; set; }

        private LabelView(OverlaySession owner, (uint, Guid) key)
        {
            _key = key;
            _view = owner.View;
            _settled.Tick += (_, _) => { _settled.Stop(); _view.Redraw(); };
            Scene = new(owner.Document, _view.MainViewport);
            _redraw.Tick += (_, _) =>
            {
                _redraw.Stop();
                _view.Redraw();
            };
        }

        internal static LabelView Acquire(OverlaySession owner, out bool created)
        {
            var key = (owner.Document.RuntimeSerialNumber, owner.View.MainViewport.Id);
            created = !Views.TryGetValue(key, out var binding);
            if (created) Views.Add(key, binding = new(owner, key));
            binding!._owners.Add(owner);
            binding.InvalidateAnchors();
            return binding;
        }

        internal void Remove(OverlaySession owner)
        {
            _owners.Remove(owner);
            if (_owners.Count == 0)
            {
                Views.Remove(_key);
                Dispose();
            }
            else InvalidateAnchors();
        }

        internal void InvalidateAnchors()
        {
            _schedule = null;
            _stylesDirty = true;
        }

        internal void Classify()
        {
            Scene.RequestMembershipCheck();
            if (Scene.Snapshot is not { } snapshot) return;
            var started = Stopwatch.GetTimestamp();
            var anchorsChanged = _schedule is null;
            if (_schedule is null)
            {
                _offsets.Clear();
                var anchors = new List<(OverlaySession, LabelAnchor)>();
                foreach (var owner in _owners.OrderBy(s => s.Owner))
                {
                    _offsets.Add(owner, anchors.Count);
                    anchors.AddRange(owner._labels.Select(anchor => (owner, anchor)));
                }
                _anchors = anchors.ToArray();
                _segments = new OcclusionSegment?[_anchors.Length];
                _pixels = new Point2[_anchors.Length];
                _schedule = new(_anchors.Length);
            }

            var viewport = _view.MainViewport;
            var dpi = DpiOverride ?? UiContext.Dpi(_view);
            viewport.GetFrustum(out _, out _, out _, out _, out var near, out var far);
            var projection = (viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen),
                _owners.First().ViewSize, dpi, near, far);
            var projectionChanged = _projection != projection;
            if (projectionChanged || anchorsChanged)
            {
                _projection = projection;
                if (projectionChanged) _projectionRevision++;
                var projector = new LabelOcclusionScene.Projection(viewport, dpi);
                for (var i = 0; i < _anchors.Length; i++)
                    _segments[i] = projector.Project(_anchors[i].Anchor.Point, out _pixels[i]);
            }

            _active.Clear();
            for (var i = 0; i < _anchors.Length; i++)
            {
                var (owner, anchor) = _anchors[i];
                if (anchor.Style.Key is { } key &&
                    (owner._selectedMembers.Contains(key) || owner._hoveredMembers.Contains(key)))
                    _active.Add(i);
            }

            _schedule.Classify(snapshot.Index, snapshot.Generation, _projectionRevision, _segments,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_active),
                () => Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2);
            for (var i = 0; i < _anchors.Length; i++)
            {
                if (_schedule.Answer(i, snapshot.Generation, _projectionRevision).Current) continue;
                _redraw.Start();
                return;
            }
            _redraw.Stop();
        }

        internal OcclusionAnswer Answer(OverlaySession owner, int index) =>
            Scene.Snapshot is { } snapshot && _schedule is { } schedule && _offsets.TryGetValue(owner, out var offset)
                ? schedule.Answer(offset + index, snapshot.Generation, _projectionRevision)
                : default;

        public void Dispose()
        {
            _redraw.Dispose();
            _settled.Dispose();
            ClearRasters();
            Scene.Dispose();
        }
    }
}
