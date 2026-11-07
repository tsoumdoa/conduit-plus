namespace ConduitPlus.Core;

/// <summary>Host-neutral brush authority. Rendering hosts retain their own cards, layout and geometry.</summary>
public interface IBrushBinding
{
    string ParticipantId { get; }
    BrushDomainSnapshot Current { get; }
    bool IsActive { get; }
    bool Toggle(string key, BrushTargetKind kind, BrushActionSource source);
    bool Clear(BrushActionSource source);
    bool Hover(BrushOrigin origin, string? key, BrushTargetKind? kind = null);
    bool ClearHover(BrushOrigin origin);
}
