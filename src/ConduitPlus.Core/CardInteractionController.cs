namespace ConduitPlus.Core;

[Flags]
public enum CardModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Other = 8 }
public enum CardResponse { Pass, Capture, Handled, Release }
public enum CardActionKind { Activate, Scroll }
public sealed record CardAction(CardActionKind Kind, string CardId, string? Key = null, int Offset = 0, BrushSelectMode Mode = BrushSelectMode.Replace, string[]? Members = null, Theme? DisplayTheme = null)
{
    public void Apply(CanvasCardState state)
    {
        if (Kind == CardActionKind.Scroll) state.Scroll(CardId, Offset);
        else if (Key is not null) state.Activate(CardId, Key, Mode, Members, DisplayTheme);
    }
}
public readonly record struct CardInteractionResult(CardResponse Response, CardAction? Action = null)
{
    public bool Repaint => Response != CardResponse.Pass;
}

/// <summary>An immutable published card, in host-local logical coordinates, independent of its raster.</summary>
public sealed record InteractionCard(CardSpec Spec, Theme Theme, Rect2 Bounds, HitRegion[] Hits)
{
    public Point2 Local(Point2 point) => new(point.X - Bounds.X, point.Y - Bounds.Y);
    public HitRegion? Hit(Point2 point)
    {
        var p = Local(point);
        return Bounds.Contains(point.X, point.Y) ? Hits.LastOrDefault(h => h.Contains(p.X, p.Y)) : null;
    }
}
public sealed class CardInteractionFrame(IReadOnlyList<InteractionCard> cards)
{
    public IReadOnlyList<InteractionCard> Cards { get; } = cards;
    public InteractionCard? CardAt(Point2 point) => Cards.LastOrDefault(c => c.Bounds.Contains(point.X, point.Y));
    public static CardInteractionFrame Stack(CardSpec[] specs, Theme theme, CanvasStackLayout layout) => new(
        specs.Zip(layout.Cards, (s, c) => new InteractionCard(s, theme, new(c.X, c.Y, c.W, c.H), c.HitRegions)).ToArray());
}

