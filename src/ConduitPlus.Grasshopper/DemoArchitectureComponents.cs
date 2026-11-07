using System.Drawing;
using System.Globalization;
using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ConduitPlus.Grasshopper;

public abstract class DemoArchitectureComponent(string name, string nickname, string description) : GH_Component(name, nickname, description, "Conduit+", "Examples")
{
    private (Brep Geometry, int Color)[] _preview = [];
    private readonly global::Rhino.Display.DisplayMaterial[] _materials = DemoArchitectureRows.TypeColors.Select(c => new global::Rhino.Display.DisplayMaterial(c, .12)).ToArray();
    protected void Preview(Brep[] geometry, IEnumerable<int> colors) => _preview = geometry.Zip(colors, (g, c) => (g, c)).ToArray();
    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
        foreach (var (geometry, color) in _preview) args.Display.DrawBrepShaded(geometry, _materials[color]);
    }
}

public sealed class DemoRoomsComponent : DemoArchitectureComponent
{
    private Bitmap? _icon;
    public DemoRoomsComponent() : base("Demo Rooms", "Rooms", "Schematic rooms in metres; fictitious area minima, not building regulations. GIA omits wall thickness.") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae210");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("R");
    protected override void RegisterInputParams(GH_InputParamManager p) => p.AddNumberParameter("Plan scale", "S", "Scale XY geometry (0.5–2); area requirements stay fixed", GH_ParamAccess.item, 1);
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBrepParameter("Rooms", "G", "Planar room faces in metres", GH_ParamAccess.list);
        p.AddTextParameter("Keys", "K", "Stable room keys", GH_ParamAccess.list);
        p.AddTextParameter("Names", "N", "Room names", GH_ParamAccess.list);
        p.AddNumberParameter("Area delta", "D", "Area minus required minimum, m²", GH_ParamAccess.list);
        p.AddTextParameter("Rows", "R", "Name, Type, Area m², Required m², Delta m², Status", GH_ParamAccess.tree);
        p.AddNumberParameter("GIA", "GIA", "Schematic gross internal area, including circulation, omitting walls, m²", GH_ParamAccess.item);
        p.AddNumberParameter("Net to gross", "NG", "Non-circulation area / GIA, percent", GH_ParamAccess.item);
        p.AddNumberParameter("Circulation", "C", "Circulation / GIA, percent", GH_ParamAccess.item);
        p.AddIntegerParameter("Failing rooms", "F", "Count below illustrative minimum", GH_ParamAccess.item);
        p.AddColourParameter("Status colors", "SC", "Bad for negative deltas, Good otherwise", GH_ParamAccess.list);
        p.AddTextParameter("Types", "T", "Distinct room types for Legend", GH_ParamAccess.list);
        p.AddColourParameter("Type colors", "TC", "Room type colors for Legend and preview", GH_ParamAccess.list);
        p.AddNumberParameter("Areas", "A", "Area per room, m²", GH_ParamAccess.list);
        p.AddNumberParameter("Required areas", "RA", "Illustrative minimum per room type, m²", GH_ParamAccess.list);
        p.AddTextParameter("Room types", "RT", "Type per room", GH_ParamAccess.list);
        p.AddTextParameter("Type keys", "TK", "Stable group keys in Types order", GH_ParamAccess.list);
        p.AddTextParameter("Type members", "TM", "One member-key branch per room type", GH_ParamAccess.tree);
        p.AddTextParameter("Failing members", "FM", "Exactly one branch, including an empty branch when all pass", GH_ParamAccess.tree);
    }
    protected override void BeforeSolveInstance() => Preview([], []);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        double scale = 1; if (!da.GetData(0, ref scale)) return;
        try
        {
            var r = DemoRooms.Create(scale);
            var geometry = r.Rooms.Select(room => Brep.CreatePlanarBreps(new Rectangle3d(Plane.WorldXY, new Interval(room.X, room.X + room.Width), new Interval(room.Y, room.Y + room.Depth)).ToNurbsCurve(), 1e-6).Single()).ToArray();
            var types = r.Rooms.Select(room => room.Type).Distinct().ToArray();
            Preview(geometry, r.Rooms.Select(room => Array.IndexOf(types, room.Type)));
            da.SetDataList(0, geometry);
            da.SetDataList(1, r.Rooms.Select(room => room.Key)); da.SetDataList(2, r.Rooms.Select(room => room.Name));
            da.SetDataList(3, r.Rooms.Select(room => room.Delta));
            da.SetDataTree(4, DemoArchitectureRows.Create(r.Rooms.Select(room => new[] { room.Name, room.Type, F(room.Area), F(room.Required), F(room.Delta), room.Pass ? "Pass" : "FAIL" })));
            da.SetData(5, r.GrossArea); da.SetData(6, r.NetToGrossPercent); da.SetData(7, r.CirculationPercent); da.SetData(8, r.Failing);
            da.SetDataList(9, r.Rooms.Select(room => ColorTranslator.FromHtml(room.Pass ? Theme.Paper.Good : Theme.Paper.Bad)));
            da.SetDataList(10, r.Rooms.Select(room => room.Type).Distinct());
            da.SetDataList(11, DemoArchitectureRows.TypeColors);
            da.SetDataList(12, r.Rooms.Select(room => room.Area)); da.SetDataList(13, r.Rooms.Select(room => room.Required)); da.SetDataList(14, r.Rooms.Select(room => room.Type));
            da.SetDataList(15, types.Select(type => "type:" + type.ToLowerInvariant()));
            da.SetDataTree(16, DemoArchitectureRows.Create(types.Select(type => r.Rooms.Where(room => room.Type == type).Select(room => room.Key).ToArray())));
            da.SetDataTree(17, DemoArchitectureRows.Create([r.Rooms.Where(room => !room.Pass).Select(room => room.Key).ToArray()]));
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    private static string F(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed class DemoMassingComponent : DemoArchitectureComponent
{
    private Bitmap? _icon;
    public DemoMassingComponent() : base("Demo Massing", "Massing", "Schematic zoning study in metres; fictitious planning limits and whole-floorplate GFA.") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae211");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("M");
    protected override void RegisterInputParams(GH_InputParamManager p) => p.AddIntegerParameter("Extra floors", "F", "Add floors to every block (-1–5)", GH_ParamAccess.item, 0);
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBrepParameter("Blocks", "B", "Building volumes in metres", GH_ParamAccess.list);
        p.AddTextParameter("Keys", "K", "Stable block keys", GH_ParamAccess.list);
        p.AddNumberParameter("Height ratios", "H", "Height / 30 m limit; >1 fails", GH_ParamAccess.list);
        p.AddTextParameter("Rows", "R", "Block, Use, Floors, Height m, Limit m, GFA m²", GH_ParamAccess.tree);
        p.AddNumberParameter("GFA", "GFA", "Total gross floor area, m²", GH_ParamAccess.item);
        p.AddNumberParameter("Allowed GFA", "A", "Site area × allowed FAR, m²", GH_ParamAccess.item);
        p.AddNumberParameter("FAR", "FAR", "GFA / 8000 m² site; allowed FAR 2.0", GH_ParamAccess.item);
        p.AddTextParameter("Uses", "U", "Distinct uses", GH_ParamAccess.list);
        p.AddNumberParameter("GFA by use", "GU", "Whole floorplate GFA by use, m²", GH_ParamAccess.list);
        p.AddColourParameter("Use colors", "UC", "Legend colors", GH_ParamAccess.list);
        p.AddCurveParameter("Site", "S", "100 × 80 m site boundary; unlinked", GH_ParamAccess.item);
        p.AddIntegerParameter("Height failures", "HF", "Blocks exceeding 30 m", GH_ParamAccess.item);
        p.AddIntegerParameter("Floors", "F", "Floor count per block", GH_ParamAccess.list);
        p.AddNumberParameter("Heights", "HM", "Height per block, m", GH_ParamAccess.list);
        p.AddNumberParameter("Height limits", "HL", "Illustrative height limit per block, m", GH_ParamAccess.list);
        p.AddNumberParameter("Block GFA", "BG", "GFA per block, m²", GH_ParamAccess.list);
        p.AddTextParameter("Block uses", "BU", "Use per block", GH_ParamAccess.list);
        p.AddTextParameter("Use keys", "UK", "Stable group keys in Uses order", GH_ParamAccess.list);
        p.AddTextParameter("Use members", "UM", "One member-key branch per use", GH_ParamAccess.tree);
        p.AddTextParameter("Height failure members", "HF", "Exactly one branch of blocks above the height limit", GH_ParamAccess.tree);
    }
    protected override void BeforeSolveInstance() => Preview([], []);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        int extra = 0; if (!da.GetData(0, ref extra)) return;
        try
        {
            var r = DemoMassing.Create(extra);
            var geometry = r.Blocks.Select(b => new BoundingBox(new Point3d(b.X, b.Y, 0), new Point3d(b.X + b.Width, b.Y + b.Depth, b.Height)).ToBrep()).ToArray();
            Preview(geometry, r.Blocks.Select(b => Array.IndexOf(r.Uses, b.Use)));
            da.SetDataList(0, geometry);
            da.SetDataList(1, r.Blocks.Select(b => b.Key)); da.SetDataList(2, r.Blocks.Select(b => b.HeightRatio));
            da.SetDataTree(3, DemoArchitectureRows.Create(r.Blocks.Select(b => new[] { b.Key, b.Use, b.Floors.ToString(CultureInfo.InvariantCulture), F(b.Height), F(b.HeightLimit), F(b.Gfa) })));
            da.SetData(4, r.TotalGfa); da.SetData(5, r.AllowedGfa); da.SetData(6, r.Far);
            da.SetDataList(7, r.Uses); da.SetDataList(8, r.GfaByUse); da.SetDataList(9, DemoArchitectureRows.TypeColors.Take(3));
            da.SetData(10, new Rectangle3d(Plane.WorldXY, r.SiteWidth, r.SiteDepth).ToNurbsCurve()); da.SetData(11, r.HeightFailures);
            da.SetDataList(12, r.Blocks.Select(b => b.Floors)); da.SetDataList(13, r.Blocks.Select(b => b.Height)); da.SetDataList(14, r.Blocks.Select(b => b.HeightLimit));
            da.SetDataList(15, r.Blocks.Select(b => b.Gfa)); da.SetDataList(16, r.Blocks.Select(b => b.Use));
            da.SetDataList(17, r.Uses.Select(use => "use:" + use.ToLowerInvariant()));
            da.SetDataTree(18, DemoArchitectureRows.Create(r.Uses.Select(use => r.Blocks.Where(b => b.Use == use).Select(b => b.Key).ToArray())));
            da.SetDataTree(19, DemoArchitectureRows.Create([r.Blocks.Where(b => b.Height > b.HeightLimit).Select(b => b.Key).ToArray()]));
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    private static string F(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}

internal static class DemoArchitectureRows
{
    internal static Color[] TypeColors => [Color.FromArgb(99, 138, 210), Color.FromArgb(228, 169, 88), Color.FromArgb(115, 169, 117), Color.FromArgb(104, 183, 190), Color.FromArgb(177, 139, 195), Color.FromArgb(170, 170, 170)];
    internal static GH_Structure<GH_String> Create(IEnumerable<string[]> rows)
    {
        var tree = new GH_Structure<GH_String>(); int i = 0;
        foreach (var row in rows)
        {
            var path = new GH_Path(i++); tree.EnsurePath(path);
            tree.AppendRange(row.Select(s => new GH_String(s)), path);
        }
        return tree;
    }
}
