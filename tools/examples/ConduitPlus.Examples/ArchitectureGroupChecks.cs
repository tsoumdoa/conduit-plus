using System.Drawing;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using CoordinateSystem = Rhino.DocObjects.CoordinateSystem;

internal static class ArchitectureGroupChecks
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Get(object obj, string name) => obj.GetType().GetProperty(name, Instance)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Instance)!.Invoke(obj, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run(GH_Document gh, RhinoDoc doc, RhinoView view, object session, bool rooms, string output, string captureState)
    {
        Console.WriteLine("Starting group interaction checks.");
        // Reserve the open centre between the 200% DPI cards for the whole model.
        view.ActiveViewport.ZoomBoundingBox(rooms ? new BoundingBox(-18, -18, -1, 38, 34, 1)
            : new BoundingBox(-70, -70, -10, 170, 150, 65));
        view.Redraw();
        var owner = (Guid)Get(session, "Owner");
        CardSpec[] Cards() => (CardSpec[])Get(session, "Cards");
        var groupKey = rooms ? "type:bedroom" : "use:residential";
        var legend = Cards().Single(c => c.Kind == CardKind.Legend);
        var target = rooms ? legend : Cards().Single(c => c.Title == "GFA by use (m²)");
        var members = target.Groups[groupKey].Members;
        var table = Cards().Single(c => c.Kind == CardKind.Table);
        var metric = Cards().Single(c => c.Title == (rooms ? "Rooms below minimum" : "Blocks above 30 m"));
        Require(members.Length == (rooms ? 5 : 2), "Unexpected category membership.");
        Require(metric.Groups.Single().Value.Members.Length == (rooms ? 8 : 2), "Unexpected failure membership.");
        Call(session, "Select", target.Id, CardMeasure.ClearKey, BrushSelectMode.Toggle);
        if (rooms)
        {
            Call(session, "Select", "model", members[0], BrushSelectMode.Replace);
            Call(session, "Select", "model", members[1], BrushSelectMode.Add);
            Require(OverlayManager.Snapshot(owner).SelectedKeys.Order(StringComparer.Ordinal).SequenceEqual(members.Take(2).Order(StringComparer.Ordinal)), "Sequential replace/add failed.");
            Capture("two-rooms-selected"); CheckFaces(members.Take(2), "two-rooms-selected");
            Call(session, "Select", "model", CardMeasure.ClearKey, BrushSelectMode.Replace);
        }
        Call(session, "Sort", table.Id, 1); // contiguous category rows; keep display-order reveal meaningful
        var events = new List<OverlayInteraction>();
        void OnAction(OverlayInteraction action) { if (action.Owner == owner) events.Add(action); }
        OverlayManager.Interaction += OnAction;
        try
        {
            Console.WriteLine("Selecting category group.");
            Call(session, "Select", target.Id, groupKey, BrushSelectMode.Toggle);
            Require(events.Count == 1 && events[0].TargetKind == BrushTargetKind.Group && events[0].AffectedKeys.SequenceEqual(members), "Group must emit exactly one member-based event.");
            Require(OverlayManager.Snapshot(owner).SelectedKeys.SequenceEqual(members), "Group Selection contains tokens or misses members.");
            var selectedTable = Cards().Single(c => c.Id == table.Id);
            Require(selectedTable.SelectedKeys.Order(StringComparer.Ordinal).SequenceEqual(members), "Table did not project every selected member.");
            Require(Cards().Single(c => c.Id == legend.Id).GroupStates[groupKey].IsFullySelected, "Legend full state missing.");
            Require(TableScroll.Offset(selectedTable) == TableScroll.Reveal(table with { SortColumn = 1, SortDescending = false },
                TableSort.Rows(selectedTable).First(r => members.Contains(r.Key)).Key), "Group action did not reveal first new row in display order.");
            if (captureState is "groupselected" or "grouphover")
            {
                if (captureState == "grouphover")
                {
                    var other = legend.Keys.First(k => k != groupKey);
                    Call(session, "SetHover", legend.Id, other);
                    Require(OverlayManager.Snapshot(owner).HoveredKeys.Length == legend.Groups[other].Members.Length, "Group hover did not expand effective geometry keys.");
                }
                var name = captureState == "grouphover" ? "selected-with-hover" : rooms ? "bedroom-selected" : "residential-selected";
                Console.WriteLine("Capturing " + name);
                Capture(name); CheckFaces(members, name);
                Call(session, "SetHover", null, null);
            }
            Call(session, "Select", table.Id, members[0], BrushSelectMode.Toggle);
            Require(Cards().Single(c => c.Id == target.Id).GroupStates[groupKey].IsPartiallySelected, "Partial state missing.");
            if (captureState == "grouppartial") Capture(rooms ? "bedroom-partial" : "residential-partial");
            Call(session, "Select", target.Id, groupKey, BrushSelectMode.Toggle); // partial -> full
            Require(OverlayManager.Snapshot(owner).SelectedKeys.SequenceEqual(members), "Partial group click must select all members.");
            Call(session, "Select", target.Id, groupKey, BrushSelectMode.Toggle); // full -> empty
            Require(OverlayManager.Snapshot(owner).SelectedKeys.Length == 0, "Full group click must remove its members.");
            Call(session, "Select", metric.Id, metric.Keys.Single(), BrushSelectMode.Toggle);
            Require(OverlayManager.Snapshot(owner).SelectedKeys.SequenceEqual(metric.Groups.Single().Value.Members), "Metric body selection did not expand group.");
            Call(session, "Select", metric.Id, CardMeasure.ClearKey, BrushSelectMode.Toggle);
            Require(OverlayManager.Snapshot(owner).SelectedKeys.Length == 0, "Metric Clear failed.");
            // Real Canvas host consumes the same incoming grouped CardSpec, while retaining private selection.
            Console.WriteLine("Checking private Canvas host.");
            var source = gh.Objects.OfType<LegendComponent>().Single();
            var canvas = new CanvasCardComponent(); canvas.CreateAttributes(); gh.AddObject(canvas, false);
            canvas.Params.Input[0].AddSource(source.Params.Output[0]); gh.NewSolution(false);
            var state = (CanvasCardState)Get(canvas, "State");
            Require(state.Spec?.Groups.ContainsKey(groupKey) == true, "Canvas lost incoming group membership.");
            state.Activate(groupKey, BrushSelectMode.Toggle); Require(state.Spec!.GroupStates[groupKey].IsFullySelected, "Canvas group action failed.");
            Require(OverlayManager.Snapshot(owner).SelectedKeys.Length == 0, "Local Canvas selection leaked into Overlay.");
            state.Activate(CardMeasure.ClearKey, BrushSelectMode.Toggle); gh.RemoveObject(canvas, false);
            // Update fixture geometry inputs: empty single-branch memberships must survive real GH data access.
            Console.WriteLine("Checking dynamic failure membership.");
            var slider = gh.Objects.OfType<GH_NumberSlider>().Single();
            slider.SetSliderValue(rooms ? 2 : -1); slider.ExpireSolution(false); gh.NewSolution(false);
            var changedMetric = Cards().Single(c => c.Id == metric.Id);
            Require(changedMetric.Value == (rooms ? "0" : "1") && changedMetric.Groups.Single().Value.Members.Length == (rooms ? 0 : 1), "Empty failure branch was lost or stale after data update.");
            slider.SetSliderValue(rooms ? 1 : 0); slider.ExpireSolution(false); gh.NewSolution(false);
            Require(Cards().Single(c => c.Id == metric.Id).Groups.Single().Value.Members.Length == (rooms ? 8 : 2), "Failure membership did not update with geometry.");
            var warnings = gh.Objects.OfType<IGH_ActiveObject>().SelectMany(o => new[] { GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Error }.SelectMany(o.RuntimeMessages)).ToArray();
            Require(warnings.Length == 0, string.Join("; ", warnings));
            File.WriteAllText(Path.Combine(output, "groups-check.json"), CardJson.Serialize(new { groupKey, members, oneEventPerGroup = true, tableProjection = true,
                metricGroup = true, emptyBranchAfterUpdate = rooms, dynamicFailureMembership = true, canvasPrivateSelection = true, captureState, events }));
            Console.WriteLine($"Group checks: {groupKey}, {members.Length} members, event/partial/Metric/Canvas/empty-branch checks passed.");
        }
        finally { OverlayManager.Interaction -= OnAction; }

        void Capture(string name)
        {
            var destination = Path.Combine(output, name); Directory.CreateDirectory(destination);
            var previous = Environment.GetEnvironmentVariable("CONDUITPLUS_RESULTS");
            try
            {
                Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", destination);
                ExportChecks.Run(doc, view, session, Cards(), destination, true, 1);
                File.Copy(Path.Combine(destination, "captures", "raster-1x.png"), Path.Combine(output, name + ".png"), true);
            }
            finally { Environment.SetEnvironmentVariable("CONDUITPLUS_RESULTS", previous); }
        }
        void CheckFaces(IEnumerable<string> keys, string name)
        {
            using var png = new Bitmap(Path.Combine(output, name + ".png"));
            var centres = rooms ? DemoRooms.Create().Rooms.ToDictionary(r => r.Key, r => new Point3d(r.X + r.Width / 2, r.Y + r.Depth / 2, 0))
                : DemoMassing.Create().Blocks.ToDictionary(b => b.Key, b => new Point3d(b.X + b.Width / 2, b.Y + b.Depth / 2, b.Height));
            var screen = view.ActiveViewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
            foreach (var key in keys)
            {
                var point = centres[key]; point.Transform(screen);
                var color = png.GetPixel((int)Math.Round(point.X), (int)Math.Round(point.Y));
                Require(color.A >= 140 && color.B > color.R + 20, "Missing selected group face fill for " + key);
            }
        }
    }
}
