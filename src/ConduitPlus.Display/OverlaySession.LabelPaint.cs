using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConduitPlus.Core;
using ConduitPlus.Platform;
using ConduitPlus.Rendering;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal sealed partial class OverlaySession
{
    internal Rect2? LabelTooltipBounds => _tooltipReady && TooltipPosition is { } position && _tooltipBitmap is { } bitmap
        ? new(position.X, position.Y, bitmap.Width, bitmap.Height) : null;

    internal LabelPlacement[] InspectLabelFrame() => _labelView?.InspectFrame() ?? [];

    internal readonly record struct LabelTiming(long Sequence, double Milliseconds, double Classify,
        double Candidates, double Layout, double Pills, double Dots, double Rings, int PillCount, bool InMotion);
    internal LabelTiming LabelFrameTiming => _labelView?.Timing ?? default;
    internal LabelTiming LabelSettledTiming => _labelView?.SettledTiming ?? default;
    internal void ClearLabelRasters() => _labelView?.ClearRasters();

    internal void SetLabelPaintBudgetForChecks(TimeProvider? clock = null, int? fixedLimit = null)
    {
        _labelView?.SetPaintClock(clock, fixedLimit);
    }

    internal const int LabelUploadAllowance = 8;
    internal (int Uploads, int PendingDots) LabelRasterWork => _labelView?.RasterWork ?? default;
    internal int LabelSettleTicks => _labelView?.SettleTicks ?? 0;
    internal void ObserveLabelPillForChecks(Action<Bitmap, int, int>? observer)
    {
        if (_labelView is { } view) view.PillObserver = observer;
    }

    private sealed partial class LabelView
    {
        internal LabelTiming Timing { get; private set; }
        internal LabelTiming SettledTiming { get; private set; }
        internal void ClearRasters() => _rasters.Dispose();

        private readonly List<LabelCandidate> _candidates = [];
        private readonly List<LabelPlacement> _drawn = [];
        private Dictionary<LabelRasterKey, DotBatch> _dots = [];
        private readonly List<DotBatch> _usedDots = [];
        private readonly List<(Rect2 Bounds, bool Pill, bool Selected, string Ink, double Opacity)> _rings = [];
        private LabelActivity[] _activity = [];

        private LabelPaintBudget _budget = new();
        private LabelLayout _layout = new();
        internal int SettleTicks { get; private set; }
        internal (int Uploads, int PendingDots) RasterWork { get; private set; }
        internal Action<Bitmap, int, int>? PillObserver { get; set; }
        private Texture? _neutralTexture;
        private Texture? _fadedNeutralTexture;
        private DotBatch _neutralDots = null!;
        private DotBatch _fadedNeutralDots = null!;

        internal void SetPaintClock(TimeProvider? clock, int? fixedLimit)
        {
            _settled.Stop();
            _budget = new(clock, fixedLimit);
            _layout = new(clock);
        }
        internal LabelPlacement[] InspectFrame() => _drawn.ToArray();
        private readonly LabelRasterCache<Texture> _rasters = new();
        private readonly UiTimer _settled = new(120);
        private bool _stylesDirty = true;
        private double _rasterDpi;
        private Dictionary<OverlaySession, Theme> _themes = [];
        private Dictionary<LabelIdentity, Prepared> _styles = [];
        private Prepared[] _ranked = [];
        private Prepared[] _priorityOrder = [];
        private bool _activityDirty;
        private readonly Dictionary<OverlaySession, (IReadOnlySet<string> Selected, IReadOnlySet<string> Hovered)> _activitySets = [];

        private Dictionary<(string Text, string Font, bool Swatch), (string Text, double Width)> _metrics = [];

        private sealed record Prepared(int Index, OverlaySession Owner, LabelCandidate Candidate,
            DotBatch Dot, DotBatch FadedDot, LabelRasterKey Short, LabelRasterKey Full);

        private sealed class DotBatch(LabelRasterKey key)
        {
            internal LabelRasterKey Key { get; } = key;
            internal int PaintOrder { get; set; }
            private List<Point3d> _previous = [];
            internal List<Point3d> Points { get; private set; } = [];
            private readonly DisplayBitmapDrawList _drawList = new();

            internal void Draw(DisplayPipeline pipeline, DisplayBitmap bitmap)
            {
                // Camera-only motion often leaves all accepted world points unchanged.
                if (!CollectionsMarshal.AsSpan(Points).SequenceEqual(CollectionsMarshal.AsSpan(_previous)))
                {
                    _drawList.SetPoints(Points);
                    (Points, _previous) = (_previous, Points);
                }
                pipeline.DrawSprites(bitmap, _drawList, (float)(5 * Key.Dpi), false);
            }
        }


        private sealed class Texture(Bitmap bitmap) : IDisposable
        {
            internal Bitmap Image { get; } = bitmap;
            internal DisplayBitmap Display { get; } = new(bitmap);
            internal long Bytes { get; } = (long)bitmap.Width * bitmap.Height * 8;
            public void Dispose() { Display.Dispose(); Image.Dispose(); }
        }

        private int Prepare(double dpi)
        {
            if (!_stylesDirty && _rasterDpi == dpi &&
                ThemesMatch())
                return 0;

            var uploads = 0;
            if (_neutralTexture is null || _rasterDpi != dpi)
            {
                _neutralTexture?.Dispose();
                _fadedNeutralTexture?.Dispose();
                var neutral = new LabelRasterKey("", null, LabelPalette.FromTheme(Theme.Paper), dpi, LabelForm.Dot, 5, 1);
                var faded = neutral with { Opacity = .45 };
                _neutralDots = new(neutral);
                _fadedNeutralDots = new(faded);
                _neutralTexture = CreateTexture(neutral);
                _fadedNeutralTexture = CreateTexture(faded);
                uploads = 2;
            }

            var widths = new Dictionary<(string Text, string Font, bool Swatch), (string Text, double Width)>();
            var keys = new HashSet<LabelRasterKey>();
            _styles = [];
            _dots = [];
            _themes = _owners.ToDictionary(owner => owner, owner => owner.Theme);
            var palettes = _themes.ToDictionary(pair => pair.Key, pair => LabelPalette.FromTheme(pair.Value));
            for (var i = 0; i < _anchors.Length; i++)
            {
                var (owner, anchor) = _anchors[i];
                var id = anchor.Identity(owner.Owner);
                var style = anchor.Style;
                var palette = palettes[owner];
                var dot = new LabelRasterKey("", style.Swatch, palette, dpi, LabelForm.Dot, 5, 1);
                var full = Pill(style.Text, LabelForm.Full);
                var shortPill = Pill(style.ShortText ?? LabelLayout.Shorten(style.Text), LabelForm.Short);
                var candidate = new LabelCandidate(id, default, style.Priority, default, default, full.Width, shortPill.Width);
                _styles.TryAdd(id, new(i, owner, candidate, Batch(dot), Batch(dot with { Opacity = .45 }), shortPill, full));
                keys.Add(dot);
                keys.Add(dot with { Opacity = .45 });
                keys.Add(full);
                keys.Add(full with { Opacity = .45 });
                keys.Add(shortPill);

                LabelRasterKey Pill(string text, LabelForm form)
                {
                    var metric = (text, palette.Font, style.Swatch.HasValue);
                    if (!widths.TryGetValue(metric, out var fitted))
                    {
                        if (_rasterDpi != dpi || !_metrics.TryGetValue(metric, out fitted))
                            fitted = LabelPainter.Fit(text, style.Swatch, palette.Font, TextMetrics.Measurer);
                        widths.Add(metric, fitted);
                    }
                    return new(fitted.Text, style.Swatch, palette, dpi, form, fitted.Width, 1);

                }
            }

            _activity = new LabelActivity[_anchors.Length];
            _ranked = LabelLayout.Rank(_styles.Values, style => style.Candidate).ToArray();
            _priorityOrder = new Prepared[_ranked.Length];
            _activitySets.Clear();
            _activityDirty = true;
            _rasters.Retain(keys.Contains);
            _metrics = widths;
            _rasterDpi = dpi;
            _stylesDirty = false;
            return uploads;
        }

        private Texture GetTexture(LabelRasterKey key)
        {
            if (_rasters.TryGet(key, out var cached)) return cached;
            var texture = CreateTexture(key);
            _rasters.Add(key, texture, texture.Bytes);
            return texture;
        }

        private static Texture CreateTexture(LabelRasterKey key)
        {
            var image = RasterDrawing.RenderLabel(key);
            try { return new(image); }
            catch { image.Dispose(); throw; }
        }

        private DotBatch Batch(LabelRasterKey key)
        {
            if (!_dots.TryGetValue(key, out var batch)) _dots.Add(key, batch = new(key));
            return batch;
        }

        private bool ThemesMatch()
        {
            if (_themes.Count != _owners.Count) return false;
            foreach (var owner in _owners)
                if (!_themes.TryGetValue(owner, out var theme) || theme != owner.Theme) return false;
            return true;
        }

        private void RankByActivity()
        {
            foreach (var owner in _owners)
            {
                var sets = (owner._selectedMembers, owner._hoveredMembers);
                if (_activitySets.TryGetValue(owner, out var previous) &&
                    ReferenceEquals(sets.Item1, previous.Selected) &&
                    ReferenceEquals(sets.Item2, previous.Hovered)) continue;
                _activitySets[owner] = sets;
                _activityDirty = true;
            }
            if (!_activityDirty) return;

            foreach (var style in _ranked)
            {
                var key = _anchors[style.Index].Anchor.Style.Key;
                _activity[style.Index] = key is null ? LabelActivity.Ordinary :
                    style.Owner._selectedMembers.Contains(key) ? LabelActivity.Selected :
                    style.Owner._hoveredMembers.Contains(key) ? LabelActivity.Hovered : LabelActivity.Ordinary;
            }

            var index = 0;
            for (var activity = LabelActivity.Selected; activity >= LabelActivity.Ordinary; activity--)
                foreach (var style in _ranked)
                    if (_activity[style.Index] == activity) _priorityOrder[index++] = style;
            _activityDirty = false;
        }

        internal void Draw(DrawEventArgs e, IReadOnlyList<Rect2> reserved, long started)
        {
            _drawn.Clear();
            _frameComplete = false;
            _popoverBounds = null;
            RasterWork = default;
            Classify();
            if (Scene.Snapshot is not { } snapshot || _schedule is not { } schedule || _projection is not { } projection)
            {
                InvalidateLabelInput();
                return;
            }
            var classified = Stopwatch.GetTimestamp();
            var dpi = projection.Dpi;
            var reservedUploads = Prepare(dpi);
            RankByActivity();
            _candidates.Clear();
            foreach (var style in _priorityOrder)
            {
                var i = style.Index;
                if (_segments[i] is null) continue;
                var answer = schedule.Answer(i, snapshot.Generation, _projectionRevision);
                if (!answer.Visible) continue;
                _candidates.Add(style.Candidate with { Anchor = _pixels[i], Visibility = answer, Activity = _activity[i] });
            }

            var candidatesBuilt = Stopwatch.GetTimestamp();
            var frame = _layout.PlaceRanked(_candidates, projection.Size, dpi, reserved);
            var placed = Stopwatch.GetTimestamp();
            var remainingUploads = LabelUploadAllowance - reservedUploads;
            var remainingPreloads = 4;
            var budget = _budget.BeginFrame(_projectionRevision);
            var inMotion = _budget.RedrawAfter.HasValue;
            var sampledPills = 0;
            var pillCount = 0;
            var pillTicks = 0L;
            foreach (var batch in _usedDots) batch.Points.Clear();
            _usedDots.Clear();
            _rings.Clear();
            for (var i = 0; i < frame.Count; i++)
            {
                var placement = frame[i];
                var style = _styles[placement.Identity];
                var activity = _activity[style.Index];
                var pillDrawn = false;
                if (placement.Form != LabelForm.Dot)
                {
                    var pill = placement.Form == LabelForm.Short ? style.Short : style.Full;
                    var key = pill with { Opacity = placement.Opacity };
                    if (budget.Allows(placement.Form, activity))
                        pillDrawn = DrawPill(key, placement.Pill, activity);
                    else if (remainingPreloads > 0 && remainingUploads > 0 && !_rasters.TryGet(key, out _))
                    {
                        // Spread the raster work for the settled frame over motion frames.
                        GetTexture(key);
                        remainingPreloads--;
                        remainingUploads--;
                    }
                }
                if (pillDrawn) pillCount++;
                if (!pillDrawn && placement.Opacity < 1) continue;
                var batch = placement.Opacity < 1 ? style.FadedDot : style.Dot;
                if (batch.Points.Count == 0) _usedDots.Add(batch);
                batch.Points.Add(_anchors[style.Index].Anchor.Point);
                _drawn.Add(pillDrawn ? placement : placement with { Form = LabelForm.Dot, Pill = default });
                var isSelected = _activity[style.Index] == LabelActivity.Selected;
                if (isSelected || _activity[style.Index] == LabelActivity.Hovered)
                    _rings.Add((pillDrawn ? placement.Pill : placement.Dot, pillDrawn, isSelected,
                        isSelected ? style.Owner.Theme.Accent : style.Owner.Theme.Muted, placement.Opacity));
            }

            var pillsDrawn = Stopwatch.GetTimestamp();
            var pendingDots = 0;
            _neutralDots.Points.Clear();
            _fadedNeutralDots.Points.Clear();
            var dotPaintOrder = 0;
            foreach (var batch in _usedDots)
            {
                batch.PaintOrder = dotPaintOrder++;
                if (batch.Key == _neutralDots.Key)
                    batch.Draw(e.Display, _neutralTexture!.Display);
                else if (batch.Key == _fadedNeutralDots.Key)
                    batch.Draw(e.Display, _fadedNeutralTexture!.Display);
                else if (_rasters.TryGet(batch.Key, out var texture))
                    batch.Draw(e.Display, texture.Display);
                else if (remainingUploads > 0)
                {
                    remainingUploads--;
                    batch.Draw(e.Display, GetTexture(batch.Key).Display);
                }
                else
                {
                    pendingDots++;
                    batch.PaintOrder = int.MaxValue;
                    var fallback = batch.Key.Opacity < 1 ? _fadedNeutralDots : _neutralDots;
                    fallback.Points.AddRange(batch.Points);
                }
            }
            if (pendingDots > 0)
            {
                if (_neutralDots.Points.Count > 0) _neutralDots.Draw(e.Display, _neutralTexture!.Display);
                if (_fadedNeutralDots.Points.Count > 0) _fadedNeutralDots.Draw(e.Display, _fadedNeutralTexture!.Display);
                _redraw.Start();
            }
            RasterWork = (LabelUploadAllowance - remainingUploads, pendingDots);

            var dotsDrawn = Stopwatch.GetTimestamp();
            foreach (var (ring, pill, isSelected, ink, opacity) in _rings)
                e.Display.DrawRoundedRectangle(new PointF((float)((ring.X + ring.Width / 2) * dpi), (float)((ring.Y + ring.Height / 2) * dpi)),
                    (float)(ring.Width * dpi), (float)(ring.Height * dpi), (float)((pill ? 6 : 2.5) * dpi),
                    Fade(ink, opacity), (float)((isSelected ? 2 : 1) * dpi), Color.Transparent);

            if (_popover is { } popover && OverlayManager.LabelInputEnabled(popover.Owner))
            {
                var index = _drawn.FindIndex(label => label.Identity == popover.Placement.Identity &&
                    LabelPicking.Retains(popover.Placement.Dot, popover.Placement.Dot,
                        new(label.Dot.X + label.Dot.Width / 2, label.Dot.Y + label.Dot.Height / 2)));
                if (index >= 0) DrawPopover(popover, _drawn[index].Dot);
                else _popover = null;
            }
            else _popover = null;
            _frameHud = reserved;
            _frameComplete = true;

            void DrawPopover((OverlaySession Owner, LabelPlacement Placement) popover, Rect2 dot)
            {
                var style = _styles[popover.Placement.Identity];
                var size = new Size2(projection.Size.Width / dpi, projection.Size.Height / dpi);
                var (bounds, left) = LabelPicking.Popover(dot, style.Full.Width, size, popover.Placement.Left);
                _popover = (popover.Owner, popover.Placement with { Left = left, Dot = dot });
                DrawPill(style.Full, bounds, LabelActivity.Hovered);
                _popoverBounds = LabelPicking.PaintedPill(bounds, dpi);
            }
            var ringsDrawn = Stopwatch.GetTimestamp();
            var redrawAfter = _layout.RedrawAfter;
            if (_budget.RedrawAfter is { } settle && (redrawAfter is null || settle < redrawAfter))
                redrawAfter = settle;
            if (redrawAfter is { } remaining && !_settled.Enabled)
            {
                _settled.Interval = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
                _settled.Start();
            }

            var wasInMotion = Timing.InMotion;
            Timing = new(Timing.Sequence + 1, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Elapsed(started, classified), Elapsed(classified, candidatesBuilt), Elapsed(candidatesBuilt, placed),
                Elapsed(placed, pillsDrawn), Elapsed(pillsDrawn, dotsDrawn), Elapsed(dotsDrawn, ringsDrawn), pillCount, inMotion);
            if (inMotion) _budget.Observe(sampledPills, Elapsed(0, pillTicks));
            else if (wasInMotion) SettledTiming = Timing;

            static double Elapsed(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

            static Color Fade(string hex, double opacity)
            {
                var color = ColorRgba.Parse(hex);
                return Color.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
            }

            bool DrawPill(LabelRasterKey key, Rect2 rect, LabelActivity activity)
            {
                var cached = _rasters.TryGet(key, out var texture);
                if (!cached)
                {
                    if (inMotion && activity == LabelActivity.Ordinary && remainingUploads == 0)
                    {
                        _redraw.Start();
                        return false;
                    }
                    if (remainingUploads > 0) remainingUploads--;
                    texture = GetTexture(key);
                }
                var sample = inMotion && cached && activity == LabelActivity.Ordinary;
                var before = sample ? Stopwatch.GetTimestamp() : 0;
                var x = (int)Math.Round(rect.X * dpi);
                var y = (int)Math.Round(rect.Y * dpi);
                e.Display.DrawBitmap(texture.Display, x, y);
                PillObserver?.Invoke(texture.Image, x, y);
                if (sample)
                {
                    pillTicks += Stopwatch.GetTimestamp() - before;
                    sampledPills++;
                }
                return true;
            }
        }
    }
}
