namespace ConduitPlus.Core;

public sealed record PinnedLayoutRequest(Rect2 Client, double Dpi, IReadOnlyList<LayoutParticipant> Participants,
    Func<string, double, string, Size2> Measure, IReadOnlyList<Rect2> Exclusions);
public sealed record PinnedLayoutResult(IReadOnlyList<CardPlacement> Placements, IReadOnlyList<(Guid Owner, string CardId)> Hidden);

/// <summary>First-cut fixed-size rails. No zoom, compaction, rotated bands or chip; omissions are explicit.</summary>
public static class PinnedCanvasLayout
{
    public const double Margin = 12, Gap = 8, KeepClear = .35;
    public static bool Intersects(Rect2 a, Rect2 b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
    public static bool Contains(Rect2 outer, Rect2 inner) => inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;
    private sealed record Item(Guid Owner, CardSpec Spec, Theme Theme, Slot Slot, int Index);
    public static PinnedLayoutResult Solve(PinnedLayoutRequest request)
    {
        var r = request.Client; var u = request.Dpi;
        if (!double.IsFinite(u) || u <= 0 || !double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.Width < 0 || r.Height < 0)
            throw new ArgumentException("Invalid client rectangle or DPI.");
        var placed = new List<CardPlacement>(); var hidden = new List<(Guid, string)>();
        var items = request.Participants.OrderBy(p => p.Owner.ToString("N"), StringComparer.Ordinal).SelectMany(p =>
            p.Cards.Zip(SlotLayout.ResolveAll(p.Cards), (c, slot) => (c, slot)).Select((x, i) => new Item(p.Owner, x.c, p.Theme, x.slot, i))).ToArray();
        var margin = Margin * u; var gap = Gap * u;
        var safe = new Rect2(r.X + margin, r.Y + margin, Math.Max(0, r.Width - margin * 2), Math.Max(0, r.Height - margin * 2));
        var railWidth = safe.Width * (1 - KeepClear) / 2;
        // Titles retain their title-only centre slots. Rails preserve producer/input order within each stack.
        foreach (var slot in new[] { Slot.Tc, Slot.Bc, Slot.Tl, Slot.Tr, Slot.Bl, Slot.Br, Slot.Lc, Slot.Rc })
        {
            var stack = items.Where(x => x.Slot == slot).ToArray();
            var sizes = stack.Select(x => FormMeasure.Size(x.Spec, x.Theme, x.Spec.Form, request.Measure)).ToArray();
            var bottom = slot is Slot.Bl or Slot.Br or Slot.Bc;
            var centre = slot is Slot.Lc or Slot.Rc;
            var right = slot is Slot.Tr or Slot.Br or Slot.Rc;
            var centredTitle = slot is Slot.Tc or Slot.Bc;
            var candidates = Enumerable.Range(0, stack.Length).Where(i =>
            {
                var fits = (centredTitle ? stack[i].Spec.Kind == CardKind.Title : Math.Ceiling(sizes[i].Width * u) <= railWidth)
                    && Math.Ceiling(sizes[i].Height * u) <= safe.Height;
                if (!fits) hidden.Add((stack[i].Owner, stack[i].Spec.Id));
                return fits;
            }).ToList();
            while (candidates.Count > 0)
            {
                var total = candidates.Sum(i => Math.Ceiling(sizes[i].Height * u)) + (candidates.Count - 1) * gap;
                var y = bottom ? safe.Y + safe.Height - total : centre ? safe.Y + (safe.Height - total) / 2 : safe.Y;
                var attempt = new List<CardPlacement>();
                foreach (var i in candidates)
                {
                    var item = stack[i]; var size = sizes[i];
                    var w = Math.Ceiling(size.Width * u); var h = Math.Ceiling(size.Height * u);
                    var x = centredTitle ? safe.X + (safe.Width - w) / 2 : right ? safe.X + safe.Width - w : safe.X;
                    var bounds = new Rect2(right ? Math.Floor(x) : Math.Ceiling(x), bottom ? Math.Floor(y) : Math.Ceiling(y), w, h);
                    var obstacles = request.Exclusions.Concat(placed.Concat(attempt).Select(p => new Rect2(p.X, p.Y, p.Width, p.Height))).ToArray();
                    if (!bottom)
                        foreach (var obstacle in obstacles.OrderBy(o => o.Y))
                            if (Intersects(bounds, obstacle)) bounds = bounds with { Y = Math.Ceiling(obstacle.Y + obstacle.Height + gap) };
                    if (!Contains(safe, bounds) || obstacles.Any(o => Intersects(bounds, o))) break;
                    attempt.Add(new(item.Owner, item.Spec.Id, bounds.X, bounds.Y, bounds.Width, bounds.Height, item.Spec.Form));
                    y = bounds.Y + bounds.Height + gap;
                }
                if (attempt.Count == candidates.Count) { placed.AddRange(attempt); break; }
                var last = candidates[^1]; candidates.RemoveAt(candidates.Count - 1);
                hidden.Add((stack[last].Owner, stack[last].Spec.Id));
            }
        }
        return new(placed, hidden);
    }
}

/// <summary>Control device pixels to local design units. Shadows and clipping never become hit regions.
/// Canvas pan/zoom are deliberately absent from this transform.</summary>
public readonly record struct PinnedHitTransform(Rect2 Body, double Dpi, Rect2 Clip)
{
    public Point2 ToLocal(Point2 control) => new((control.X - Body.X) / Dpi, (control.Y - Body.Y) / Dpi);
    public Point2 ToControl(Point2 local) => new(Body.X + local.X * Dpi, Body.Y + local.Y * Dpi);
    public bool Contains(Point2 control) => Body.Contains(control.X, control.Y) && Clip.Contains(control.X, control.Y);
    public Rect2 ToControl(HitRegion hit) => new(Body.X + hit.X * Dpi, Body.Y + hit.Y * Dpi, hit.W * Dpi, hit.H * Dpi);
}

/// <summary>Raster identity deliberately excludes window location and definition pan/zoom.</summary>
public sealed record PinnedRasterKey(Guid Owner, string CardId, long Revision, Theme Theme, Size2 Size, CanvasRasterPlan Plan);
