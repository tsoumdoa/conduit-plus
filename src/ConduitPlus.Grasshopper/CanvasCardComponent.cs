using System.Drawing;
using System.Windows.Forms;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using ConduitPlus.Rhino;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Undo;
using Rhino;

namespace ConduitPlus.Grasshopper;

public sealed class CanvasCardComponent : GH_Component
{
    private string _themeName = Theme.NewComponentDefault.Name;
    private Bitmap? _icon;
    private bool _active;
    private long _generation;
    internal CanvasCardState State { get; private set; } = new();
    internal Theme CardTheme => RhinoTheme.Resolve(Theme.ReadSelection(_themeName));
    internal string Placeholder { get; private set; } = "Connect one Conduit+ Card";
    internal Size2 BodySize { get; private set; } = new(280, 92);
    private string? _inputError;
    public CanvasCardComponent() : base("Canvas Card", "Canvas Card", "Display exactly one Conduit+ card on the Grasshopper canvas", "Conduit+", "Display") => GdiCanvas.RegisterTextMetrics();
    public override Guid ComponentGuid => new("b28d203e-2254-4b6f-94c0-d4db7e251c5a");
    protected override Bitmap Icon => _icon ??= CardComponent.CreateIcon("C");
    public override void CreateAttributes() => m_attributes = new CanvasCardAttributes(this);
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Card", "C", "Exactly one Metric, Bar, Table, Line, Text or Legend card across the whole tree", GH_ParamAccess.tree);
        p[0].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        CardSpec? spec = null; string? error = null;
        if (da.GetDataTree(0, out GH_Structure<IGH_Goo> tree) && tree.DataCount > 0)
        {
            if (tree.DataCount != 1) error = "Canvas Card requires exactly one valid Card across the entire input tree.";
            else
            {
                spec = tree.AllData(true).SingleOrDefault()?.ScriptVariable() as CardSpec;
                if (spec is null) error = "Input must be a Conduit+ Card.";
                else
                {
                    try
                    {
                        spec.Validate();
                        if (!Enum.IsDefined(spec.Kind)) throw new ArgumentException("Unsupported card kind.");
                        spec = CardJson.Deserialize<CardSpec>(CardJson.Serialize(spec));
                    }
                    catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or NullReferenceException)
                    { error = e.Message; spec = null; }
                }
            }
        }
        if (error is not null) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        WarnTheme();
        var snapshot = error is null ? spec : null;
        var generation = _generation;
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            if (!_active || generation != _generation || Locked) return;
            var oldSize = BodySize;
            var attributes = Attributes as CanvasCardAttributes;
            attributes?.CancelGesture();
            State.Replace(snapshot);
            _inputError = error;
            MeasureBody();
            Placeholder = error is null ? "Connect one Conduit+ Card" : "Invalid Card input";
            if (snapshot is null) attributes?.ReleaseFrame();
            if (oldSize != BodySize) attributes?.ExpireLayout();
            attributes?.RequestDisplay();
        }));
    }
    private void WarnTheme()
    {
        if (!Theme.TryGet(_themeName, out _))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Theme '{_themeName}' is unavailable; using Paper.");
    }
    private void MeasureBody() => BodySize = State.Spec is { } spec ? CardMeasure.Measure(spec, CardTheme) : new(280, 92);
    public override bool Write(GH_IWriter writer) { writer.SetString("ConduitPlus.CanvasTheme", _themeName); return base.Write(writer); }
    public override bool Read(GH_IReader reader)
    {
        var size = BodySize;
        var result = base.Read(reader);
        var savedName = "";
        var found = reader.TryGetString("ConduitPlus.CanvasTheme", ref savedName);
        _themeName = found ? savedName : Theme.ReadSelection(null).Name;
        if (!found && reader.ItemExists("ConduitPlus.CanvasTheme")) _themeName = "Invalid saved theme";
        WarnTheme(); MeasureBody();
        if (size != BodySize) (Attributes as CanvasCardAttributes)?.ExpireLayout();
        return result;
    }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendSeparator(menu);
        var themes = Menu_AppendItem(menu, "Theme");
        foreach (var theme in Theme.Presets)
            Menu_AppendItem(themes.DropDown, theme.Name, (_, _) => ChangeTheme(theme.Name), true, string.Equals(_themeName, theme.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void ChangeTheme(string name)
    {
        if (name == _themeName) return;
        RecordUndoEvent("Canvas Card theme", new ThemeUndoAction(InstanceGuid, _themeName, name));
        ApplyTheme(name);
    }
    private void ApplyTheme(string name)
    {
        var size = BodySize;
        (Attributes as CanvasCardAttributes)?.CancelGesture();
        _themeName = name; ClearRuntimeMessages(); WarnTheme();
        if (_inputError is not null) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _inputError);
        MeasureBody();
        if (size != BodySize) Attributes.ExpireLayout();
        (Attributes as CanvasCardAttributes)?.RequestDisplay();
        OnPingDocument()?.Modified();
    }
    public sealed class ThemeUndoAction : GH_ObjectUndoAction
    {
        private Guid _owner;
        private string _before = "Paper", _after = "Paper";
        public ThemeUndoAction() : base(Guid.Empty) { }
        public ThemeUndoAction(Guid owner, string before, string after) : base(owner) { _owner = owner; _before = before; _after = after; }
        public override bool ExpiresSolution => false;
        public override bool ExpiresDisplay => true;
        protected override void Object_Undo(GH_Document doc, IGH_DocumentObject obj) { if (obj is CanvasCardComponent card) card.ApplyTheme(_before); }
        protected override void Object_Redo(GH_Document doc, IGH_DocumentObject obj) { if (obj is CanvasCardComponent card) card.ApplyTheme(_after); }
        protected override void Internal_Undo(GH_Document doc) => Object_Undo(doc, doc.FindObject(_owner, false));
        protected override void Internal_Redo(GH_Document doc) => Object_Redo(doc, doc.FindObject(_owner, false));
        public override bool Write(GH_IWriter writer)
        { writer.SetGuid("Owner", _owner); writer.SetString("Before", _before); writer.SetString("After", _after); return base.Write(writer); }
        public override bool Read(GH_IReader reader)
        { _owner = reader.GetGuid("Owner"); _before = reader.GetString("Before"); _after = reader.GetString("After"); return base.Read(reader); }
    }
    public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); Activate(); }
    public override void RemovedFromDocument(GH_Document document) { Deactivate(true); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);
        if (context == GH_DocumentContext.Close) Deactivate(true);
        else if (context == GH_DocumentContext.Unloaded) Deactivate(false);
        else if (context is GH_DocumentContext.Loaded or GH_DocumentContext.Open) Activate();
        else if (context == GH_DocumentContext.Lock) (Attributes as CanvasCardAttributes)?.CancelGesture();
    }
    private void Activate()
    {
        if (_active) return;
        _generation++; _active = true;
        RhinoApp.AppSettingsChanged += AppearanceChanged;
        AppearanceChanged(this, EventArgs.Empty);
    }
    private void AppearanceChanged(object? sender, EventArgs e) => RhinoApp.InvokeOnUiThread((Action)(() =>
    {
        if (!_active || Theme.ReadSelection(_themeName) != Theme.Auto) return;
        var size = BodySize;
        var attributes = Attributes as CanvasCardAttributes;
        attributes?.CancelGesture();
        MeasureBody();
        if (size != BodySize) attributes?.ExpireLayout();
        attributes?.RequestDisplay();
    }));
    private void Deactivate(bool clearState)
    {
        RhinoApp.AppSettingsChanged -= AppearanceChanged;
        _active = false; var generation = ++_generation;
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            if (_active || generation != _generation) return;
            (Attributes as CanvasCardAttributes)?.Detach();
            // GH 8.20 also unloads on a document-tab switch, then loads without expiring solved objects.
            // Keep managed results/local state there; all images, callbacks and subscriptions are released.
            if (clearState) State = new();
            MeasureBody();
        }));
    }
}
