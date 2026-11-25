using System.Diagnostics;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using ConduitPlus.Rhino;
using Rhino;
using Rhino.Display;

internal static class DistributionBench
{
    public static CardSpec Fixture(int count, DistributionMode mode = DistributionMode.Dots, double? width = null, bool coincident = false)
    {
        var values = Enumerable.Range(0, count).Select(i => (double?)(coincident ? 12 : 12 + i % 23 + .08 * (i % 7))).ToArray();
        var keys = InputKeys.Numbered(count);
        return new()
        {
            Id = "distribution", Kind = CardKind.Distribution, Title = "Room areas", Unit = "m²",
            Values = values, Keys = keys, Labels = keys, Width = 340, Distribution = new(mode, BinWidth: width)
        };
    }

    public static void Run(string output)
    {
        var rows = new List<object>();
        foreach (var (name, card) in new[]
        {
            ("histogram", Fixture(2000, DistributionMode.Histogram)),
            ("dots", Fixture(2000)),
            ("tiny-width", Fixture(2000, width: .000001)),
            ("coincident", Fixture(2000, coincident: true)),
            ("visible-dots", Fixture(60))
        })
        {
            var before = Distribution.LayoutBuilds;
            var started = Stopwatch.GetTimestamp();
            var layout = Distribution.Geometry(card, Theme.Paper);
            var layoutMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var coldBuilds = Distribution.LayoutBuilds - before;
            var key = layout.Marks[0].Hit.Key;
            foreach (var state in new[] { "hover", "selection" })
            {
                var samples = new List<double>();
                before = Distribution.LayoutBuilds;
                for (var i = 0; i < 120; i++)
                {
                    var projected = state == "hover" ? card with { HoverKey = i % 2 == 0 ? key : null }
                        : card with { SelectedKeys = card.Keys.Take(i % 2 == 0 ? 30 : 0).ToArray() };
                    started = Stopwatch.GetTimestamp();
                    _ = CardMeasure.Hits(projected, Theme.Paper);
                    using var raster = RasterDrawing.Render(projected, Theme.Paper, 2);
                    if (i >= 20) samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                var warmBuilds = Distribution.LayoutBuilds - before;
                samples.Sort();
                var row = new { name, state, count = layout.Count, mode = layout.Mode.ToString(), layout.Forced, layout.Fallback,
                    coldBuilds, layoutMs, warmBuilds, paintP50 = samples[49], paintP95 = samples[94] };
                rows.Add(row);
                Console.WriteLine(CardJson.Serialize(row));
                if (warmBuilds != 0) throw new InvalidOperationException("Warm repaint rebuilt Distribution geometry.");
            }
        }
        File.WriteAllText(Path.Combine(output, "samples-distribution.json"), CardJson.Serialize(rows));
    }

    public static void Export(RhinoDoc doc, RhinoView view, Theme theme, Action export)
    {

        var dots = Fixture(60) with { Id = "distribution-dots", Title = "Room areas · dots", WiredSlot = Slot.Tl };
        var histogram = Fixture(60, DistributionMode.Histogram) with { Id = "distribution-histogram", Title = "Room areas · histogram", WiredSlot = Slot.Tr };
        var forced = Fixture(2000) with { Id = "distribution-forced", Title = "Room areas · forced histogram", WiredSlot = Slot.Bl };
        var scale = ColourScale.Create(dots.Values, ["Blue"], 17, 30).Legend;
        var coloured = Distribution.Create(dots with { Id = "distribution-scale", Title = "Room areas · Colour Scale", WiredSlot = Slot.Br }, scale, null);
        var thresholds = ThresholdSet.Resolve([18, 29], ["#B44E48", "#20856E"], ["Below", "Above"], []);
        var threshold = Distribution.Create(histogram with { Id = "distribution-threshold", Title = "Room areas · thresholds", WiredSlot = Slot.Lc }, null, thresholds);
        var crowded = Distribution.Create(dots with { Id = "distribution-crowded", Title = "Crowded thresholds", WiredSlot = Slot.Lc },
            null, ThresholdSet.Resolve([22, 22.1, 50, 51], ["#B44E48", "#20856E", "#B44E48", "#20856E"], null, []));
        var decimalWidth = histogram with
        {
            Id = "distribution-decimal", Title = "Decimal bin edges", Unit = "m", WiredSlot = Slot.Rc,
            Values = [-.15, -.1, 0, .3, .4], Keys = ["1", "2", "3", "4", "5"], Labels = ["1", "2", "3", "4", "5"],
            Distribution = new(DistributionMode.Histogram, BinWidth: .1)
        };
        OverlayManager.Set(doc, view, new Guid("b644c623-4ae0-478b-b398-b2cfde7bc505"),
            [dots, histogram, forced, coloured, threshold, crowded, decimalWidth], theme: theme);
        view.Redraw();
        RhinoApp.Wait();
        export();
    }
}
