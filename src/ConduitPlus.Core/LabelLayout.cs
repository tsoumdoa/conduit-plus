using System.Globalization;
using System.Runtime.CompilerServices;

namespace ConduitPlus.Core;

public enum LabelForm { Dot, Short, Full }
public enum LabelActivity { Ordinary, Hovered, Selected }

public readonly record struct LabelCandidate(LabelIdentity Identity, Point2 Anchor, double Priority,
    LabelActivity Activity, OcclusionAnswer Visibility, double FullWidth, double ShortWidth);
public readonly record struct LabelPlacement(LabelIdentity Identity, Rect2 Dot, Rect2 Pill,
    LabelForm Form, bool Left, double Opacity);

/// <summary>Viewport-wide placement in CSS pixels. Candidate anchors and obstacles arrive in device pixels.</summary>
public sealed class LabelLayout(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Dictionary<LabelIdentity, History> _history = [];
    private Dictionary<LabelIdentity, History> _next = [];
    private readonly Grid<Point2> _points = new(26);
    private readonly Grid<Rect2> _occupied = new(32);
    private readonly Dictionary<Point2, int> _counts = [];
    private readonly HashSet<Point2> _placedAnchors = [];
    private readonly List<LabelPlacement> _result = [];

    private readonly record struct History(LabelForm Form, bool Left, LabelForm Pending, bool PendingLeft, long Since);
    public TimeSpan? RedrawAfter { get; private set; }

    public static IOrderedEnumerable<T> Rank<T>(IEnumerable<T> items, Func<T, LabelCandidate> candidate) =>
        items.OrderByDescending(x => candidate(x).Activity).ThenByDescending(x => candidate(x).Priority)
            .ThenBy(x => candidate(x).Identity.Owner).ThenBy(x => candidate(x).Identity.Source.Component)
            .ThenBy(x => candidate(x).Identity.Source.Path, StringComparer.Ordinal)
            .ThenBy(x => candidate(x).Identity.LinkKey, StringComparer.Ordinal).ThenBy(x => candidate(x).Identity.PointIndex);

    public LabelPlacement[] Place(IEnumerable<LabelCandidate> source, Size2 deviceSize, double dpi,
        IEnumerable<Rect2> obstacles, bool ranked = false) =>
        PlaceRanked(ranked ? source as IReadOnlyList<LabelCandidate> ?? source.ToArray() :
            Rank(source, x => x).ToArray(), deviceSize, dpi, obstacles as IReadOnlyList<Rect2> ?? obstacles.ToArray()).ToArray();

    /// <summary>Returns placements backed by reusable storage, valid until the next call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IReadOnlyList<LabelPlacement> PlaceRanked(IReadOnlyList<LabelCandidate> source,
        Size2 deviceSize, double dpi, IReadOnlyList<Rect2> obstacles)
    {
        var now = _clock.GetTimestamp();
        RedrawAfter = null;
        var size = new Size2(deviceSize.Width / dpi, deviceSize.Height / dpi);
        var points = _points;
        points.Reset(size);
        var counts = _counts;
        counts.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var candidate = source[i];
            if (candidate.Visibility.Opacity(candidate.Activity != LabelActivity.Ordinary) <= 0) continue;
            var p = new Point2(candidate.Anchor.X / dpi, candidate.Anchor.Y / dpi);
            if (counts.TryAdd(p, 1)) points.Add(new(p.X, p.Y, 0, 0), p);
            else counts[p]++;
        }

        var occupied = _occupied;
        occupied.Reset(size);
        for (var i = 0; i < obstacles.Count; i++)
        {
            var r = obstacles[i];
            var css = new Rect2(r.X / dpi, r.Y / dpi, r.Width / dpi, r.Height / dpi);
            if (css.Width > 0 && css.Height > 0) occupied.Add(css, css);
        }

        var next = _next;
        next.Clear();
        var result = _result;
        result.Clear();
        var placedAnchors = _placedAnchors;
        placedAnchors.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var c = source[i];
            if (next.ContainsKey(c.Identity)) continue;
            if (c.Visibility.Opacity(c.Activity != LabelActivity.Ordinary) <= 0) continue;
            var p = new Point2(c.Anchor.X / dpi, c.Anchor.Y / dpi);
            if (placedAnchors.Contains(p)) continue;
            var dot = new Rect2(p.X - 2.5, p.Y - 2.5, 5, 5);
            if (!Fits(dot, 0)) continue;
            var known = _history.TryGetValue(c.Identity, out var old);
            var active = c.Activity != LabelActivity.Ordinary;
            var shortThreshold = known ? old.Form == LabelForm.Dot ? 22 : 14 : 18;
            var spacing = active ? 96 : Spacing(p, shortThreshold);
            var form = LabelForm.Dot;
            var left = known && old.Left;
            Rect2 pill = default;
            var history = new History(form, left, LabelForm.Dot, false, now);
            var occluded = c.Visibility.Occluded;
            for (var trial = LabelForm.Full; trial >= LabelForm.Short; trial--)
            {
                if (occluded && trial != LabelForm.Full) break;
                var threshold = trial == LabelForm.Full ? 48 : 18;
                var promoting = known && trial > old.Form && !active;
                if (!active && spacing <= threshold + (known ? promoting ? 4 : -4 : 0)) continue;
                var width = trial == LabelForm.Full ? c.FullWidth : c.ShortWidth;
                if (width is <= 0 or > 160) continue;
                if (!TrySide(left, out var rect, out var side) && !TrySide(!left, out rect, out side)) continue;
                if (promoting)
                {
                    var since = old.Pending == trial && old.PendingLeft == side ? old.Since : now;
                    var remaining = LabelPaintBudget.PromoteInterval - _clock.GetElapsedTime(since, now);
                    if (remaining > TimeSpan.Zero)
                    {
                        if (history.Pending == LabelForm.Dot)
                        {
                            history = history with { Pending = trial, PendingLeft = side, Since = since };
                            if (RedrawAfter is null || remaining < RedrawAfter) RedrawAfter = remaining;
                        }
                        continue;
                    }
                }
                form = trial;
                pill = rect;
                left = side;
                break;

                bool TrySide(bool onLeft, out Rect2 bounds, out bool side)
                {
                    side = onLeft;
                    bounds = new(onLeft ? dot.X - 4 - width : dot.X + dot.Width + 4, p.Y - 9, width, 18);
                    return Fits(bounds, promoting ? 4 : 0) && (!promoting || Fits(dot, 4));
                }
            }
            if (occluded && form != LabelForm.Full) continue;
            next.Add(c.Identity, history with { Form = form, Left = left });
            placedAnchors.Add(p);
            occupied.Add(dot, dot);
            if (form != LabelForm.Dot) occupied.Add(pill, pill);
            result.Add(new(c.Identity, dot, pill, form, left, c.Visibility.Opacity(active)));
        }
        (_history, _next) = (_next, _history);
        return result;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        double Spacing(Point2 p, int shortThreshold)
        {
            if (counts[p] > 1) return 0;
            var distance = 96d * 96;
            var cx = points.Cell(p.X);
            var cy = points.Cell(p.Y);
            for (var ring = 0; ring <= 2; ring++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                    if (points.At(cx + dx, cy + dy) is not { } cell) continue;
                    foreach (var other in cell)
                    {
                        if (other == p) continue;
                        var x = other.X - p.X;
                        var y = other.Y - p.Y;
                        distance = Math.Min(distance, x * x + y * y);
                        if (distance <= shortThreshold * shortThreshold) return shortThreshold;
                    }
                }

                // No eligibility test distinguishes spacing above 52 px.
                // Any unvisited cell is at least this far from the anchor.
                var outside = Math.Min(Math.Min(p.X - (cx - ring) * 26, (cx + ring + 1) * 26 - p.X),
                    Math.Min(p.Y - (cy - ring) * 26, (cy + ring + 1) * 26 - p.Y));
                if (outside >= 52 || distance <= outside * outside) break;
            }
            return Math.Sqrt(distance);
        }
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        bool Fits(Rect2 r, double spare)
        {
            if (r.X < spare || r.Y < spare || r.X + r.Width > size.Width - spare || r.Y + r.Height > size.Height - spare)
                return false;
            var gap = 2 + spare;
            var probe = new Rect2(r.X - gap, r.Y - gap, r.Width + 2 * gap, r.Height + 2 * gap);
            var right = occupied.Cell(probe.X + probe.Width);
            var bottom = occupied.Cell(probe.Y + probe.Height);
            for (var x = occupied.Cell(probe.X); x <= right; x++)
            for (var y = occupied.Cell(probe.Y); y <= bottom; y++)
            {
                if (occupied.At(x, y) is not { } cell) continue;
                foreach (var b in cell)
                    if (probe.X < b.X + b.Width && probe.X + probe.Width > b.X &&
                        probe.Y < b.Y + b.Height && probe.Y + probe.Height > b.Y) return false;
            }

            return true;
        }
    }

    private sealed class Grid<T>(int cell)
    {
        private const int Border = 2;
        private readonly Dictionary<(int, int), List<T>> _outside = [];
        private readonly Stack<List<T>> _pool = [];
        private readonly List<int> _used = [];
        private List<T>?[] _cells = [];
        private int _width;
        private int _height;

        internal void Reset(Size2 size)
        {
            foreach (var index in _used)
            {
                Recycle(_cells[index]!);
                _cells[index] = null;
            }
            _used.Clear();
            foreach (var list in _outside.Values) Recycle(list);
            _outside.Clear();
            _width = Math.Clamp(Cell(size.Width) + Border * 2 + 1, 0, 512);
            _height = Math.Clamp(Cell(size.Height) + Border * 2 + 1, 0, 512);
            if (_cells.Length < _width * _height) _cells = new List<T>?[_width * _height];
        }

        private void Recycle(List<T> list)
        {
            list.Clear();
            _pool.Push(list);
        }

        internal void Add(Rect2 r, T value)
        {
            var right = Cell(r.X + r.Width);
            var bottom = Cell(r.Y + r.Height);
            for (var x = Cell(r.X); x <= right; x++)
            for (var y = Cell(r.Y); y <= bottom; y++)
            {
                var index = Index(x, y);
                if (index >= 0)
                {
                    var list = _cells[index];
                    if (list is null)
                    {
                        _used.Add(index);
                        _cells[index] = list = Rent();
                    }
                    list.Add(value);
                }
                else
                {
                    var key = (x, y);
                    if (!_outside.TryGetValue(key, out var list)) _outside.Add(key, list = Rent());
                    list.Add(value);
                }
            }
        }

        private List<T> Rent() => _pool.TryPop(out var list) ? list : [];
        internal int Cell(double value) => (int)Math.Floor(value / cell);
        private int Index(int x, int y) =>
            (uint)(x + Border) < (uint)_width && (uint)(y + Border) < (uint)_height
                ? (y + Border) * _width + x + Border : -1;

        internal List<T>? At(int x, int y)
        {
            var index = Index(x, y);
            return index >= 0 ? _cells[index] : _outside.GetValueOrDefault((x, y));
        }
    }

    public static string Shorten(string text)
    {
        var elements = StringInfo.ParseCombiningCharacters(text);
        return elements.Length <= 5 ? text : text[..elements[5]] + "…";
    }
}

