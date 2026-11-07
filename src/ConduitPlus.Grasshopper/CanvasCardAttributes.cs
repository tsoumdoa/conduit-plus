using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace ConduitPlus.Grasshopper;

internal sealed class CanvasCardAttributes(CanvasCardComponent component) : GH_ComponentAttributes(component)
{
    internal CanvasCardComponent Component { get; } = component;
    internal CanvasCardFrame? Frame { get; private set; }
    internal bool Pending { get; set; }
    internal long SettleAt { get; private set; }
    internal long LastVisible { get; private set; }
    private GH_Canvas? _canvas;
    private RectangleF _body, _strip;
    private double _scale = 1;
    private string? _pressed;
    private Point2 _down;
    private bool _dragScroll, _cancelClick;
    private double _grab;
    private int _originalOffset;
    private PointF? _hoverPoint;
    private CanvasFrameKey? _failed;
    private bool _tooltipOwned;
    private const string TrackKey = "\u0001canvas-scroll-track";
    private int _pageOffset;
    internal bool HasGesture => _pressed is not null || _dragScroll;
    private bool Disabled => Component.Locked || Component.OnPingDocument()?.Enabled == false;
    private bool Interactive => !Disabled && _canvas?.Document == Component.OnPingDocument() && (Frame is null || Frame.Spec.Id == Component.State.Spec?.Id);
    internal bool Visible
    {
        get
        {
            if (_canvas is null || _canvas.IsDisposed || _canvas.Document != Component.OnPingDocument()) return false;
            var bounds = Bounds;
            return _canvas.Viewport.IsVisible(ref bounds);
        }
    }
    protected override void Layout()
    {
        // Geometry belongs to the published frame too. Keep the previous complete frame if replacement fails.
        var size = Frame?.Key.Size ?? Component.BodySize;
        _strip = new(Pivot.X, Pivot.Y, (float)size.Width + 8, 26);
        _body = new(Pivot.X + 4, Pivot.Y + 26, (float)size.Width, (float)size.Height);
        m_innerBounds = _strip;
        LayoutInputParams(Component, _strip);
        Bounds = LayoutBounds(Component, new RectangleF(_strip.X, _strip.Y, _strip.Width, _strip.Height + _body.Height + 4));
    }
    internal void RequestDisplay()
    {
        _failed = null;
        if (_canvas is not null) { Pending = true; CanvasCardCache.Queue(this); _canvas.Invalidate(); }
    }
    private void Attach(GH_Canvas canvas)
    {
        if (_canvas == canvas) return;
        DetachCanvas(); _canvas = canvas;
        canvas.DocumentChanged += DocumentChanged;
        canvas.MouseCaptureChanged += CaptureChanged;
        canvas.MouseLeave += MouseLeft;
        canvas.MouseMove += CanvasMouseMoved;
        canvas.MouseDown += NavigationStarted;
        canvas.MouseWheel += NavigationStarted;
        canvas.ViewportChanged += ViewportChanged;
        canvas.Disposed += CanvasDisposed;
        CanvasCardCache.Register(this);
    }
    private void DocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e) { CancelGesture(); ClearHover(); Pending = false; }
    private void ViewportChanged(object? sender, GH_CanvasViewportChangedEventArgs e) { CancelGesture(); ClearHover(); }
    private void CaptureChanged(object? sender, EventArgs e) { if (_canvas?.Capture != true) CancelGesture(); }
    private void MouseLeft(object? sender, EventArgs e) { ClearHover(); }
    private void CanvasDisposed(object? sender, EventArgs e) => Detach();
    private void NavigationStarted(object? sender, MouseEventArgs e)
    { if (e.Button != MouseButtons.Left || Control.ModifierKeys != Keys.None || e.Delta != 0) { CancelGesture(); ClearHover(); } }
    private void CanvasMouseMoved(object? sender, MouseEventArgs e)
    {
        if (_pressed is not null || _dragScroll) return;
        if (!Interactive || _canvas?.IsActiveInteraction == true || e.Button != MouseButtons.None || Control.ModifierKeys != Keys.None)
        { ClearHover(); return; }
        if (_canvas is null) return;
        var ev = new GH_CanvasMouseEvent(_canvas.Viewport, e);
        if (!_body.Contains(ev.CanvasLocation) || _canvas.Document?.FindAttribute(ev.CanvasLocation, false)?.DocObject != Component)
        { ClearHover(); return; }
        _hoverPoint = ev.CanvasLocation; RefreshHover();
    }
    private void ClearHover()
    {
        _hoverPoint = null;
        var rev = Component.State.Spec?.Rev;
        Component.State.Hover(null);
        if (rev != Component.State.Spec?.Rev) RequestDisplay();
        HideTooltip();
    }
    private void HideTooltip()
    {
        if (_tooltipOwned && _canvas is not null && GH_Tooltip.IsOwner(_canvas)) GH_Tooltip.Clear();
        _tooltipOwned = false;
    }
    private void RefreshHover()
    {
        var hit = _hoverPoint is { } p ? Hit(p) : null;
        var rev = Component.State.Spec?.Rev;
        Component.State.Hover(hit is not null && !CardMeasure.IsReserved(hit.Key) ? hit.Key : null);
        if (rev != Component.State.Spec?.Rev) RequestDisplay();
    }
    private Point2 Local(PointF point) => CanvasCardGeometry.ToLocal(new(point.X, point.Y), new(_body.X, _body.Y));
    private HitRegion? Hit(PointF point)
    {
        var p = Local(point);
        return Frame?.Hits.LastOrDefault(h => h.Contains(p.X, p.Y));
    }
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects) { base.Render(canvas, graphics, channel); return; }
        var export = canvas.DrawingMode != GH_CanvasMode.Control;
        if (!export) { Attach(canvas); if (!Visible) return; LastVisible = Environment.TickCount64; }
        var saved = graphics.Save();
        try
        {
            // World -> Device includes the complete target projection and page/DPI transform exactly once.
            PointF[] basis = [new(0, 0), new(1, 0), new(0, 1)];
            graphics.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, basis);
            var scale = CanvasCardGeometry.DeviceScale(new(basis[0].X, basis[0].Y), new(basis[1].X, basis[1].Y), new(basis[2].X, basis[2].Y));
            if (!double.IsFinite(scale) || scale <= 0) return;
            var palette = GH_CapsuleRenderEngine.GetImpliedPalette(Component);
            var style = GH_CapsuleRenderEngine.GetImpliedStyle(palette, Selected, Disabled, false);
            var exportSize = Disabled ? Frame?.Key.Size ?? Component.BodySize : Component.BodySize;
            var paintBounds = export ? LayoutBounds(Component, new RectangleF(_strip.X, _strip.Y, (float)exportSize.Width + 8, (float)exportSize.Height + 30)) : Bounds;
            using (var capsule = GH_Capsule.CreateCapsule(paintBounds, palette))
            {
                capsule.AddInputGrip(Component.Params.Input[0].Attributes.InputGrip.Y);
                capsule.Render(graphics, style);
            }
            RenderComponentParameters(canvas, graphics, Component, style);
            using (var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(style.Text))
                graphics.DrawString(Disabled ? "Canvas Card · Disabled" : "Canvas Card", font, brush, _strip.X + 8, _strip.Y + 5);
            if (export)
            {
                // Verified against GH 8.20 GenerateHiResImageTile: Export mode, projected target Graphics.
                var spec = Disabled ? Frame?.Spec : Component.State.Spec;
                var theme = Disabled ? Frame?.Theme : Component.CardTheme;
                if (spec is not null && theme is not null)
                {
                    spec = spec with { HoverKey = null, ScrollOffset = _dragScroll ? _originalOffset : spec.ScrollOffset };
                    var plan = CanvasRasterPlan.Create(CardMeasure.Measure(spec, theme), scale);
                    using var image = RasterDrawing.Render(spec, theme, plan.Scale);
                    graphics.DrawImage(image, WithMargin(new RectangleF(_body.X, _body.Y, (float)exportSize.Width, (float)exportSize.Height), theme, plan.Scale));
                }
                else DrawPlaceholder(graphics);
                return;
            }
            if (Math.Abs(_scale - scale) > .0001)
            { _scale = scale; SettleAt = Environment.TickCount64 + 120; Pending = true; CanvasCardCache.Queue(this); }
            var key = DesiredKey();
            if (!Disabled && key is not null && Frame?.Key != key && _failed != key)
            { Pending = true; CanvasCardCache.Queue(this); }
            if (!Interactive) { CancelGesture(); ClearHover(); }
            if (Frame is { } frame)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // A failed data/size replacement keeps the old frame's geometry and hit map together.
                graphics.DrawImage(frame.Bitmap, WithMargin(new RectangleF(_body.X, _body.Y, (float)frame.Key.Size.Width, (float)frame.Key.Size.Height), frame.Theme, frame.Key.Raster.Scale));
            }
            else DrawPlaceholder(graphics);
        }
        finally { graphics.Restore(saved); }
    }
    // Themed bitmaps carry a shadow margin on every side; grow the target so the card body keeps its measured size.
    private static RectangleF WithMargin(RectangleF body, Theme theme, double scale)
    {
        var m = (float)(RasterDrawing.Margin(theme, scale) / scale);
        return m <= 0 ? body : new(body.X - m, body.Y - m, body.Width + 2 * m, body.Height + 2 * m);
    }
    private void DrawPlaceholder(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.DimGray);
        using var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(Component.Placeholder, font, brush, _body.X + 12, _body.Y + 16);
    }
    private CanvasFrameKey? DesiredKey() => Component.State.Spec is { } spec
        ? new(spec.Id, spec.Rev, Component.CardTheme, Component.BodySize, CanvasRasterPlan.Create(Component.BodySize, _scale, CanvasCardCache.BitmapBudget(this))) : null;
    internal void Regenerate()
    {
        Pending = false;
        if (Disabled || Component.State.Spec is not { } spec || DesiredKey() is not { } key || key == _failed || Frame?.Key == key) return;
        try
        {
            CanvasCardCache.Reserve(this, key.Raster.Bytes);
            var bitmap = RasterDrawing.Render(spec, key.Theme, key.Raster.Scale);
            CanvasCardFrame next;
            try { next = new(bitmap, spec, key.Theme, key); }
            catch { bitmap.Dispose(); throw; }
            HideTooltip();
            var old = Frame; Frame = next;
            if ((old?.Key.Size ?? Component.BodySize) != next.Key.Size) ExpireLayout();
            old?.Dispose();
            RefreshHover(); _canvas?.Invalidate();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            _failed = key;
            Component.AddRuntimeMessage(global::Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning, "Canvas Card render failed: " + e.Message);
        }
    }
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (!Interactive || e.Button != MouseButtons.Left || Control.MouseButtons != MouseButtons.Left || Control.ModifierKeys != Keys.None || Frame is not { } frame)
            return base.RespondToMouseDown(sender, e);
        var local = Local(e.CanvasLocation);
        var bar = TableScroll.Bar(frame.Spec, frame.Theme);
        var hit = Hit(e.CanvasLocation);
        if (bar?.Track.Contains(local.X, local.Y) != true && hit is null) return base.RespondToMouseDown(sender, e);
        ClearHover();
        _down = new(e.ControlX, e.ControlY); _originalOffset = Component.State.Spec?.ScrollOffset ?? 0;
        _cancelClick = false;
        if (bar is { } scroll && scroll.Track.Contains(local.X, local.Y))
        {
            if (scroll.Thumb.Contains(local.X, local.Y)) { _dragScroll = true; _grab = local.Y - scroll.Thumb.Y; }
            else { _pressed = TrackKey; _pageOffset = local.Y < scroll.Thumb.Y ? -TableScroll.Visible(frame.Spec) : TableScroll.Visible(frame.Spec); }
        }
        else _pressed = hit!.Key;
        sender.Capture = true; CanvasCardCache.Queue(this); RequestDisplay();
        return GH_ObjectResponse.Capture;
    }
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_pressed is null && !_dragScroll) return base.RespondToMouseMove(sender, e);
        if (!Interactive || Control.ModifierKeys != Keys.None) { CancelGesture(); return GH_ObjectResponse.Release; }
        if (!CanvasCardGeometry.WithinClick(_down, new(e.ControlX, e.ControlY))) _cancelClick = true;
        if (_dragScroll && Frame is { } frame)
        { Component.State.Scroll(TableScroll.OffsetForThumbTop(frame.Spec, frame.Theme, Local(e.CanvasLocation).Y - _grab)); HideTooltip(); RequestDisplay(); }
        return GH_ObjectResponse.Handled;
    }
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_pressed is null && !_dragScroll) return base.RespondToMouseUp(sender, e);
        var key = _pressed;
        var commit = Interactive && e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.None;
        if (commit && !_cancelClick && key is not null && CanvasCardGeometry.WithinClick(_down, new(e.ControlX, e.ControlY)))
        {
            var local = Local(e.CanvasLocation);
            if (key == TrackKey && Frame is { } frame && TableScroll.Bar(frame.Spec, frame.Theme) is { } bar &&
                bar.Track.Contains(local.X, local.Y) && !bar.Thumb.Contains(local.X, local.Y)) Component.State.Scroll(_originalOffset + _pageOffset);
            else if (Hit(e.CanvasLocation)?.Key == key) Component.State.Activate(key);
        }
        FinishGesture(!commit); _hoverPoint = e.CanvasLocation; RefreshHover(); RequestDisplay();
        return GH_ObjectResponse.Release;
    }
    public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape || (_pressed is null && !_dragScroll)) return base.RespondToKeyDown(sender, e);
        CancelGesture(); return GH_ObjectResponse.Release;
    }
    internal void CheckCapture()
    {
        if ((_pressed is not null || _dragScroll) && (!Interactive || _canvas?.ActiveObject != Component || _canvas.Capture != true)) CancelGesture();
    }
    internal void CancelGesture() => FinishGesture(true);
    private void FinishGesture(bool cancel)
    {
        var active = _pressed is not null || _dragScroll;
        var restore = cancel && _dragScroll;
        _pressed = null; _dragScroll = false;
        if (restore) { Component.State.Scroll(_originalOffset); RequestDisplay(); }
        if (active && _canvas is not null)
        { if (_canvas.ActiveObject == Component) _canvas.ActiveObject = null; _canvas.Capture = false; }
    }
    public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs e)
    {
        if (!Interactive || _pressed is not null || _dragScroll || _canvas?.IsActiveInteraction == true) return;
        if (Frame is { } frame && Hit(point) is { } hit && Tooltip.For(frame.Spec, hit.Key) is { } lines)
        {
            _tooltipOwned = true;
            e.Title = frame.Spec.Title; e.Text = string.Join(Environment.NewLine, lines); e.Description = "";
            // GH 8.20 requires control coordinates here, unlike the SetupTooltip point argument.
            var x = _canvas!.Viewport.ProjectX((float)(_body.X + hit.X));
            var y = _canvas.Viewport.ProjectY((float)(_body.Y + hit.Y));
            e.Region = new Rectangle((int)x, (int)y, Math.Max(1, (int)(hit.W * _canvas.Viewport.Zoom)), Math.Max(1, (int)(hit.H * _canvas.Viewport.Zoom)));
        }
        else base.SetupTooltip(point, e);
    }
    internal void ReleaseFrame()
    {
        var old = Frame; Frame = null;
        if (old is not null && old.Key.Size != Component.BodySize) ExpireLayout();
        old?.Dispose();
    }
    private void DetachCanvas()
    {
        CancelGesture(); ClearHover();
        if (_canvas is { } canvas)
        {
            canvas.DocumentChanged -= DocumentChanged; canvas.MouseCaptureChanged -= CaptureChanged;
            canvas.MouseLeave -= MouseLeft; canvas.MouseMove -= CanvasMouseMoved;
            canvas.MouseDown -= NavigationStarted; canvas.MouseWheel -= NavigationStarted;
            canvas.ViewportChanged -= ViewportChanged; canvas.Disposed -= CanvasDisposed;
        }
        _canvas = null;
    }
    internal void Detach() { DetachCanvas(); Pending = false; ReleaseFrame(); CanvasCardCache.Remove(this); }
}
