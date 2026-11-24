using System.Drawing;
using System.Diagnostics;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Rhino;
using Rhino.Display;
using Rhino.FileIO;

namespace ConduitPlus.Rhino;

internal sealed class LabelExportFrame(double dpi) : IDisposable
{
    private readonly List<(LabelRasterKey Key, Rect2 Bounds, string? Accent)> _glyphs = [];
    private readonly List<(Bitmap Image, DisplayBitmap Texture, int X, int Y)> _rasters = [];

    internal void Add(LabelRasterKey key, Rect2 bounds, string? accent) => _glyphs.Add((key, bounds, accent));

    internal void PrepareRaster()
    {
        foreach (var (key, bounds, accent) in _glyphs)
        {
            var image = RasterDrawing.RenderLabel(key);
            try
            {
                _rasters.Add((image, new DisplayBitmap(image), (int)Math.Round(bounds.X * dpi), (int)Math.Round(bounds.Y * dpi)));
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
    }

    internal void DrawRaster(DisplayPipeline display)
    {
        foreach (var (_, texture, x, y) in _rasters) display.DrawBitmap(texture, x, y);
        foreach (var (key, bounds, accent) in _glyphs)
        {
            if (accent is null) continue;
            display.DrawRoundedRectangle(new PointF((float)((bounds.X + bounds.Width / 2) * dpi),
                (float)((bounds.Y + bounds.Height / 2) * dpi)), (float)(bounds.Width * dpi),
                (float)(bounds.Height * dpi), (float)((key.Form == LabelForm.Dot ? 2.5 : 6) * dpi),
                GdiCanvas.ParseColor(LabelPainter.Fade(accent, key.Opacity)), (float)(2 * dpi), Color.Transparent);
        }
    }

    internal void DrawPdf(FilePdf pdf, int page)
    {
        foreach (var (key, bounds, accent) in _glyphs)
        {
            using var canvas = new PdfCanvas(pdf, page, bounds.X * dpi, bounds.Y * dpi, dpi, dpi);
            LabelPainter.Paint(canvas, key, applyOpacity: true);
            if (accent is not null)
                canvas.StrokeRoundedRect(new(0, 0, bounds.Width, bounds.Height),
                    key.Form == LabelForm.Dot ? 2.5 : 6, LabelPainter.Fade(accent, key.Opacity), 2);
        }
    }

    public void Dispose()
    {
        foreach (var (image, texture, _, _) in _rasters)
        {
            texture.Dispose();
            image.Dispose();
        }
        _rasters.Clear();
    }
}

internal sealed partial class OverlaySession
{
    internal LabelExportFrame? CaptureLabels { get; set; }

    internal static LabelExportFrame? SnapshotLabels(RhinoDoc doc, RhinoView view, double multiplier,
        bool omitFloatingCards = false)
    {
        var sessions = OverlayManager.InView(doc, view).Where(session => session.Visible).ToArray();
        var labels = sessions.Select(session => session._labelView).FirstOrDefault(binding => binding is not null);
        if (labels is null) return null;

        OverlayManager.Settle(doc, view);
        foreach (var session in sessions)
        {
            if (OverlayManager.HasPendingInteraction(session)) session.Update(session.Cards, preserveGesture: true);
            session.ResolveDrawTheme();
        }
        var reserved = sessions.SelectMany(session => OverlayManager.Placements(session, false)
            .Where(p => !omitFloatingCards || p.Id != OverlayManager.ShownId(session))
            .Select(p => new Rect2(p.X * multiplier, p.Y * multiplier, p.W * multiplier, p.H * multiplier))).ToArray();
        return labels.Snapshot(multiplier, reserved);
    }

    private sealed partial class LabelView
    {
        internal LabelExportFrame Snapshot(double multiplier, IReadOnlyList<Rect2> reserved)
        {
            var started = Stopwatch.GetTimestamp();
            Scene.Refresh();
            while (!Scene.Snapshot.HasValue)
            {
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(10))
                    throw new InvalidOperationException("The label occlusion scene did not finish preparing for export.");
                Thread.Sleep(1);
                Scene.Refresh();
            }

            var index = Scene.Snapshot.Value.Index;
            var dpi = (DpiOverride ?? UiContext.Dpi(_view)) * multiplier;
            var viewport = _view.MainViewport;
            var projector = new LabelOcclusionScene.Projection(viewport, dpi / multiplier);
            var size = viewport.Size;
            var candidates = new List<LabelCandidate>();
            var styles = new Dictionary<LabelIdentity, (LabelRasterKey Full, LabelRasterKey Short, string? Accent)>();
            foreach (var owner in _owners.Where(owner => owner.Visible))
            {
                var palette = LabelPalette.FromTheme(owner.Theme);
                foreach (var anchor in owner._labels)
                {
                    if (projector.Project(anchor.Point, out var pixel) is not { } segment) continue;
                    var style = anchor.Style;
                    var selected = style.Key is { } key && owner._selectedMembers.Contains(key);
                    var occluded = segment.IsOccluded(index);
                    if (occluded && !selected) continue;
                    var id = anchor.Identity(owner.Owner);
                    var full = Fit(style.Text, LabelForm.Full);
                    var shortPill = Fit(style.ShortText ?? LabelLayout.Shorten(style.Text), LabelForm.Short);
                    if (!styles.TryAdd(id, (full, shortPill, selected ? owner.Theme.Accent : null))) continue;
                    candidates.Add(new(id, new(pixel.X * multiplier, pixel.Y * multiplier), style.Priority,
                        selected ? LabelActivity.Selected : LabelActivity.Ordinary, new(true, occluded, true),
                        full.Width, shortPill.Width));

                    LabelRasterKey Fit(string text, LabelForm form)
                    {
                        var fitted = LabelPainter.Fit(text, style.Swatch, palette.Font, TextMetrics.Measurer);
                        return new(fitted.Text, style.Swatch, palette, dpi, form, fitted.Width, 1);
                    }
                }
            }

            var frame = new LabelExportFrame(dpi);
            foreach (var placement in LabelExportLayout.Place(candidates,
                         new(size.Width * multiplier, size.Height * multiplier), dpi, reserved))
            {
                var (full, shortPill, accent) = styles[placement.Identity];
                var dot = full with { Form = LabelForm.Dot, Text = "", Width = 5, Opacity = placement.Opacity };
                if (placement.Form == LabelForm.Dot)
                {
                    frame.Add(dot, placement.Dot, accent);
                    continue;
                }
                var pill = placement.Form == LabelForm.Full ? full : shortPill;
                frame.Add(pill with { Opacity = placement.Opacity }, placement.Pill, accent);
                frame.Add(dot, placement.Dot, null);
            }
            return frame;
        }
    }
}