/// <summary>Host-neutral press/release arbitration. The adapter owns physical capture and applies returned actions.</summary>
public sealed class CardInteractionController
{
    private string? _key;
    private Distribution.Layout? _distributionLayout;
    private Rect2 _distributionBounds;
    public string? PressedCard { get; private set; }
    public int OriginalOffset { get; private set; }
    public CardModifiers Modifiers { get; private set; }
    public bool Active { get; private set; }
    public bool OwnsRelease { get; private set; }
    public bool DraggingScroll { get; private set; }
    private Point2 _down;
    private double _grab;
    private int _page;
    private bool _moved;
    public bool ModifiersValid(CardModifiers modifiers) => (modifiers & ~Modifiers) == 0;
    public bool IsSelectionGesture => Active && _key is not null && (!CardMeasure.IsReserved(_key) || _key.StartsWith(Distribution.BinPrefix, StringComparison.Ordinal));
    public CardInteractionResult Down(CardInteractionFrame frame, Point2 point, Point2 control, CardModifiers modifiers,
        bool leftOnly = true, bool enabled = true, bool busy = false, bool consumeBody = false)
    {
        // A new press means any release we kept after a cancellation is gone.
        if (!Active) OwnsRelease = false;
        if (Active || !leftOnly || !enabled || busy || (modifiers & ~(CardModifiers.Shift | CardModifiers.Control)) != 0) return new(CardResponse.Pass);
        var card = frame.CardAt(point);
        if (card is null) return new(CardResponse.Pass);
        var local = card.Local(point);
        var bar = TableScroll.Bar(card.Spec, card.Theme);
        var track = bar?.Track.Contains(local.X, local.Y) == true;
        var hit = card.Hit(point);
        var bin = hit?.Key.StartsWith(Distribution.BinPrefix, StringComparison.Ordinal) == true;
        if (!bin && modifiers != CardModifiers.None && (track || hit is null || CardMeasure.IsReserved(hit.Key) || !BrushState.Keys(card.Spec).Contains(hit.Key, StringComparer.Ordinal))) return new(CardResponse.Pass);
        if (!track && hit is null && !consumeBody) return new(CardResponse.Pass);
        Active = OwnsRelease = true; _key = hit?.Key; PressedCard = card.Spec.Id;
        _distributionLayout = card.Spec.Kind == CardKind.Distribution ? Distribution.Geometry(card.Spec, card.Theme) : null;
        _distributionBounds = card.Bounds;
        Modifiers = modifiers; _down = control; _moved = false; OriginalOffset = card.Spec.ScrollOffset;
        DraggingScroll = track && bar!.Value.Thumb.Contains(local.X, local.Y);
        if (track)
        {
            _key = null;
            _grab = local.Y - bar!.Value.Thumb.Y;
            _page = local.Y < bar.Value.Thumb.Y ? -TableScroll.Visible(card.Spec) : TableScroll.Visible(card.Spec);
        }
        else _page = 0;
        return new(CardResponse.Capture);
    }
    public CardInteractionResult Move(CardInteractionFrame frame, Point2 point, Point2 control, CardModifiers modifiers, bool enabled = true)
    {
        if (!Active) return new(CardResponse.Pass);
        if (!enabled || !ModifiersValid(modifiers)) return Cancel();
        if (!CanvasCardGeometry.WithinClick(_down, control)) _moved = true;
        var card = frame.Cards.FirstOrDefault(c => c.Spec.Id == PressedCard);
        if (card is null) return Cancel();
        return new(CardResponse.Handled, DraggingScroll ? new(CardActionKind.Scroll, card.Spec.Id,
            Offset: TableScroll.OffsetForThumbTop(card.Spec, card.Theme, card.Local(point).Y - _grab)) : null);
    }
    public CardInteractionResult Up(CardInteractionFrame frame, Point2 point, Point2 control, CardModifiers modifiers, bool left = true, bool enabled = true)
    {
        if (!OwnsRelease) return new(CardResponse.Pass);
        var valid = Active && enabled && left && ModifiersValid(modifiers);
        CardAction? action = null;
        var card = frame.CardAt(point);
        if (valid && !_moved && CanvasCardGeometry.WithinClick(_down, control) && card is not null && card.Spec.Id == PressedCard)
        {
            var local = card.Local(point);
            if (_page != 0 && !DraggingScroll && TableScroll.Bar(card.Spec, card.Theme) is { } bar && bar.Track.Contains(local.X, local.Y) && !bar.Thumb.Contains(local.X, local.Y))
                action = new(CardActionKind.Scroll, card.Spec.Id, Offset: OriginalOffset + _page);
            else if (_key is not null && card.Hit(point)?.Key == _key && (_distributionLayout is null || _distributionBounds == card.Bounds && ReferenceEquals(_distributionLayout, Distribution.Geometry(card.Spec, card.Theme))))
                action = new(CardActionKind.Activate, card.Spec.Id, _key, Mode: BrushSelection.FromModifiers((Modifiers & CardModifiers.Shift) != 0, (Modifiers & CardModifiers.Control) != 0), Members: Distribution.Members(card.Spec, card.Theme, _key), DisplayTheme: _distributionLayout is null ? null : card.Theme);
        }
        if (!valid && Active && DraggingScroll && PressedCard is { } id) action = new(CardActionKind.Scroll, id, Offset: OriginalOffset);
        Reset(); OwnsRelease = false;
        return new(CardResponse.Release, action);
    }
    public CardInteractionResult Cancel()
    {
        var action = Active && DraggingScroll && PressedCard is { } id ? new CardAction(CardActionKind.Scroll, id, Offset: OriginalOffset) : null;
        var active = Active;
        Reset(); // The accepted release remains ours even after capture was cancelled.
        return new(active ? CardResponse.Release : CardResponse.Pass, action);
    }
    private void Reset() { Active = false; DraggingScroll = false; _key = null; PressedCard = null; }
}

