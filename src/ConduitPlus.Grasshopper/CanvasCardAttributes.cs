using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using ConduitPlus.Rendering;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace ConduitPlus.Grasshopper;

internal sealed class CanvasCardAttributes(CanvasCardComponent component) : GH_ComponentAttributes(component), ICanvasRasterOwner
{
    public object? RasterDocument => Component.OnPingDocument();
    public string RasterOwner => "inline:" + Component.InstanceGuid.ToString("N");
    public long RasterBytes => Frame?.Bytes ?? 0;
    public long RasterLastVisible => LastVisible;
    public void EvictCanvasRasters() => EvictRasters();
    internal CanvasCardComponent Component { get; } = component;
    internal CanvasCardFrame? Frame { get; private set; }
    internal bool Pending { get; set; }
    internal long SettleAt { get; private set; }
    internal long LastVisible { get; private set; }
    private GH_Canvas? _canvas;
    private RectangleF _body, _strip;
    private double _scale = 1;
    private readonly CardInteractionController _interaction = new();
    private static CardModifiers Modifiers => CardHostModifiers.Read(HostInput.ModifierKeys);
    private CardInteractionFrame InteractionFrame => Frame is { } f ? CardInteractionFrame.Stack(f.Specs, f.Theme, f.Layout) : new([]);
    private PointF? _hoverPoint;
    private CanvasStackFrameKey? _failed;
    private bool _tooltipOwned;
    internal bool HasGesture => _interaction.Active || _interaction.OwnsRelease;
    private bool Disabled => Component.Locked || Component.OnPingDocument()?.Enabled == false;
    private bool Interactive => !Disabled && Component.BrushEnabled && _canvas?.Document == Component.OnPingDocument() && (Frame is null || Frame.Specs.Select(c => c.Id).SequenceEqual(Component.State.Specs.Select(c => c.Id)));
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
        LayoutOutputParams(Component, _strip);
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
    private void ViewportChanged(object? sender, GH_CanvasViewportChangedEventArgs e) { CancelGesture(); ClearHover(); RequestDisplay(); }
    private void CaptureChanged(object? sender, EventArgs e) { if (_canvas?.Capture != true) CancelGesture(); }
    private void MouseLeft(object? sender, EventArgs e) { ClearHover(); }
    private void CanvasDisposed(object? sender, EventArgs e) => Detach();
    private void NavigationStarted(object? sender, MouseEventArgs e)
    { if (e.Button != MouseButtons.Left || (HostInput.ModifierKeys != Keys.None && !(_interaction.IsSelectionGesture && _interaction.ModifiersValid(Modifiers))) || e.Delta != 0) { CancelGesture(); ClearHover(); } }
    private void CanvasMouseMoved(object? sender, MouseEventArgs e)
    {
        if (_interaction.Active) return;
        if (!Interactive || _canvas?.IsActiveInteraction == true || e.Button != MouseButtons.None || HostInput.ModifierKeys != Keys.None)
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
        var rev = Component.State.Revision;
        Component.State.Hover(null);
        if (rev != Component.State.Revision) RequestDisplay();
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
        var rev = Component.State.Revision;
        Component.State.Hover(hit?.CardId, hit?.Key);
        if (rev != Component.State.Revision) RequestDisplay();
    }
    private Point2 Local(PointF point) => CanvasCardGeometry.ToLocal(new(point.X, point.Y), new(_body.X, _body.Y));
    private CanvasStackHit? Hit(PointF point)
    {
        var p = Local(point);
        return Frame?.Layout.Hit(p);
    }
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects) { base.Render(canvas, graphics, channel); return; }
        ConduitPlus.Rhino.OverlayManager.Start();
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
            // macOS Retina: raster at the backing factor into the same rectangle (Windows: × 1, unchanged).
            if (HostInput.IsMac) scale *= HostDisplay.CanvasRatio(canvas, scale, canvas.Viewport.Zoom);
            var palette = GH_CapsuleRenderEngine.GetImpliedPalette(Component);
            var style = GH_CapsuleRenderEngine.GetImpliedStyle(palette, Selected, Disabled, false);
            var exportSize = Disabled ? Frame?.Key.Size ?? Component.BodySize : Component.BodySize;
            var paintBounds = export ? LayoutBounds(Component, new RectangleF(_strip.X, _strip.Y, (float)exportSize.Width + 8, (float)exportSize.Height + 30)) : Bounds;
            using (var capsule = GH_Capsule.CreateCapsule(paintBounds, palette))
            {
                foreach (var input in Component.Params.Input) capsule.AddInputGrip(input.Attributes.InputGrip.Y);
                foreach (var output in Component.Params.Output) capsule.AddOutputGrip(output.Attributes.OutputGrip.Y);
                capsule.Render(graphics, style);
            }
            RenderComponentParameters(canvas, graphics, Component, style);
            using (var font = new Font(GdiCanvas.Family("Segoe UI"), 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(style.Text))
                graphics.DrawString(Disabled ? "Canvas Card · Disabled" : "Canvas Card", font, brush, _strip.X + 8, _strip.Y + 5);
            if (export)
            {
                // Verified against GH 8.20 GenerateHiResImageTile: Export mode, projected target Graphics.
                var specs = Disabled ? Frame?.Specs : Component.State.Specs;
                var theme = Disabled ? Frame?.Theme : Component.CardTheme;
                if (specs is { Length: > 0 } && theme is not null)
                {
                    specs = specs.Select(spec => BrushPresentation.WithoutHover(spec) with
                    { ScrollOffset = _interaction.DraggingScroll && spec.Id == _interaction.PressedCard ? _interaction.OriginalOffset : spec.ScrollOffset }).ToArray();
                    var layout = new CanvasStackLayout(specs, theme);
                    for (var i = 0; i < specs.Length; i++)
                    {
                        var card = layout.Cards[i];
                        var plan = Plan(new(card.W, card.H), theme, scale);
                        CanvasCardCache.Reserve(this, plan.Bytes);
                        using var image = RasterDrawing.Render(specs[i], theme, plan.Scale);
                        graphics.DrawImage(image, WithMargin(CardBody(card), theme, plan.Scale));
                    }
                }
                else DrawPlaceholder(graphics);
                return;
            }
            if (Math.Abs(_scale - scale) > .0001)
            { _scale = scale; SettleAt = Environment.TickCount64 + 120; Pending = true; CanvasCardCache.Queue(this); }
            var key = DesiredKey();
            if (!Disabled && key is not null && Frame?.IsReady(key) != true && _failed != key)
            { Pending = true; CanvasCardCache.Queue(this); }
            if (!Interactive) { CancelGesture(); ClearHover(); }
            if (Frame is { } frame)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // A failed data/size replacement keeps the old frame's geometry and hit map together.
                for (var i = 0; i < frame.Bitmaps.Length; i++)
                {
                    var target = WithMargin(CardBody(frame.Layout.Cards[i]), frame.Theme, frame.Rasters[i].Scale);
                    if (frame.Bitmaps[i] is { } bitmap) graphics.DrawImage(bitmap, target);
                    else if (_failed != frame.Key && canvas.Viewport.IsVisible(ref target))
                    {
                        // An unusually large viewport can expose more surfaces than can be retained.
                        // Paint those sequentially at the same density, accounting for the temporary too.
                        try
                        {
                            CanvasCardCache.Reserve(this, frame.Rasters[i].Bytes);
                            using var temporary = RasterDrawing.Render(frame.Specs[i], frame.Theme, frame.Rasters[i].Scale);
                            graphics.DrawImage(temporary, target);
                        }
                        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
                        {
                            _failed = frame.Key;
                            Component.AddRuntimeMessage(global::Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning, "Canvas Card render failed: " + e.Message);
                        }
                    }
                }
            }
            else DrawPlaceholder(graphics);
        }
        finally { graphics.Restore(saved); }
    }
    private RectangleF CardBody(CardLayout card) => new(_body.X + (float)card.X, _body.Y + (float)card.Y, (float)card.W, (float)card.H);
    // Themed bitmaps carry a shadow margin on every side; grow the target so the card body keeps its measured size.
    private static RectangleF WithMargin(RectangleF body, Theme theme, double scale)
    {
        var m = (float)(RasterDrawing.Margin(theme, scale) / scale);
        return m <= 0 ? body : new(body.X - m, body.Y - m, body.Width + 2 * m, body.Height + 2 * m);
    }
    private void DrawPlaceholder(Graphics graphics)
    {
        // Match the capsule's own text colour so the message stays readable on error/warning capsules.
        var style = global::Grasshopper.GUI.Canvas.GH_CapsuleRenderEngine.GetImpliedStyle(
            global::Grasshopper.GUI.Canvas.GH_CapsuleRenderEngine.GetImpliedPalette(Component), this);
        using var brush = new SolidBrush(style.Text);
        using var font = new Font(GdiCanvas.Family("Segoe UI"), 12, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(Component.Placeholder, font, brush, new RectangleF(_body.X + 12, _body.Y + 16, _body.Width - 24, _body.Height - 28));
    }
    private static CanvasRasterPlan Plan(Size2 size, Theme theme, double scale, long budget = CanvasRasterPlan.BitmapBudget)
        => CanvasRasterPlan.Create(size, scale, budget, theme.Shadow ? theme.ShadowExtent + 1 : 0);
    private CanvasStackFrameKey? DesiredKey()
    {
        if (Component.State.Specs.Length == 0) return null;
        var theme = Component.CardTheme;
        var layout = new CanvasStackLayout(Component.State.Specs, theme);
        var visible = 0;
        for (var i = 0; i < layout.Cards.Length; i++)
        {
            var bounds = CardBody(layout.Cards[i]);
            bounds.Inflate((float)(theme.ShadowExtent + 1), (float)(theme.ShadowExtent + 1));
            if (_canvas is null || _canvas.Viewport.IsVisible(ref bounds)) visible |= 1 << i;
        }
        var retained = CanvasStackRaster.RetainedMask(CanvasStackRaster.Plan(layout, theme, _scale), visible);
        return new(Component.State.Revision, theme, Component.BodySize, _scale, retained);
    }
    internal void Regenerate()
    {
        ConduitPlus.Rhino.OverlayManager.Start();
        Pending = false;
        if (Disabled || Component.State.Specs.Length == 0 || DesiredKey() is not { } key || key == _failed || Frame?.IsReady(key) == true) return;
        try
        {
            var next = new CanvasCardFrame(Component.State.Specs, key);
            CanvasCardCache.Reserve(this, next.AdditionalBytes(Frame));
            next.Render(Frame);
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
    private GH_ObjectResponse Apply(CardInteractionResult result)
    {
        result.Action?.Apply(Component.State);
        if (result.Repaint) { HideTooltip(); RequestDisplay(); }
        if (result.Response == CardResponse.Release && _canvas is { } canvas && canvas.ActiveObject == Component)
        { if (!_interaction.OwnsRelease) canvas.ActiveObject = null; canvas.Capture = false; }
        return result.Response == CardResponse.Release && _interaction.OwnsRelease ? GH_ObjectResponse.Handled : CardHostModifiers.Response(result.Response);
    }
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        var result = _interaction.Down(InteractionFrame, Local(e.CanvasLocation), new(e.ControlX, e.ControlY), Modifiers,
            e.Button == MouseButtons.Left && HostInput.MouseButtons == MouseButtons.Left, Interactive, sender.IsActiveInteraction);
        if (result.Response == CardResponse.Pass) return base.RespondToMouseDown(sender, e);
        ClearHover(); sender.Capture = true; CanvasCardCache.Queue(this);
        return Apply(result);
    }
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        var result = _interaction.Move(InteractionFrame, Local(e.CanvasLocation), new(e.ControlX, e.ControlY), Modifiers, Interactive);
        return result.Response == CardResponse.Pass ? base.RespondToMouseMove(sender, e) : Apply(result);
    }
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        var result = _interaction.Up(InteractionFrame, Local(e.CanvasLocation), new(e.ControlX, e.ControlY), Modifiers, e.Button == MouseButtons.Left, Interactive);
        if (result.Response == CardResponse.Pass) return base.RespondToMouseUp(sender, e);
        var response = Apply(result); _hoverPoint = e.CanvasLocation; RefreshHover();
        return response;
    }
    public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape || !_interaction.Active) return base.RespondToKeyDown(sender, e);
        CancelGesture(); return GH_ObjectResponse.Handled;
    }
    internal void CheckCapture()
    {
        if (_interaction.Active && (!Interactive || _canvas?.ActiveObject != Component || _canvas.Capture != true)) CancelGesture();
        if (!_interaction.Active && _interaction.OwnsRelease && (HostInput.MouseButtons & MouseButtons.Left) == 0)
            Apply(_interaction.Up(new([]), new(), new(), CardModifiers.None, enabled: false));
    }
    internal void CancelGesture() { Component.State.CancelHover(); Apply(_interaction.Cancel()); }
    public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs e)
    {
        if (!Interactive || _interaction.Active || _canvas?.IsActiveInteraction == true) return;
        if (Frame is { } frame && Hit(point) is { } hit && Tooltip.For(frame.Specs.Single(c => c.Id == hit.CardId), hit.Key, Component.State.Membership, frame.Theme) is { } lines)
        {
            _tooltipOwned = true;
            e.Title = frame.Specs.Single(c => c.Id == hit.CardId).Title; e.Text = string.Join(Environment.NewLine, lines); e.Description = "";
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
    internal void EvictRasters()
    {
        // Keep the last published immutable specs/theme/layout, including for locked/disabled cards.
        // Missing screen surfaces and GH exports render budgeted temporaries from this snapshot.
        Frame?.ReleaseRasters();
        _canvas?.Invalidate();
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
        if (_canvas?.ActiveObject == Component) { _canvas.ActiveObject = null; _canvas.Capture = false; }
        _canvas = null;
    }
    internal void Detach() { DetachCanvas(); Pending = false; ReleaseFrame(); CanvasCardCache.Remove(this); }
}
