using System.Drawing;
using ConduitPlus.Core;
using ConduitPlus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
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
        p.AddIntegerParameter("Visible rows", "N", "Rows shown at once; more rows scroll (wheel or scrollbar)", GH_ParamAccess.item, TableScroll.DefaultVisibleRows);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var headers = new List<string>(); var keys = new List<string>(); string title = "Structural results";
        if (!da.GetDataList(0, headers) || !da.GetDataTree(1, out GH_Structure<GH_String> tree)) return;
        da.GetDataList(2, keys); da.GetData(3, ref title);
        var visibleRows = TableScroll.DefaultVisibleRows; da.GetData(5, ref visibleRows);
        if (keys.Count != 0 && keys.Count != tree.PathCount) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Key count must match row count."); return; }
        var rows = tree.Branches.Select((branch, i) => new TableRow(keys.Count == 0 ? tree.Paths[i].ToString() : keys[i], branch.Select(v => v?.Value).ToArray())).ToArray();
        try { Output(da, new CardSpec { Id = CardId, Kind = CardKind.Table, Title = title, Headers = headers.ToArray(), Rows = rows, Width = 380, MaxRows = visibleRows, WiredSlot = ReadSlot(da, 4) }); }
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
    private readonly object _publishLock = new();
    private bool _publishScheduled;
    private sealed record PendingPublish(CardSpec[] Cards, Link[] Links, bool Interaction, uint? DocumentSerial, Guid? ViewId);
    private PendingPublish? _pending;
    private bool _solveScheduled, _interactionSolve;
    private bool _active;
    private Bitmap? _icon;
    public OverlayComponent() : base("Overlay", "Overlay", "Display cards in the active Rhino viewport; click bars/rows for keyed selection", "Conduit+", "Display") { }
    public override Guid ComponentGuid => new("a7f32adb-ce5f-49c8-a33f-e3834a0ae204");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("+");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cards", "C", "Metric, Bar or Table cards", GH_ParamAccess.list);
        p.AddGenericParameter("Links", "L", "Optional keyed geometry from Link", GH_ParamAccess.list); p[1].Optional = true;
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
    private void Deactivate() { _active = false; lock (_publishLock) { _pending = null; _solveScheduled = false; _interactionSolve = false; } OverlayManager.Interaction -= OnInteraction; RhinoApp.InvokeOnUiThread((Action)(() => OverlayManager.Remove(InstanceGuid))); }
    private void OnInteraction(OverlayInteraction e)
    {
        if (!_active || e.Owner != InstanceGuid) return;
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
        lock (_publishLock) _solveScheduled = false;
        var wrapped = new List<IGH_Goo>(); var wrappedLinks = new List<IGH_Goo>();
        da.GetDataList(0, wrapped); da.GetDataList(1, wrappedLinks);
        var cards = wrapped.Select(g => g?.ScriptVariable()).OfType<CardSpec>().ToArray();
        var links = wrappedLinks.Select(g => g?.ScriptVariable()).OfType<Link>().ToArray();
        if (cards.Length != wrapped.Count || links.Length != wrappedLinks.Count)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cards must come from Conduit+ card components; Links must come from Link."); QueuePublish([], []); return; }
        var snapshot = OverlayManager.Snapshot(InstanceGuid);
        var live = cards.SelectMany(BrushState.Keys).Concat(links.SelectMany(l => l.Keys)).ToHashSet(StringComparer.Ordinal);
        var selection = snapshot.SelectedKeys.Where(live.Contains).ToArray();
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
            _pending = new(cards, links, _interactionSolve || (_pending?.Interaction ?? false), binding?.DocumentSerial, binding?.ViewId);
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
        if (!_active || pending is null) return;
        try
        {
            if (pending.Cards.Length == 0) { OverlayManager.Remove(InstanceGuid); return; }
            // A closed binding must not redirect a queued interaction to another active document.
            if (pending.Interaction && pending.DocumentSerial is null) return;
            var doc = pending.DocumentSerial is { } serial ? RhinoDoc.FromRuntimeSerialNumber(serial) : RhinoDoc.ActiveDoc;
            var view = pending.Interaction && pending.ViewId is { } id ? doc?.Views.Find(id) ?? doc?.Views.ActiveView : doc?.Views.ActiveView;
            if (doc is not null && view is not null) OverlayManager.Set(doc, view, InstanceGuid, pending.Cards, pending.Links);
        }
        catch (Exception e) { RhinoApp.WriteLine($"Conduit+ Overlay: {e.Message}"); }
    }
}
