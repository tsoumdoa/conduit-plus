using System.Diagnostics;
using System.Drawing.Imaging;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

public static class RasterBenchmark
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (var spec in StructuralDemo.Create().Where(c => c.Id is "metric-0" or "members" or "results").Append(StructuralDemo.Line()))
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
            rows.Add(new { kind = spec.Kind.ToString(), scale = 2, samples, p50 = sorted[99], p95 = sorted[189] });
        }
        File.WriteAllText(Path.Combine(output, "samples-raster-micro.json"), CardJson.Serialize(new { measured = DateTimeOffset.Now, warmups = 20, count = 200, includes = "bitmap allocation + GDI+ paint + drawing-object cleanup; excludes returned-bitmap disposal, spec revision, DisplayBitmap and GPU", rows }));
    }
}
