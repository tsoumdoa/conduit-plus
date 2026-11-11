namespace ConduitPlus.Core;

public sealed record LayoutOptions(double RefWidth = 1600, double RefHeight = 900, double MinZoom = .625, double MaxZoom = 2, double ZoomStep = 1d / 32, double KeepClear = .35, double TopInsetCss = 30, double Margin = 12, double Gap = 8, double ChipReserve = 34, bool EnableCompact = true, double BottomLeftReserve = 0);
public sealed record LayoutParticipant(Guid Owner, double Size, Theme Theme, IReadOnlyList<CardSpec> Cards);
public sealed record LayoutRequest(Size2 Viewport, double Dpi, IReadOnlyList<LayoutParticipant> Participants, Func<string, double, string, Size2> Measure, LayoutOptions Options);
/// <summary>Presentation only; future title forms can extend this without changing authored cards.</summary>
public sealed record CardForm(int? VisibleRows = null, bool Compact = false, double? Width = null, int? DescriptionLines = null, bool DropSubtitle = false, int? TitleLines = null)
{
    public static CardForm None { get; } = new();
}
public static class FormMeasure
{
    public static Size2 Size(CardSpec c, Theme t, CardForm f, Func<string, double, string, Size2> measure)
    {
        if (c.Kind == CardKind.Title) return TitleMetrics.Layout(c with { Form = f }, t, measure).Size;
        var text = c.Kind == CardKind.Text ? TextMetrics.Layout(c, t, measure) : null;
        var width = c.Kind == CardKind.Legend && !c.Min.HasValue ? LegendCardPainter.CategoricalWidth(c, t, measure) : c.Width;
        return new(width, c.Kind switch
        {
            CardKind.Table => 48 + (Math.Min(Math.Min(c.MaxRows, c.Rows.Length), f.VisibleRows ?? int.MaxValue) + 1) * t.RowHeight,
            CardKind.Bar => 48 + Math.Min(c.Values.Length, f.VisibleRows ?? int.MaxValue) * t.RowHeight,
            CardKind.Line => f.Compact ? 140 : 220,
            CardKind.Metric => f.Compact ? 48 : 92,
            CardKind.Legend => c.Min.HasValue ? f.Compact ? LegendCardPainter.GradientCompactHeight : LegendCardPainter.GradientHeight : 48 + c.Colors.Length * t.RowHeight,
            _ => f.Compact ? text!.Top + Math.Min(2, text.Lines.Length) * text.LineHeight + t.Padding : text!.Height
        });
    }
}
public sealed record CardPlacement(Guid Owner, string CardId, double X, double Y, double Width, double Height, CardForm Form);
public enum LayoutOrientation { Columns, Bands }
public sealed record LayoutReport(IReadOnlyDictionary<Guid, double> BaseZoom, double ShrinkMultiplier, int WrapLines, LayoutOrientation Orientation, IReadOnlyList<string> CompactSteps, int HiddenCount);
public sealed record ViewportSolution(IReadOnlyList<CardPlacement> Placements, IReadOnlyList<(Guid Owner, string CardId)> Hidden, IReadOnlyDictionary<Guid, double> Zoom, IReadOnlyDictionary<Guid, double> Unit, Rect2? Chip, LayoutReport Report);
public static class ViewportLayout
{
    private sealed record Item(LayoutParticipant P, CardSpec C, int Index, Slot Slot);
    public static string Signature(LayoutRequest r) => CardJson.Serialize(new
    {
        r.Viewport, r.Dpi, r.Options,
        Participants = r.Participants.OrderBy(p => p.Owner.ToString("N"), StringComparer.Ordinal).Select(p => new
        {
            p.Owner, p.Size, p.Theme.FontSize, p.Theme.RowHeight, p.Theme.Padding, p.Theme.Font,
            Cards = p.Cards.Zip(SlotLayout.ResolveAll(p.Cards), (c, slot) => (c, slot)).Select((x, i) => (x.c, x.slot, i)).Select(x => new { x.c.Id, x.c.Kind, x.c.Width, Index = x.i, Slot = x.slot, x.c.Title, x.c.Subtitle, x.c.Background, x.c.Text, x.c.Value, x.c.Unit, Rows = x.c.Rows.Length, Values = x.c.Values.Length, Colors = x.c.Colors.Length, Labels = x.c.Labels.Length, HasMin = x.c.Min.HasValue, x.c.MaxRows,
                LegendLabels = x.c.Kind == CardKind.Legend && !x.c.Min.HasValue ? x.c.Labels : null,
                LegendValues = x.c.Kind == CardKind.Legend && !x.c.Min.HasValue ? x.c.ValueLabels : null,
                LegendHasKeys = x.c.Kind == CardKind.Legend && !x.c.Min.HasValue && x.c.Keys.Length > 0 })
        })
    });
    public static ViewportSolution Solve(LayoutRequest r)
    {
        var o = r.Options;
        if (!double.IsFinite(r.Dpi) || r.Dpi <= 0 || r.Viewport.Width < 0 || r.Viewport.Height < 0 || o.ZoomStep <= 0 || o.MinZoom <= 0 || o.MaxZoom < o.MinZoom) throw new ArgumentException("Invalid layout dimensions or scale.");
        var owners = r.Participants.OrderBy(p => p.Owner.ToString("N"), StringComparer.Ordinal).ToArray();
        var items = owners.SelectMany(p => p.Cards.Zip(SlotLayout.ResolveAll(p.Cards), (c, slot) => (c, slot)).Select((x, i) => new Item(p, x.c, i, x.slot))).ToArray();
        double Snap(double z) => Math.Round(z / o.ZoomStep, MidpointRounding.AwayFromZero) * o.ZoomStep;
        var basis = Math.Min(r.Viewport.Width / r.Dpi / o.RefWidth, r.Viewport.Height / r.Dpi / o.RefHeight);
        var initial = owners.ToDictionary(p => p.Owner, p => Math.Clamp(Snap(Math.Clamp(basis * p.Size, o.MinZoom, o.MaxZoom)), o.MinZoom, o.MaxZoom));
        var zoom = new Dictionary<Guid, double>(initial);
        var forms = items.ToDictionary(x => (x.P.Owner, x.C.Id), _ => CardForm.None);
        var hidden = new List<Item>();
        var steps = new List<string>();
        var portrait = r.Viewport.Width < r.Viewport.Height;
        var multiplier = 1d;
        Rect2? chip = null;
        List<CardPlacement> placed = [];
        var lines = 0;
        var cache = new Dictionary<(Guid, string, CardForm), Size2>();
        Size2 Measure(Item x)
        {
            var f = forms[(x.P.Owner, x.C.Id)];
            var key = (x.P.Owner, x.C.Id, f);
            if (!cache.TryGetValue(key, out var s)) cache[key] = s = FormMeasure.Size(x.C, x.P.Theme, f, r.Measure);
            var u = r.Dpi * zoom[x.P.Owner];
            return new(s.Width * u, s.Height * u);
        }
        bool Fit(bool reserve)
        {
            placed = []; lines = 0;
            var u = r.Dpi * (zoom.Count == 0 ? o.MinZoom : zoom.Values.Min());
            var margin = o.Margin * u; var gap = o.Gap * u;
            var top = Math.Max(margin, o.TopInsetCss * r.Dpi);
            chip = reserve ? new Rect2(Math.Max(0, r.Viewport.Width - margin - 100 * u), Math.Max(0, r.Viewport.Height - margin - o.ChipReserve * u), Math.Min(100 * u, r.Viewport.Width), Math.Min(o.ChipReserve * u, r.Viewport.Height)) : null;
            var titles = items.Where(x => x.C.Kind == CardKind.Title && !hidden.Contains(x)).ToArray();
            bool IsTop(Item x) => x.Slot is Slot.Tl or Slot.Tc or Slot.Tr;
            double Zone(Slot slot) => titles.Where(x => x.Slot == slot).Sum(x => Measure(x).Height + gap);
            foreach (var x in titles)
            {
                var middle = x.Slot is Slot.Tc or Slot.Bc;
                var hasMiddle = titles.Any(y => IsTop(y) == IsTop(x) && y.Slot is Slot.Tc or Slot.Bc);
                forms[(x.P.Owner,x.C.Id)] = forms[(x.P.Owner,x.C.Id)] with { Width = r.Viewport.Width * (middle ? .6 : hasMiddle ? .3 : .45) / (r.Dpi * zoom[x.P.Owner]) };
            }
            var topBand = portrait ? new[]{Slot.Tl,Slot.Tc,Slot.Tr}.Max(Zone) : 0;
            var bottomBand = portrait ? new[]{Slot.Bl,Slot.Bc,Slot.Br}.Max(Zone) : 0;
            var extents = new double[2]; var exempt = new double[2];
            for (var rail = 0; rail < 2; rail++)
            {
                Slot[] slots = rail == 0 ? [Slot.Tl, Slot.Lc, Slot.Bl] : [Slot.Tr, Slot.Rc, Slot.Br];
                var ordered = slots.SelectMany(s => items.Where(x => x.C.Kind != CardKind.Title && x.Slot == s)).Where(x => !hidden.Contains(x)).ToArray();
                var groups = new List<List<Item>>();
                // Keep the bottom-left corner clear (Rhino's world axes icon): the left rail, or the bottom band in portrait, ends above it.
                var bottom = margin + ((portrait ? rail == 1 : rail == 0) ? o.BottomLeftReserve : 0);
                bottom += portrait ? bottomBand : Zone(rail == 0 ? Slot.Bl : Slot.Br);
                var railTop = top + (portrait ? topBand : Zone(rail == 0 ? Slot.Tl : Slot.Tr));
                var capacity = portrait ? r.Viewport.Width - 2 * margin : r.Viewport.Height - railTop - bottom;
                foreach (var x in ordered)
                {
                    var size = Measure(x); var length = portrait ? size.Width : size.Height;
                    if (length > capacity + 1e-7) return false;
                    if (groups.Count == 0 || groups[^1].Sum(y => portrait ? Measure(y).Width : Measure(y).Height) + groups[^1].Count * gap + length > capacity) groups.Add([]);
                    groups[^1].Add(x);
                }
                var offset = 0d;
                foreach (var group in groups)
                {
                    lines++;
                    var thickness = group.Max(x => portrait ? Measure(x).Height : Measure(x).Width);
                    var middle = group.Where(y => y.Slot is Slot.Lc or Slot.Rc).ToArray();
                    var middleTotal = middle.Sum(y => Measure(y).Height) + Math.Max(0, middle.Length - 1) * gap;
                    var cursor = portrait ? margin : railTop;
                    foreach (var x in group)
                    {
                        var s = Measure(x);
                        if (!portrait)
                        {
                            var remaining = group.SkipWhile(y => y != x).ToArray();
                            var rest = remaining.Sum(y => Measure(y).Height) + (remaining.Length - 1) * gap;
                            if (x.Slot is Slot.Bl or Slot.Br) cursor = Math.Max(cursor, r.Viewport.Height - bottom - rest);
                            // Centre the middle-slot cards as a block, clamped between the top and bottom stacks.
                            else if (middle.Length > 0 && ReferenceEquals(x, middle[0])) cursor = Math.Max(cursor, Math.Min((r.Viewport.Height - middleTotal) / 2, r.Viewport.Height - bottom - rest));
                        }
                        var px = portrait ? cursor : rail == 0 ? margin + offset : r.Viewport.Width - margin - offset - s.Width;
                        var py = portrait ? rail == 0 ? railTop + offset : r.Viewport.Height - bottom - offset - s.Height : cursor;
                        var rect = new Rect2(px, py, s.Width, s.Height);
                        if (px < 0 || py < 0 || px + s.Width > r.Viewport.Width + 1e-7 || py + s.Height > r.Viewport.Height + 1e-7 || chip is { } ch && Overlap(rect, ch) || placed.Any(p => Overlap(rect, new(p.X, p.Y, p.Width, p.Height)))) return false;
                        placed.Add(new(x.P.Owner, x.C.Id, px, py, s.Width, s.Height, forms[(x.P.Owner, x.C.Id)]));
                        cursor += (portrait ? s.Width : s.Height) + gap;
                    }
                    offset += thickness + gap;
                }
                extents[rail] = Math.Max(0, offset - gap);
                // A card too wide for the keep-clear budget may sit alone in its rail's first column; overlap is checked above.
                var axisLength = portrait ? r.Viewport.Height : r.Viewport.Width;
                if (groups.Count > 0 && groups[0].Count == 1)
                {
                    var first = portrait ? Measure(groups[0][0]).Height : Measure(groups[0][0]).Width;
                    if (first + o.KeepClear * axisLength > axisLength) exempt[rail] = first + (groups.Count > 1 ? gap : 0);
                }
            }
            var axis = portrait ? r.Viewport.Height : r.Viewport.Width;
            if (extents.Sum() - exempt.Sum() + o.KeepClear * axis > axis + 1e-7) return false;
            foreach (var zone in new[]{Slot.Tl,Slot.Tr,Slot.Bl,Slot.Br,Slot.Tc,Slot.Bc})
            {
                var stack = titles.Where(x => x.Slot == zone).ToArray();
                var upper = zone is Slot.Tl or Slot.Tc or Slot.Tr;
                var left = zone is Slot.Tl or Slot.Bl;
                var middle = zone is Slot.Tc or Slot.Bc;
                var bottomEdge = r.Viewport.Height - margin - (zone == Slot.Bl ? o.BottomLeftReserve : zone == Slot.Br && reserve ? o.ChipReserve * u + gap : 0);
                var cursor = upper ? top : bottomEdge;
                foreach (var x in stack)
                {
                    var lo = margin; var hi = r.Viewport.Width - margin;
                    if (middle)
                    {
                        // Only things in the Title's own rows constrain it. Narrowing can make it taller, so settle the gap in a few passes.
                        var key = (x.P.Owner, x.C.Id);
                        for (var pass = 0; pass < 3; pass++)
                        {
                            forms[key] = forms[key] with { Width = Math.Min(.6 * r.Viewport.Width, hi - lo) / (r.Dpi * zoom[x.P.Owner]) };
                            var h = Measure(x).Height; var y0 = upper ? cursor : cursor - h; var y1 = y0 + h;
                            double nlo = margin, nhi = r.Viewport.Width - margin;
                            foreach (var p in placed.Where(p => p.Y < y1 && p.Y + p.Height > y0))
                            {
                                if (p.X + p.Width / 2 < r.Viewport.Width / 2) nlo = Math.Max(nlo, p.X + p.Width + gap);
                                else nhi = Math.Min(nhi, p.X - gap);
                            }
                            if (nhi <= nlo) return false;
                            var settled = nlo == lo && nhi == hi;
                            lo = nlo; hi = nhi;
                            if (settled) break;
                        }
                        forms[key] = forms[key] with { Width = Math.Min(.6 * r.Viewport.Width, hi - lo) / (r.Dpi * zoom[x.P.Owner]) };
                    }
                    var size = Measure(x);
                    var px = middle ? Math.Clamp((r.Viewport.Width-size.Width)/2,lo,Math.Max(lo,hi-size.Width)) : left ? margin : r.Viewport.Width-margin-size.Width;
                    var py = upper ? cursor : cursor-size.Height;
                    var rect = new Rect2(px,py,size.Width,size.Height);
                    if(px<0 || py<0 || px+size.Width>r.Viewport.Width+1e-7 || py+size.Height>r.Viewport.Height+1e-7 || chip is {} ch && Overlap(rect,ch) || placed.Any(p=>Overlap(rect,new(p.X,p.Y,p.Width,p.Height)))) return false;
                    placed.Add(new(x.P.Owner,x.C.Id,px,py,size.Width,size.Height,forms[(x.P.Owner,x.C.Id)]));
                    cursor += upper ? size.Height+gap : -size.Height-gap;
                }
            }
            return true;
        }
        var fits = Fit(false);
        while (!fits && zoom.Values.Any(z => z > o.MinZoom))
        {
            multiplier = Math.Max(0, multiplier - o.ZoomStep);
            foreach (var p in owners) zoom[p.Owner] = Math.Max(o.MinZoom, Snap(initial[p.Owner] * multiplier));
            fits = Fit(false);
        }
        (CardKind Kind, int? Rows, string Name)[] compact = [(CardKind.Table,5,"Tables5"),(CardKind.Bar,10,"Bars10"),(CardKind.Title,2,"TitleDescriptions2"),(CardKind.Line,null,"Line"),(CardKind.Legend,null,"GradientLegend"),(CardKind.Text,null,"Text2"),(CardKind.Metric,null,"Metric"),(CardKind.Bar,6,"Bars6"),(CardKind.Table,3,"Tables3"),(CardKind.Title,0,"TitleDescriptions0")];
        if (o.EnableCompact) foreach (var step in compact)
        {
            if (fits) break;
            var changed = false;
            foreach (var x in items.Where(x => x.C.Kind == step.Kind && (step.Kind != CardKind.Legend || x.C.Min.HasValue)))
            {
                var key = (x.P.Owner, x.C.Id);
                var next = x.C.Kind == CardKind.Title ? forms[key] with { DescriptionLines = step.Rows } : step.Rows is { } n ? new CardForm(Math.Min(n, x.C.Kind == CardKind.Table ? Math.Min(x.C.MaxRows,x.C.Rows.Length) : x.C.Values.Length)) : new CardForm(null,true);
                if (FormMeasure.Size(x.C,x.P.Theme,next,r.Measure) == MeasureDesign(x)) continue;
                forms[key] = next; changed = true;
            }
            if (changed) { steps.Add(step.Name); fits = Fit(false); }
        }
        if (!fits)
        {
            var removal = Enumerable.Range(0, owners.Select(p => p.Cards.Count).DefaultIfEmpty().Max()).SelectMany(rank => owners.Where(p => p.Cards.Count > rank).Select(p => items.FirstOrDefault(x => x.C.Kind != CardKind.Title && x.P.Owner == p.Owner && x.Index == p.Cards.Count - 1 - rank)));
            foreach (var x in removal.OfType<Item>()) { hidden.Add(x); if (Fit(true)) break; }
            foreach (var x in hidden.ToArray().AsEnumerable().Reverse()) { hidden.Remove(x); if (!Fit(hidden.Count > 0)) hidden.Add(x); }
            if (!Fit(hidden.Count > 0))
            {
                foreach(var x in items.Where(x=>x.C.Kind == CardKind.Title)) forms[(x.P.Owner,x.C.Id)] = forms[(x.P.Owner,x.C.Id)] with { DropSubtitle=true, TitleLines=1 };
                if(!Fit(hidden.Count>0))
                {
                    var removed = new List<Item>();
                    foreach(var x in items.Where(x=>x.C.Kind==CardKind.Title).Reverse()) { hidden.Add(x); removed.Add(x); if(Fit(true)) break; }
                    foreach(var x in removed.AsEnumerable().Reverse()) { hidden.Remove(x); if(!Fit(hidden.Count>0)) hidden.Add(x); }
                }
            }
            Fit(hidden.Count > 0);
        }
        return new(items.Where(x => !hidden.Contains(x)).Select(x => placed.Single(p => p.Owner == x.P.Owner && p.CardId == x.C.Id)).ToArray(), hidden.Select(x => (x.P.Owner,x.C.Id)).ToArray(), zoom, zoom.ToDictionary(x => x.Key,x => x.Value*r.Dpi), chip, new(initial,multiplier,lines,portrait ? LayoutOrientation.Bands : LayoutOrientation.Columns,steps,hidden.Count));
        Size2 MeasureDesign(Item x) => FormMeasure.Size(x.C,x.P.Theme,forms[(x.P.Owner,x.C.Id)],r.Measure);
    }
    private static bool Overlap(Rect2 a, Rect2 b) => a.X < b.X+b.Width && b.X < a.X+a.Width && a.Y < b.Y+b.Height && b.Y < a.Y+a.Height;
}

