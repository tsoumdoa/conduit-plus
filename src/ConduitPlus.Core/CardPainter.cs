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
            var value = NumericText.TrueMinus(c.Value ?? "—");
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
                canvas.Text("Clear", new(clear.X + 5, clear.Y + 3), t.FontSize, t.Muted, t.Font);
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
            canvas.Text("Clear", new(clear.X + 5, clear.Y + 3), t.FontSize, t.Muted, t.Font);
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
            var value = NumericText.TrueMinus(c.Value ?? "—");
            canvas.Text(value, new(t.Padding, 36), 28, t.Ink, t.Font);
            var measured = canvas.MeasureText(value, 28, t.Font);
            canvas.Text(c.Unit, new(Math.Min(c.Width - 45, t.Padding + measured.Width + 8), 51), t.FontSize, t.Muted, t.Font);
            return;
        }
        var hits = CardMeasure.DataHits(c, t);
        var max = Math.Max(1e-9, Math.Max(c.HasThreshold ? c.Threshold : 0, c.Values.Select(v => Math.Abs(v ?? 0)).DefaultIfEmpty(0).Max()));
        if (c.Kind == CardKind.Bar)
        {
            var layout = BarPresentation.Layout(c, t);
            var labelWidth = layout.LabelWidth;
            var order = BarPresentation.Order(c);
            var hasThreshold = BarPresentation.UsesThreshold(c);
            var within = hasThreshold && c.GoodWhen == BarGoodWhen.Within;
            var signed = within || c.Values.Any(v => v < 0);
            for (var i = 0; i < hits.Length; i++)
            {
                var dataIndex = order[TableScroll.Offset(c) + i];
                var h = hits[i]; Highlight(canvas, c, t, h);
                using (canvas.Clip(new(h.X, h.Y, labelWidth - 4, h.H)))
                    canvas.Text(c.Labels[dataIndex], new(h.X + 2, h.Y + 2), t.FontSize, t.Ink, t.Font);
                var x = h.X + labelWidth; var w = h.W - labelWidth - layout.ValueWidth; var y = h.Y + 7;
                canvas.Rect(new(x, y, w, 8), t.Track);
                var value = c.Values[dataIndex];
                // Signed axis: negatives extend left of zero, nulls remain gaps.
                var origin = signed ? x + w / 2 : x; var span = signed ? w / 2 : w;
                var toleranceWidth = c.Threshold / max * span;
                if (within) canvas.Rect(new(origin - toleranceWidth, y - 2, 2 * toleranceWidth, 12), t.Line);
                if (value.HasValue) canvas.Rect(new(value < 0 ? origin - Math.Abs(value.Value) / max * span : origin, y, Math.Abs(value.Value) / max * span, 8), BarPresentation.Color(c, t, dataIndex));
                if (hasThreshold) canvas.Line(new(origin + toleranceWidth, y - 2), new(origin + toleranceWidth, y + 10), t.Muted);
                if (within) canvas.Line(new(origin - toleranceWidth, y - 2), new(origin - toleranceWidth, y + 10), t.Muted);
                using (canvas.Clip(new(h.X + h.W - layout.ValueWidth + 6, h.Y, layout.ValueWidth - 6, h.H)))
                    canvas.Text(layout.Values[dataIndex], new(h.X + h.W - layout.ValueWidths[dataIndex], h.Y + 2), t.FontSize, t.Ink, t.Font);
            }
            TableScroll.Paint(canvas, c, t);
        }
        else
        {
            var layout = TablePresentation.Layout(c, t);
            var columns = layout.Columns;
            foreach (var header in layout.Headers)
                if (c.HoverKey == header.Key) canvas.Rect(new(header.X, header.Y, header.W, header.H), t.Track);
            for (var col = 0; col < c.Headers.Length; col++)
            {
                Cell(c.Headers[col], col, CardMeasure.BodyTop, t.Muted, columns[col].Numeric);
                if (c.SortColumn != col) continue;
                // Overlay the triangle in the right padding; no per-column sort gutter.
                var x = columns[col].X + columns[col].Width - 3; var y = CardMeasure.BodyTop + 10;
                using var glyphClip = canvas.Clip(new(columns[col].X, CardMeasure.BodyTop, columns[col].Width, t.RowHeight));
                canvas.FillPolygon(c.SortDescending ? [new(x - 2.5, y - 2.5), new(x + 2.5, y - 2.5), new(x, y + 2.5)] :
                    [new(x - 2.5, y + 2.5), new(x + 2.5, y + 2.5), new(x, y - 2.5)], t.Ink);
            }
            var rows = TableSort.Rows(c);
            var offset = TableScroll.Offset(c);
            for (var row = 0; row < hits.Length; row++)
            {
                var h = hits[row]; Highlight(canvas, c, t, h);
                canvas.Line(new(h.X, h.Y), new(h.X + h.W, h.Y), t.Line);
                for (var col = 0; col < c.Headers.Length; col++)
                    Cell(layout.Content.Cell(rows[offset + row], col), col, h.Y, t.Ink, right: columns[col].Numeric);
            }
            TableScroll.Paint(canvas, c, t);
            void Cell(string text, int col, double y, string color, bool right = false)
            {
                var column = columns[col];
                var available = Math.Max(0, column.Width - 12);
                // GDI uses the clip as its text layout box. Leave slack beyond the fitted
                // right edge so float rounding cannot add a second, unwanted ellipsis.
                using var cellClip = canvas.Clip(new(column.X + 6, y, available + 1.5, t.RowHeight));
                var fitted = layout.Content.Fit(text, available);
                var x = right ? column.X + 6 + available - fitted.Width : column.X + 6;
                canvas.Text(fitted.Text, new(x, y + 2), t.FontSize, color, t.Font);
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
        // The selection stripe lives in existing card padding; it never consumes text width.
        var left = CardMeasure.TickLeft(h);
        var metric = c.Kind == CardKind.Metric;
        if (selected || (metric ? hovered : direct && group is not { IsPartiallySelected: true }))
        {
            var fillLeft = metric && !c.Form.Compact ? left : h.X;
            canvas.Rect(new(fillLeft, h.Y, h.W + h.X - fillLeft, h.H), selected ? t.Highlight : t.Track);
        }
        if (selected) canvas.Rect(new(left, h.Y, 3, h.H), t.Accent);
        if (c.Kind != CardKind.Metric && group is { IsPartiallySelected: true })
            canvas.Rect(new(left, h.Y + h.H / 4, 3, h.H / 2), t.Accent);
        if (hovered && c.Kind != CardKind.Metric) Outline(canvas, h, t.Accent);
    }
    internal static void Outline(ICanvas2D canvas, HitRegion h, string color) =>
        canvas.Polyline([new(h.X + .5, h.Y + .5), new(h.X + h.W - .5, h.Y + .5),
            new(h.X + h.W - .5, h.Y + h.H - .5), new(h.X + .5, h.Y + h.H - .5), new(h.X + .5, h.Y + .5)], color);
}
