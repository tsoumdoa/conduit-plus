using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
namespace ConduitPlus.Rhino;
public static partial class OverlayManager
{
    private sealed class ViewCoordinator : IDisposable
    {
        internal ViewportSolution? Frame;
        internal readonly Dictionary<Guid, string?> Reports = [];
        internal long Generation;
        internal Size2 Size;
        internal double Dpi;
        internal string? Signature;
        internal readonly System.Windows.Forms.Timer Timer = new() { Interval = 120 };
        public void Dispose() { foreach (var owner in Reports.Keys) OverflowChanged?.Invoke(owner, null); Timer.Dispose(); }
    }
    public static event Action<Guid, string?>? OverflowChanged;
    private static readonly Dictionary<(uint Doc, Guid View), ViewCoordinator> Coordinators = [];
    private static LayoutRequest Request(RhinoView view, IEnumerable<OverlaySession> sessions, bool compact = true) => new(
        new(view.MainViewport.Size.Width, view.MainViewport.Size.Height), UiContext.Dpi(view),
        sessions.Where(s => s.Visible).Select(s => new LayoutParticipant(s.Owner, s.Size, s.Theme, s.AuthoredCards)).ToArray(),
        TextMetrics.Measurer, new LayoutOptions { EnableCompact = compact, BottomLeftReserve = AxesIconReserve(view) });
    /// <summary>Rhino draws its world axes icon in the bottom-left corner, about 72 CSS px tall including labels; cards stay above it.</summary>
    private static double AxesIconReserve(RhinoView view) => view.MainViewport.WorldAxesVisible ? 72 * UiContext.Dpi(view) : 0;
    private static ViewCoordinator Coordinator(RhinoDoc doc, RhinoView view)
    {
        var key = (doc.RuntimeSerialNumber, view.MainViewport.Id);
        if (!Coordinators.TryGetValue(key, out var c))
        {
            c = new(); Coordinators.Add(key, c);
            c.Timer.Tick += (_, _) => { c.Timer.Stop(); Settle(doc, view); };
        }
        return c;
    }
    internal static void Settle(RhinoDoc doc, RhinoView view)
    {
        // Removal paths reach here for documents that are closing or already disposed; never touch their views.
        if (!_started || doc.IsClosing || RhinoDoc.FromRuntimeSerialNumber(doc.RuntimeSerialNumber) is null)
        { DisposeCoordinators(k => k.Doc == doc.RuntimeSerialNumber); return; }
        var c = Coordinator(doc, view);
        c.Timer.Stop();
        var sessions = InView(doc, view);
        var request = Request(view, sessions);
        var signature = ViewportLayout.Signature(request);
        if (c.Frame is null || c.Signature != signature)
        {
            c.Frame = ViewportLayout.Solve(request); c.Generation++;
            foreach (var owner in c.Reports.Keys.Except(request.Participants.Select(p => p.Owner)).ToArray())
            { c.Reports.Remove(owner); OverflowChanged?.Invoke(owner, null); }
            foreach (var session in sessions.Where(s => s.Visible))
            {
                var text = OverflowText.For(c.Frame.Hidden.Count(h => h.Owner == session.Owner), session.Cards.Length, view.MainViewport.Name, view.MainViewport.Size.Width, view.MainViewport.Size.Height);
                if (!c.Reports.TryGetValue(session.Owner, out var previous) || previous != text)
                { c.Reports[session.Owner] = text; OverflowChanged?.Invoke(session.Owner, text); }
            }
            c.Signature = signature; c.Size = request.Viewport; c.Dpi = request.Dpi;
        }
        foreach (var session in sessions)
            if (c.Frame.Unit.TryGetValue(session.Owner, out var unit))
                session.ApplyLayout(unit, c.Frame.Placements.Where(p => p.Owner == session.Owner).ToDictionary(p => p.CardId, p => p.Form, StringComparer.Ordinal));
        view.Redraw();
    }
    private static void CheckFrame(OverlaySession session)
    {
        var c = Coordinator(session.Document, session.View);
        if (c.Frame is null) { Settle(session.Document, session.View); return; }
        var size = session.View.MainViewport.Size;
        if (c.Size != new Size2(size.Width, size.Height) || c.Dpi != UiContext.Dpi(session.View))
        { c.Timer.Stop(); c.Timer.Start(); }
    }
    internal static CardLayout[] Placements(OverlaySession session)
    {
        ViewportSolution frame;
        if (Sessions.Values.Contains(session))
        {
            var c = Coordinator(session.Document, session.View);
            if (c.Frame is null) Settle(session.Document, session.View);
            frame = c.Frame!;
        }
        else
        {
            // Unregistered benchmark sessions never receive forms, so lay them out at full size.
            frame = ViewportLayout.Solve(Request(session.View, [session], compact: false));
            if (frame.Unit.TryGetValue(session.Owner, out var unit)) session.PrepareUnit(unit);
        }
        return frame.Placements.Where(p => p.Owner == session.Owner && !frame.Hidden.Contains((p.Owner, p.CardId)))
            .Select(p => session.Card(p.CardId) is { } card ? new CardLayout(card.Id, card.Rev, p.X, p.Y, p.Width, p.Height, CardMeasure.Hits(card, session.Theme)) : null)
            .OfType<CardLayout>().ToArray();
    }
    private static void DisposeCoordinators(Func<(uint Doc, Guid View), bool> match)
    {
        foreach (var key in Coordinators.Keys.Where(match).ToArray()) { Coordinators[key].Dispose(); Coordinators.Remove(key); }
    }
}

