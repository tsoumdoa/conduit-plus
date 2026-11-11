using System.Globalization;

namespace ConduitPlus.Core;

public static class CardPainter
{
    public static void Paint(ICanvas2D canvas, CardSpec c, Theme t)
    {
        if (c.Kind == CardKind.Title) { TitleMetrics.Paint(canvas, c, t); return; }
        var size = CardMeasure.Measure(c, t);
        ThemePainter.Shadow(canvas, size, t);
        using var clip = canvas.Clip(new(0, 0, size.Width, size.Height));
        canvas.RoundedRect(new(0, 0, size.Width, size.Height), t.Radius, t.Surface);
        ThemePainter.Border(canvas, size, t);
        var clear = CardMeasure.Clear(c, t);
        if (c.Kind == CardKind.Metric && c.Form.Compact)
        {
            foreach (var hit in CardMeasure.DataHits(c, t)) Highlight(canvas, c, t, hit);
            var value = c.Value ?? "—";
            var unitSize = canvas.MeasureText(c.Unit, t.FontSize, t.Font);
            var valueSize = canvas.MeasureText(value, 18, t.Font);
            var end = clear is null ? c.Width - t.Padding : clear.X - 8;
            var valueX = end - unitSize.Width - (c.Unit.Length > 0 ? 6 : 0) - valueSize.Width;
            var title = c.Title;
            var available = Math.Max(0, valueX - 8 - t.Padding);
            if (canvas.MeasureText(title, t.FontSize, t.Font).Width > available)
            {
                while (title.Length > 0 && canvas.MeasureText(title + "…", t.FontSize, t.Font).Width > available) title = title[..^1];
                title = available >= canvas.MeasureText("…", t.FontSize, t.Font).Width ? title + "…" : "";
            }
            canvas.Text(title, new(t.Padding, (48 - canvas.MeasureText(title, t.FontSize, t.Font).Height) / 2), t.FontSize, t.Muted, t.Font);
            canvas.Text(value, new(valueX, (48 - valueSize.Height) / 2), 18, t.Ink, t.Font);
            canvas.Text(c.Unit, new(end - unitSize.Width, (48 - unitSize.Height) / 2), t.FontSize, t.Muted, t.Font);
            if (clear is not null)
            {
                canvas.RoundedRect(new(clear.X, clear.Y, clear.W, clear.H), 3, t.Track);
                canvas.Text("Clear", new(clear.X + 5, clear.Y + 3), t.FontSize, t.Accent, t.Font);
            }
            return;
        }
        // A gradient legend shows its unit at the right of the title row; keep the title clear of it.
        var titleRight = clear?.X ?? (size.Width - t.Padding);
        if (c.Kind == CardKind.Legend && c.Min.HasValue && !string.IsNullOrWhiteSpace(c.Unit)) titleRight = Math.Min(titleRight, c.Width - t.Padding - canvas.MeasureText(c.Unit, t.FontSize, t.Font).Width - 8);
        var titleGap = c.Kind == CardKind.Legend && !c.Min.HasValue && clear is null ? 0 : 4;
        using (canvas.Clip(new(t.Padding, 7, titleRight - t.Padding - titleGap, 24)))
            canvas.Text(c.Title, new(t.Padding, 10), t.FontSize, t.Muted, t.Font);
        if (clear is not null)
        {
            canvas.RoundedRect(new(clear.X, clear.Y, clear.W, clear.H), 3, t.Track);
            canvas.Text("Clear", new(clear.X + 5, clear.Y + 3), t.FontSize, t.Accent, t.Font);
        }
        if (c.Kind == CardKind.Line) { LineCardPainter.Paint(canvas, c, t); return; }
        if (c.Kind == CardKind.Text)
        {
            var layout = TextMetrics.Layout(c, t);
            using var bodyClip = canvas.Clip(new(t.Padding, layout.Top, c.Width - 2 * t.Padding, layout.Lines.Length * layout.LineHeight));
            for (var i = 0; i < layout.Lines.Length; i++) canvas.Text(layout.Lines[i], new(t.Padding, layout.Top + i * layout.LineHeight), t.FontSize, t.Ink, t.Font);
            return;
        }
        if (c.Kind == CardKind.Legend) { LegendCardPainter.Paint(canvas, c, t); return; }
        if (c.Kind == CardKind.Metric)
        {
            foreach (var hit in CardMeasure.DataHits(c, t)) Highlight(canvas, c, t, hit);
            var value = c.Value ?? "—";
            canvas.Text(value, new(t.Padding, 36), 28, t.Ink, t.Font);
            var measured = canvas.MeasureText(value, 28, t.Font);
            canvas.Text(c.Unit, new(Math.Min(c.Width - 45, t.Padding + measured.Width + 8), 51), t.FontSize, t.Muted, t.Font);
            return;
        }
        var hits = CardMeasure.DataHits(c, t);
        var max = Math.Max(1e-9, Math.Max(c.Threshold, c.Values.Select(v => Math.Abs(v ?? 0)).DefaultIfEmpty(0).Max()));
        if (c.Kind == CardKind.Bar)
        {
            var labelWidth = Math.Min((c.Width - 2 * t.Padding - 55) * .55,
                Math.Max(55, c.Labels.Select(label => canvas.MeasureText(label, t.FontSize, t.Font).Width + 8).DefaultIfEmpty(55).Max()));
            for (var i = 0; i < hits.Length; i++)
            {
                var dataIndex = TableScroll.Offset(c) + i;
                var h = hits[i]; Highlight(canvas, c, t, h);
                using (canvas.Clip(new(h.X, h.Y, labelWidth - 4, h.H)))
                    canvas.Text(c.Labels[dataIndex], new(h.X + 2, h.Y + 2), t.FontSize, t.Ink, t.Font);
                var x = h.X + labelWidth; var w = h.W - labelWidth - 55; var y = h.Y + 7;
                canvas.Rect(new(x, y, w, 8), t.Track);
                var value = c.Values[dataIndex];
                // Signed axis: negatives extend left of zero, nulls remain gaps.
                var signed = c.Values.Any(v => v < 0); var origin = signed ? x + w / 2 : x; var span = signed ? w / 2 : w;
                if (value.HasValue) canvas.Rect(new(value < 0 ? origin - Math.Abs(value.Value) / max * span : origin, y, Math.Abs(value.Value) / max * span, 8), c.Colors.Length > dataIndex ? c.Colors[dataIndex] : value > c.Threshold ? t.Bad : t.Good);
                canvas.Line(new(origin + c.Threshold / max * span, y - 2), new(origin + c.Threshold / max * span, y + 10), t.Muted);
                canvas.Text(value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—", new(h.X + h.W - 43, h.Y + 2), t.FontSize, t.Ink, t.Font);
            }
            if (TableScroll.Bar(c, t) is { } bar)
            {
                canvas.RoundedRect(bar.Track, TableScroll.BarWidth / 2, t.Track);
                canvas.RoundedRect(bar.Thumb, TableScroll.BarWidth / 2, t.Muted);
            }
        }
        else
        {
            var width = (c.Width - 2 * t.Padding) / c.Headers.Length;
            foreach (var header in TableSort.Headers(c, t))
                if (c.HoverKey == header.Key) canvas.Rect(new(header.X, header.Y, header.W, header.H), t.Track);
            for (var col = 0; col < c.Headers.Length; col++)
            {
                Cell(c.Headers[col], col, CardMeasure.BodyTop, t.Muted, c.SortColumn == col ? 14 : 0);
                if (c.SortColumn != col) continue;
                // Glyph hugs the header text so it can't be read as belonging to the next column.
                var textWidth = Math.Min(canvas.MeasureText(c.Headers[col], t.FontSize, t.Font).Width, width - 3 - 14 - 2);
                var x = t.Padding + col * width + 2 + textWidth + 8; var y = CardMeasure.BodyTop + 10;
                canvas.FillPolygon(c.SortDescending ? [new(x - 4, y - 2), new(x + 4, y - 2), new(x, y + 3)] :
                    [new(x - 4, y + 2), new(x + 4, y + 2), new(x, y - 3)], t.Muted);
            }
            var rows = TableSort.Rows(c);
            var offset = TableScroll.Offset(c);
            for (var row = 0; row < hits.Length; row++)
            {
                var h = hits[row]; Highlight(canvas, c, t, h);
                canvas.Line(new(h.X, h.Y), new(h.X + h.W, h.Y), t.Line);
                for (var col = 0; col < c.Headers.Length; col++) Cell(rows[offset + row].Cells[col] ?? "—", col, h.Y, t.Ink);
            }
            if (TableScroll.Bar(c, t) is { } bar)
            {
                canvas.RoundedRect(bar.Track, TableScroll.BarWidth / 2, t.Track);
                canvas.RoundedRect(bar.Thumb, TableScroll.BarWidth / 2, t.Muted);
            }
            void Cell(string text, int col, double y, string color, double reserve = 0)
            {
                using var cellClip = canvas.Clip(new(t.Padding + col * width, y, width - 3 - reserve, t.RowHeight));
                canvas.Text(text, new(t.Padding + col * width + 2, y + 2), t.FontSize, color, t.Font);
            }
        }
    }
    internal static void Highlight(ICanvas2D canvas, CardSpec c, Theme t, HitRegion h)
    {
        c.GroupStates.TryGetValue(h.Key, out var group);
        if (group is { IsEnabled: false }) return;
        var selected = group?.IsFullySelected ?? c.SelectedKeys.Contains(h.Key);
        var direct = group?.IsDirectTarget ?? c.HoverKey == h.Key;
        var hovered = group is not null ? group.HoveredMemberCount > 0 || direct : c.HoveredKeys.Contains(h.Key) || direct;
        if (selected || direct) canvas.Rect(new(h.X, h.Y, h.W, h.H), selected ? t.Highlight : t.Track);
        if (selected) canvas.Rect(new(h.X, h.Y, 2, h.H), t.Accent);
        if (group is { IsPartiallySelected: true })
            canvas.Line(new(h.X + h.W + 2, h.Y + h.H / 2), new(h.X + h.W + 8, h.Y + h.H / 2), t.Accent, 2);
        if (hovered) Outline(canvas, h, t.Accent);
    }
    internal static void Outline(ICanvas2D canvas, HitRegion h, string color) =>
        canvas.Polyline([new(h.X + .5, h.Y + .5), new(h.X + h.W - .5, h.Y + .5),
            new(h.X + h.W - .5, h.Y + h.H - .5), new(h.X + .5, h.Y + h.H - .5), new(h.X + .5, h.Y + .5)], color);
}
