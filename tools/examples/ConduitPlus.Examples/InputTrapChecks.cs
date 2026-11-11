using System.Drawing;
using System.Collections.Immutable;
using System.Reflection;
using ConduitPlus.Core;
using ConduitPlus.Grasshopper;
using ConduitPlus.Rhino;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class InputTrapChecks
{
    internal static void Run()
    {
        using var gh = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        var count = 0;
        T Add<T>(T obj) where T : IGH_DocumentObject { obj.CreateAttributes(); gh.AddObject(obj, false); return obj; }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        void Solve(GH_Component component) { component.ExpireSolution(false); gh.NewSolution(false); }
        CardSpec[] Cards(GH_Component component) => component.Params.Output[0].VolatileData.AllData(true).Select(g => g?.ScriptVariable()).OfType<CardSpec>().ToArray();
        CardSpec Card(GH_Component component) => Cards(component).Single();
        bool Has(GH_Component c, GH_RuntimeMessageLevel level, string message) => c.RuntimeMessages(level).Any(m => m.Contains(message, StringComparison.Ordinal));
        void Text(GH_Component c, int index, params string[] values)
        { var p = (Param_String)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_String(v))); p.ExpireSolution(false); }
        void Numbers(GH_Component c, int index, params double[] values)
        { var p = (Param_Number)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.AppendRange(values.Select(v => new GH_Number(v))); p.ExpireSolution(false); }
        var bar = Add(new BarComponent()); Numbers(bar, 0, 4, 8); Solve(bar);
        Check(Card(bar).Title == "Bar" && Card(bar).Labels.SequenceEqual(new[] { "1", "2" }) && !Card(bar).HasThreshold, "New Bar defaults.");
        Text(bar, 1, "Bath", "Bath"); Solve(bar);
        Check(Card(bar).Keys.SequenceEqual(new[] { "1", "2" }) && !bar.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Repeated labels without Keys.");
        Text(bar, 2, "Bath", "Bath"); Solve(bar);
        Check(Has(bar, GH_RuntimeMessageLevel.Error, "Keys repeat ('Bath' ×2)"), "Repeated explicit keys.");
        Text(bar, 1); Text(bar, 2); Solve(bar);
        var unsetChunk = new GH_Archive().CreateTopLevelNode("UnsetBar"); bar.Write(unsetChunk);
        var unsetRestored = Add(new BarComponent()); unsetRestored.Read((GH_IReader)unsetChunk); Solve(unsetRestored);
        Check(!Card(unsetRestored).HasThreshold && Card(unsetRestored).Keys.SequenceEqual(new[] { "1", "2" }), "New unset Bar threshold survives save/reopen.");
        Numbers(bar, 4, 1); Text(bar, 3, "Stored title"); Solve(bar);
        var archive = new GH_Archive(); var chunk = archive.CreateTopLevelNode("Bar"); bar.Write(chunk);
        var restored = Add(new BarComponent()); restored.Read((GH_IReader)chunk); Solve(restored);
        Check(Card(restored).Title == "Stored title" && Card(restored).Threshold == 1 && Card(restored).HasThreshold && Card(restored).Keys[0] == "1", "New defaults marker and persistent values roundtrip.");
        chunk.RemoveItem("ConduitPlus.DefaultsVersion");
        var legacy = Add(new BarComponent()); legacy.Read((GH_IReader)chunk); Solve(legacy);
        Check(Card(legacy).Keys.SequenceEqual(new[] { "M01", "M02" }) && Card(legacy).Title == "Stored title" && Card(legacy).Threshold == 1, "Unmarked Bar retains legacy generated keys and saved values.");
        var legacyChunk = new GH_Archive().CreateTopLevelNode("Legacy"); legacy.Write(legacyChunk);
        var legacyAgain = Add(new BarComponent()); legacyAgain.Read((GH_IReader)legacyChunk); Solve(legacyAgain);
        Check(Card(legacyAgain).Keys[0] == "M01", "Legacy defaults survive resaving.");
        Check(archive.Messages is null || !archive.Messages.Any(m => m.Type == GH_Message_Type.error), "Default migration archive clean.");
        Text(bar, 5, "top-left"); Solve(bar);
        Check(Has(bar, GH_RuntimeMessageLevel.Warning, "Unknown slot 'top-left'; use tl, tr, bl, br, lc or rc."), "Unknown Slot warning.");
        Text(bar, 5);
        var line = Add(new LineComponent()); Numbers(line, 0, 1, 2); Numbers(line, 4, 1.5); Solve(line);
        Check(Card(line).Title == "Line" && Card(line).Threshold == 1.5, "Internalised Line threshold.");
        Text(line, 1, "A", "A"); Solve(line);
        Check(Card(line).Keys.SequenceEqual(new[] { "1", "2" }), "Repeated Line labels.");
        var title = Add(new TitleComponent()); Solve(title);
        Check(Card(title).Title == "Title" && !title.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "Fresh Title is valid.");
        var legend = Add(new LegendComponent());
        ((Param_Colour)legend.Params.Input[0]).PersistentData.AppendRange(new[] { new GH_Colour(Color.Blue), new GH_Colour(Color.Red) });
        Numbers(legend, 2, 0); Numbers(legend, 3, 10); Solve(legend);
        Check(Card(legend).Min == 0 && Card(legend).Max == 10, "Internalised Legend gradient bounds.");
        Numbers(legend, 3); Text(legend, 1, "Low", "High"); Solve(legend);
        Check(Card(legend).Min is null && Has(legend, GH_RuntimeMessageLevel.Warning, "Min is set but Max isn't"), "One-bound swatch fallback.");
        Text(legend, 1); Solve(legend);
        Check(Has(legend, GH_RuntimeMessageLevel.Error, "Legend has 2 colours and 0 labels."), "One-bound mismatched labels error.");
        var metric = Add(new MetricComponent()); Text(metric, 0, "1", "2", "3"); Text(metric, 1, "One", "Two", "Three"); Text(metric, 2, "m", "mm", "kg"); Solve(metric);
        var metrics = Cards(metric);
        Check(metrics.Length == 3 && metrics.Select(c => c.Id).Distinct().Count() == 3 && metrics[0].Id == metric.InstanceGuid.ToString("N") && metrics[2].Id.EndsWith(":2", StringComparison.Ordinal), "Metric list positional identities.");
        Check(metrics.Select(c => c.Title).SequenceEqual(new[] { "One", "Two", "Three" }) && metrics.Select(c => c.Unit).SequenceEqual(new[] { "m", "mm", "kg" }), "Native Metric Title/Unit matching.");
        Text(metric, 5, "group"); ((Param_String)metric.Params.Input[6]).PersistentData.Append(new GH_String("a"), new GH_Path(0)); metric.Params.Input[6].ExpireSolution(false); Solve(metric);
        Check(Cards(metric).Length == 0 && Has(metric, GH_RuntimeMessageLevel.Error, "Members needs a single Value; use one Metric per group."), "Multi-run grouped Metric rejects all outputs: " + Cards(metric).Length + " cards, " + string.Join("; ", metric.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var branched = Add(new BarComponent()); var values = (Param_Number)branched.Params.Input[0];
        values.PersistentData.Append(new GH_Number(1), new GH_Path(0)); values.PersistentData.Append(new GH_Number(2), new GH_Path(1)); Solve(branched);
        Check(Cards(branched).Length == 2 && Has(branched, GH_RuntimeMessageLevel.Warning, "Flatten Values for one chart."), "One-item branch warning and unique Bar cards.");
        var branchedLine = Add(new LineComponent());
        var lineValues = (Param_Number)branchedLine.Params.Input[0];
        lineValues.PersistentData.Append(new GH_Number(1), new GH_Path(0)); lineValues.PersistentData.Append(new GH_Number(2), new GH_Path(1)); Solve(branchedLine);
        Check(Has(branchedLine, GH_RuntimeMessageLevel.Warning, "Flatten Values for one chart.") && Has(branchedLine, GH_RuntimeMessageLevel.Error, "at least two samples"), "One-item Line branches warn with a flattening remedy and retain minimum sample validation.");
        var link = Add(new LinkComponent()); var geometry = (Param_GenericObject)link.Params.Input[0];
        geometry.PersistentData.AppendRange(new[] { new GH_ObjectWrapper(new Point3d(0, 0, 0)), new GH_ObjectWrapper(new Point3d(1, 0, 0)) }); Solve(link);
        var linked = (Link)link.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Check(linked.Keys.SequenceEqual(new[] { "1", "2" }) && linked.ToString() == "Link · 2 keys · 2 objects", "Link per-item defaults and readable summary.");
        geometry.PersistentData.Clear(); geometry.PersistentData.Append(new GH_ObjectWrapper(new Point3d(0, 0, 0)), new GH_Path(0)); geometry.PersistentData.Append(new GH_ObjectWrapper(new Point3d(1, 0, 0)), new GH_Path(1)); geometry.ExpireSolution(false); Solve(link);
        Check(((Link)link.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).Keys.SequenceEqual(new[] { "1", "2" }), "Link per-branch defaults.");
        var input = Add(new Param_GenericObject());
        input.PersistentData.AppendRange(new[] { new GH_ObjectWrapper(metrics[0]), new GH_ObjectWrapper(null), new GH_ObjectWrapper(5), new GH_ObjectWrapper(metrics[0]), new GH_ObjectWrapper(metrics[1]) });
        var overlay = Add(new OverlayComponent()); overlay.Params.Input[0].AddSource(input); Solve(overlay);
        Check(Has(overlay, GH_RuntimeMessageLevel.Warning, "1 is empty because its component has an error") && Has(overlay, GH_RuntimeMessageLevel.Error, "Cards item 2") && Has(overlay, GH_RuntimeMessageLevel.Error, "Cards item 3 is connected twice; connect each card once."), "Overlay diagnoses null, foreign and duplicate siblings.");
        var originals = (CardSpec[])typeof(OverlayComponent).GetField("_originals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
        Check(originals.Length == 2, "Overlay keeps valid siblings and first duplicate.");
        var canvas = Add(new CanvasCardComponent()); canvas.Params.Input[0].AddSource(legend.Params.Output[0]); Solve(canvas);
        Check(Has(canvas, GH_RuntimeMessageLevel.Warning, "Card has an error upstream") && (string)typeof(CanvasCardComponent).GetProperty("Placeholder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)! == "Card has an error upstream", "Canvas upstream error warning/placeholder.");
        input.PersistentData.Clear(); input.ExpireSolution(false); Solve(overlay);
        Check(Has(overlay, GH_RuntimeMessageLevel.Warning, "Showing 0 of 1 cards"), "Connected-empty Overlay solves and removes stale cards.");
        CardSpec[] Published(OverlayComponent c) => (CardSpec[])typeof(OverlayComponent).GetField("_originals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        Link[] PublishedLinks(OverlayComponent c)
        {
            var pending = typeof(OverlayComponent).GetField("_lastPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
            return (Link[])pending.GetType().GetProperty("Links")!.GetValue(pending)!;
        }
        var treeBar = Add(new BarComponent()); var treeValues = (Param_Number)treeBar.Params.Input[0];
        treeValues.PersistentData.AppendRange(new[] { new GH_Number(1), new GH_Number(2) }, new GH_Path(0));
        treeValues.PersistentData.AppendRange(new[] { new GH_Number(3), new GH_Number(4) }, new GH_Path(1));
        var treeOverlay = Add(new OverlayComponent()); treeOverlay.Params.Input[0].AddSource(treeBar.Params.Output[0]); Solve(treeOverlay);
        Check(Published(treeOverlay).Length == 2 && Published(treeOverlay).Select(c => c.Values[0]).SequenceEqual(new double?[] { 1, 3 }), "Overlay reads both Bar branches by default.");
        var singleMetric = Add(new MetricComponent()); Text(singleMetric, 0, "10"); treeOverlay.Params.Input[0].AddSource(singleMetric.Params.Output[0]); Solve(treeOverlay);
        Check(Published(treeOverlay).Length == 3 && Published(treeOverlay).Count(c => c.Kind == CardKind.Bar) == 2, "Branched Bar beside Metric publishes all three cards.");
        var grafted = Add(new MetricComponent()); Text(grafted, 0, "7", "8", "9"); grafted.Params.Input[0].DataMapping = GH_DataMapping.Graft;
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(grafted.Params.Output[0]); Solve(treeOverlay);
        Check(Cards(grafted).Length == 3 && grafted.Params.Output[0].VolatileData.PathCount == 3 && Published(treeOverlay).Length == 3, "Grafted Metric list publishes one card per value.");
        treeOverlay.Params.Input[0].AddSource(treeBar.Params.Output[0]); treeOverlay.Params.Input[0].AddSource(singleMetric.Params.Output[0]);
        foreach (var mapping in Enum.GetValues<GH_DataMapping>())
        {
            treeOverlay.Params.Input[0].DataMapping = mapping; treeOverlay.Params.Input[0].ExpireSolution(false); Solve(treeOverlay);
            Check(Published(treeOverlay).Length == 6, "Mixed grafted Metrics, branched Bar and Metric survive Cards mapping " + mapping);
        }
        var linkInput = Add(new Param_GenericObject());
        var firstLink = new Link(new[] { "1", "2" }, new[] { ("1", (object)new Point3d(0, 0, 0), "first"), ("2", (object)new Point3d(1, 0, 0), "second") });
        var secondLink = new Link(new[] { "3" }, new[] { ("3", (object)new Point3d(2, 0, 0), "third") });
        linkInput.PersistentData.Append(new GH_ObjectWrapper(firstLink), new GH_Path(0)); linkInput.PersistentData.Append(new GH_ObjectWrapper(secondLink), new GH_Path(1));
        treeOverlay.Params.Input[1].AddSource(linkInput);
        foreach (var mapping in Enum.GetValues<GH_DataMapping>())
        {
            treeOverlay.Params.Input[1].DataMapping = mapping; treeOverlay.Params.Input[1].ExpireSolution(false); Solve(treeOverlay);
            var allLinks = PublishedLinks(treeOverlay);
            Check(allLinks.Length == 2 && allLinks[0].LinkedGeometryKeys.SequenceEqual(new[] { "1", "2" }) && allLinks[1].LinkedGeometryKeys.SequenceEqual(new[] { "3" }), "All Link branches and their geometry survive Links mapping " + mapping);
        }
        var across = Add(new Param_GenericObject());
        across.PersistentData.Append(new GH_ObjectWrapper(metrics[0]), new GH_Path(0)); across.PersistentData.Append(new GH_ObjectWrapper(metrics[1]), new GH_Path(1));
        across.PersistentData.Append(new GH_ObjectWrapper(5.5), new GH_Path(2));
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(across); treeOverlay.Params.Input[0].DataMapping = GH_DataMapping.None; Solve(treeOverlay);
        Check(Published(treeOverlay).Length == 2, "Foreign final branch does not erase valid earlier cards.");
        across.PersistentData.Append(null, new GH_Path(3)); across.ExpireSolution(false); Solve(treeOverlay);
        Check(Published(treeOverlay).Length == 2 && Has(treeOverlay, GH_RuntimeMessageLevel.Warning, "1 is empty"), "Actual-null final branch is counted without erasing valid cards.");
        across.PersistentData.Append(new GH_ObjectWrapper(metrics[0]), new GH_Path(4)); across.ExpireSolution(false);
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(across); treeOverlay.Params.Input[0].DataMapping = GH_DataMapping.None; Solve(treeOverlay);
        Check(Published(treeOverlay).Length == 2 && Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Cards item 4 is connected twice; connect each card once."), "Duplicates are detected across branches while valid siblings remain.");
        Check(Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Cards item 2 is a number, not a Conduit+ card."), "Friendly number wording uses zero-based item indices.");
        // Simulate the list access and mapping persisted by an older Overlay archive.
        treeOverlay.Params.Input[0].Access = treeOverlay.Params.Input[1].Access = GH_ParamAccess.list;
        var oldChunk = new GH_Archive().CreateTopLevelNode("OldOverlay"); treeOverlay.Write(oldChunk);
        var oldRestored = Add(new OverlayComponent()); oldRestored.Read((GH_IReader)oldChunk);
        oldRestored.Params.Input[0].RemoveAllSources(); oldRestored.Params.Input[1].RemoveAllSources();
        oldRestored.Params.Input[0].AddSource(across); oldRestored.Params.Input[1].AddSource(linkInput); Solve(oldRestored);
        Check(oldRestored.Params.Input[0].Access == GH_ParamAccess.tree && oldRestored.Params.Input[1].Access == GH_ParamAccess.tree && Published(oldRestored).Length == 2 && PublishedLinks(oldRestored).Length == 2, "Restored list-access Overlay upgrades to whole-tree reads.");
        treeOverlay.Params.Input[0].Access = treeOverlay.Params.Input[1].Access = GH_ParamAccess.tree;
        var broken = Add(new BarComponent()); Numbers(broken, 0, 1, 2); Text(broken, 2, "same", "same");
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(broken.Params.Output[0]); treeOverlay.Params.Input[0].AddSource(singleMetric.Params.Output[0]); Solve(treeOverlay);
        Check(Has(treeOverlay, GH_RuntimeMessageLevel.Warning, "1 is empty because its component has an error (Bar)."), "Skipped-card warning names an errored Bar and uses singular grammar.");
        var brokenLegend = Add(new LegendComponent());
        ((Param_Colour)brokenLegend.Params.Input[0]).PersistentData.Append(new GH_Colour(Color.Red));
        treeOverlay.Params.Input[0].AddSource(brokenLegend.Params.Output[0]); Solve(treeOverlay);
        Check(Has(treeOverlay, GH_RuntimeMessageLevel.Warning, "2 are empty because their components have errors (Bar, Legend)."), "Skipped-card warning names both errored components and uses plural grammar.");
        var foreign = Add(new Param_GenericObject()); foreign.PersistentData.AppendRange(new IGH_Goo[] { new GH_Integer(2), new GH_String("hello"), new GH_Point(new Point3d(0, 0, 0)) });
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(foreign); Solve(treeOverlay);
        Check(Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Cards item 0 is an integer") && Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Cards item 1 is text") && Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Cards item 2 is geometry"), "Foreign items use friendly integer, text and geometry names.");
        treeOverlay.Params.Input[1].RemoveAllSources(); treeOverlay.Params.Input[1].AddSource(singleMetric.Params.Output[0]); Solve(treeOverlay);
        Check(Has(treeOverlay, GH_RuntimeMessageLevel.Error, "Links item 0 is a card, not a Conduit+ Link."), "Cards in Links have a friendly type name.");
        treeOverlay.Params.Input[0].RemoveAllSources(); treeOverlay.Params.Input[0].AddSource(treeBar.Params.Output[0]);
        var badLink = Add(new LinkComponent()); ((Param_GenericObject)badLink.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper("bad geometry"));
        treeOverlay.Params.Input[1].RemoveAllSources(); treeOverlay.Params.Input[1].AddSource(badLink.Params.Output[0]); Solve(treeOverlay);
        Check(!Has(treeOverlay, GH_RuntimeMessageLevel.Warning, "Link keys don't match") && !Has(treeOverlay, GH_RuntimeMessageLevel.Warning, "Link: ."), "Errored Link produces no misleading key mismatch.");
        var scope = Add(new ScopeComponent()); var scoped = Add(new OverlayComponent()); var peer = Add(new CanvasCardComponent());
        var groupInput = Add(new Param_GenericObject());
        groupInput.PersistentData.Append(new GH_ObjectWrapper(metrics[0] with { Keys = ["G"], Groups = ImmutableDictionary<string, GroupDefinition>.Empty.Add("G", new(["a"])) }));
        var peerInput = Add(new Param_GenericObject()); peerInput.PersistentData.Append(new GH_ObjectWrapper(metrics[1] with { Keys = ["G"] }));
        scoped.Params.Input[0].AddSource(groupInput); scoped.Params.Input[3].AddSource(scope.Params.Output[0]);
        peer.Params.Input[0].AddSource(peerInput); peer.Params.Input[1].AddSource(scope.Params.Output[0]);
        var localLink = Add(new Param_GenericObject()); localLink.PersistentData.Append(new GH_ObjectWrapper(new Link(new[] { "b" }, new[] { ("b", (object)new Point3d(0, 0, 0), "local") })));
        scoped.Params.Input[1].AddSource(localLink); Solve(scoped);
        Check(!Has(scoped, GH_RuntimeMessageLevel.Warning, "Link keys don't match"), "Committed Scope peer collision suppresses mismatch warning.");
        peerInput.PersistentData.Clear(); peerInput.PersistentData.Append(new GH_ObjectWrapper(metrics[1] with { Keys = ["other"] })); peerInput.ExpireSolution(false); Solve(peer);
        Check(Has(scoped, GH_RuntimeMessageLevel.Warning, "Wire the same keys into Link Keys."), "Peer-only membership change refreshes scoped key diagnostics.");
        peerInput.PersistentData.Clear(); peerInput.PersistentData.Append(new GH_ObjectWrapper(metrics[1] with { Keys = ["G"] })); peerInput.ExpireSolution(false); Solve(peer);
        Check(!Has(scoped, GH_RuntimeMessageLevel.Warning, "Link keys don't match"), "Peer-only collision removes stale key diagnostics.");
        Console.WriteLine($"PR A input trap checks passed: {count} assertions.");
    }
}
