using Point = System.Drawing.Point;
using ConduitPlus.Core;
using Rhino.Display;
using Rhino.DocObjects;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    internal readonly record struct LabelHit(OverlaySession Owner, LabelAnchor Anchor, LabelPlacement Placement);

    internal static LabelHit? PickLabel(RhinoView view, Point point, bool retain = false) =>
        LabelView.Pick(view, point, retain);

    internal Rect2? InspectLabelPopover() => _labelView?.PopoverBounds;

    internal void SetLabelPopover(LabelPlacement? placement) => _labelView?.SetPopover(this, placement);

    private sealed partial class LabelView
    {
        private (OverlaySession Owner, LabelPlacement Placement)? _popover;
        private Rect2? _popoverBounds;
        internal Rect2? PopoverBounds => _popoverBounds;
        private bool _frameComplete;
        private IReadOnlyList<Rect2> _frameHud = [];

        internal static LabelHit? Pick(RhinoView view, Point point, bool retain)
        {
            if (!Views.TryGetValue((view.Document.RuntimeSerialNumber, view.MainViewport.Id), out var binding))
                return null;
            return binding.Pick(point, retain);
        }

        private LabelHit? Pick(Point point, bool retain)
        {
            if (!_frameComplete || _projection is not { } projection) return null;
            var viewport = _view.MainViewport;
            viewport.GetFrustum(out _, out _, out _, out _, out var near, out var far);
            if (projection.Size != new Size2(viewport.Size.Width, viewport.Size.Height) ||
                projection.Dpi != (DpiOverride ?? UiContext.Dpi(_view)) ||
                projection.Near != near || projection.Far != far ||
                !SameScreenTransform(viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen), projection.Screen))
                return null;

            foreach (var hud in _frameHud)
                if (hud.Contains(point.X, point.Y)) return null;
            var css = new Point2(point.X / projection.Dpi, point.Y / projection.Dpi);
            if (_popover is { } popover && _popoverBounds is { } bounds &&
                (bounds.Contains(css.X, css.Y) || retain && LabelPicking.Retains(popover.Placement.Dot, bounds, css)))
                return Resolve(popover.Placement);
            return LabelPicking.Hit(_drawn, css, true, false, DotPaintOrder, projection.Dpi) is { } placement ? Resolve(placement) : null;
        }

        private int DotPaintOrder(LabelIdentity identity) => _styles[identity].Dot.PaintOrder;

        private LabelHit? Resolve(LabelPlacement placement) =>
            _styles.TryGetValue(placement.Identity, out var style) && OverlayManager.LabelInputEnabled(style.Owner)
                ? new(style.Owner, _anchors[style.Index].Anchor, placement)
                : null;

        internal void SetPopover(OverlaySession owner, LabelPlacement? placement)
        {
            if (placement is { Form: LabelForm.Dot } dot)
            {
                if (_popover is { } previous && previous.Placement.Identity == dot.Identity) return;
                _popover = (owner, dot);
            }
            else
            {
                if (_popover?.Owner != owner) return;
                _popover = null;
            }
            _popoverBounds = null;
            OverlayManager.RequestInteractionRedraw(owner);
        }

        private void InvalidateLabelInput()
        {
            OverlayManager.RecheckLabelPointer(_view);
            _frameComplete = false;
            _popover = null;
            _popoverBounds = null;
        }
    }
}
