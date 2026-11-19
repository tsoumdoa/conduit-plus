using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;

namespace ConduitPlus.CanvasWidgets;

/// <summary>The only assembly using the excluded widget API. No paint-event or independent input-router fallback.</summary>
public sealed class WidgetService : IDisposable
{
    private readonly Dictionary<GH_Canvas, PinnedWidget> _widgets = [];
    private bool _disposed;
    private static long _rasterizations;
    public static long RasterizationCount => System.Threading.Interlocked.Read(ref _rasterizations);
    internal static void Rasterized() => System.Threading.Interlocked.Increment(ref _rasterizations);
    private WidgetService()
    {
        try
        {
            GH_Canvas.WidgetListCreated += WidgetListCreated;
            global::Grasshopper.Instances.CanvasCreated += CanvasCreated;
            CanvasOverlayHost.Changed += Changed; CanvasOverlayHost.ShowRequested += Show;
            // 8.20 exposes Canvases as internal, not public. Keep this optional discovery at the adapter boundary.
            var existing = typeof(GH_Canvas).GetProperty("Canvases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as IEnumerable<GH_Canvas>;
            foreach (var canvas in existing?.ToArray() ?? []) CanvasCreated(canvas);
            if (global::Grasshopper.Instances.ActiveCanvas is { } active) CanvasCreated(active);
        }
        catch { Dispose(); throw; }
    }
    public static IDisposable Start() => new WidgetService();
    private void WidgetListCreated(object sender, GH_CanvasWidgetListEventArgs e)
    {
        try
        {
            var widget = new PinnedWidget();
            if (sender is GH_Canvas canvas)
            {
                if (_widgets.Remove(canvas, out var previous)) previous.Dispose();
                widget.Attach(canvas); _widgets.Add(canvas, widget);
            }
            e.AddWidget(widget);
        }
        catch (Exception error) { Failed(error); }
    }
    private void CanvasCreated(GH_Canvas canvas)
    {
        if (_disposed || canvas.IsDisposed) return;
        canvas.HandleCreated -= HandleCreated; canvas.HandleCreated += HandleCreated;
        canvas.Disposed -= CanvasDisposed; canvas.Disposed += CanvasDisposed;
        if (canvas.IsHandleCreated) QueueReconcile(canvas);
    }
    private void HandleCreated(object? sender, EventArgs e) { if (sender is GH_Canvas canvas) QueueReconcile(canvas); }
    private void QueueReconcile(GH_Canvas canvas)
    {
        // Never add/remove during widget paint/input enumeration. Constructor-list registration uses AddWidget instead.
        canvas.BeginInvoke((Action)(() =>
        {
            if (_disposed || canvas.IsDisposed) return;
            try
            {
                var present = canvas.Widgets.OfType<PinnedWidget>().FirstOrDefault();
                if (present is null)
                {
                    if (!_widgets.TryGetValue(canvas, out present)) present = new();
                    canvas.Widgets.Add(present);
                }
                present.Attach(canvas); _widgets[canvas] = present; present.Changed(true);
            }
            catch (Exception error) { Failed(error); }
        }));
    }
    private void CanvasDisposed(object? sender, EventArgs e)
    {
        if (sender is not GH_Canvas canvas) return;
        canvas.HandleCreated -= HandleCreated; canvas.Disposed -= CanvasDisposed;
        if (_widgets.Remove(canvas, out var widget)) widget.Dispose();
    }
    private void Changed(bool cancel) { foreach (var w in _widgets.Values.ToArray()) w.Changed(cancel); }
    private void Show()
    {
        var active = global::Grasshopper.Instances.ActiveCanvas;
        if (active is not null && _widgets.TryGetValue(active, out var widget)) { widget.Visible = true; widget.Changed(true); }
    }
    private void Failed(Exception error)
    {
        try { CanvasOverlayHost.Fail("Canvas Overlay registration failed: " + error.Message); }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        GH_Canvas.WidgetListCreated -= WidgetListCreated;
        global::Grasshopper.Instances.CanvasCreated -= CanvasCreated;
        CanvasOverlayHost.Changed -= Changed; CanvasOverlayHost.ShowRequested -= Show;
        foreach (var (canvas, widget) in _widgets)
        {
            canvas.HandleCreated -= HandleCreated; canvas.Disposed -= CanvasDisposed;
            try { widget.Dispose(); } catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
            // Keep the inert registered widget: shutdown may occur during enumeration or process teardown.
        }
        _widgets.Clear();
    }
}

internal sealed class PinnedWidget : GH_Widget, IDisposable, ICanvasRasterOwner
{
    private readonly string _cacheId = "pinned:" + Guid.NewGuid().ToString("N");
    private long _buildingBytes, _lastVisible;
    private string? _evictedSignature;
    public object? RasterDocument => _canvas?.Document;
    public string RasterOwner => _cacheId;
    public long RasterBytes => _cards.Sum(c => (long)c.Image.Width * c.Image.Height * 4) + _buildingBytes;
    public long RasterLastVisible => _lastVisible;
    public void EvictCanvasRasters()
    {
        Cancel(); ClearHover(); _builder?.Dispose(); _builder = null; _buildingSignature = null;
        foreach (var card in _cards) card.Image.Dispose(); _cards.Clear(); _accepting = false;
        _evictedSignature = Signature(); _signature = _evictedSignature;
        foreach (var display in Displays) display.SetNotice("memory", "Pinned cards omitted under the shared document raster budget; show pinned cards again or change the layout to retry.");
        _canvas?.Invalidate();
    }
    private bool _visible = true, _faulted, _disposed, _pending, _accepting, _tooltipOwned, _geometryValid;
    private GH_Canvas? _canvas;
    private GH_Document? _document;
    private long _generation;
    private string? _signature, _buildingSignature, _faultMessage;
    private IEnumerator<bool>? _builder;
    private bool _checkingOtherWidgets;
    private readonly Dictionary<Guid, (CardSpec[] Specs, Theme Theme)> _frozen = [];
    private readonly CardInteractionController _interaction = new();
    private ICanvasOverlayDisplay? _gestureOwner;
    private CardInteractionFrame _gestureFrame = new([]);
    private readonly List<ScreenCard> _cards = [];
    private readonly Bitmap _icon = MakeIcon();
    private readonly System.Windows.Forms.Timer _captureTimer = new() { Interval = 30 };
    private Point? _hoverPoint;
    public override string Name => "Conduit+ cards";
    public override string Description => "Cards pinned to this Grasshopper canvas window";
    public override Bitmap Icon_24x24 => _icon;
    public override bool Visible
    {
        get => _visible && !_faulted && !_disposed;
        set { if (_visible == value) return; _visible = value; Withdraw(); Changed(true); }
    }
    internal void Attach(GH_Canvas value)
    {
        if (_canvas == value) return;
        Detach(); Owner = value; _canvas = value;
        value.DocumentChanged += DocumentChanged; value.MouseCaptureChanged += CaptureChanged;
        value.MouseLeave += MouseLeft; value.MouseWheel += Navigation; value.MouseDown += Navigation;
        value.SizeChanged += GeometryChanged; value.DpiChangedAfterParent += GeometryChanged; value.ViewportChanged += Navigated;
        value.Disposed += CanvasDisposed;
        _captureTimer.Tick += CheckCapture; CanvasRasterCache.Register(this); ObserveDocument();
    }
    private ICanvasOverlayDisplay[] Displays => CanvasOverlayHost.Displays.Where(d => ReferenceEquals(d.Document, _canvas?.Document) && (d is not IGH_DocumentObject obj || _canvas!.Document.Objects.Contains(obj))).OrderBy(d => d.OwnerId.ToString("N"), StringComparer.Ordinal).ToArray();
    private ICanvasOverlayDisplay[] ShownDisplays => Displays.Where(d => d.CardsVisible).ToArray();
    private bool Eligible => Visible && _canvas is { IsDisposed: false, DrawingMode: GH_CanvasMode.Control } && _canvas.Document is { Context: not (GH_DocumentContext.Close or GH_DocumentContext.Unloaded) };
    private double Dpi => (_canvas?.DeviceDpi ?? 96) / 96d;
    private Rect2 Client => _canvas is { } c ? new(0, 0, c.ClientSize.Width, c.ClientSize.Height) : new();
    private Rect2[] Exclusions()
    {
        if (_canvas is not { } canvas) return [];
        var rectangles = new List<Rect2>();
        foreach (var fixedWidget in canvas.Widgets.OfType<GH_CanvasWidget_FixedObject>().Where(w => w.Visible))
        {
            var p = fixedWidget.ControlLocation(canvas.Viewport); var s = fixedWidget.Size; var padding = 4 * Dpi;
            rectangles.Add(new(p.X - padding, p.Y - padding, s.Width + 2 * padding, s.Height + 2 * padding));
        }
        // Actual child controls include navigation/scrollbar chrome. Profiler intentionally has no reservation.
        foreach (Control child in canvas.Controls)
            if (child.Visible && child is ScrollBar) rectangles.Add(new(child.Left, child.Top, child.Width, child.Height));
        return rectangles.ToArray();
    }
    private string Signature() => CardJson.Serialize(new
    {
        Client, Dpi, Exclusions = Exclusions(),
        Owners = ShownDisplays.Select(d => new { d.OwnerId, d.State.Revision, d.Theme, d.Disabled, d.BrushEnabled })
    });
    internal void Changed(bool cancel)
    {
        if (_faultMessage is { } fault)
            foreach (var display in Displays) display.SetNotice("adapter", fault, true);
        Safe(() =>
        {
            if (cancel) { Cancel(); ClearHover(); _accepting = false; _signature = null; _evictedSignature = null; }
            if (!Eligible) { Withdraw(); return; }
            if (_signature != Signature()) _accepting = false;
            Schedule(); _canvas?.Invalidate();
        });
    }
    private void Schedule()
    {
        if (_pending || !Eligible || _canvas is not { IsHandleCreated: true } canvas) return;
        if (_evictedSignature == Signature()) return;
        _pending = true; var document = canvas.Document; var generation = _generation;
        canvas.BeginInvoke((Action)(() =>
        {
            if (_disposed || _faulted || generation != _generation || !ReferenceEquals(canvas.Document, document)) return;
            _pending = false; if (!Eligible) return;
            Safe(Build);
        }));
    }
    public override void Render(GH_Canvas canvas) => Safe(() =>
    {
        // GH export may invoke Render; it must never publish hits, build rasters, or draw the HUD.
        if (!Eligible) return;
        var signature = Signature();
        if (signature != _signature) { _accepting = false; Schedule(); }
        _lastVisible = Environment.TickCount64;
        var graphics = canvas.Graphics; var saved = graphics.Save();
        try
        {
            graphics.ResetTransform(); graphics.PageUnit = GraphicsUnit.Pixel; graphics.PageScale = 1;
            graphics.SetClip(new Rectangle(0, 0, canvas.ClientSize.Width, canvas.ClientSize.Height));
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            foreach (var card in _cards.Where(c => _geometryValid && c.Display.CardsVisible && Displays.Contains(c.Display)))
            {
                var m = RasterDrawing.Margin(card.Theme, card.Scale);
                graphics.DrawImageUnscaled(card.Image, (int)card.Transform.Body.X - m, (int)card.Transform.Body.Y - m);
            }
        }
        finally { graphics.Restore(saved); }
    });
    private void Build()
    {
        var signature = Signature();
        if (_builder is null || _buildingSignature != signature)
        {
            _builder?.Dispose(); _buildingSignature = signature; _builder = BuildSteps().GetEnumerator();
        }
        if (_builder.MoveNext()) Schedule();
        else { _builder.Dispose(); _builder = null; _buildingSignature = null; }
    }
    private IEnumerable<bool> BuildSteps()
    {
        if (_canvas is not { } canvas) yield break;
        var allDisplays = Displays;
        var displays = allDisplays.Where(d => d.CardsVisible).ToArray();
        foreach (var owner in _frozen.Keys.Where(id => !allDisplays.Any(d => d.OwnerId == id)).ToArray()) _frozen.Remove(owner);
        foreach (var display in allDisplays)
            if (!display.Disabled || !_frozen.ContainsKey(display.OwnerId)) _frozen[display.OwnerId] = (display.State.Specs, display.Theme);
        var participants = displays.Select(d => new LayoutParticipant(d.OwnerId, 1, _frozen[d.OwnerId].Theme, _frozen[d.OwnerId].Specs)).ToArray();
        var layout = PinnedCanvasLayout.Solve(new(Client, Dpi, participants, TextMetrics.Measurer, Exclusions()));
        var next = new List<ScreenCard>(); var allocated = new List<Bitmap>();
        var omitted = layout.Hidden.ToList(); long bytes = 0; var published = false;
        try
        {
            foreach (var placement in layout.Placements)
            {
                var display = displays.Single(d => d.OwnerId == placement.Owner);
                var snapshot = _frozen[display.OwnerId]; var spec = snapshot.Specs.Single(s => s.Id == placement.CardId); var theme = snapshot.Theme;
                var bodySize = CardMeasure.Measure(spec, theme);
                var plan = CanvasRasterPlan.Create(bodySize, Dpi, margin: theme.Shadow ? theme.ShadowExtent + 1 : 0);
                // Pinned cards never silently reduce density. Half the budget leaves room for atomic replacement.
                if (Math.Abs(plan.Scale - Dpi) > .000001 || bytes + plan.Bytes > CanvasRasterPlan.DocumentBudget / 2)
                { omitted.Add((display.OwnerId, spec.Id)); continue; }
                var key = new PinnedRasterKey(display.OwnerId, spec.Id, spec.Rev, theme, bodySize, plan);
                var previous = _cards.FirstOrDefault(c => c.Display == display && c.Key == key);
                var image = previous?.Image; var rasterized = image is null;
                if (image is null)
                {
                    CanvasRasterCache.Reserve(this, plan.Bytes);
                    image = RasterDrawing.Render(spec, theme, Dpi); WidgetService.Rasterized(); allocated.Add(image); _buildingBytes += plan.Bytes;
                }
                bytes += plan.Bytes;
                next.Add(new(display, spec, theme, Dpi, key, image, new(new(placement.X, placement.Y, bodySize.Width * Dpi, bodySize.Height * Dpi), Dpi, Client)));
                if (rasterized) yield return true; // At most one new raster per UI message.
            }
            foreach (var old in _cards.Where(c => !next.Any(n => ReferenceEquals(c.Image, n.Image)))) old.Image.Dispose();
            _cards.Clear(); _cards.AddRange(next); published = true; _buildingBytes = 0; _signature = Signature(); _accepting = true; _geometryValid = true;
            foreach (var display in allDisplays)
            {
                var count = omitted.Count(h => h.Owner == display.OwnerId);
                display.SetNotice("memory", null);
                display.SetNotice("overflow", count == 0 ? null : $"{count} pinned card(s) omitted: window fit or raster memory/size limit. Enlarge the canvas or reduce authored card sizes.");
            }
            RefreshHover(); canvas.Invalidate();
        }
        finally
        {
            if (!published) foreach (var image in allocated) image.Dispose();
            _buildingBytes = 0;
        }
    }
    private bool OtherWidgetAt(Point point)
    {
        if (_canvas is not { } canvas) return false;
        if (_checkingOtherWidgets) return true;
        _checkingOtherWidgets = true;
        try
        {
            var local = new PointF(canvas.Viewport.UnprojectX(point.X), canvas.Viewport.UnprojectY(point.Y));
            return canvas.Widgets.Any(w => !ReferenceEquals(w, this) && w.Visible && w.Contains(point, local));
        }
        finally { _checkingOtherWidgets = false; }
    }
    private ScreenCard? At(Point point) => Eligible && _geometryValid && !OtherWidgetAt(point)
        ? _cards.LastOrDefault(c => c.Display.CardsVisible && Displays.Contains(c.Display) && c.Transform.Contains(new(point.X, point.Y))) : null;
    public override bool Contains(Point control, PointF canvas) => Safe(() => At(control) is not null, false);
    private static CardModifiers Modifiers
    {
        get
        {
            var keys = Control.ModifierKeys;
            return ((keys & Keys.Shift) != 0 ? CardModifiers.Shift : 0) | ((keys & Keys.Control) != 0 ? CardModifiers.Control : 0) | ((keys & Keys.Alt) != 0 ? CardModifiers.Alt : 0);
        }
    }
    private CardInteractionFrame Frame(ICanvasOverlayDisplay display) => new(_cards.Where(c => c.Display == display).Select(c =>
        new InteractionCard(c.Spec, c.Theme, new(c.Transform.Body.X / Dpi, c.Transform.Body.Y / Dpi, c.Transform.Body.Width / Dpi, c.Transform.Body.Height / Dpi), CardMeasure.Hits(c.Spec, c.Theme))).ToArray());
    private Point2 Logical(GH_CanvasMouseEvent e) => new(e.ControlX / Dpi, e.ControlY / Dpi);
    private static Point2 ControlPoint(GH_CanvasMouseEvent e) => new(e.ControlX, e.ControlY);
    private GH_ObjectResponse Apply(CardInteractionResult result)
    {
        if (result.Action is { } action && _gestureOwner is { } display) { action.Apply(display.State); CanvasOverlayHost.Invalidate(); }
        if (result.Response == CardResponse.Release) ReleaseCapture();
        if (result.Repaint) _canvas?.Invalidate();
        return result.Response switch { CardResponse.Capture => GH_ObjectResponse.Capture, CardResponse.Release => _interaction.OwnsRelease ? GH_ObjectResponse.Handled : GH_ObjectResponse.Release, CardResponse.Handled => GH_ObjectResponse.Handled, _ => GH_ObjectResponse.Ignore };
    }
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e) => Safe(() =>
    {
        var card = At(new(e.ControlX, e.ControlY));
        if (card is null || sender.IsActiveInteraction || (sender.ActiveWidget is { } other && other != this)) return GH_ObjectResponse.Ignore;
        if (e.Button == MouseButtons.Right && Modifiers == CardModifiers.None)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy card as image", null, (_, _) => Safe(() =>
            {
                // Re-read on click: a solve may update or remove the card while the menu is open.
                var current = _cards.LastOrDefault(c => c.Display == card.Display && c.Spec.Id == card.Spec.Id && c.Display.CardsVisible && Displays.Contains(c.Display));
                var error = current is null ? "Could not copy card as image: the card is no longer shown." : null;
                if (current is not null) CardClipboard.TryCopy([current.Spec], current.Theme, out error);
                if (error is not null) global::Rhino.RhinoApp.WriteLine(error);
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Find Canvas Overlay component", null, (_, _) =>
            {
                if (card.Display is GH_Component component) { component.Attributes.Selected = true; sender.Viewport.MidPoint = component.Attributes.Pivot; sender.Invalidate(); }
            });
            menu.Items.Add("Hide pinned cards", null, (_, _) => Visible = false);
            menu.Closed += (_, _) => menu.Dispose(); menu.Show(sender, new Point(e.ControlX, e.ControlY));
            return GH_ObjectResponse.Handled;
        }
        if (!_accepting || card.Display.Disabled || !card.Display.BrushEnabled)
            return e.Button == MouseButtons.Left && Modifiers == CardModifiers.None ? GH_ObjectResponse.Handled : GH_ObjectResponse.Ignore;
        _gestureOwner = card.Display; _gestureFrame = Frame(card.Display);
        var result = _interaction.Down(_gestureFrame, Logical(e), ControlPoint(e), Modifiers,
            e.Button == MouseButtons.Left && Control.MouseButtons == MouseButtons.Left, consumeBody: true);
        if (result.Response == CardResponse.Capture)
        {
            ClearHover(); sender.ActiveWidget = this; sender.Capture = true; _captureTimer.Start();
        }
        return Apply(result);
    }, GH_ObjectResponse.Ignore);
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e) => Safe(() =>
    {
        if (_interaction.Active)
            return Apply(_interaction.Move(_gestureFrame, Logical(e), ControlPoint(e), Modifiers, GestureEligible));
        if (_accepting && e.Button == MouseButtons.None && Modifiers == CardModifiers.None && !sender.IsActiveInteraction) { _hoverPoint = new(e.ControlX, e.ControlY); RefreshHover(); }
        else ClearHover();
        return At(new(e.ControlX, e.ControlY)) is not null ? GH_ObjectResponse.Handled : GH_ObjectResponse.Ignore;
    }, GH_ObjectResponse.Ignore);
    private bool GestureEligible => Eligible && _gestureOwner is { CardsVisible: true, Disabled: false, BrushEnabled: true } owner && Displays.Contains(owner);
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e) => Safe(() =>
    {
        var response = Apply(_interaction.Up(_gestureFrame, Logical(e), ControlPoint(e), Modifiers, e.Button == MouseButtons.Left, GestureEligible));
        _gestureOwner = null; _hoverPoint = new(e.ControlX, e.ControlY); RefreshHover(); return response;
    }, GH_ObjectResponse.Release);
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e) => e.Button == MouseButtons.Left && (Modifiers & CardModifiers.Alt) == 0 && Contains(new(e.ControlX, e.ControlY), e.CanvasLocation) ? GH_ObjectResponse.Handled : GH_ObjectResponse.Ignore;
    public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e) => Safe(() =>
    {
        if (e.KeyCode != Keys.Escape || !_interaction.Active) return GH_ObjectResponse.Ignore;
        Cancel(); return GH_ObjectResponse.Handled;
    }, GH_ObjectResponse.Release);
    private void CheckCapture(object? sender, EventArgs e) => Safe(() =>
    {
        if (_interaction.Active && (!GestureEligible || _canvas?.ActiveWidget != this || _canvas.Capture != true)) Cancel();
        if (!_interaction.Active && _interaction.OwnsRelease && (Control.MouseButtons & MouseButtons.Left) == 0)
        {
            // A release outside the control may not be dispatched by GH. Do not leave an idle active widget.
            _interaction.Up(new([]), new(), new(), CardModifiers.None, enabled: false); ReleaseCapture(); _gestureOwner = null;
        }
    });
    private void ReleaseCapture()
    {
        if (!_interaction.OwnsRelease || _faulted || _disposed) _captureTimer.Stop();
        if (_canvas?.ActiveWidget == this)
        {
            if (!_interaction.OwnsRelease || _faulted || _disposed) _canvas.ActiveWidget = null;
            _canvas.Capture = false;
        }
    }
    private void Cancel() { Apply(_interaction.Cancel()); _gestureOwner?.State.CancelHover(); ReleaseCapture(); }
    private void ClearHover()
    {
        _hoverPoint = null;
        foreach (var display in Displays.Concat(_cards.Select(c => c.Display)).Distinct()) display.State.Hover(null);
        if (_tooltipOwned && _canvas is not null && GH_Tooltip.IsOwner(_canvas)) GH_Tooltip.Clear();
        _tooltipOwned = false;
    }
    private void RefreshHover()
    {
        if (!_accepting || _interaction.Active || _hoverPoint is not { } point) return;
        var card = At(point);
        foreach (var display in Displays)
        {
            var before = display.State.Revision;
            if (card?.Display == display && !display.Disabled && display.BrushEnabled)
            {
                var local = card.Transform.ToLocal(new(point.X, point.Y));
                var hit = CardMeasure.Hits(card.Spec, card.Theme).LastOrDefault(h => h.Contains(local.X, local.Y));
                display.State.Hover(card.Spec.Id, hit?.Key);
            }
            else display.State.Hover(null);
            if (before != display.State.Revision) Changed(false);
        }
    }
    // GH's widget tooltip point is assumed canvas-space, matching attributes; the region is control-space.
    private Point TooltipControl(PointF point) => _canvas is { } canvas ? new((int)canvas.Viewport.ProjectX(point.X), (int)canvas.Viewport.ProjectY(point.Y)) : Point.Empty;
    public override bool IsTooltipRegion(PointF point) => Safe(() => _accepting && !_interaction.Active && At(TooltipControl(point)) is { Display.Disabled: false }, false);
    public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs e) => Safe(() =>
    {
        if (!_accepting || _interaction.Active || At(TooltipControl(point)) is not { } card || card.Display.Disabled) return;
        var p = TooltipControl(point); var local = card.Transform.ToLocal(new(p.X, p.Y));
        var hit = CardMeasure.Hits(card.Spec, card.Theme).LastOrDefault(h => h.Contains(local.X, local.Y));
        if (hit is null || Tooltip.For(card.Spec, hit.Key, card.Display.State.Membership) is not { } lines) return;
        var region = card.Transform.ToControl(hit);
        e.Title = card.Spec.Title; e.Text = string.Join(Environment.NewLine, lines); e.Description = "";
        e.Region = Rectangle.Ceiling(new RectangleF((float)region.X, (float)region.Y, (float)region.Width, (float)region.Height)); _tooltipOwned = true;
    });
    public override void AppendToMenu(ToolStripDropDownMenu menu) => Safe(() =>
    {
        var item = new ToolStripMenuItem("Conduit+ cards") { Checked = Visible, CheckOnClick = false };
        item.Click += (_, _) => Visible = !Visible; menu.Items.Add(item);
    });
    private void ObserveDocument()
    {
        if (_document == _canvas?.Document) return;
        if (_document is { } old) { old.ObjectsDeleted -= ObjectsDeleted; old.ContextChanged -= ContextChanged; }
        _document = _canvas?.Document;
        if (_document is { } document) { document.ObjectsDeleted += ObjectsDeleted; document.ContextChanged += ContextChanged; }
    }
    private void ObjectsDeleted(object sender, GH_DocObjectEventArgs e) => Safe(() =>
    {
        if (_document?.Context == GH_DocumentContext.Close) Withdraw();
        else Changed(true);
    });
    private void ContextChanged(object sender, GH_DocContextEventArgs e) => Safe(() =>
    {
        if (e.Context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) Withdraw();
        else Changed(true);
    });
    private void DocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e) => Safe(() => { Withdraw(); ObserveDocument(); Changed(true); });
    private void CaptureChanged(object? sender, EventArgs e) { if (_canvas?.Capture != true) Safe(Cancel); }
    private void MouseLeft(object? sender, EventArgs e) => Safe(ClearHover);
    private void Navigation(object? sender, MouseEventArgs e)
    {
        if (e.Delta != 0 || e.Button != MouseButtons.Left || (Modifiers & CardModifiers.Alt) != 0) Safe(() => { Cancel(); ClearHover(); });
    }
    private void Navigated(object? sender, GH_CanvasViewportChangedEventArgs e) => Safe(() => { Cancel(); ClearHover(); });
    private void GeometryChanged(object? sender, EventArgs e) => Safe(() =>
    {
        // Withdraw unsafe placements immediately, but retain their rasters for relocation at the same DPI.
        _geometryValid = false; _pending = false; _generation++;
        _builder?.Dispose(); _builder = null; _buildingSignature = null;
        Changed(true);
    });
    private void CanvasDisposed(object? sender, EventArgs e) => Dispose();
    private void Withdraw()
    {
        _accepting = false; _geometryValid = false; _pending = false; _generation++; _signature = null;
        try { Cancel(); ClearHover(); }
        finally
        {
            _builder?.Dispose(); _builder = null; _buildingSignature = null;
            foreach (var card in _cards) card.Image.Dispose(); _cards.Clear(); _frozen.Clear();
        }
    }
    private void Fault(Exception error)
    {
        if (_faulted || _disposed) return; _faulted = true;
        try { Withdraw(); } catch { _accepting = false; }
        ReleaseCapture();
        _faultMessage = "Canvas Overlay withdrawn after a widget failure: " + error.Message;
        foreach (var display in Displays) display.SetNotice("adapter", _faultMessage, true);
        _canvas?.Invalidate();
    }
    private void Safe(Action action) { if (_disposed || _faulted) return; try { action(); } catch (Exception error) { Fault(error); } }
    private T Safe<T>(Func<T> action, T fallback) { if (_disposed || _faulted) return fallback; try { return action(); } catch (Exception error) { Fault(error); return fallback; } }
    private void Detach()
    {
        try { Withdraw(); }
        finally
        {
            if (_canvas?.ActiveWidget == this) { _canvas.ActiveWidget = null; _canvas.Capture = false; }
            CanvasRasterCache.Remove(this);
            if (_canvas is { } c)
            {
                c.DocumentChanged -= DocumentChanged; c.MouseCaptureChanged -= CaptureChanged; c.MouseLeave -= MouseLeft;
                c.MouseWheel -= Navigation; c.MouseDown -= Navigation; c.SizeChanged -= GeometryChanged;
                c.DpiChangedAfterParent -= GeometryChanged; c.ViewportChanged -= Navigated; c.Disposed -= CanvasDisposed;
            }
            if (_document is { } document) { document.ObjectsDeleted -= ObjectsDeleted; document.ContextChanged -= ContextChanged; }
            _document = null; _captureTimer.Tick -= CheckCapture; _canvas = null;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { Detach(); }
        finally { _disposed = true; _captureTimer.Dispose(); _icon.Dispose(); }
    }
    private sealed record ScreenCard(ICanvasOverlayDisplay Display, CardSpec Spec, Theme Theme, double Scale, PinnedRasterKey Key, Bitmap Image, PinnedHitTransform Transform);
    private static Bitmap MakeIcon()
    {
        var image = new Bitmap(24, 24); using var g = Graphics.FromImage(image); g.Clear(Color.FromArgb(24, 30, 26));
        using var pen = new Pen(Color.White, 1.5f); g.DrawRectangle(pen, 4, 5, 16, 14); g.DrawRectangle(pen, 6, 8, 6, 3); g.DrawRectangle(pen, 6, 13, 6, 3); return image;
    }
}
