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
    private const string CardsDescription = "Metric, Bar, Table, Line, Title, Text, Legend or Key Legend cards in tree order; up to 12 unique cards";
    private string _themeName = Theme.NewComponentDefault.Name;
    private Bitmap? _icon;
    private bool _active;
    private long _generation;
    internal CanvasCardState State { get; private set; } = new();
    internal Theme CardTheme
    {
        get
        {
            var background = global::Grasshopper.GUI.Canvas.GH_Skin.canvas_back;
            return RhinoTheme.Resolve(Theme.ReadSelection(_themeName)) with
            { PlainTitleInk = TitleMetrics.InkForBackground(new(background.R, background.G, background.B)) };
        }
    }
    internal string Placeholder { get; private set; } = "Connect Conduit+ Cards";
    internal Size2 BodySize { get; private set; } = new(280, 92);
    private readonly List<(GH_RuntimeMessageLevel Level, string Message)> _inputMessages = [];
    private MembershipIndex? _membership;
    private ScopeBinding? _scope;
    private bool _scopeValid = true;
    private readonly InteractionRefreshQueue _privateRefresh = new();
    private readonly ParticipantLease _privateLease = new(new(0, Guid.Empty), "private", Guid.NewGuid());
    private bool _privateScheduled;
    internal bool BrushEnabled => _scopeValid && (_scope?.IsActive ?? true);
    public CanvasCardComponent() : base("Canvas Card", "Canvas Card", "Display a stack of Conduit+ cards on the Grasshopper canvas", "Conduit+", "Display") {
        // Grasshopper constructs every component while scanning plug-ins, so this must not start Display's global hooks.
        GdiCanvas.RegisterTextMetrics();
        ObjectChanged += (_, e) =>
        {
            if (e.Type != GH_ObjectEventType.Enabled) return;
            _scope?.SetEnabled(!Locked);
            State.CancelHover();
            if (!Locked) RefreshScope();
        };
        State.Changed += PrivateChanged;
        _privateRefresh.Watch(_privateLease, State.Current, false);
    }
    public override Guid ComponentGuid => new("b28d203e-2254-4b6f-94c0-d4db7e251c5a");
    protected override Bitmap Icon => _icon ??= ConduitIcons.Create(ComponentIcon.CanvasCard);
    public override void CreateAttributes() => m_attributes = new CanvasCardAttributes(this);
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cards", "C", CardsDescription, GH_ParamAccess.tree);
        p[0].Optional = true;
        p.AddGenericParameter("Scope", "S", "Optional explicit selection Scope; exactly one token across the tree", GH_ParamAccess.tree);
        p[1].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddTextParameter("Selection", "S", "Sorted selected member keys for the entire domain", GH_ParamAccess.list);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var cards = new List<CardSpec>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        _inputMessages.Clear();
        void Notice(GH_RuntimeMessageLevel level, string message)
        { _inputMessages.Add((level, message)); AddRuntimeMessage(level, message); }
        var empty = 0;
        var itemCount = 0;
        if (da.GetDataTree(0, out GH_Structure<IGH_Goo> tree))
        {
            var index = 0;
            foreach (var goo in tree.AllData(false))
            {
                var value = goo?.ScriptVariable();
                if (value is null) empty++;
                else if (value is not CardSpec spec)
                {
                    var type = value switch { string => "text", double or float or int or long or decimal => "a number", bool => "a boolean", _ => goo!.TypeName };
                    Notice(GH_RuntimeMessageLevel.Error, $"Cards item {index} is {type}, not a Conduit+ card.");
                }
                else
                {
                    try
                    {
                        spec.Validate();
                        if (!Enum.IsDefined(spec.Kind)) throw new ArgumentException("Unsupported card kind.");
                        if (!ids.Add(spec.Id)) Notice(GH_RuntimeMessageLevel.Warning, $"Card '{spec.Title}' is connected twice; showing it once.");
                        else if (cards.Count < CanvasStackLayout.Limit)
                            cards.Add(spec.Kind == CardKind.Title ? spec with { Background = TitleBackground.Card } : spec);
                    }
                    catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or NullReferenceException)
                    { Notice(GH_RuntimeMessageLevel.Error, $"Cards item [{index}]: {e.Message}"); }
                }
                index++;
            }
            itemCount = index;
        }
        var emptySources = Params.Input[0].Sources.Where(p => p.VolatileData.DataCount == 0).ToArray();
        empty += emptySources.Length;
        if (empty > 0)
        {
            var names = Params.Input[0].Sources.Where(p => p.VolatileData.DataCount == 0 || p.VolatileData.AllData(false).Any(g => g?.ScriptVariable() is null))
                .Select(p => p.Attributes?.GetTopLevel.DocObject as GH_Component
                    ?? OnPingDocument()?.Objects.OfType<GH_Component>().FirstOrDefault(c => c.Params.Output.Contains(p)))
                .OfType<GH_Component>().Select(p => p.Name).Distinct().ToArray();
            Notice(GH_RuntimeMessageLevel.Warning,
                $"Showing {cards.Count} of {itemCount + emptySources.Length} cards. {empty} {(empty == 1 ? "is empty because its component has an error" : "are empty because their components have errors")}{(names.Length > 0 ? " (" + string.Join(", ", names) + ")" : "")}.");
        }
        if (ids.Count > CanvasStackLayout.Limit)
            Notice(GH_RuntimeMessageLevel.Warning, $"Showing 12 of {ids.Count} cards; use another Canvas Card for the rest.");
        _membership = MembershipIndex.Create([new BrushRegistration("canvas", cards, [])], _membership);
        foreach (var diagnostic in _membership.Diagnostics)
            Notice(GH_RuntimeMessageLevel.Warning, $"Card '{cards.First(c => c.Id == diagnostic.CardId).Title}', key '{diagnostic.Key}': {diagnostic.Message}");
        WarnTheme();
        var token = ScopeInput.Read(this, da, 1, out _scopeValid);
        if (_scope?.Lease.Token != token || _scope?.IsValid == false)
        {
            DetachScope();
            if (token is not null && OnPingDocument() is { } document)
            {
                _scope = ScopeBroker.ForDocument(document).Attach(token, this, _ => RefreshScope(), selectionOutput: true);
                _scope.Invalidated += ScopeInvalidated;
                _scope.ActivityChanged += ScopeActivity;
            }
        }
        State.Bind(_scope, InstanceGuid, _scopeValid);
        var attributes = Attributes as CanvasCardAttributes;
        attributes?.CancelGesture();
        var oldSize = BodySize;
        State.ReplaceCards(cards);
        if (_scope is { } scope)
        {
            scope.Publish(cards.ToArray(), []);
            da.SetDataList(0, scope.Current.SelectedKeys);
            scope.RecordOutputs(); scope.ReportDiagnostics();
        }
        else
        {
            da.SetDataList(0, _scopeValid ? State.Current.SelectedKeys : []);
            _privateRefresh.RecordOutputs(_privateLease, State.Current);
        }
        MeasureBody();
        Placeholder = _inputMessages.FirstOrDefault(m => m.Level == GH_RuntimeMessageLevel.Error).Message
            ?? (_inputMessages.Count == 0 ? "Connect Conduit+ Cards" : "No card received. Check upstream components.");
        if (cards.Count == 0) attributes?.ReleaseFrame();
        if (oldSize != BodySize) attributes?.ExpireLayout();
        attributes?.RequestDisplay();
    }
    private void RefreshScope()
    {
        if (!_active || Locked) return;
        State.Refresh();
        (Attributes as CanvasCardAttributes)?.RequestDisplay();
    }
    private void DetachScope()
    {
        if (_scope is not { } scope) return;
        scope.Invalidated -= ScopeInvalidated; scope.ActivityChanged -= ScopeActivity;
        scope.Dispose(); _scope = null;
    }
    private void ScopeInvalidated()
    {
        _scopeValid = false;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Scope is no longer available; connect a live Scope from this document.");
        State.Bind(null, InstanceGuid, false);
        (Attributes as CanvasCardAttributes)?.CancelGesture();
        (Attributes as CanvasCardAttributes)?.RequestDisplay();
    }
    private void ScopeActivity(bool active)
    {
        State.CancelHover();
        if (active) RefreshScope();
        else (Attributes as CanvasCardAttributes)?.Detach();
    }
    private void PrivateChanged(InteractionChange change)
    {
        if (_scope is not null || !_scopeValid || !_active) return;
        _privateRefresh.Observe(_privateLease, change);
        if (change.Kind != InteractionChangeKind.Selection) return; // SolveInstance reads reconciled private outputs directly.
        var decision = _privateRefresh.Take();
        if (decision.Refresh is null) return;
        if (_privateScheduled) return;
        SchedulePrivateOutputs();
    }
    private void SchedulePrivateOutputs()
    {
        if (OnPingDocument() is not { } document || _privateScheduled) return;
        _privateScheduled = true;
        var generation = _generation;
        document.ScheduleSolution(1, _ =>
        {
            if (!_active || generation != _generation) return;
            _privateScheduled = false;
            if (!Locked && _privateRefresh.IsDirty(_privateLease)) ExpireSolution(false);
        });
    }

    private void WarnTheme()
    {
        if (!Theme.TryGet(_themeName, out _))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Theme '{_themeName}' is unavailable; using Paper.");
    }
    private void MeasureBody()
    {
        OverlayManager.Start();
        BodySize = new CanvasStackLayout(State.Specs, CardTheme).Size;
    }
    public override bool Write(GH_IWriter writer) { writer.SetString("ConduitPlus.CanvasTheme", _themeName); return base.Write(writer); }
    public override bool Read(GH_IReader reader)
    {
        var size = BodySize;
        var result = base.Read(reader);
        Params.Input[0].Name = "Cards";
        Params.Input[0].Description = CardsDescription;
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
        foreach (var (level, message) in _inputMessages) AddRuntimeMessage(level, message);
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
        else if (context == GH_DocumentContext.Lock) { _scope?.SetEnabled(false); (Attributes as CanvasCardAttributes)?.CancelGesture(); }
        else if (context == GH_DocumentContext.Unlock) { _scope?.SetEnabled(true); RefreshScope(); }
    }
    private void Activate()
    {
        if (_active) return;
        OverlayManager.Start();
        _generation++; _active = true;
        RhinoApp.AppSettingsChanged += AppearanceChanged;
        AppearanceChanged(this, EventArgs.Empty);
        RefreshScope();
        if (_scope is null && _privateRefresh.IsDirty(_privateLease)) SchedulePrivateOutputs();
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
        if (clearState) DetachScope();
        State.CancelHover();
        _privateScheduled = false;
        _active = false; var generation = ++_generation;
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            if (_active || generation != _generation) return;
            (Attributes as CanvasCardAttributes)?.Detach();
            // GH 8.20 also unloads on a document-tab switch, then loads without expiring solved objects.
            // Keep managed results/local state there; all images, callbacks and subscriptions are released.
            if (clearState) { State.Changed -= PrivateChanged; State = new(); State.Changed += PrivateChanged; _privateRefresh.Clear(); _privateRefresh.Watch(_privateLease, State.Current, false); }
            MeasureBody();
        }));
    }
}
