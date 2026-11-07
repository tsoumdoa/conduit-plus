using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using System.Diagnostics;
using System.Drawing;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    // Owner is implicit in the session. Cursor movement is deliberately absent from the cache identity.
    private sealed record TooltipTarget(string? Card, string Key, long Rev, double Dpi, bool Model, long DomainVersion);
    private readonly System.Windows.Forms.Timer _tooltipTimer = new() { Interval = 50 };
    private (string Card, string Key)? _cardHover;
    private TooltipTarget? _tooltipTarget;
    private string[]? _tooltipLines;
    private Bitmap? _tooltipBitmap;
    private DisplayBitmap? _tooltipDisplay;
    private Point2 _cursor;
    private bool _cursorInView, _tooltipReady;
    private long _tooltipSince;
    internal void Cursor(Point2 point, bool inView)
    {
        _cursor = point; _cursorInView = inView && new Rect2(0, 0, ViewSize.Width, ViewSize.Height).Contains(point.X, point.Y);
        if (!_cursorInView) HideTooltip();
    }
    internal void HideTooltip()
    {
        var visible = _tooltipReady;
        _tooltipTimer.Stop(); _tooltipTarget = null; _tooltipLines = null; _tooltipReady = false;
        _tooltipDisplay?.Dispose(); _tooltipDisplay = null;
        _tooltipBitmap?.Dispose(); _tooltipBitmap = null;
        if (visible && !_disposed) View.Redraw();
    }
    private bool TooltipAllowed => !_disposed && Visible && _cursorInView && !OverlayManager.TooltipBlocked && !Command.InCommand();
    internal void SyncTooltip()
    {
        if (!TooltipAllowed) { HideTooltip(); return; }
        var model = OverlayManager.ModelHover(this);
        var key = model ? _brush.HoverKey : _cardHover?.Key;
        var domain = OverlayManager.DomainSnapshot(this);
        var membership = domain?.Membership;
        CardSpec? card = null; string[]? lines = null;
        if (!string.IsNullOrEmpty(key) && !CardMeasure.IsReserved(key))
        {
            if (model)
            {
                foreach (var c in Cards)
                    if (!c.Groups.ContainsKey(key) && Tooltip.For(c, key) is { } content) { card = c; lines = content; break; }
                lines = Tooltip.WithMemberGroups([key, .. (lines ?? []).Where(line => line != key)], membership, key);
            }
            else if (_cardHover is { } hover && Card(hover.Card) is { } c)
            { card = c; lines = Tooltip.For(c, key, membership); }
        }
        if (lines is null || key is null) { HideTooltip(); return; }
        var next = new TooltipTarget(card?.Id, key, card?.Rev ?? 0, UiContext.Dpi(View), model, domain?.Version ?? 0);
        if (_tooltipTarget == next) { if (_tooltipReady) View.Redraw(); return; }
        HideTooltip(); _tooltipTarget = next; _tooltipLines = lines;
        _tooltipSince = Stopwatch.GetTimestamp(); _tooltipTimer.Start();
    }
    private void TickTooltip()
    {
        // Also observe exits/commands when Rhino sends no more mouse moves.
        if (!TooltipAllowed || !View.ScreenRectangle.Contains(System.Windows.Forms.Control.MousePosition)) { HideTooltip(); return; }
        if (_tooltipTarget is not { } target) return;
        if (target.Dpi != UiContext.Dpi(View)) { SyncTooltip(); return; }
        if (_tooltipReady || Stopwatch.GetElapsedTime(_tooltipSince).TotalMilliseconds < 250) return;
        try
        {
            _tooltipBitmap = RasterDrawing.RenderTooltip(_tooltipLines!, Theme, target.Dpi);
            _tooltipDisplay = new(_tooltipBitmap); _tooltipReady = true;
            View.Redraw();
        }
        catch (Exception ex) { HideTooltip(); RhinoApp.WriteLine($"Conduit+ tooltip: {ex.Message}"); }
    }
    internal void DrawTooltip(DisplayPipeline display)
    {
        if (!TooltipAllowed) { HideTooltip(); return; }
        if (!_tooltipReady || _tooltipDisplay is null || _tooltipBitmap is null) return;
        // The bitmap includes the theme's shadow margin on every side; position the tooltip body, not the margin.
        var margin = RasterDrawing.Margin(Theme, _tooltipTarget?.Dpi ?? UiContext.Dpi(View));
        var point = TooltipPainter.Position(_cursor, new(_tooltipBitmap.Width - 2 * margin, _tooltipBitmap.Height - 2 * margin), ViewSize, UiContext.Dpi(View));
        display.DrawBitmap(_tooltipDisplay, (int)Math.Round(point.X) - margin, (int)Math.Round(point.Y) - margin);
    }
}
