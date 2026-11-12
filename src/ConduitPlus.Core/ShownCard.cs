namespace ConduitPlus.Core;

public static class ShownCard
{
    public static Rect2 Place(Size2 card, Size2 viewport, double margin, double leftEdge, double rightEdge) => new(
        Math.Clamp((leftEdge + rightEdge - card.Width) / 2, margin, Math.Max(margin, viewport.Width - margin - card.Width)),
        Math.Clamp((viewport.Height - card.Height) / 2, margin, Math.Max(margin, viewport.Height - margin - card.Height)), card.Width, card.Height);

    public static CardForm Form(CardSpec card, Theme theme, double maxHeightDesignUnits, Func<string, double, string, Size2> measure)
    {
        bool Fits(CardForm form) => FormMeasure.Size(card, theme, form, measure).Height <= maxHeightDesignUnits;
        if (Fits(CardForm.None)) return CardForm.None;
        if (card.Kind is CardKind.Table or CardKind.Bar)
        {
            var rows = card.Kind == CardKind.Table ? Math.Min(card.RowLimit, card.Rows.Length) : Math.Min(card.RowLimit, card.Values.Length);
            for (var n = rows; n >= 3; n--) if (Fits(new(n))) return new(n);
            return CardForm.None;
        }
        var compact = new CardForm(Compact: true);
        return Fits(compact) ? compact : CardForm.None;
    }
}
