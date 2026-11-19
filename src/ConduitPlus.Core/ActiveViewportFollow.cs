namespace ConduitPlus.Core;

/// <summary>A follow move stays in the bound document and only targets a different standard model view.</summary>
public static class ActiveViewportFollow
{
    public static Guid? Target(uint boundDocument, Guid? boundView, uint? activeDocument, Guid? activeView,
        IEnumerable<Guid> standardViews)
    {
        if (activeDocument != boundDocument || activeView is not { } target || target == boundView) return null;
        return standardViews.Contains(target) ? target : null;
    }
}
