using System.Collections.Immutable;
using ConduitPlus.Core;

namespace ConduitPlus.Rhino;

/// <summary>Stable additive wire shape shared by hosts and Rhino-free serialization checks.</summary>
public sealed record OverlayInteraction(Guid Owner, string CardId, string Key, string Kind)
{
    public BrushTargetKind TargetKind { get; init; } = BrushTargetKind.Member;
    public ImmutableArray<string> AffectedKeys { get; init; } = [];
    public ImmutableArray<string> SelectedKeys { get; init; } = [];
    public string? SourceSurface { get; init; }
    public static OverlayInteraction From(BrushEvent action) => new(action.Owner, action.CardId, action.Key, action.Kind)
    {
        TargetKind = action.TargetKind, AffectedKeys = action.AffectedKeys,
        SelectedKeys = action.SelectedKeys, SourceSurface = action.SourceSurface
    };
}
