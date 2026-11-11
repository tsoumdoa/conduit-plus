using System.Drawing;
using System.Globalization;
using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ConduitPlus.Grasshopper;

public sealed class DemoTrussComponent : GH_Component
{
    private Bitmap? _icon;
    public DemoTrussComponent() : base("Demo Truss", "Truss", "Illustrative truss dashboard data only — NOT structural analysis or design verification. Fixed fictitious capacities and stiffness.", "Conduit+", "Examples") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae209");
    protected override Bitmap Icon => _icon ??= ConduitIcons.Create(ComponentIcon.Truss);
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Span", "S", "Span in metres (0–1000, exclusive zero)", GH_ParamAccess.item, 24);
        p.AddIntegerParameter("Bays", "B", "Number of bays (2–40)", GH_ParamAccess.item, 8);
        p.AddNumberParameter("Depth", "D", "Truss depth in metres (0–100, exclusive zero)", GH_ParamAccess.item, 3);
        p.AddNumberParameter("Load", "W", "Illustrative uniform load in kN/m (0–10000)", GH_ParamAccess.item, 12);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddLineParameter("Members", "M", "Truss lines in metres", GH_ParamAccess.list);
        p.AddTextParameter("Keys", "K", "Stable member keys M01…", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "Member labels", GH_ParamAccess.list);
        p.AddNumberParameter("Utilisation", "U", "Illustrative ratios, not analysis", GH_ParamAccess.list);
        p.AddNumberParameter("Deflection", "D", "Illustrative downward deflection at panel points, mm", GH_ParamAccess.list);
        p.AddNumberParameter("Mass", "T", "Illustrative mass, tonnes", GH_ParamAccess.item);
        p.AddTextParameter("Rows", "R", "One branch per member: Member, Type, Util., Length (m)", GH_ParamAccess.tree);
        p.AddNumberParameter("Max utilisation", "MU", "Maximum illustrative ratio", GH_ParamAccess.item);
        p.AddNumberParameter("Max deflection", "MD", "Maximum sampled deflection, mm", GH_ParamAccess.item);
        p.AddTextParameter("Members passing", "P", "Passing / total at ratio <= 1", GH_ParamAccess.item);
        p.AddTextParameter("Stations", "X", "Panel point positions along span, m", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        double span = 24, depth = 3, load = 12; int bays = 8;
        if (!da.GetData(0, ref span) || !da.GetData(1, ref bays) || !da.GetData(2, ref depth) || !da.GetData(3, ref load)) return;
        try
        {
            var result = DemoTruss.Create(span, bays, depth, load); var members = result.Members;
            da.SetDataList(0, members.Select(m => new Line(new Point3d(m.Start.X, 0, m.Start.Y), new Point3d(m.End.X, 0, m.End.Y))));
            da.SetDataList(1, members.Select(m => m.Key)); da.SetDataList(2, members.Select(m => m.Label));
            da.SetDataList(3, members.Select(m => m.Utilisation)); da.SetDataList(4, result.Deflection); da.SetData(5, Math.Round(result.Mass, 1));
            var rows = new GH_Structure<GH_String>();
            for (var i = 0; i < members.Length; i++)
            {
                var m = members[i];
                rows.AppendRange(new[] { m.Key, m.Type, m.Utilisation.ToString("0.00", CultureInfo.InvariantCulture), m.Length.ToString("0.00", CultureInfo.InvariantCulture) }.Select(s => new GH_String(s)), new GH_Path(i));
            }
            da.SetDataTree(6, rows); // Rounded for display: Metric cards show the text as given.
            da.SetData(7, Math.Round(result.MaxUtilisation, 2)); da.SetData(8, Math.Round(result.MaxDeflection, 1));
            da.SetData(9, $"{result.Passing} / {members.Length}"); da.SetDataList(10, result.Positions.Select(x => x.ToString("0.#", CultureInfo.InvariantCulture)));
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}
