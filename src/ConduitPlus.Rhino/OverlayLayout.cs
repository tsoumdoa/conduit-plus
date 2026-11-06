using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;
using Rhino.Commands;
using Rhino.Display;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    private sealed class LayoutDrag(string cardId, CardSpec[] cards, CardLayout[] frozen, Size2 viewport, double dpi, Point2 grab, Bitmap bitmap) : IDisposable
    {
        public string CardId { get; } = cardId;
        public CardSpec[] Cards { get; } = cards;
        public CardLayout[] Frozen { get; } = frozen;
        public Size2 Viewport { get; } = viewport;
        public double Dpi { get; } = dpi;
        public Point2 Grab { get; } = grab;
        public Point2 Cursor { get; set; }
        public Slot Target { get; set; }
        public int Index { get; set; }
        public DisplayBitmap Ghost { get; } = new(bitmap);
        public void Dispose() { Ghost.Dispose(); bitmap.Dispose(); }
    }
    private LayoutDrag? _layoutDrag;
    private string? _titleHover;
    internal bool CommittingLayout { get; private set; }
    internal Size2 ViewSize => new(View.ActiveViewport.Size.Width, View.ActiveViewport.Size.Height);
    internal bool LayoutEnvironmentValid() => _layoutDrag is not { } d ||
        (!_disposed && Visible && ViewSize == d.Viewport && UiContext.Dpi(View) == d.Dpi && !Command.InCommand());
    internal bool TitleAt(string id, double x, double y) => Card(id) is { } c && Local(id, x, y) is { } p && CardMeasure.IsTitle(c, Theme, p.X, p.Y);
    internal void TitleHover(string? id, double x = 0, double y = 0)
    {
        var next = id is not null && Card(id) is { } c && (string.IsNullOrEmpty(c.Title) || TitleAt(id, x, y)) ? id : null;
        if (_titleHover == next) return;
        _titleHover = next; View.Redraw();
    }
    internal bool BeginLayoutDrag(string id, Point2 press)
    {
        if (Card(id) is not { WiredSlot: null }) return false;
        var dpi = UiContext.Dpi(View); var viewport = ViewSize;
        var frozen = SlotLayout.Place(Cards, Theme, viewport, dpi);
        var source = frozen.First(l => l.Id == id);
        _raster.Prepare(Cards, Theme, dpi);
        if (!_raster.Prepared.TryGetValue(id, out var raster)) return false;
        // Own both resources: a cache replacement/capture must never dispose the ghost's source.
        var original = raster.Capture.Bitmap;
        var bitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (var g = Graphics.FromImage(bitmap))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = .85f });
                g.DrawImage(original, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            _layoutDrag = new(id, Cards, frozen, viewport, dpi, new(press.X - source.X, press.Y - source.Y), bitmap);
        }
        catch { bitmap.Dispose(); throw; }
        _titleHover = null;
        MoveLayoutDrag(press);
        return true;
    }
    internal void MoveLayoutDrag(Point2 point)
    {
        if (_layoutDrag is not { } drag) return;
        drag.Cursor = point;
        drag.Target = LayoutEditing.NearestSlot(point, drag.Viewport, drag.Dpi);
        drag.Index = LayoutEditing.InsertionIndex(drag.Cards, drag.Frozen, drag.CardId, drag.Target, point.Y);
        View.Redraw();
    }
    internal void DropLayoutDrag()
    {
        if (_layoutDrag is not { } drag) return;
        CommittingLayout = true;
        try { OverlayManager.ProposeLayout(this, LayoutEditing.Drop(drag.Cards, OverlayManager.LayoutSnapshot(this), drag.CardId, drag.Target, drag.Index)); }
        finally { CommittingLayout = false; EndLayoutDrag(); }
    }
    internal void EndLayoutDrag()
    {
        var old = _layoutDrag; _layoutDrag = null;
        old?.Dispose();
        if (old is not null && !_disposed) View.Redraw();
    }
    private void DrawLayoutEditing(DisplayPipeline display, CardLayout[] layouts, double scale)
    {
        var accent = ColorTranslator.FromHtml(Theme.Accent);
        if (_layoutDrag is not { } drag)
        {
            if (_titleHover is not { } id || Card(id) is not { } card || layouts.FirstOrDefault(l => l.Id == id) is not { } l) return;
            var x = l.X + 3 * scale; var y = l.Y + 10 * scale;
            if (card.WiredSlot.HasValue)
            {
                display.Draw2dRectangle(new((int)x, (int)(y + 6 * scale), (int)(8 * scale), (int)(7 * scale)), accent, 1, Color.Transparent);
                display.DrawRoundedRectangle(new((float)(x + 4 * scale), (float)(y + 5 * scale)), (float)(6 * scale), (float)(8 * scale), (float)(3 * scale), accent, 1, Color.Transparent);
            }
            else for (var row = 0; row < 3; row++)
                display.Draw2dLine(new PointF((float)x, (float)(y + row * 5 * scale)), new PointF((float)(x + 5 * scale), (float)(y + row * 5 * scale)), accent, (float)(2 * scale));
            return;
        }
        var source = drag.Frozen.First(l => l.Id == drag.CardId);
        // Dashed source outline, drawn without allocating a bitmap on each move.
        void Dash(double x1, double y1, double x2, double y2)
        {
            var length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            for (var t = 0d; t < length; t += 10 * scale)
            {
                var end = Math.Min(length, t + 5 * scale);
                display.Draw2dLine(new PointF((float)(x1 + (x2 - x1) * t / length), (float)(y1 + (y2 - y1) * t / length)),
                    new PointF((float)(x1 + (x2 - x1) * end / length), (float)(y1 + (y2 - y1) * end / length)), accent, 1);
            }
        }
        Dash(source.X, source.Y, source.X + source.W, source.Y); Dash(source.X, source.Y + source.H, source.X + source.W, source.Y + source.H);
        Dash(source.X, source.Y, source.X, source.Y + source.H); Dash(source.X + source.W, source.Y, source.X + source.W, source.Y + source.H);
        foreach (var slot in Enum.GetValues<Slot>())
        {
            var r = LayoutEditing.Zone(slot, drag.Viewport, scale); var active = slot == drag.Target;
            display.DrawRoundedRectangle(new((float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2)), (float)r.Width, (float)r.Height,
                (float)(5 * scale), active ? accent : Color.FromArgb(100, accent), active ? 2 : 1, Color.FromArgb(active ? 40 : 15, accent));
            display.Draw2dText(slot.ToString().ToUpperInvariant(), accent, new global::Rhino.Geometry.Point2d(r.X + r.Width / 2, r.Y + r.Height / 2), true, (int)(12 * scale));
        }
        var stack = LayoutEditing.Stack(drag.Cards, drag.Target, drag.CardId).Select(c => drag.Frozen.First(l => l.Id == c.Id)).ToArray();
        var zone = LayoutEditing.Zone(drag.Target, drag.Viewport, scale);
        var yMarker = stack.Length == 0 ? zone.Y + zone.Height / 2 : drag.Index < stack.Length ? stack[drag.Index].Y - 4 * scale : stack[^1].Y + stack[^1].H + 4 * scale;
        var markerX = stack.Length == 0 ? zone.X : stack[0].X;
        var markerW = stack.Length == 0 ? zone.Width : stack.Max(l => l.W);
        display.Draw2dLine(new PointF((float)markerX, (float)yMarker), new PointF((float)(markerX + markerW), (float)yMarker), accent, (float)(3 * scale));
        display.DrawBitmap(drag.Ghost, (int)Math.Round(drag.Cursor.X - drag.Grab.X), (int)Math.Round(drag.Cursor.Y - drag.Grab.Y));
    }
}
