using ConduitPlus.Core;
using Rhino;
using Rhino.Display;
namespace ConduitPlus.Rhino;
public static partial class OverlayManager
{
    private sealed class ViewCoordinator : IDisposable
    {
        internal bool ListOpen;
        internal (Guid Owner, string CardId)? Shown;

        internal void ShowHidden(Guid owner, string cardId) => Shown = Shown == (owner, cardId) ? null : (owner, cardId);
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
            c.Frame = ViewportLayout.Solve(request);
            if (c.Shown is { } shown && !c.Frame.Hidden.Contains(shown)) c.Shown = null;
            if (c.Frame.Hidden.Count == 0) c.ListOpen = false; c.Generation++;
            foreach (var owner in c.Reports.Keys.Except(request.Participants.Select(p => p.Owner)).ToArray())
            { c.Reports.Remove(owner); OverflowChanged?.Invoke(owner, null); }
            foreach (var session in sessions.Where(s => s.Visible))
            {
                var text = OverflowText.For(c.Frame.Hidden.Count(h => h.Owner == session.Owner), session.Cards.Length, view.MainViewport.Name, view.MainViewport.Size.Width, view.MainViewport.Size.Height, session.Cards.Any(card => card.Kind == CardKind.Title && c.Frame.Hidden.Any(h => h.Owner == session.Owner && h.CardId == card.Id)));
                if (!c.Reports.TryGetValue(session.Owner, out var previous) || previous != text)
                { c.Reports[session.Owner] = text; OverflowChanged?.Invoke(session.Owner, text); }
            }
            c.Signature = signature; c.Size = request.Viewport; c.Dpi = request.Dpi;
        }
        foreach (var session in sessions)
            if (c.Frame.Unit.TryGetValue(session.Owner, out var unit))
                {
                var forms = c.Frame.Placements.Where(p => p.Owner == session.Owner).ToDictionary(p => p.CardId, p => p.Form, StringComparer.Ordinal);
                if (c.Shown is { } shown && shown.Owner == session.Owner && session.AuthoredCards.FirstOrDefault(x => x.Id == shown.CardId) is { } card)
                    forms[card.Id] = ShownCard.Form(card, session.Theme, (c.Size.Height - 24 * c.Dpi) / unit, TextMetrics.Measurer);
                session.ApplyLayout(unit, forms);
            }
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
    internal static CardLayout[] Placements(OverlaySession session, bool includeShown = true)
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
        var placements = frame.Placements.AsEnumerable();
        if (includeShown && Coordinators.TryGetValue((session.Document.RuntimeSerialNumber, session.View.MainViewport.Id), out var coordinator) && coordinator.Shown is { } shown && shown.Owner == session.Owner && session.Card(shown.CardId) is { } floating)
        {
            var unit = frame.Unit[session.Owner]; var size = CardMeasure.Measure(floating, session.Theme);
            // Only card columns bound the clear gap; Titles (top or bottom rows) don't.
            Slot? SlotOf(CardPlacement placement)
            {
                var cards = InView(session.Document, session.View).First(s => s.Owner == placement.Owner).AuthoredCards;
                var index = Array.FindIndex(cards, card => card.Id == placement.CardId);
                return index < 0 || cards[index].Kind == CardKind.Title ? null : SlotLayout.ResolveAll(cards)[index];
            }
            var left = frame.Placements.Where(p => SlotOf(p) is Slot.Tl or Slot.Lc or Slot.Bl).Select(p => p.X + p.Width).DefaultIfEmpty(0).Max();
            var right = frame.Placements.Where(p => SlotOf(p) is Slot.Tr or Slot.Rc or Slot.Br).Select(p => p.X).DefaultIfEmpty(coordinator.Size.Width).Min();
            var rect = ShownCard.Place(new(size.Width * unit, size.Height * unit), coordinator.Size, 12 * coordinator.Dpi, left, right);
            placements = placements.Append(new(session.Owner, floating.Id, rect.X, rect.Y, rect.Width, rect.Height, floating.Form));
        }
        return placements.Where(p => p.Owner == session.Owner)
            .Select(p => session.Card(p.CardId) is { } card ? new CardLayout(card.Id, card.Rev, p.X, p.Y, p.Width, p.Height, CardMeasure.Hits(card, session.Theme)) : null)
            .OfType<CardLayout>().ToArray();
    }
    internal static bool CoveredByShown(OverlaySession session, double x, double y)
    {
        if (!Coordinators.TryGetValue((session.Document.RuntimeSerialNumber, session.View.MainViewport.Id), out var c) || c.Shown is not { } shown || shown.Owner == session.Owner) return false;
        var owner = InView(session.Document, session.View).FirstOrDefault(s => s.Owner == shown.Owner);
        return owner is not null && Placements(owner).Any(p => p.Id == shown.CardId && new Rect2(p.X, p.Y, p.W, p.H).Contains(x, y));
    }
    internal static string? ShownId(OverlaySession session) => Coordinators.TryGetValue((session.Document.RuntimeSerialNumber, session.View.MainViewport.Id), out var c) && c.Shown is { } shown && shown.Owner == session.Owner ? shown.CardId : null;
    private static void DisposeCoordinators(Func<(uint Doc, Guid View), bool> match)
    {
        foreach (var key in Coordinators.Keys.Where(match).ToArray()) { Coordinators[key].Dispose(); Coordinators.Remove(key); }
    }
}

