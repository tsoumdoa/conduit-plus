using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace ConduitPlus.Grasshopper;

public sealed class LabelsComponent() : GH_Component("Labels", "Labels", "Describe viewport labels for an Overlay", "Conduit+", "Display")
{
    private Bitmap? _icon;
    public override Guid ComponentGuid => new("3ab043c9-1e34-4181-9296-44fea8130e44");
    protected override Bitmap Icon => _icon ??= ConduitIcons.Create(ComponentIcon.Labels);

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Link", "L", "One Link per branch to label by member key", GH_ParamAccess.list);
        p.AddPointParameter("Points", "P", "Explicit anchor points", GH_ParamAccess.list);
        p.AddTextParameter("Text", "T", "Full text, defaults to Link keys", GH_ParamAccess.list);
        p.AddTextParameter("Keys", "K", "One optional member key per explicit point", GH_ParamAccess.list);
        p.AddTextParameter("Short text", "S", "Optional compact label text", GH_ParamAccess.list);
        p.AddColourParameter("Colours", "C", "Optional label swatches", GH_ParamAccess.list);
        p.AddNumberParameter("Priority", "R", "Optional priority, higher values rank first", GH_ParamAccess.list);
        for (var i = 0; i < 7; i++) p[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p) =>
        p.AddGenericParameter("Labels", "L", "Connect to Overlay Labels", GH_ParamAccess.item);

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var source = new LabelSource(InstanceGuid, da.ParameterTargetPath(0).ToString());
        try
        {
            var links = new List<IGH_Goo>();
            da.GetDataList(0, links);
            if (links.Count > 1) throw new ArgumentException($"Path {source.Path}: Supply at most one Link per branch.");
            var link = links.Count == 0 ? null : (links[0]?.ScriptVariable() as Link ??
                throw new ArgumentException($"Path {source.Path}: Link [0] must be a Conduit+ Link."));

            var points = Read<GH_Point>(1, "Points");
            for (var i = 0; i < points.Count; i++)
                if (!points[i].Value.IsValid) throw new ArgumentException($"Path {source.Path}: Points [{i}] must be finite.");
            var styles = LabelAlignment.Align(source.Path, link?.Keys, points.Count,
                Read<GH_String>(2, "Text").Select(x => x.Value).ToArray(),
                Read<GH_String>(3, "Keys").Select(x => x.Value).ToArray(),
                Read<GH_String>(4, "Short text").Select(x => x.Value).ToArray(),
                Read<GH_Colour>(5, "Colours").Select(x => x.Value.ToArgb()).ToArray(),
                Read<GH_Number>(6, "Priority").Select(x => x.Value).ToArray());
            var spec = link is not null ? LabelSpec.FromLink(link, source, styles) :
                new LabelSpec(source, null, points.Select((p, i) => new LabelAnchor(source, null, i, p.Value, styles[i])).ToArray(), []);
            foreach (var remark in spec.Remarks) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Path {source.Path}: {remark}");
            da.SetData(0, new GH_ObjectWrapper(spec));
        }
        catch (ArgumentException ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }

        List<T> Read<T>(int index, string name) where T : class, IGH_Goo
        {
            var values = new List<IGH_Goo>();
            da.GetDataList(index, values);
            var typed = new List<T>(values.Count);
            for (var i = 0; i < values.Count; i++)
                typed.Add(values[i] as T ?? throw new ArgumentException($"Path {source.Path}: {name} [{i}] is invalid."));
            return typed;
        }
    }
}
