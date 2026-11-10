using System.Windows.Forms;
using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace ConduitPlus.Grasshopper;

public sealed class Param_SizeOption : Param_Number
{
    public override Guid ComponentGuid => new("f25b40d5-9fef-4966-9b27-8c26499dd01f");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    /// <summary>True when a wire or a right-click/persistent value supplies this input.</summary>
    public bool HasValue => SourceCount > 0 || PersistentData.DataCount > 0;
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        var current = PersistentData.DataCount == 1 ? PersistentData.AllData(true).OfType<GH_Number>().FirstOrDefault()?.Value : null;
        foreach (var option in new[] { 0.75, 1, 1.25, 1.5 })
            Menu_AppendItem(menu, option.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×", (_, _) => ApplyOption(option), SourceCount == 0,
                SourceCount == 0 && PersistentData.DataCount <= 1 && option == current);
    }
    public void ApplyOption(double option)
    {
        if (SourceCount != 0 || !new[] { 0.75, 1, 1.25, 1.5 }.Contains(option)) return;
        RecordPersistentDataEvent("Set: " + option.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×");
        PersistentData.Clear();
        PersistentData.Append(new GH_Number(option));
        ExpireSolution(true);
    }
}