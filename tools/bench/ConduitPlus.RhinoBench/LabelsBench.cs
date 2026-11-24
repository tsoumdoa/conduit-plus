using System.Reflection;
using ConduitPlus.Grasshopper;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ConduitPlus.Rhino;

internal static class LabelsBench
{
    internal static void Run(RhinoDoc doc, RhinoView view, Action pump)
    {
        CheckAnchors();
        var server = new GH_ComponentServer();
        typeof(Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, server);
        var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(Assembly), typeof(string), typeof(GH_LoadingMechanism)], null)!;
        foreach (var assembly in new[] { typeof(Instances).Assembly, typeof(OverlayComponent).Assembly })
            parse.Invoke(server, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
        var components = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rhino 8", "Plug-ins", "Grasshopper", "Components");
        foreach (var name in new[] { "SurfaceComponents", "XformComponents", "MathComponents", "VectorComponents" })
        {
            var assembly = Assembly.LoadFrom(Path.Combine(components, name + ".gha"));
            parse.Invoke(server, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
        }
        GH_Document.EnableSolutions = true;

        using var gh = new GH_Document { Enabled = true };
        var points = new Param_Point();
        points.PersistentData.Append(new GH_Point(view.MainViewport.CameraTarget));
        var link = new LinkComponent();
        var labels = new LabelsComponent();
        var overlay = new OverlayComponent();
        foreach (var obj in new IGH_DocumentObject[] { points, link, labels, overlay }) gh.AddObject(obj, false);
        link.Params.Input[0].AddSource(points);
        labels.Params.Input[0].AddSource(link.Params.Output[0]);
        ((Param_String)labels.Params.Input[2]).PersistentData.Append(new GH_String("Explicit caption"));
        overlay.Params.Input[1].AddSource(link.Params.Output[0]);
        Check(!overlay.ShowLinkKeys, "Show Link keys starts off");
        overlay.ToggleLinkKeys();
        Check(overlay.ShowLinkKeys, "Show Link keys toggles on");
        gh.UndoServer.PerformUndo();
        overlay = gh.Objects.OfType<OverlayComponent>().Single();
        Check(!overlay.ShowLinkKeys, "Show Link keys is undoable");
        gh.UndoServer.PerformRedo();
        overlay = gh.Objects.OfType<OverlayComponent>().Single();
        Check(overlay.ShowLinkKeys, "Show Link keys is redoable");
        gh.NewSolution(false);
        var session = OverlayManager.Get(doc, view, overlay.InstanceGuid);
        Check(session.Cards.Length == 0 && session.InspectLabels().Single().Anchor.Style.Text == "1", "Labels-only automatic publication");

        overlay.Params.Input[6].AddSource(link.Params.Output[0]);
        overlay.Params.Input[6].AddSource(labels.Params.Output[0]);
        overlay.ExpireSolution(false);
        gh.NewSolution(false);
        var second = new OverlayComponent();
        gh.AddObject(second, false);
        second.Params.Input[6].AddSource(link.Params.Output[0]);
        gh.NewSolution(false);
        var otherSession = OverlayManager.Get(doc, view, second.InstanceGuid);
        var scene = session.LabelScene ?? throw new InvalidOperationException("Missing label scene");
        Check(otherSession.Cards.Length == 0 && ReferenceEquals(scene, otherSession.LabelScene), "Labels-only owners share a viewport scene");

        for (var i = 0; i < 100; i++)
        {
            pump();
            view.Redraw();
            if (session.InspectLabels().Single().Answer.Current && otherSession.InspectLabels().Single().Answer.Current)
                break;
            Thread.Sleep(10);
        }
        var resolved = session.InspectLabels().Single();
        Check(resolved.Anchor.Style.Text == "Explicit caption" && resolved.Answer is { Current: true, Visible: true }, "Explicit supersession and classified anchor");

        Check(otherSession.InspectLabels().Single().Answer is { Current: true, Visible: true } &&
            scene.HarvestCount == 1, "One harvest and combined classification through the viewport conduit");
        gh.RemoveObject(second, false);
        Check(otherSession.LabelScene is null && ReferenceEquals(session.LabelScene, scene) &&
            !scene.IsDisposed, "Removing one owner keeps the shared scene alive");

        var saved = new GH_Archive();
        Check(saved.AppendObject(gh, "Definition"), "Save Labels definition");
        using var reopened = new GH_Document();
        Check(saved.ExtractObject(reopened, "Definition"), "Reopen Labels definition");
        var restored = reopened.Objects.OfType<OverlayComponent>().Single();
        Check(restored.ShowLinkKeys && restored.Params.Input[6].SourceCount == 2, "Restore toggle and Labels wires");

        overlay.Params.Input[6].RemoveAllSources();
        overlay.ToggleLinkKeys();
        overlay.ExpireSolution(false);
        gh.NewSolution(false);
        Check(session.InspectLabels().Length == 0 && session.LabelScene is null && scene.IsDisposed &&
            OverlayManager.Binding(overlay.InstanceGuid) is null,
            "Publication without labels releases the last owner and disposes the viewport scene");

        var pointLabels = new LabelsComponent();
        gh.AddObject(pointLabels, false);
        var explicitPoints = (Param_Point)pointLabels.Params.Input[1];
        explicitPoints.PersistentData.Append(new GH_Point(new Point3d(1, 2, 3)), new global::Grasshopper.Kernel.Data.GH_Path(3));
        explicitPoints.PersistentData.Append(new GH_Point(new Point3d(4, 5, 6)), new global::Grasshopper.Kernel.Data.GH_Path(3));
        ((Param_String)pointLabels.Params.Input[2]).PersistentData.Append(new GH_String("Point caption"));
        ((Param_Number)pointLabels.Params.Input[6]).PersistentData.Append(new GH_Number(4));
        gh.NewSolution(false);
        var explicitSpec = (LabelSpec)pointLabels.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Check(explicitSpec.Anchors.Length == 2 && explicitSpec.Anchors.All(a =>
            a.Style is { Key: null, Text: "Point caption", Priority: 4 }), "Explicit points broadcast text and priority");
        Check(explicitSpec.Anchors.Select(a => a.Identity(overlay.InstanceGuid)).Distinct().Count() == 2, "Explicit points retain distinct identities");

        var frozen = new GH_Archive();
        using var legacy = new GH_Document();
        Check(frozen.ReadFromFile(Path.Combine("examples", "hello.gh")) && frozen.ExtractObject(legacy, "Definition"), "Open pre-label fixture");
        var old = legacy.Objects.OfType<OverlayComponent>().Single();
        Check(!old.ShowLinkKeys && old.Params.Input.Count == 7 && old.Params.Input[6].Optional, "Append optional Labels when reopening");
        // Ordered Source entries from the frozen archive's param_input chunks.
        Guid[][] wires =
        [
            [new("aafb7038-ca42-4e52-99ec-37f2d0291665")],
            [new("aca4dea7-6a40-4468-b57f-775271abc82e")],
            [],
            [new("3ea64dc0-fc4f-47e7-9332-a64b77dbfa48")],
            [],
            []
        ];
        Check(MatchesFrozenWires(old), "First read preserves all six frozen input source lists");
        var roundTrip = new GH_Archive();
        using var legacyReopened = new GH_Document();
        Check(roundTrip.AppendObject(legacy, "Definition") && roundTrip.ExtractObject(legacyReopened, "Definition"), "Round-trip pre-label fixture");
        Check(MatchesFrozenWires(legacyReopened.Objects.OfType<OverlayComponent>().Single()),
            "Resave preserves all six frozen input source lists");

        bool MatchesFrozenWires(OverlayComponent component) => component.Params.Input.Take(6)
            .Select((p, i) => p.Sources.Select(s => s.InstanceGuid).SequenceEqual(wires[i])).All(same => same);
    }

    private static void CheckAnchors()
    {
        using var outer = new Circle(10).ToNurbsCurve();
        using var inner = new Circle(3).ToNurbsCurve();
        var planar = Brep.CreatePlanarBreps(new[] { outer, inner }, 0.001);
        using var holed = planar.Single();
        var holeAnchor = Link.FindAnchor(holed, "holed face").Point;
        Check(holeAnchor.IsValid && Math.Abs(holeAnchor.DistanceTo(Point3d.Origin) - 3) < 1e-5, "Holed trimmed face projects to inner rim");
        using var box = new BoundingBox(new Point3d(-2, -3, -4), new Point3d(2, 3, 4)).ToBrep();
        var solid = Link.FindAnchor(box, "closed box").Point;
        Check(solid.DistanceTo(new Point3d(2, 0, 0)) < 1e-5, "Closed solid projects to the first nearest face (+X)");
        using var mesh = new Mesh();
        // Equal parallel triangles put the centroid midway between the two faces.
        foreach (var z in new[] { 2, -2 })
        {
            var first = mesh.Vertices.Count;
            mesh.Vertices.Add(0, 0, z);
            mesh.Vertices.Add(6, 0, z);
            mesh.Vertices.Add(0, 3, z);
            mesh.Faces.AddFace(first, first + 1, first + 2);
        }
        Check(Link.FindAnchor(mesh, "parallel triangles").Point.DistanceTo(new Point3d(2, 1, 2)) < 1e-5,
            "Off-surface mesh centroid projects to the first nearest triangle");
        var link = new Link(["empty", "multi"], [("multi", (object)new Point3d(4, 3, 2)), ("multi", new Point3d(9, 9, 9))]);
        var spec = LabelSpec.FromLink(link, default);
        Check(spec.Anchors.Single().Point == new Point3d(4, 3, 2) && spec.Remarks.Single().Contains("1 Link"), "Multipart first anchor and empty-key diagnostic");
        using var degenerate = new LineCurve(Point3d.Origin, Point3d.Origin);
        var fallback = Link.FindAnchor(degenerate, "collapsed curve");
        Check(fallback.Point == Point3d.Origin && fallback.Diagnostic is not null, "Degenerate curve falls back to endpoint");
        using var bent = new PolylineCurve(new[] { Point3d.Origin, new Point3d(2, 0, 0), new Point3d(2, 8, 0) });
        Check(Link.FindAnchor(bent, "bent curve").Point.DistanceTo(new Point3d(2, 3, 0)) < 1e-6, "Curve uses half arc length");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
