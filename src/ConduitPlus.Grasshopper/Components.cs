using System.Drawing;
using System.Collections.Immutable;
using System.Windows.Forms;
using GH_IO.Serialization;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Undo;
using Rhino;
using Rhino.Geometry;

namespace ConduitPlus.Grasshopper;

public sealed class AssemblyInfo : GH_AssemblyInfo
{
    public override string Name => "Conduit+";
    public override string Description => "Viewport cards for structural results";
    public override Guid Id => new("b02874c1-57b6-43d2-871a-3cb89603f101");
    public override string AuthorName => "Tomo S";
    public override string AuthorContact => "";
    public override GH_LibraryLicense License => GH_LibraryLicense.opensource;
}

public abstract class CardComponent(string name, string nickname, string description) : GH_Component(name, nickname, description, "Conduit+", "Cards")
{
    private Bitmap? _icon;
    protected override Bitmap Icon => _icon ??= CreateIcon(NickName[..1]);
    internal static Bitmap CreateIcon(string letter)
    {
        var b = new Bitmap(24, 24); using var g = Graphics.FromImage(b); g.Clear(Color.FromArgb(43, 89, 240));
        using var f = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel); g.DrawString(letter, f, Brushes.White, 5, 3); return b;
    }
    protected void Output(IGH_DataAccess da, CardSpec c) { c.Validate(); da.SetData(0, new GH_ObjectWrapper(c)); }
    protected bool HasInput(int index) => Params.Input[index].SourceCount > 0 || Params.Input[index].VolatileData.PathCount > 0;
    protected ImmutableDictionary<string, GroupDefinition> ReadMembers(IGH_DataAccess da, int index,
        string[] keys, int entries, bool explicitKeys, bool gradient = false)
    {
        if (!HasInput(index)) return GroupInputs.Create(keys, entries, explicitKeys, null, gradient);
        da.GetDataTree(index, out GH_Structure<GH_String> tree);
        var branches = tree is null ? [] : tree.Paths.Select((path, i) =>
            new MemberInputBranch(path.ToString(), tree.Branches[i].Select(v => v?.Value).ToArray())).ToArray();
        return GroupInputs.Create(keys, entries, explicitKeys, branches, gradient);
    }
    protected string CardId => InstanceGuid.ToString("N");
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Card", "C", "Connect to Overlay", GH_ParamAccess.item);
    protected static Slot? ReadSlot(IGH_DataAccess da, int index)
    { string slot = ""; return da.GetData(index, ref slot) && Enum.TryParse<Slot>(slot, true, out var value) && Enum.IsDefined(value) ? value : null; }
}

