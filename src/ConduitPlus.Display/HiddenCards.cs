using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
namespace ConduitPlus.Rhino;
public static partial class OverlayManager
{
    private static void HiddenEscape(object? sender, EventArgs e)
    { if (global::Rhino.Commands.Command.InCommand()) return; foreach (var c in Coordinators.Values) c.ListOpen = false; foreach (var s in Sessions.Values) s.View.Redraw(); }
    private static (HiddenRow[] Rows, HiddenPanel Panel, double Unit) HiddenGeometry(RhinoView view, ViewCoordinator c)
    {
        var f = c.Frame!; var unit = f.Unit.Values.Min();
        var rows = HiddenList.Build(InView(view.Document, view).Where(s => s.Visible).Select(s => new HiddenParticipant(s.Label, s.Owner, s.Cards, f.Hidden.Where(h => h.Owner == s.Owner).Select(h => h.CardId).ToHashSet(StringComparer.Ordinal))));
        return (rows, HiddenList.Layout(f.Chip!.Value, f.Chip.Value.Y, 22 * unit, rows.Length), unit);
    }
    private static bool HiddenHit(RhinoView view, double x, double y, bool click = false)
    {
        if (!Coordinators.TryGetValue((view.Document.RuntimeSerialNumber, view.MainViewport.Id), out var c) || c.Frame?.Chip is not { } chip) return false;
        if (chip.Contains(x,y)) { if (click) { c.ListOpen = !c.ListOpen; view.Redraw(); } return true; }
        if (!c.ListOpen) return false;
        var g = HiddenGeometry(view,c);
        if (g.Panel.Bounds.Contains(x,y))
        { if (click) for (var i=0;i<g.Panel.Rows.Length;i++) if (g.Panel.Rows[i].Contains(x,y) && g.Rows[i].CardId is { } id) c.ShowHidden(g.Rows[i].Owner,id); return true; }
        if (click) { c.ListOpen=false; view.Redraw(); } return false;
    }
    private static void DrawHiddenList(RhinoView view, ViewCoordinator c, DrawEventArgs e)
    {
        if (!c.ListOpen) return;
        var g=HiddenGeometry(view,c); var b=g.Panel.Bounds;
        e.Display.Draw2dRectangle(new System.Drawing.Rectangle((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height), System.Drawing.Color.FromArgb(242,31,35,41),0,System.Drawing.Color.FromArgb(242,31,35,41));

        // Keep long titles inside the panel: measure in design units, then compare to the panel's inner width.
        var inner = (b.Width - 16 * g.Unit) / g.Unit;
        void Text(string raw, double y, bool muted) => e.Display.Draw2dText(HiddenList.Fit(raw, inner, t => TextMetrics.Measurer(t, 12, "Segoe UI").Width), muted ? System.Drawing.Color.FromArgb(169,176,191) : System.Drawing.Color.White, new global::Rhino.Geometry.Point2d(b.X+8*g.Unit,y+4*g.Unit),false,Math.Max(1,(int)(12*g.Unit)));
        Text("Hidden cards",b.Y,false);
        for(var i=0;i<g.Panel.Rows.Length;i++) Text(g.Rows[i].Text,g.Panel.Rows[i].Y,g.Rows[i].CardId is null);
        if(g.Panel.More>0 && b.Height>=44*g.Unit) Text($"+{g.Panel.More} more",b.Y+b.Height-22*g.Unit,true);
    }
}
