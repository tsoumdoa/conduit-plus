using System.Drawing;
using ConduitPlus.Core;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace ConduitPlus.Grasshopper;

/// <summary>A runtime token, deliberately without text/Guid conversions or persistence.</summary>
public sealed class ScopeGoo : GH_Goo<ScopeToken>
{
    public ScopeGoo() { }
    public ScopeGoo(ScopeToken token, string name = "") { Value = token; Name = name; }
    public string Name { get; private set; } = "";
    public override bool IsValid => Value is not null;
    public override string TypeName => "Conduit+ Scope";
    public override string TypeDescription => "Explicit, transient selection scope in one Grasshopper document";
    public override IGH_Goo Duplicate() => Value is null ? new ScopeGoo() : new ScopeGoo(Value, Name);
    public override string ToString() => Value is null ? "Invalid Scope" : string.IsNullOrWhiteSpace(Name) ? "Scope" : $"Scope: {Name}";
    public override bool CastFrom(object source) => false;
    public override bool CastTo<Q>(ref Q target) => false;
    public override bool Write(GH_IWriter writer) => true;
    public override bool Read(GH_IReader reader) { Value = null!; Name = ""; return true; }
}

/// <summary>Nonpersistent typed parameter; the Scope component/wires regenerate the token on open.</summary>
public sealed class ScopeParam() : GH_Param<ScopeGoo>("Scope", "S", "Conduit+ selection scope", "Conduit+", "Display", GH_ParamAccess.item)
{
    public override Guid ComponentGuid => new("d26bc4e7-6425-4706-9ac7-03b436ebcf91");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}

public sealed class ScopeComponent() : GH_Component("Scope", "Scope", "Share member selection and hover between explicitly connected displays", "Conduit+", "Display")
{
    private ScopeBroker? _broker;
    private Bitmap? _icon;
    public override Guid ComponentGuid => new("41140e0a-5669-460b-a6a9-59e583ecbaca");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("S");
    protected override void RegisterInputParams(GH_InputParamManager p)
    { p.AddTextParameter("Name", "N", "Optional display label; never establishes sharing", GH_ParamAccess.item); p[0].Optional = true; }
    protected override void RegisterOutputParams(GH_OutputParamManager p) =>
        p.AddParameter(new ScopeParam(), "Scope", "S", "Explicit scope token for this document instance", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var document = OnPingDocument();
        if (document is null) return;
        _broker = ScopeBroker.ForDocument(document);
        var name = ""; da.GetData(0, ref name);
        da.SetData(0, new ScopeGoo(_broker.DefineScope(InstanceGuid), name));
    }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        _broker = ScopeBroker.ForDocument(document);
        _broker.DefineScope(InstanceGuid);
    }
    public override void RemovedFromDocument(GH_Document document)
    { _broker?.InvalidateScope(InstanceGuid); _broker = null; base.RemovedFromDocument(document); }
}
