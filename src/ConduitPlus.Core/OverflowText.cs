namespace ConduitPlus.Core;

/// <summary>The Overlay warning shown when the layout hides some of its cards.</summary>
public static class OverflowText
{
    public static string? For(int hidden, int total, string viewName, int width, int height) => hidden <= 0 ? null :
        $"{hidden} of {total} {(total == 1 ? "card" : "cards")} hidden in {viewName} ({width} × {height}). Enlarge the viewport or remove cards.";
}
