using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;

namespace ConduitPlus.Grasshopper;

public sealed class AssemblyInfo : GH_AssemblyInfo
{
    public override string Name => "Conduit+";
    public override string Description => "Structural HUD renderer feasibility spike";
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
    protected string CardId => InstanceGuid.ToString("N");
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Card", "C", "Connect to Overlay", GH_ParamAccess.item);
    protected static Slot? ReadSlot(IGH_DataAccess da, int index)
    { string slot = ""; return da.GetData(index, ref slot) && Enum.TryParse<Slot>(slot, true, out var value) ? value : null; }
}

public sealed class MetricComponent : CardComponent
{
    public MetricComponent() : base("Metric", "Metric", "A structural result metric") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae201");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Value", "V", "Formatted value; missing displays an em dash", GH_ParamAccess.item); p[0].Optional = true;
        p.AddTextParameter("Title", "T", "Metric title", GH_ParamAccess.item, "Metric");
        p.AddTextParameter("Unit", "U", "Unit suffix", GH_ParamAccess.item, "");
        p.AddTextParameter("Slot", "S", "Optional wired slot: tl,tr,bl,br,lc,rc", GH_ParamAccess.item); p[3].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string value = "", title = "Metric", unit = ""; var hasValue = da.GetData(0, ref value); da.GetData(1, ref title); da.GetData(2, ref unit);
        Output(da, new CardSpec { Id = CardId, Kind = CardKind.Metric, Title = title, Value = hasValue ? value : null, Unit = unit, WiredSlot = ReadSlot(da, 3) });
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
        p.AddNumberParameter("Threshold", "Th", "Positive utilisation threshold", GH_ParamAccess.item, 1);
        p.AddTextParameter("Slot", "S", "tl,tr,bl,br,lc,rc", GH_ParamAccess.item); p[5].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var values = new List<double>(); var labels = new List<string>(); var keys = new List<string>(); string title = "Member utilisation"; double threshold = 1;
        if (!da.GetDataList(0, values)) return; da.GetDataList(1, labels); da.GetDataList(2, keys); da.GetData(3, ref title); da.GetData(4, ref threshold);
        if (labels.Count == 0) labels = Enumerable.Range(1, values.Count).Select(i => $"M{i:00}").ToList(); if (keys.Count == 0) keys = [.. labels];
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Bar, Title = title, Values = values.Select(v => double.IsNaN(v) ? (double?)null : v).ToArray(), Labels = labels.ToArray(), Keys = keys.ToArray(), Threshold = threshold, WiredSlot = ReadSlot(da, 5) }); }
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
        p.AddTextParameter("Slot", "S", "tl,tr,bl,br,lc,rc", GH_ParamAccess.item); p[4].Optional = true;
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var headers = new List<string>(); var keys = new List<string>(); string title = "Structural results";
        if (!da.GetDataList(0, headers) || !da.GetDataTree(1, out GH_Structure<GH_String> tree)) return;
        da.GetDataList(2, keys); da.GetData(3, ref title);
        if (keys.Count != 0 && keys.Count != tree.PathCount) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Key count must match row count."); return; }
        var rows = tree.Branches.Select((branch, i) => new TableRow(keys.Count == 0 ? tree.Paths[i].ToString() : keys[i], branch.Select(v => v?.Value).ToArray())).ToArray();
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Table, Title = title, Headers = headers.ToArray(), Rows = rows, Width = 380, WiredSlot = ReadSlot(da, 4) }); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class OverlayComponent : GH_Component
{
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private readonly Queue<string> _events = new();
    private bool _publishing;
    private (CardSpec[] Cards, RendererKind Renderer)? _pending;
    private bool _active;
    private Bitmap? _icon;
    public OverlayComponent() : base("Overlay", "Overlay", "Display cards in the active Rhino viewport; click bars/rows for keyed selection", "Conduit+", "Display") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae204");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("+");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cards", "C", "Metric, Bar or Table cards", GH_ParamAccess.list);
        p.AddTextParameter("Renderer", "R", "Native or Web", GH_ParamAccess.item, "Native");
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    { p.AddTextParameter("Selection", "S", "Clicked member keys", GH_ParamAccess.list); p.AddTextParameter("Events", "E", "Last 32 click events as JSON", GH_ParamAccess.list); }
    public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); _active = true; OverlayManager.Interaction += OnInteraction; }
    public override void RemovedFromDocument(GH_Document document) { Deactivate(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) Deactivate();
        else if (context == GH_DocumentContext.Loaded && !_active) { _active = true; OverlayManager.Interaction += OnInteraction; }
    }
    private void Deactivate() { _active = false; _pending = null; OverlayManager.Interaction -= OnInteraction; RhinoApp.InvokeOnUiThread((Action)(() => OverlayManager.Remove(InstanceGuid))); }
    private void OnInteraction(OverlayInteraction e)
    {
        if (!_active || e.Owner != InstanceGuid) return;
        if (!_selection.Add(e.Key)) _selection.Remove(e.Key);
        _events.Enqueue(CardJson.Serialize(e)); while (_events.Count > 32) _events.Dequeue();
        OnPingDocument()?.ScheduleSolution(1, _ => { if (_active) ExpireSolution(false); });
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var wrapped = new List<IGH_Goo>(); string renderer = "Native";
        if (!da.GetDataList(0, wrapped)) { RhinoApp.InvokeOnUiThread((Action)(() => OverlayManager.Remove(InstanceGuid))); return; }
        da.GetData(1, ref renderer);
        if (!Enum.TryParse<RendererKind>(renderer, true, out var kind)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Renderer must be Web or Native."); return; }
        var cards = wrapped.Select(g => g.ScriptVariable()).OfType<CardSpec>().Select(c => c with { SelectedKeys = c.Keys.Concat(c.Rows.Select(r => r.Key)).Where(_selection.Contains).ToArray() }).ToArray();
        if (cards.Length != wrapped.Count) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cards must come from Conduit+ card components."); return; }
        da.SetDataList(0, _selection.Order()); da.SetDataList(1, _events.ToArray());
        _pending = (cards, kind);
        RhinoApp.InvokeOnUiThread((Action)(() => { if (!_publishing && _active) { _publishing = true; OverlayManager.RunOnUi(PublishAsync); } }));
    }
    private async Task PublishAsync()
    {
        try
        {
            while (_pending is { } next && _active)
            {
                _pending = null;
                var doc = RhinoDoc.ActiveDoc; var view = doc?.Views.ActiveView; if (doc is null || view is null) return;
                await OverlayManager.SetAsync(doc, view, InstanceGuid, next.Cards, next.Renderer);
            }
        }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+ Overlay: {e.Message}"); }
        finally { _publishing = false; }
    }
}
