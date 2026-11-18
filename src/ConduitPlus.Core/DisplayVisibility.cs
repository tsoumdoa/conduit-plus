namespace ConduitPlus.Core;

/// <summary>Per-component presentation and Scope eligibility; document and widget states are separate.</summary>
public readonly record struct DisplayVisibility(bool Shown, bool ScopeEnabled)
{
    public static DisplayVisibility Evaluate(bool hidden, bool locked) => new(!hidden && !locked, !locked);
}
