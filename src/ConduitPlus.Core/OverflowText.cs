namespace ConduitPlus.Core;

/// <summary>The Overlay warning shown when the layout hides some of its cards.</summary>
public static class OverflowText
{
    public static string? For(int hidden, int total, string viewName, int width, int height, bool hasHiddenTitle = false) => hidden <= 0 ? null :
        $"{hidden} of {total} {(hasHiddenTitle ? total == 1 ? "item" : "items" : total == 1 ? "card" : "cards")} hidden in {viewName} ({width} × {height}). Enlarge the viewport or remove {(hasHiddenTitle ? "items" : "cards")}.";
}
