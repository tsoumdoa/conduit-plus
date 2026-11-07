using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace ConduitPlus.Grasshopper;

internal static class ScopeInput
{
    internal static ScopeToken? Read(GH_Component owner, IGH_DataAccess da, int index, out bool valid)
    {
        valid = true;
        if (owner.Params.Input[index].SourceCount == 0) return null;
        if (da.GetDataTree(index, out GH_Structure<IGH_Goo> tree) && tree.DataCount == 1 &&
            tree.AllData(true).SingleOrDefault() is ScopeGoo { Value: { } token } &&
            owner.OnPingDocument() is { } document && ScopeBroker.ForDocument(document).IsValid(token)) return token;
        valid = false;
        owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
            "Scope requires exactly one live Conduit+ Scope token from this Grasshopper document across the entire tree.");
        return null;
    }
}
