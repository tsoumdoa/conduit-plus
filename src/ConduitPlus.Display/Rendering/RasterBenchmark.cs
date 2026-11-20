using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

public static class RasterBenchmark
{
    public static CardSpec LargeTable() => new()
    {
        Id = "bench-large-table", Kind = CardKind.Table, Title = "600 rows × 12 columns", Width = 900,
        Headers = Enumerable.Range(0, 12).Select(i => $"Column {i + 1}").ToArray(),
        Rows = Enumerable.Range(0, 600).Select(i => new TableRow($"r{i}", Enumerable.Range(0, 12)
            .Select(j => (string?)(i * 12 + j).ToString(CultureInfo.InvariantCulture)).ToArray())).ToArray(),
        SortColumn = 1
    };
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (var spec in StructuralDemo.Create().Where(c => c.Id is "metric-0" or "members" or "results").Append(StructuralDemo.Line()).Append(LargeTable()))
        {
            var samples = new List<double>();
            for (var i = 0; i < 220; i++)
            {
                var timer = Stopwatch.StartNew();
                using var bitmap = RasterDrawing.Render(spec, Theme.Paper, 2);
                var ms = timer.Elapsed.TotalMilliseconds;
                if (i >= 20) samples.Add(ms);
                if (i == 219 && spec.Kind == CardKind.Line)
                {
                    var captures = Path.Combine(output, "captures"); Directory.CreateDirectory(captures);
                    bitmap.Save(Path.Combine(captures, "raster-line-2x.png"), ImageFormat.Png);
                }
            }
            var sorted = samples.Order().ToArray();
            rows.Add(new { id = spec.Id, kind = spec.Kind.ToString(), scale = 2, samples, p50 = sorted[99], p95 = sorted[189] });
        }
        File.WriteAllText(Path.Combine(output, "samples-raster-micro.json"), CardJson.Serialize(new { measured = DateTimeOffset.Now, warmups = 20, count = 200, includes = "bitmap allocation + GDI+ paint + drawing-object cleanup; excludes returned-bitmap disposal, spec revision, DisplayBitmap and GPU", rows }));
        var interactions = new List<object>();
        foreach (var scroll in new[] { false, true })
        {
            var store = new CardStore(); store.Update([LargeTable()]);
            var samples = new List<double>();
            for (var i = 0; i < 220; i++)
            {
                var previous = store.Snapshot[0];
                var timer = Stopwatch.StartNew();
                store.Update([scroll ? previous with { ScrollOffset = i % 590 } : previous with { HoverKey = $"r{i % 8}" }]);
                var spec = store.Snapshot[0];
                _ = CardMeasure.Hits(spec, Theme.Paper);
                using var bitmap = RasterDrawing.Render(spec, Theme.Paper, 2);
                var ms = timer.Elapsed.TotalMilliseconds;
                if (i >= 20) samples.Add(ms);
            }
            var sorted = samples.Order().ToArray();
            interactions.Add(new { interaction = scroll ? "scroll" : "hover", scale = 2, samples, p50 = sorted[99], p95 = sorted[189] });
        }
        File.WriteAllText(Path.Combine(output, "samples-table-interaction.json"), CardJson.Serialize(new
        {
            measured = DateTimeOffset.Now, warmups = 20, count = 200, rows = 600, columns = 12, width = 900,
            includes = "CardStore update + sorted hit regions + bitmap allocation + GDI+ paint; excludes DisplayBitmap, redraw and GPU", interactions
        }));
    }
}
