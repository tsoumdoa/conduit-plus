using System.Windows.Forms;
using ConduitPlus.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace ConduitPlus.Grasshopper;

public sealed class Param_TextOption : Param_String
{
    public override Guid ComponentGuid => new("35c52238-41f5-4e49-b891-109d41ab06a2");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    public Func<IReadOnlyList<InputOption>>? OptionsProvider { get; init; }
    public IReadOnlyList<InputOption> Options { get; init; } = InputOptions.Slots;
    /// <summary>True when a wire or a right-click/persistent value supplies this input.</summary>
    public bool HasValue => SourceCount > 0 || PersistentData.DataCount > 0;
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        var current = PersistentData.DataCount == 1 ? PersistentData.AllData(true).OfType<GH_String>().FirstOrDefault()?.Value : null;
        foreach (var option in OptionsProvider?.Invoke() ?? Options)
            Menu_AppendItem(menu, option.Label, (_, _) => ApplyOption(option), SourceCount == 0,
                SourceCount == 0 && PersistentData.DataCount <= 1 && option.Matches(current));
    }
    public void ApplyOption(InputOption option)
    {
        if (SourceCount != 0 || (OptionsProvider is null && !Options.Contains(option))) return;
        RecordPersistentDataEvent("Set: " + option.Label);
        PersistentData.Clear();
        if (option.Value is not null) PersistentData.Append(new GH_String(option.Value));
        ExpireSolution(true);
    }
}
