using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using ConduitPlus.Core;
using System.Windows.Forms;

namespace ConduitPlus.Grasshopper;

public sealed class DistributionComponent() : CardComponent("Distribution", "Distribution", "Show values as stacked dots or a histogram")
{
    private DistributionMode _mode;
    private bool _quartiles = true;
    private bool _thresholdLines = true;
    public override Guid ComponentGuid => new("937ae19c-69c5-4f44-a3c7-28f1548558c1");
    protected override ComponentIcon IconKind => ComponentIcon.Distribution;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Values", "V", "Observations", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "One per value; defaults to 1…n", GH_ParamAccess.list);
        p.AddTextParameter("Keys", "K", "Unique member keys; defaults to labels", GH_ParamAccess.list);
        p.AddTextParameter("Title", "T", "Card title", GH_ParamAccess.item, "Distribution");
        p.AddTextParameter("Unit", "U", "Value unit", GH_ParamAccess.item, "");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position", GH_ParamAccess.item);
        p.AddGenericParameter("Scale", "Sc", "Legend output from Colour Scale", GH_ParamAccess.item);
        p.AddColourParameter("Colors", "C", "One colour per value; overrides Scale fills", GH_ParamAccess.list);
        p.AddNumberParameter("Thresholds", "Th", "Threshold values", GH_ParamAccess.list);
        p.AddColourParameter("Threshold colours", "TC", "One colour or one per threshold", GH_ParamAccess.list);
        p.AddParameter(new Param_TextOption { Options = [new("Above", "Above"), new("Below", "Below")] },
            "Threshold sides", "TS", "Above (≥) or Below (≤)", GH_ParamAccess.list);
        p.AddParameter(new Param_TextOption { Options = [new("Dots", "Dots"), new("Histogram", "Histogram")] },
            "Show as", "As", "Dots or Histogram", GH_ParamAccess.item);
        p.AddNumberParameter("Bin width", "W", "Positive width; unset uses Auto", GH_ParamAccess.item);
        for (var i = 0; i < 13; i++) p[i].Optional = true;
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var values = new List<GH_Number>(); var labels = new List<string>(); var keys = new List<string>();
        var colors = new List<GH_Colour>(); var thresholds = new List<GH_Number>();
        var thresholdColors = new List<GH_Colour>(); var sides = new List<string>();
        da.GetDataList(0, values); da.GetDataList(1, labels); da.GetDataList(2, keys);
        da.GetDataList(7, colors); da.GetDataList(8, thresholds); da.GetDataList(9, thresholdColors); da.GetDataList(10, sides);
        string title = "Distribution", unit = "", display = "";
        da.GetData(3, ref title); da.GetData(4, ref unit);
        double width = 0;
        var hasWidth = da.GetData(12, ref width);
        GH_ObjectWrapper? wrapper = null;
        da.GetData(6, ref wrapper);
        try
        {
            if (HasInput(6) && wrapper?.Value is not CardSpec { Kind: CardKind.Legend, Mapping: not null })
                throw new ArgumentException("Scale needs a Legend from Colour Scale");
            foreach (var (index, count) in new[] { (1, labels.Count), (2, keys.Count), (7, colors.Count) })
                if (HasInput(index) && count != values.Count)
                    throw new ArgumentException($"{Params.Input[index].Name} needs one entry per value.");
            DistributionMode? wired = null;
            if (da.GetData(11, ref display))
            {
                if (!Enum.TryParse<DistributionMode>(display, true, out var parsed) || !Enum.IsDefined(parsed))
                    throw new ArgumentException("Show as needs Dots or Histogram.");
                wired = parsed;
            }
            var messages = new List<ColourMessage>();
            var resolved = HasInput(8) || HasInput(9) || HasInput(10) ? ThresholdSet.Resolve(
                thresholds.Select(n => n?.Value ?? double.NaN).ToArray(),
                HasInput(9) ? thresholdColors.Select(Hex).ToArray() : null, HasInput(10) ? sides : null, messages) : null;
            if (labels.Count == 0) labels.AddRange(InputKeys.Numbered(values.Count));
            var card = Distribution.Create(new CardSpec
            {
                Id = CardId(da), Kind = CardKind.Distribution, Title = title, Unit = unit, WiredSlot = ReadSlot(da, 5),
                Values = values.Select(n => (double?)n?.Value).ToArray(), Labels = labels.ToArray(), Keys = ReadKeys(labels, keys),
                Colors = colors.Select(Hex).ToArray(), Width = 340,
                Distribution = new(_mode, wired, hasWidth ? width : null, _quartiles, _thresholdLines)
            }, wrapper?.Value as CardSpec, HasInput(8) ? resolved : null);
            var layout = Distribution.Geometry(card, Theme.Paper);
            foreach (var message in messages) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message.Text);
            if (layout.Fallback) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Distribution.BinWidthWarning);
            if (layout.Forced) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Distribution.ForcedRemark(layout.Count));
            if (layout.Count < values.Count) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{values.Count - layout.Count} missing values not shown");
            Output(da, card);
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }

    private static string Hex(GH_Colour? color)
    {
        if (color is null) throw new ArgumentException("Colours cannot be missing.");
        var c = color.Value;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        var modes = Menu_AppendItem(menu, "Show as").DropDown;
        foreach (var mode in Enum.GetValues<DistributionMode>())
            Menu_AppendItem(modes, mode.ToString(), (_, _) =>
            {
                if (_mode == mode) return;
                RecordUndoEvent("Show as");
                _mode = mode;
                ExpireSolution(true);
            }, true, _mode == mode);
        Menu_AppendItem(menu, "Median and quartiles", (_, _) =>
        {
            RecordUndoEvent("Median and quartiles");
            _quartiles = !_quartiles;
            ExpireSolution(true);
        }, true, _quartiles);
        Menu_AppendItem(menu, "Threshold lines", (_, _) =>
        {
            RecordUndoEvent("Threshold lines");
            _thresholdLines = !_thresholdLines;
            ExpireSolution(true);
        }, true, _thresholdLines);
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("DistributionMode", (int)_mode);
        writer.SetBoolean("Quartiles", _quartiles);
        writer.SetBoolean("ThresholdLines", _thresholdLines);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _mode = reader.ItemExists("DistributionMode") && reader.GetInt32("DistributionMode") == 1 ? DistributionMode.Histogram : DistributionMode.Dots;
        _quartiles = !reader.ItemExists("Quartiles") || reader.GetBoolean("Quartiles");
        _thresholdLines = !reader.ItemExists("ThresholdLines") || reader.GetBoolean("ThresholdLines");
        return base.Read(reader);
    }
}
