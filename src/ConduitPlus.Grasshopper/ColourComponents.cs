using System.Drawing;
using System.Windows.Forms;
using ConduitPlus.Core;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace ConduitPlus.Grasshopper;

/// <summary>Host conversion only: all mapping, validation and card construction live in Core.</summary>
public abstract class ColourComponent(string name, string nickname, string description)
    : GH_Component(name, nickname, description, "Conduit+", "Colour")
{
    private Bitmap? _icon;
    protected abstract ComponentIcon IconKind { get; }
    protected override Bitmap Icon => _icon ??= ConduitIcons.Create(IconKind);
    public override IEnumerable<string> Keywords => ["color", "palette", "gradient"];
    protected string CardId => InstanceGuid.ToString("N");
    protected static void PaletteInput(GH_InputParamManager p, bool categories)
    {
        static string OrderedGroup(string name) => name switch
        {
            "Viridis" or "Magma" or "Cividis" or "Greys" or "Greens" => "Perceptual",
            "Red–Yellow–Green" or "Brown–Teal" or "Orange–Purple" or "Spectral" => "Diverging",
            "Daylight" or "Solar" or "Comfort" or "Wind" => "Analysis-style",
            _ => "Ordered"
        };
        var options = Palettes.All.Where(s => categories || s.Kind != PaletteKind.Categorical)
            .OrderBy(s => s.Kind == PaletteKind.Categorical ? Palettes.Styles.Contains(s) ? 1 : 0 : 2)
            .SelectMany(s => s.Kind == PaletteKind.Categorical
                ? new[] { new InputOption(s.Name, s.Name, Palettes.Styles.Contains(s) ? "Diagram styles" : "Categorical") }
                : new[] { new InputOption(s.Name, s.Name, OrderedGroup(s.Name)), new InputOption(s.Name + " reversed", s.Name + " reversed", OrderedGroup(s.Name)) }).ToArray();
        var index = p.AddParameter(new Param_TextOption { Options = options }, "Palette", "P", categories ? "Right-click to choose, or set your own colours (one per category, repeating)" : "Right-click to choose, or set 2–32 colours as evenly spaced stops; append ' reversed' to a name", GH_ParamAccess.tree);
        p[index].Optional = true;
    }
    protected static string[] ReadPalette(IGH_DataAccess da, int index) =>
        Palettes.NormalizeInput(ReadTextTree(da, index));
    protected static string[] ReadTextTree(IGH_DataAccess da, int index)
    {
        da.GetDataTree(index, out GH_Structure<GH_String> tree);
        return tree?.Branches.SelectMany(b => b.Select(v => v?.Value ?? "")).ToArray() ?? [];
    }
    protected bool SingleIteration(IGH_DataAccess da, string name, string controls)
    {
        // GH otherwise inserts null placeholders for unassigned later iterations.
        da.DisableGapLogic();
        if (da.Iteration == 0) return true;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{name} makes one legend, so it needs a single {controls}.");
        return false;
    }
    protected Slot? ReadSlot(IGH_DataAccess da, int index)
    {
        string text = "";
        if (!da.GetData(index, ref text) || string.IsNullOrWhiteSpace(text)) return null;
        if (InputOptions.Slots.Any(o => o.Matches(text)) && Enum.TryParse<Slot>(text, true, out var slot)) return slot;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown slot '{text}'; use tl, tr, bl, br, lc or rc.");
        return null;
    }
    protected static Color Colour(string hex)
    { var c = ColorRgba.Parse(hex); return Color.FromArgb(c.R, c.G, c.B); }
    protected static GH_Structure<GH_Colour> ColourTree<T>(GH_Structure<T> source, IReadOnlyList<string> colours) where T : IGH_Goo
    {
        var output = new GH_Structure<GH_Colour>(); var index = 0;
        for (var branch = 0; branch < source.PathCount; branch++)
        {
            var path = source.Paths[branch]; output.EnsurePath(path);
            foreach (var unused in source.Branches[branch]) output.Append(new GH_Colour(Colour(colours[index++])), path);
        }
        return output;
    }
    protected void Messages(IEnumerable<ColourMessage> messages)
    {
        foreach (var message in messages) AddRuntimeMessage(message.Level == ColourMessageLevel.Warning
            ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, message.Text);
    }
}