public sealed class MetricComponent : CardComponent
{
    public MetricComponent() : base("Metric", "Metric", "A result metric with optional numeric precision") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae201");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Value", "V", "Formatted value; missing displays an em dash", GH_ParamAccess.item); p[0].Optional = true;
        p.AddTextParameter("Title", "T", "Metric title", GH_ParamAccess.item, "Metric");
        p.AddTextParameter("Unit", "U", "Unit suffix", GH_ParamAccess.item, "");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[3].Optional = true;
        p.AddIntegerParameter("Decimals", "D", "Optional numeric precision (0–12); right-click for 0–6, clear persistent data for automatic", GH_ParamAccess.item); p[4].Optional = true;
        for (var precision = 0; precision <= 6; precision++) ((global::Grasshopper.Kernel.Parameters.Param_Integer)p[4]).AddNamedValue(precision.ToString(), precision);
        p.AddTextParameter("Key", "K", "Optional stable member or group key", GH_ParamAccess.item); p[5].Optional = true;
        p.AddTextParameter("Members", "M", "Optional group members in exactly one branch; requires Key", GH_ParamAccess.tree); p[6].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string value = "", title = "Metric", unit = ""; var hasValue = da.GetData(0, ref value); da.GetData(1, ref title); da.GetData(2, ref unit);
        int decimals = 0; var hasDecimals = da.GetData(4, ref decimals);
        string key = ""; var hasKey = da.GetData(5, ref key);
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Metric, Keys = hasKey ? [key] : [], Groups = ReadMembers(da, 6, hasKey ? [key] : [], 1, hasKey), Title = title, Value = MetricFormatting.Format(hasValue ? value : null, hasDecimals ? decimals : null), Unit = unit, WiredSlot = ReadSlot(da, 3) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class BarComponent : CardComponent
{
    public BarComponent() : base("Bar", "Bar", "Keyed signed utilisation bars; NaN becomes a missing value") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae202");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Values", "V", "Member utilisation values", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "Optional labels (default M01…)", GH_ParamAccess.list); p[1].Optional = true;
        p.AddTextParameter("Keys", "K", "Unique stable member keys; default labels", GH_ParamAccess.list); p[2].Optional = true;
        p.AddTextParameter("Title", "T", "Chart title", GH_ParamAccess.item, "Member utilisation");
        p.AddNumberParameter("Threshold", "Th", "Nonnegative threshold; zero for signed deltas", GH_ParamAccess.item, 1);
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[5].Optional = true;
        p.AddColourParameter("Colors", "C", "Optional per-bar colors; default compares values to Threshold", GH_ParamAccess.list); p[6].Optional = true;
        p.AddTextParameter("Members", "M", "One path-ordered branch per bar, including empty branches; requires explicit Keys", GH_ParamAccess.tree); p[7].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var values = new List<double>(); var labels = new List<string>(); var keys = new List<string>(); string title = "Member utilisation"; double threshold = 1;
        if (!da.GetDataList(0, values)) return; da.GetDataList(1, labels); da.GetDataList(2, keys); da.GetData(3, ref title); da.GetData(4, ref threshold);
        var colors = new List<Color>(); da.GetDataList(6, colors);
        var explicitKeys = keys.Count > 0;
        if (labels.Count == 0) labels = Enumerable.Range(1, values.Count).Select(i => $"M{i:00}").ToList(); if (keys.Count == 0) keys = [.. labels];
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Bar, Groups = ReadMembers(da, 7, keys.ToArray(), values.Count, explicitKeys), Title = title, Values = values.Select(v => double.IsNaN(v) ? (double?)null : v).ToArray(), Labels = labels.ToArray(), Keys = keys.ToArray(), Colors = colors.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToArray(), Threshold = threshold, WiredSlot = ReadSlot(da, 5) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class LineComponent : CardComponent
{
    public LineComponent() : base("Line", "Line", "Keyed result samples; NaN becomes a gap") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae206");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Values", "V", "Ordered sample values (at least two)", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "Optional labels; default 1…n", GH_ParamAccess.list); p[1].Optional = true;
        p.AddTextParameter("Keys", "K", "Unique stable keys; default labels", GH_ParamAccess.list); p[2].Optional = true;
        p.AddTextParameter("Title", "T", "Chart title", GH_ParamAccess.item, "Structural results");
        p.AddNumberParameter("Threshold", "Th", "Optional positive threshold; unwired or nonpositive hides the band", GH_ParamAccess.item); p[4].Optional = true;
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[5].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var values = new List<double>(); var labels = new List<string>(); var keys = new List<string>(); string title = "Structural results"; double threshold = 0;
        if (!da.GetDataList(0, values)) return; da.GetDataList(1, labels); da.GetDataList(2, keys); da.GetData(3, ref title);
        if (Params.Input[4].SourceCount > 0) da.GetData(4, ref threshold);
        if (labels.Count == 0) labels = Enumerable.Range(1, values.Count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(); if (keys.Count == 0) keys = [.. labels];
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Line, Title = title, Values = values.Select(v => double.IsNaN(v) ? (double?)null : v).ToArray(), Labels = labels.ToArray(), Keys = keys.ToArray(), Threshold = threshold <= 0 ? 0 : threshold, WiredSlot = ReadSlot(da, 5) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class TextComponent : CardComponent
{
    public TextComponent() : base("Text", "Text", "A wrapped note with explicit newlines (up to 30 lines)") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae207");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Title", "T", "Optional note title", GH_ParamAccess.item); p[0].Optional = true;
        p.AddTextParameter("Text", "N", "Note text; newlines are preserved", GH_ParamAccess.item);
        p.AddNumberParameter("Width", "W", "Card width in CSS pixels (180–1200)", GH_ParamAccess.item, 280);
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[3].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string title = "", text = ""; double width = 280;
        da.GetData(0, ref title); if (!da.GetData(1, ref text)) return; da.GetData(2, ref width);
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Text, Title = title, Text = text, Width = width, WiredSlot = ReadSlot(da, 3) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class LegendComponent : CardComponent
{
    public LegendComponent() : base("Legend", "Legend", "Categorical swatches or a gradient scale when Min and Max are wired") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae208");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddColourParameter("Colors", "C", "Swatch colors or 2+ evenly spaced gradient stops", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "L", "One label per color in categorical mode", GH_ParamAccess.list); p[1].Optional = true;
        p.AddNumberParameter("Min", "Mn", "Wire both Min and Max for a gradient", GH_ParamAccess.item); p[2].Optional = true;
        p.AddNumberParameter("Max", "Mx", "Gradient maximum", GH_ParamAccess.item); p[3].Optional = true;
        p.AddTextParameter("Unit", "U", "Optional gradient unit", GH_ParamAccess.item, "");
        p.AddTextParameter("Keys", "K", "Optional unique categorical selection keys", GH_ParamAccess.list); p[5].Optional = true;
        p.AddTextParameter("Title", "T", "Legend title", GH_ParamAccess.item, "Legend");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[7].Optional = true;
        p.AddTextParameter("Members", "M", "One path-ordered branch per category, including empty branches; requires explicit Keys", GH_ParamAccess.tree); p[8].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var colors = new List<Color>(); var labels = new List<string>(); var keys = new List<string>(); string title = "Legend", unit = ""; double min = 0, max = 0;
        if (!da.GetDataList(0, colors)) return; da.GetDataList(1, labels); da.GetData(4, ref unit); da.GetDataList(5, keys); da.GetData(6, ref title);
        var hasMin = Params.Input[2].SourceCount > 0; var hasMax = Params.Input[3].SourceCount > 0;
        if ((hasMin && !da.GetData(2, ref min)) || (hasMax && !da.GetData(3, ref max))) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wired Min and Max must supply numbers."); return; }
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Legend, Groups = ReadMembers(da, 8, keys.ToArray(), colors.Count, keys.Count > 0, hasMin || hasMax), Title = title, Colors = colors.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToArray(), Labels = labels.ToArray(), Keys = keys.ToArray(), Min = hasMin ? min : null, Max = hasMax ? max : null, Unit = unit, WiredSlot = ReadSlot(da, 7) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class TableComponent : CardComponent
{
    public TableComponent() : base("Table", "Table", "One branch per result row; optional stable row keys") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae203");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Headers", "H", "Column headers", GH_ParamAccess.list);
        p.AddTextParameter("Rows", "R", "One branch per row, one item per column", GH_ParamAccess.tree);
        p.AddTextParameter("Keys", "K", "Optional stable row keys; default branch paths", GH_ParamAccess.list); p[2].Optional = true;
        p.AddTextParameter("Title", "T", "Table title", GH_ParamAccess.item, "Structural results");
        p.AddParameter(new Param_TextOption(), "Slot", "S", "Right-click to choose a position, or wire tl, tr, bl, br, lc or rc", GH_ParamAccess.item); p[4].Optional = true;
        p.AddIntegerParameter("Visible rows", "N", "Rows shown at once; more rows scroll (wheel or scrollbar)", GH_ParamAccess.item, TableScroll.DefaultVisibleRows);
        p.AddNumberParameter("Width", "W", "Table width in CSS pixels (180–1200)", GH_ParamAccess.item, 380);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var headers = new List<string>(); var keys = new List<string>(); string title = "Structural results";
        if (!da.GetDataList(0, headers) || !da.GetDataTree(1, out GH_Structure<GH_String> tree)) return;
        da.GetDataList(2, keys); da.GetData(3, ref title);
        var visibleRows = TableScroll.DefaultVisibleRows; da.GetData(5, ref visibleRows);
        double width = 380; da.GetData(6, ref width);
        if (keys.Count != 0 && keys.Count != tree.PathCount) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Key count must match row count."); return; }
        var rows = tree.Branches.Select((branch, i) => new TableRow(keys.Count == 0 ? tree.Paths[i].ToString() : keys[i], branch.Select(v => v?.Value).ToArray())).ToArray();
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Table, Title = title, Headers = headers.ToArray(), Rows = rows, Width = width, MaxRows = visibleRows, WiredSlot = ReadSlot(da, 4) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class LinkComponent : GH_Component
{
    private Bitmap? _icon;
    public LinkComponent() : base("Link", "Link", "Map geometry branches or items to stable card keys", "Conduit+", "Display") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae205");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("L");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Geometry", "G", "Points, lines, polylines, curves, breps, meshes, surfaces or extrusions", GH_ParamAccess.tree);
        p.AddTextParameter("Keys", "K", "Required keys: one per branch (preferred) or flattened geometry item; repeated keys combine", GH_ParamAccess.list);
        p[1].DataMapping = GH_DataMapping.Flatten;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Link", "L", "Connect to Overlay Links", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var keys = new List<string>();
        if (!da.GetDataTree(0, out GH_Structure<IGH_Goo> tree) || !da.GetDataList(1, keys)) return;
        try
        {
            var mapping = LinkKeyMapping.Create(tree.Branches.Select(b => b.Count).ToArray(), keys);
            var items = new List<(string Key, object Geometry, string Location)>();
            for (var branch = 0; branch < tree.PathCount; branch++)
            for (var index = 0; index < tree.Branches[branch].Count; index++)
            {
                try { items.Add((keys[mapping[branch][index]], Normalize(tree.Branches[branch][index]), $"Geometry path {tree.Paths[branch]}, item [{index}]")); }
                catch (ArgumentException e) { throw new ArgumentException($"Geometry path {tree.Paths[branch]}, item [{index}]: {e.Message}", e); }
            }
            da.SetData(0, new GH_ObjectWrapper(new Link(keys, items)));
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    private static object Normalize(object? value)
    {
        if (value is GH_ObjectWrapper wrapped) return Normalize(wrapped.Value);
        if (value is IGH_GeometricGoo geometric) geometric.LoadGeometry();
        object? geometry = value switch
        {
            GH_Point p => p.Value,
            GH_Line l => l.Value,
            GH_Curve c => c.Value,
            GH_Brep b => b.Value,
            GH_Mesh m => m.Value,
            GH_Surface s => s.Value,
            GH_Extrusion e => e.Value,
            Point3d or Line or Polyline or Curve or Brep or Mesh or Surface or Extrusion or global::Rhino.Geometry.Point => value,
            _ => null
        };
        var valid = geometry switch
        {
            Point3d p => p.IsValid,
            Line l => l.IsValid,
            Polyline p => p.IsValid,
            GeometryBase g => g.IsValid,
            _ => false
        };
        return valid ? geometry! : throw new ArgumentException($"Null, invalid or unsupported geometry ({value?.GetType().Name ?? "null"}).");
    }
}

public sealed class OverlayComponent : GH_Component
{
    private readonly Queue<string> _events = new();
    private ScopeBinding? _scope;
    private bool _scopeValid = true;
    private long _generation;
    private PendingPublish? _lastPublish;
    private bool _rhinoSuspended;
    private MembershipIndex? _membership;
    private readonly object _publishLock = new();
    private bool _publishScheduled;
    private sealed record PendingPublish(CardSpec[] Cards, Link[] Links, Theme Theme, bool Interaction, uint? DocumentSerial, Guid? ViewId, long Generation, ScopeBinding? Scope, bool BrushEnabled, string? RequestedView);
    private PendingPublish? _pending;
    private bool _solveScheduled, _interactionSolve;
    private bool _active;
    private Bitmap? _icon;
    public OverlayComponent() : base("Overlay", "Overlay", "Display cards in the active or named Rhino viewport; click bars/rows for keyed selection", "Conduit+", "Display")
    {
        ObjectChanged += (_, e) =>
        {
            if (e.Type != GH_ObjectEventType.Enabled) return;
            _scope?.SetEnabled(!Locked && !_rhinoSuspended);
            OverlayManager.SetInteractionEnabled(InstanceGuid, !Locked && _scopeValid);
            if (!Locked) OverlayManager.RefreshDomain(InstanceGuid);
        };
    }
    private CardSpec[] _originals = [];
    private Theme _menuTheme = Theme.NewComponentDefault;
    private Theme _selectedTheme = Theme.NewComponentDefault;
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("ConduitPlus.Theme", _menuTheme.Name);
        return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
        var removed = new List<(IGH_Param Param, int Index)>();
        for (var i = Params.Input.Count - 1; i >= 0 && !reader.ChunkExists("param_input", i); i--)
        { var parameter = Params.Input[i]; removed.Add((parameter, i)); Params.UnregisterInputParameter(parameter, false); }
        bool result;
        try { result = base.Read(reader); }
        finally { foreach (var entry in removed.OrderBy(e => e.Index)) Params.RegisterInputParam(entry.Param, entry.Index); }
        var themeName = "";
        var themeFound = reader.TryGetString("ConduitPlus.Theme", ref themeName);
        _menuTheme = Theme.ReadSelection(themeFound ? themeName : null);
        _selectedTheme = _menuTheme;
        return result;
    }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        // A wired or right-click-set Theme input wins, so the menu is shown but disabled while it has a value.
        var setByInput = ThemeInputSet;
        var themes = Menu_AppendItem(menu, setByInput ? "Theme (set by input)" : "Theme");
        foreach (var theme in Theme.Presets)
            Menu_AppendItem(themes.DropDown, theme.Name, (_, _) => ChangeTheme(theme), !setByInput, _menuTheme == theme);
    }
    private bool ThemeInputSet => Params.Input[2] is Param_TextOption { HasValue: true };
    private void ChangeTheme(Theme theme)
    {
        if (OnPingDocument() is null || _menuTheme == theme) return;
        RecordUndoEvent("Change theme", new ThemeUndoAction(InstanceGuid, _menuTheme.Name, theme.Name));
        ApplyTheme(theme.Name);
    }
    private void ApplyTheme(string name)
    {
        Theme.TryGet(name, out _menuTheme);
        // Menu edits and undo stay in the bound view and do not trigger a data solve.
        if (!ThemeInputSet)
        {
            lock (_publishLock)
            {
                _selectedTheme = _menuTheme;
                if (_pending is not null) _pending = _pending with { Theme = _selectedTheme };
            }
            OverlayManager.ReplaceTheme(InstanceGuid, _selectedTheme);
        }
        OnPingDocument()?.Modified();
    }
    public sealed class ThemeUndoAction : GH_ObjectUndoAction
    {
        private Guid _owner;
        private string _before = "Paper", _after = "Paper";
        public ThemeUndoAction() : base(Guid.Empty) { }
        public ThemeUndoAction(Guid owner, string before, string after) : base(owner)
        { _owner = owner; _before = before; _after = after; }
        public override bool ExpiresSolution => false;
        public override bool ExpiresDisplay => true;
        protected override void Object_Undo(GH_Document doc, IGH_DocumentObject obj) { if (obj is OverlayComponent overlay) overlay.ApplyTheme(_before); }
        protected override void Object_Redo(GH_Document doc, IGH_DocumentObject obj) { if (obj is OverlayComponent overlay) overlay.ApplyTheme(_after); }
        protected override void Internal_Undo(GH_Document doc) => Object_Undo(doc, doc.FindObject(_owner, false));
        protected override void Internal_Redo(GH_Document doc) => Object_Redo(doc, doc.FindObject(_owner, false));
        public override bool Write(GH_IWriter writer)
        {
            writer.SetGuid("Owner", _owner); writer.SetString("Before", _before); writer.SetString("After", _after);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            _owner = reader.GetGuid("Owner"); _before = reader.GetString("Before"); _after = reader.GetString("After");
            return base.Read(reader);
        }
    }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae204");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("+");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cards", "C", "Metric, Bar, Table, Line, Text or Legend cards", GH_ParamAccess.list);
        p.AddGenericParameter("Links", "L", "Optional keyed geometry from Link", GH_ParamAccess.list); p[1].Optional = true;
        p.AddParameter(new Param_TextOption { Options = InputOptions.Themes }, "Theme", "T", "Right-click to choose, or wire Auto, Paper, Night, Smoke or Blueprint (case-insensitive). Auto follows Rhino's UI mode. A set value overrides the Theme menu.", GH_ParamAccess.item); p[2].Optional = true;
        p.AddGenericParameter("Scope", "S", "Optional explicit selection Scope; exactly one token across the tree", GH_ParamAccess.tree); p[3].Optional = true;
        p.AddParameter(new Param_TextOption { OptionsProvider = ViewOptions }, "View", "V", "Active viewport or a named standard Rhino viewport", GH_ParamAccess.item); p[4].Optional = true;
    }
    private readonly Dictionary<string, (GH_RuntimeMessageLevel Level, string Text)> _notices = [];
    private readonly List<(GH_RuntimeMessageLevel Level, string Text)> _solveMessages = [];
    private bool _replaying;
    private GH_Document? _noticeDocument;
    public override void AddRuntimeMessage(GH_RuntimeMessageLevel level, string text)
    { if (!_replaying) _solveMessages.Add((level, text)); base.AddRuntimeMessage(level, text); }
    public override void ClearData() { _solveMessages.Clear(); base.ClearData(); }
    private void ReplayNotices()
    {
        _replaying = true;
        try { ClearRuntimeMessages(); foreach (var message in _solveMessages.Concat(_notices.Values)) base.AddRuntimeMessage(message.Level, message.Text); }
        finally { _replaying = false; }
        global::Grasshopper.Instances.ActiveCanvas?.Invalidate();
    }
    private void NoticeSolutionEnd(object sender, GH_SolutionEventArgs e)
    { if (_noticeDocument is { } doc) doc.SolutionEnd -= NoticeSolutionEnd; _noticeDocument = null; ReplayNotices(); }
    private void SetNotice(string kind, GH_RuntimeMessageLevel level, string? text)
    {
        if (text is null) _notices.Remove(kind); else _notices[kind] = (level, text);
        var doc = OnPingDocument();
        if (doc?.SolutionState == GH_ProcessStep.Process)
        { if (_noticeDocument is null) { _noticeDocument = doc; doc.SolutionEnd += NoticeSolutionEnd; } }
        else ReplayNotices();
    }
    protected override void AfterSolveInstance() { base.AfterSolveInstance(); ReplayNotices(); }
    private string? _requestedView;
    private IReadOnlyList<InputOption> ViewOptions()
    {
        var binding = OverlayManager.Binding(InstanceGuid);
        var doc = binding is { } b ? RhinoDoc.FromRuntimeSerialNumber(b.DocumentSerial) : RhinoDoc.ActiveDoc;
        var names = (doc?.Views.GetStandardRhinoViews() ?? []).Select(v => v.MainViewport.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_requestedView is { } stored && !names.Contains(stored, StringComparer.OrdinalIgnoreCase)) names.Add(stored);
        return new[] { new InputOption("Active viewport", null) }.Concat(names.Select(n => new InputOption(n, n))).ToArray();
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    { p.AddTextParameter("Selection", "S", "Clicked member keys", GH_ParamAccess.list); p.AddTextParameter("Events", "E", "Last 32 click events as JSON", GH_ParamAccess.list); }
    public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); Activate(); }
    private void Activate()
    {
        if (_active) return;
        OverlayManager.Start();
        _active = true; _generation++;
        OverlayManager.Interaction += OnInteraction;
        OverlayManager.BoundDocumentClosed += OnBoundDocumentClosed;
        OverlayManager.ViewsChanged += OnViewsChanged;
        if (_scope is { IsValid: true } && _lastPublish is { } last)
        { _pending = last with { Interaction = true, Generation = _generation }; Publish(); }
    }
    public override void RemovedFromDocument(GH_Document document) { Deactivate(true); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);
        if (context == GH_DocumentContext.Close) Deactivate(true);
        else if (context == GH_DocumentContext.Unloaded) Deactivate(false);
        else if (context is GH_DocumentContext.Loaded or GH_DocumentContext.Open) Activate();
        else if (context == GH_DocumentContext.Lock) _scope?.SetEnabled(false);
        else if (context == GH_DocumentContext.Unlock) { _scope?.SetEnabled(!_rhinoSuspended); OverlayManager.RefreshDomain(InstanceGuid); }
    }
    private void Deactivate(bool clear)
    {
        if (_noticeDocument is { } noticeDoc) noticeDoc.SolutionEnd -= NoticeSolutionEnd; _noticeDocument = null;
        _active = false; _generation++;
        lock (_publishLock) { _pending = null; _publishScheduled = false; _solveScheduled = false; _interactionSolve = false; }
        OverlayManager.Interaction -= OnInteraction;
        OverlayManager.BoundDocumentClosed -= OnBoundDocumentClosed;
        OverlayManager.ViewsChanged -= OnViewsChanged;
        if (clear) { DetachScope(); _lastPublish = null; _events.Clear(); }
        OverlayManager.Remove(InstanceGuid);
    }
    private void OnViewsChanged()
    {
        if (!_active || _rhinoSuspended || Locked || _lastPublish is not { RequestedView: not null } last) return;
        // A queued publication has newer data and resolves the name itself.
        lock (_publishLock) { if (_pending is not null) return; _pending = last with { Interaction = true, Generation = _generation, Theme = _selectedTheme }; }
        Publish();
    }
    private void OnBoundDocumentClosed(Guid owner)
    {
        if (owner != InstanceGuid) return;
        _rhinoSuspended = true;
        _lastPublish = null; _pending = null; _publishScheduled = false; _generation++;
        if (_scope is { } scope)
        {
            scope.SetEnabled(false);
            if (scope.BeginPublication() is { } ticket) scope.Stage(ticket, null);
        }
    }
    private void DetachScope()
    {
        if (_scope is not { } scope) return;
        scope.Invalidated -= ScopeInvalidated; scope.ActivityChanged -= ScopeActivity;
        scope.Dispose(); _scope = null;
    }
    private void ScopeInvalidated()
    {
        _scopeValid = false; _pending = null; _lastPublish = null; _publishScheduled = false; _generation++;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Scope is no longer available; connect a live Scope from this document.");
        OverlayManager.Remove(InstanceGuid);
    }
    private void ScopeActivity(bool active)
    {
        if (!active) OverlayManager.Remove(InstanceGuid);
        else if (_active && _lastPublish is { } last)
        { _pending = last with { Interaction = true, Generation = _generation }; Publish(); }
    }
    private void OnInteraction(OverlayInteraction e)
    {
        if (!_active || _scope is not null || e.Owner != InstanceGuid) return;
        var snapshot = OverlayManager.Snapshot(InstanceGuid);
        var document = OnPingDocument();
        if (document is null) return;
        lock (_publishLock)
        {
            _events.Enqueue(CardJson.Serialize(e)); while (_events.Count > 32) _events.Dequeue();
            _interactionSolve = true;
            if (_solveScheduled) return;
            _solveScheduled = true;
        }
        // Read the manager snapshot now; SolveInstance reads it again after coalescing clicks.
        Message = $"{snapshot.SelectedKeys.Length} selected";
        document.ScheduleSolution(1, _ =>
        {
            lock (_publishLock) _solveScheduled = false;
            if (_active) ExpireSolution(false);
        });
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string requested = ""; da.GetData(4, ref requested); _requestedView = ViewportNames.Requested(requested);
        if (Params.Input[4].VolatileData.DataCount > 1) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Several View values supplied; using the last.");
        lock (_publishLock) _solveScheduled = false;
        var theme = _menuTheme;
        if (ThemeInputSet)
        {
            var name = "";
            da.GetData(2, ref name);
            if (!Theme.TryGet(name, out theme)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown theme '{name}'. Use Auto, Paper, Night, Smoke or Blueprint; falling back to Paper.");
        }
        lock (_publishLock) _selectedTheme = theme;
        var token = ScopeInput.Read(this, da, 3, out _scopeValid);
        if (_scope?.Lease.Token != token || _scope?.IsValid == false)
        {
            DetachScope();
            _interactionSolve = false; _pending = null;
            _events.Clear();
            OverlayManager.Remove(InstanceGuid);
            if (token is not null && OnPingDocument() is { } document)
            {
                _scope = ScopeBroker.ForDocument(document).Attach(token, this,
                    _ => OverlayManager.RefreshDomain(InstanceGuid), selectionOutput: true, eventsOutput: true);
                _scope.Invalidated += ScopeInvalidated;
                _scope.ActivityChanged += ScopeActivity;
                _scope.OutputRefreshRequested += () => _interactionSolve = true;
            }
            _generation++;
        }
        var wrapped = new List<IGH_Goo>(); var wrappedLinks = new List<IGH_Goo>();
        da.GetDataList(0, wrapped); da.GetDataList(1, wrappedLinks);
        var cards = wrapped.Select(g => g?.ScriptVariable()).OfType<CardSpec>().ToArray();
        var links = wrappedLinks.Select(g => g?.ScriptVariable()).OfType<Link>().ToArray();
        if (cards.Length != wrapped.Count || links.Length != wrappedLinks.Count)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cards must come from Conduit+ card components; Links must come from Link.");
            if (_scope?.BeginPublication() is { } ticket) _scope.Stage(ticket, null);
            QueuePublish([], []); return; }
        if (_scope is { } scope)
        {
            var suspendedInteraction = _rhinoSuspended && _interactionSolve;
            scope.SetEnabled(!suspendedInteraction);
            try { if (!suspendedInteraction) scope.Publish(cards, links.SelectMany(l => l.Keys)); }
            catch (ArgumentException e)
            {
                if (scope.BeginPublication() is { } ticket) scope.Stage(ticket, null);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); QueuePublish([], []); return;
            }
            da.SetDataList(0, scope.Current.SelectedKeys);
            da.SetDataList(1, scope.Current.Events.Select(e => CardJson.Serialize(OverlayInteraction.From(e))));
            Message = $"{scope.Current.SelectedKeys.Count} selected";
            scope.RecordOutputs(); scope.ReportDiagnostics();
            QueuePublish(cards, links); return;
        }
        var snapshot = OverlayManager.Snapshot(InstanceGuid);
        var membership = _membership = MembershipIndex.Create([new BrushRegistration("private", cards, links.SelectMany(l => l.Keys))], _membership);
        foreach (var diagnostic in membership.Diagnostics)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Card {diagnostic.CardId}, key '{diagnostic.Key}': {diagnostic.Message}");
        var live = membership.MemberKeys;
        var selection = _scopeValid ? snapshot.SelectedKeys.Where(live.Contains).ToArray() : [];
        Message = $"{selection.Length} selected";
        da.SetDataList(0, selection);
        lock (_publishLock) da.SetDataList(1, _events.ToArray());
        QueuePublish(cards, links);
    }
    private void QueuePublish(CardSpec[] cards, Link[] links)
    {
        var binding = OverlayManager.Binding(InstanceGuid);
        lock (_publishLock)
        {
            _pending = new(cards, links, _selectedTheme, _interactionSolve || (_pending?.Interaction ?? false), binding?.DocumentSerial, binding?.ViewId, _generation, _scope, _scopeValid, _requestedView);
            _interactionSolve = false;
            if (_publishScheduled) return;
            _publishScheduled = true;
        }
        RhinoApp.InvokeOnUiThread((Action)Publish);
    }
    private void Publish()
    {
        PendingPublish? pending;
        lock (_publishLock)
        {
            pending = _pending;
            _pending = null;
            _publishScheduled = false;
        }
        if (!_active || pending is null || pending.Generation != _generation || Locked) return;
        try
        {
            if (pending.Cards.Length == 0) { SetNotice("view", GH_RuntimeMessageLevel.Warning, null); OverlayManager.Remove(InstanceGuid); _lastPublish = null; return; }
            // A closed binding must not redirect a queued interaction to another active document.
            if (pending.Interaction && (pending.DocumentSerial is null || _rhinoSuspended)) return;
            var doc = pending.DocumentSerial is { } serial ? RhinoDoc.FromRuntimeSerialNumber(serial) : RhinoDoc.ActiveDoc;
            var view = pending.Interaction ? pending.ViewId is { } id ? doc?.Views.Find(id) : null : doc?.Views.ActiveView;
            if (doc is not null && pending.RequestedView is { } name)
            {
                // Keep the viewport only while it serves this same request, so changing View always looks the name up again.
                var bound = string.Equals(_lastPublish?.RequestedView, name, StringComparison.OrdinalIgnoreCase) ? pending.ViewId : null;
                var resolved = ViewportNames.Resolve(doc.Views.GetStandardRhinoViews().Select(v => (v.MainViewport.Id, v.MainViewport.Name)), name, bound);
                view = resolved.Id is { } target ? doc.Views.Find(target) : null;
                SetNotice("view", GH_RuntimeMessageLevel.Warning, view is null ? $"View \"{name}\" isn't open, so nothing is drawn. Open it or pick another View." : resolved.Duplicate ? $"More than one viewport is named \"{name}\". Drawing in one of them." : null);
                if (view is null) OverlayManager.Detach(doc, InstanceGuid);
            }
            else SetNotice("view", GH_RuntimeMessageLevel.Warning, null);
            if (doc is not null) _lastPublish = pending with { DocumentSerial = doc.RuntimeSerialNumber, ViewId = view?.MainViewport.Id };
            if (doc is not null && view is not null)
            {
                lock (_publishLock)
                {
                    _originals = pending.Cards;
                    _rhinoSuspended = false;
                    OverlayManager.Set(doc, view, InstanceGuid, _originals, pending.Links, _selectedTheme, pending.Scope, pending.BrushEnabled);
                    _lastPublish = pending with { DocumentSerial = doc.RuntimeSerialNumber, ViewId = view.MainViewport.Id };
                }
            }
            else if (doc is null)
            {
                // Headless Rhino document disposal can precede its close notification.
                // Treat an unavailable captured document exactly like the close callback.
                OnBoundDocumentClosed(InstanceGuid);
                OverlayManager.Remove(InstanceGuid);
            }
        }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+ Overlay: {e.Message}"); }
    }
}
