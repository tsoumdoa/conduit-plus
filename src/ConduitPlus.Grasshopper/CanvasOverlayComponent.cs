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

public sealed class CanvasOverlayComponent : GH_Component, ICanvasOverlayDisplay
{
    private Theme _menuTheme = Theme.NewComponentDefault, _selectedTheme = Theme.NewComponentDefault;
    private Bitmap? _icon;
    private bool _active, _scopeValid = true, _scheduled;
    private long _generation;
    private ScopeBinding? _scope;
    private MembershipIndex? _membership;
    private readonly InteractionRefreshQueue _refresh = new();
    private readonly ParticipantLease _lease = new(new(0, Guid.Empty), "private", Guid.NewGuid());
    public CanvasCardState State { get; } = new("canvas-pinned", retainSlots: true);
    public Guid OwnerId => InstanceGuid;
    public object? Document => OnPingDocument();
    public bool Disabled => Locked || OnPingDocument()?.Enabled != true;
    public bool BrushEnabled => _scopeValid && (_scope?.IsActive ?? true);
    public Theme Theme
    {
        get
        {
            var background = global::Grasshopper.GUI.Canvas.GH_Skin.canvas_back;
            return RhinoTheme.Resolve(_selectedTheme) with { PlainTitleInk = TitleMetrics.InkForBackground(new(background.R, background.G, background.B)) };
        }
    }
    private bool ThemeInputSet => Params.Input[1] is Param_TextOption { HasValue: true };
    public CanvasOverlayComponent() : base("Canvas Overlay", "Canvas Overlay", "Pin Conduit+ cards to the Grasshopper canvas window", "Conduit+", "Display")
    {
        GdiCanvas.RegisterTextMetrics();
        State.Changed += PrivateChanged;
        _refresh.Watch(_lease, State.Current, false);
        ObjectChanged += (_, e) =>
        {
            if (e.Type != GH_ObjectEventType.Enabled) return;
            CanvasOverlayHost.Invalidate(true); State.CancelHover(); _scope?.SetEnabled(!Disabled);
            if (!Disabled) Refresh();
        };
    }
    public override Guid ComponentGuid => new("8d2f45de-2973-4b53-a55b-594870589246");
    protected override Bitmap Icon => _icon ??= ConduitIcons.Create(ComponentIcon.CanvasOverlay);
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Cards", "C", "Conduit+ cards, collected once in tree order; each card's Slot controls its rail", GH_ParamAccess.tree); p[0].Optional = true;
        p.AddParameter(new Param_TextOption { Options = InputOptions.Themes }, "Theme", "T", "Auto, Paper, Night, Smoke or Blueprint; a set input overrides the Theme menu", GH_ParamAccess.item); p[1].Optional = true;
        p.AddGenericParameter("Scope", "S", "Optional explicit selection Scope; exactly one token across the tree", GH_ParamAccess.tree); p[2].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddTextParameter("Selection", "S", "Sorted selected member keys for the entire domain, including overflow cards", GH_ParamAccess.list);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        CanvasOverlayHost.Invalidate(true);
        var cards = new List<CardSpec>(); var seen = new HashSet<string>(StringComparer.Ordinal); var empty = 0;
        da.GetDataTree(0, out GH_Structure<IGH_Goo> tree);
        var items = tree?.AllData(false).ToArray() ?? [];
        for (var i = 0; i < items.Length; i++)
        {
            var value = items[i]?.ScriptVariable();
            if (value is null) { empty++; continue; }
            if (value is not CardSpec card) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cards item {i} is not a Conduit+ card ({items[i].TypeName})."); continue; }
            try { card.Validate(); if (!Enum.IsDefined(card.Kind)) throw new ArgumentException("Unsupported card kind."); }
            catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or NullReferenceException)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cards item {i}: {e.Message}"); continue; }
            if (!seen.Add(card.Id)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cards item {i} is connected twice; connect each card once."); continue; }
            cards.Add(card.Kind == CardKind.Title ? card with { Form = card.Form with { Width = card.Width } } : card);
        }
        var emptySources = Params.Input[0].Sources.Where(p => p.VolatileData.DataCount == 0).ToArray(); empty += emptySources.Length;
        if (empty > 0)
        {
            var names = Params.Input[0].Sources.Where(p => p.VolatileData.DataCount == 0 || p.VolatileData.AllData(false).Any(g => g?.ScriptVariable() is null))
                .Select(p => p.Attributes?.GetTopLevel.DocObject).OfType<GH_Component>().Select(p => p.Name).Distinct().ToArray();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Showing {cards.Count} of {items.Length + emptySources.Length} cards. {empty} empty upstream card(s){(names.Length > 0 ? " (" + string.Join(", ", names) + ")" : "")}.");
        }
        _selectedTheme = _menuTheme;
        if (ThemeInputSet)
        {
            var name = ""; da.GetData(1, ref name);
            if (!Core.Theme.TryGet(name, out _selectedTheme)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown theme '{name}'; using Paper.");
        }
        var token = ScopeInput.Read(this, da, 2, out _scopeValid);
        if (_scopeValid) SetNotice("scope", null);
        if (_scope?.Lease.Token != token || _scope?.IsValid == false)
        {
            DetachScope();
            if (token is not null && OnPingDocument() is { } document)
            {
                _scope = ScopeBroker.ForDocument(document).Attach(token, this, _ => Refresh(), selectionOutput: true);
                _scope.Invalidated += ScopeInvalidated; _scope.ActivityChanged += ScopeActivity;
            }
        }
        State.Bind(_scope, InstanceGuid, _scopeValid);
        State.ReplaceCards(cards);
        _membership = MembershipIndex.Create([new BrushRegistration("canvas-pinned", cards, [])], _membership);
        foreach (var d in _membership.Diagnostics) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Card {d.CardId}, key '{d.Key}': {d.Message}");
        if (_scope is { } scope) { scope.Publish(cards, []); scope.RecordOutputs(); scope.ReportDiagnostics(); }
        else _refresh.RecordOutputs(_lease, State.Current);
        da.SetDataList(0, _scopeValid ? State.Current.SelectedKeys : []);
        CanvasOverlayHost.Publish(this);
    }
    private void Refresh()
    {
        if (!_active || Disabled) return;
        State.Refresh(); CanvasOverlayHost.Invalidate();
    }
    private void ScopeInvalidated()
    {
        _scopeValid = false; State.Bind(null, InstanceGuid, false);
        SetNotice("scope", "Scope is no longer available; connect a live Scope from this document.", true);
        CanvasOverlayHost.Invalidate(true);
    }
    private void ScopeActivity(bool active) { State.CancelHover(); if (active) Refresh(); else CanvasOverlayHost.Invalidate(true); }
    private void DetachScope()
    {
        if (_scope is not { } scope) return;
        scope.Invalidated -= ScopeInvalidated; scope.ActivityChanged -= ScopeActivity; scope.Dispose(); _scope = null;
    }
    private void PrivateChanged(InteractionChange change)
    {
        if (_scope is not null || !_scopeValid || !_active) return;
        _refresh.Observe(_lease, change);
        if (change.Kind == InteractionChangeKind.Selection && _refresh.Take().Refresh is not null) ScheduleOutputs();
    }
    private void ScheduleOutputs()
    {
        if (_scheduled || OnPingDocument() is not { } document) return;
        _scheduled = true; var generation = _generation;
        document.ScheduleSolution(1, _ =>
        {
            if (!_active || generation != _generation) return;
            _scheduled = false;
            if (!Disabled && _refresh.IsDirty(_lease)) ExpireSolution(false);
        });
    }
    private readonly Dictionary<string, (GH_RuntimeMessageLevel Level, string Text)> _notices = [];
    private readonly List<(GH_RuntimeMessageLevel Level, string Text)> _solveMessages = [];
    private bool _replaying;
    public override void AddRuntimeMessage(GH_RuntimeMessageLevel level, string text) { if (!_replaying) _solveMessages.Add((level, text)); base.AddRuntimeMessage(level, text); }
    public override void ClearData() { _solveMessages.Clear(); base.ClearData(); }
    public void SetNotice(string kind, string? message, bool error = false)
    {
        var level = error ? GH_RuntimeMessageLevel.Error : GH_RuntimeMessageLevel.Remark;
        if (message is null) { if (!_notices.Remove(kind)) return; }
        else { if (_notices.TryGetValue(kind, out var existing) && existing == (level, message)) return; _notices[kind] = (level, message); }
        ReplayNotices();
    }
    private void ReplayNotices()
    {
        _replaying = true;
        try { ClearRuntimeMessages(); foreach (var m in _solveMessages.Concat(_notices.Values)) base.AddRuntimeMessage(m.Level, m.Text); }
        finally { _replaying = false; }
    }
    protected override void AfterSolveInstance() { base.AfterSolveInstance(); ReplayNotices(); }
    public override bool Write(GH_IWriter writer) { writer.SetString("ConduitPlus.Theme", _menuTheme.Name); return base.Write(writer); }
    public override bool Read(GH_IReader reader)
    {
        var result = base.Read(reader); var name = "";
        _menuTheme = Core.Theme.ReadSelection(reader.TryGetString("ConduitPlus.Theme", ref name) ? name : null); _selectedTheme = _menuTheme;
        return result;
    }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Show pinned cards", (_, _) => CanvasOverlayHost.Show());
        var themes = Menu_AppendItem(menu, ThemeInputSet ? "Theme (set by input)" : "Theme");
        foreach (var theme in Core.Theme.Presets)
            Menu_AppendItem(themes.DropDown, theme.Name, (_, _) =>
            {
                if (_menuTheme == theme) return;
                RecordUndoEvent("Canvas Overlay theme", new ThemeUndoAction(InstanceGuid, _menuTheme.Name, theme.Name)); ApplyTheme(theme.Name);
            }, !ThemeInputSet, _menuTheme == theme);
    }
    private void ApplyTheme(string name)
    {
        Core.Theme.TryGet(name, out _menuTheme); if (!ThemeInputSet) _selectedTheme = _menuTheme;
        State.CancelHover(); CanvasOverlayHost.Invalidate(true); OnPingDocument()?.Modified();
    }
    public sealed class ThemeUndoAction : GH_ObjectUndoAction
    {
        private Guid _owner; private string _before = "Paper", _after = "Paper";
        public ThemeUndoAction() : base(Guid.Empty) { }
        public ThemeUndoAction(Guid owner, string before, string after) : base(owner) { _owner = owner; _before = before; _after = after; }
        public override bool ExpiresSolution => false;
        public override bool ExpiresDisplay => true;
        protected override void Object_Undo(GH_Document doc, IGH_DocumentObject obj) { if (obj is CanvasOverlayComponent c) c.ApplyTheme(_before); }
        protected override void Object_Redo(GH_Document doc, IGH_DocumentObject obj) { if (obj is CanvasOverlayComponent c) c.ApplyTheme(_after); }
        protected override void Internal_Undo(GH_Document doc) => Object_Undo(doc, doc.FindObject(_owner, false));
        protected override void Internal_Redo(GH_Document doc) => Object_Redo(doc, doc.FindObject(_owner, false));
        public override bool Write(GH_IWriter w) { w.SetGuid("Owner", _owner); w.SetString("Before", _before); w.SetString("After", _after); return base.Write(w); }
        public override bool Read(GH_IReader r) { _owner = r.GetGuid("Owner"); _before = r.GetString("Before"); _after = r.GetString("After"); return base.Read(r); }
    }
    public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); Activate(); }
    public override void RemovedFromDocument(GH_Document document) { Deactivate(true); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);
        if (context == GH_DocumentContext.Close) Deactivate(true);
        else if (context == GH_DocumentContext.Unloaded) Deactivate(false);
        else if (context is GH_DocumentContext.Open or GH_DocumentContext.Loaded) Activate();
        else if (context == GH_DocumentContext.Lock) { _scope?.SetEnabled(false); State.CancelHover(); CanvasOverlayHost.Invalidate(true); }
        else if (context == GH_DocumentContext.Unlock) { _scope?.SetEnabled(!Locked); Refresh(); if (_refresh.IsDirty(_lease)) ScheduleOutputs(); }
    }
    private void Activate()
    {
        if (_active) return;
        _active = true; _generation++; RhinoApp.AppSettingsChanged += AppearanceChanged;
        if (OnPingDocument() is { } document) document.EnabledChanged += DocumentEnabledChanged;
        Refresh(); CanvasOverlayHost.Publish(this); if (_scope is null && _refresh.IsDirty(_lease)) ScheduleOutputs();
    }
    private void DocumentEnabledChanged(object sender, GH_DocEnabledEventArgs e)
    {
        State.CancelHover(); _scope?.SetEnabled(!Disabled); CanvasOverlayHost.Invalidate(true);
        if (!Disabled) { Refresh(); if (_scope is null && _refresh.IsDirty(_lease)) ScheduleOutputs(); }
    }
    private void AppearanceChanged(object? sender, EventArgs e) => RhinoApp.InvokeOnUiThread((Action)(() =>
    { if (_active && _selectedTheme == Core.Theme.Auto) CanvasOverlayHost.Invalidate(true); }));
    private void Deactivate(bool clear)
    {
        _active = false; _generation++; _scheduled = false;
        RhinoApp.AppSettingsChanged -= AppearanceChanged;
        if (OnPingDocument() is { } document) document.EnabledChanged -= DocumentEnabledChanged;
        CanvasOverlayHost.Remove(this); State.CancelHover();
        if (clear) { DetachScope(); State.Bind(null, InstanceGuid); State.ReplaceCards([]); _refresh.Clear(); _refresh.Watch(_lease, State.Current, false); }
    }
}