public sealed class ColourScaleComponent : ColourComponent
{
    private ColourRangeMode _rangeMode;
    private int _clipPercent = 2;

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("RangeMode", (int)_rangeMode);
        writer.SetInt32("ClipPercent", _clipPercent);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        var mode = reader.ItemExists("RangeMode") ? (ColourRangeMode)reader.GetInt32("RangeMode") : ColourRangeMode.Auto;
        _rangeMode = Enum.IsDefined(mode) ? mode : ColourRangeMode.Auto;
        var percent = reader.ItemExists("ClipPercent") ? reader.GetInt32("ClipPercent") : 2;
        _clipPercent = percent is 1 or 2 or 5 ? percent : 2;
        return base.Read(reader);
    }

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendSeparator(menu);
        var range = Menu_AppendItem(menu, "Range").DropDown;
        void AddMode(string label, ColourRangeMode mode)
        {
            Menu_AppendItem(range, label, (_, _) =>
            {
                if (_rangeMode == mode) return;
                RecordUndoEvent("Range mode");
                _rangeMode = mode;
                ExpireSolution(true);
            }, true, _rangeMode == mode);
        }
        AddMode("Auto (rounded)", ColourRangeMode.Auto);
        AddMode("Exact data min/max", ColourRangeMode.ExactData);
        AddMode("Clip outliers", ColourRangeMode.ClipOutliers);
        var clip = Menu_AppendItem(range, "Clip at");
        clip.Enabled = _rangeMode == ColourRangeMode.ClipOutliers;
        foreach (var percent in new[] { 1, 2, 5 })
        {
            Menu_AppendItem(clip.DropDown, $"{percent}%", (_, _) =>
            {
                if (_clipPercent == percent) return;
                RecordUndoEvent("Clip at");
                _clipPercent = percent;
                ExpireSolution(true);
            }, true, _clipPercent == percent);
        }
    }

    public ColourScaleComponent() : base("Colour Scale", "ColScale", "Numbers to model colours and a matching gradient Legend") { }
    public override Guid ComponentGuid => new("a7447cd8-1554-460c-b806-bc6f3caeee57");
    protected override ComponentIcon IconKind => ComponentIcon.ColourScale;
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Values", "V", "Numbers to colour; one scale across all branches. Null/NaN/infinity are missing", GH_ParamAccess.tree); p[0].Optional = true;
        PaletteInput(p, false);
        p.AddNumberParameter("Min", "Mn", "Value at the first colour; unset follows the Range menu", GH_ParamAccess.item); p[2].Optional = true;
        p.AddNumberParameter("Max", "Mx", "Value at the last colour; unset follows the Range menu", GH_ParamAccess.item); p[3].Optional = true;
        p.AddNumberParameter("Mid", "Md", "Value at the middle colour; defaults to centre, or zero for diverging/Status ranges spanning zero", GH_ParamAccess.item); p[4].Optional = true;
        p.AddTextParameter("Unit", "U", "Legend unit", GH_ParamAccess.item, "");
        p.AddTextParameter("Title", "T", "Legend title", GH_ParamAccess.item, "Legend");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position", GH_ParamAccess.item); p[7].Optional = true;
        p.AddColourParameter("Below", "Bl", "Colour for values below Min; unset uses the first colour", GH_ParamAccess.item); p[8].Optional = true;
        p.AddColourParameter("Above", "Ab", "Colour for values above Max; unset uses the last colour", GH_ParamAccess.item); p[9].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddColourParameter("Colours", "C", "One colour per value, preserving paths and empty branches; to Custom Preview or Bar Colors", GH_ParamAccess.tree);
        p.AddGenericParameter("Legend", "L", "Gradient Legend for Overlay or Canvas Card", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SingleIteration(da, "Colour Scale", "Title, Unit, Slot, Min, Max, Mid, Below and Above")) return;
        da.GetDataTree(0, out GH_Structure<GH_Number> tree); tree ??= new();
        double min = 0, max = 0, mid = 0;
        var hasMin = da.GetData(2, ref min); var hasMax = da.GetData(3, ref max); var hasMid = da.GetData(4, ref mid);
        if (tree.DataCount == 0 && !hasMin && !hasMax)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect Values (or set Min and Max)."); return; }
        string unit = "", title = "Legend"; da.GetData(5, ref unit); da.GetData(6, ref title);
        try
        {
            string? ReadColour(int index)
            {
                var colour = Color.Empty;
                return da.GetData(index, ref colour) ? $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}" : null;
            }
            var result = ColourScale.Create(tree.Branches.SelectMany(b => b.Select(v => (double?)v?.Value)).ToArray(),
                ReadPalette(da, 1), hasMin ? min : null, hasMax ? max : null, hasMid ? mid : null, title, unit, CardId, ReadSlot(da, 7),
                ReadColour(8), ReadColour(9), _rangeMode, _clipPercent);
            da.SetDataTree(0, ColourTree(tree, result.Colours)); da.SetData(1, new GH_ObjectWrapper(result.Legend)); Messages(result.Messages);
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class ColourCategoriesComponent : ColourComponent
{
    private bool _showCounts = true;
    public ColourCategoriesComponent() : base("Colour Categories", "ColCat", "Categories to model colours, a Legend and grouped Bar inputs; wire Order to pin colours") { }
    public override Guid ComponentGuid => new("278d5e5a-301f-4c97-b21a-7bea73731c19");
    protected override ComponentIcon IconKind => ComponentIcon.ColourCategories;
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Categories", "Ca", "One category per item; blank is No data", GH_ParamAccess.tree); p[0].Optional = true;
        p.AddTextParameter("Keys", "K", "Optional member key per item in path order, as on Link; enables legend selection", GH_ParamAccess.tree); p[1].Optional = true;
        PaletteInput(p, true);
        p.AddTextParameter("Order", "O", "All categories in legend order; absent categories keep their slots and empty groups", GH_ParamAccess.tree); p[3].Optional = true;
        p.AddTextParameter("Title", "T", "Legend title", GH_ParamAccess.item, "Legend");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position", GH_ParamAccess.item); p[5].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddColourParameter("Colours", "C", "One colour per item; same tree as Categories", GH_ParamAccess.tree);
        p.AddGenericParameter("Legend", "L", "Categorical Legend for Overlay or Canvas Card", GH_ParamAccess.item);
        p.AddTextParameter("Categories", "Ca", "Distinct categories in legend order; to Bar Labels", GH_ParamAccess.list);
        p.AddColourParameter("Category colours", "CC", "One colour per category; to Bar Colors", GH_ParamAccess.list);
        p.AddTextParameter("Group keys", "GK", "Selection keys for each category row (generated, unique to this component); to Bar Keys. Empty without Keys", GH_ParamAccess.list);
        p.AddTextParameter("Members", "M", "One branch per category, including empty branches; to Bar Members. Empty without Keys", GH_ParamAccess.tree);
    }
    public override bool Write(GH_IWriter writer) { writer.SetBoolean("ShowCounts", _showCounts); return base.Write(writer); }
    public override bool Read(GH_IReader reader) { _showCounts = !reader.ItemExists("ShowCounts") || reader.GetBoolean("ShowCounts"); return base.Read(reader); }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Show counts", (_, _) => { RecordUndoEvent("Show counts"); _showCounts = !_showCounts; ExpireSolution(true); }, true, _showCounts);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SingleIteration(da, "Colour Categories", "Title and Slot")) return;
        da.GetDataTree(0, out GH_Structure<GH_String> tree); tree ??= new();
        da.GetDataTree(1, out GH_Structure<GH_String> keys);
        var hasKeys = Params.Input[1].SourceCount > 0 || Params.Input[1].VolatileData.PathCount > 0;
        var order = ReadTextTree(da, 3);
        if (tree.DataCount == 0 && order.Length == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect Categories (or set Order)."); return; }
        string title = "Legend"; da.GetData(4, ref title);
        try
        {
            var result = ColourCategories.Create(tree.Branches.SelectMany(b => b.Select(v => v?.Value)).ToArray(),
                hasKeys ? keys?.Branches.SelectMany(b => b.Select(v => v?.Value)).ToArray() ?? [] : null,
                ReadPalette(da, 2), order, title, _showCounts, CardId, ReadSlot(da, 5), InstanceGuid.ToString("N")[..8]);
            var members = new GH_Structure<GH_String>();
            for (var i = 0; i < result.Members.Length; i++)
            {
                var path = new GH_Path(i); members.EnsurePath(path);
                members.AppendRange(result.Members[i].Select(k => new GH_String(k)), path);
            }
            da.SetDataTree(0, ColourTree(tree, result.Colours)); da.SetData(1, new GH_ObjectWrapper(result.Legend));
            da.SetDataList(2, result.Categories); da.SetDataList(3, result.CategoryColours.Select(Colour));
            da.SetDataList(4, result.GroupKeys); da.SetDataTree(5, members); Messages(result.Messages);
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class ColourPaletteComponent : ColourComponent
{
    public ColourPaletteComponent() : base("Colour Palette", "ColPal", "A stable set of diagram colours; grow Count without changing the first colours") { }
    public override Guid ComponentGuid => new("907c17a3-fdf5-4d8b-9ab6-10400ae88ed2");
    protected override ComponentIcon IconKind => ComponentIcon.ColourPalette;
    public override IEnumerable<string> Keywords => ["color", "palette", "swatch", "diagram", "scheme"];
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        var style = new Param_TextOption { Options = Palettes.Styles.Select(s => new InputOption(s.Name, s.Name, "Diagram styles"))
            .Append(new InputOption("Categorical", "Categorical", "Categorical")).ToArray() };
        p.AddParameter(style, "Style", "S", "Right-click to choose a diagram style; a typed scale name is sampled evenly", GH_ParamAccess.item);
        style.PersistentData.Append(new GH_String("Programme")); p[0].Optional = true;
        p.AddIntegerParameter("Count", "N", "How many colours (0–256; zero gives empty outputs)", GH_ParamAccess.item, 8);
        p.AddIntegerParameter("Shift", "Sh", "Start further along the style; wraps in either direction", GH_ParamAccess.item, 0); p[2].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddColourParameter("Colours", "C", "N colours; diagram styles keep the first N when Count grows", GH_ParamAccess.list);
        p.AddTextParameter("Hex", "H", "The same colours as #RRGGBB text", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string style = "Programme"; int count = 8, shift = 0;
        da.GetData(0, ref style);
        if (!da.GetData(1, ref count)) return;
        da.GetData(2, ref shift);
        try
        {
            var result = DiagramPalette.Create(style, count, shift);
            da.SetDataList(0, result.Colours.Select(Colour)); da.SetDataList(1, result.Colours); Messages(result.Messages);
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}
