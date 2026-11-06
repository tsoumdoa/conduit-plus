namespace ConduitPlus.Core;

public sealed record BrushSnapshot(string[] SelectedKeys, string? HoverKey);

/// <summary>Owner state; the host supplies synchronization and lifetime.</summary>
public sealed class BrushState
{
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private HashSet<string> _live = new(StringComparer.Ordinal);
    private string? _hover;
    public BrushSnapshot Snapshot => new(_selected.Order(StringComparer.Ordinal).ToArray(), _hover);
    public static IEnumerable<string> Keys(CardSpec c) => c.Kind switch
    { CardKind.Table => c.Rows.Select(r => r.Key), CardKind.Bar or CardKind.Line => c.Keys, CardKind.Legend when !c.Min.HasValue => c.Keys, _ => [] };
    public void Prune(IEnumerable<CardSpec> cards, IEnumerable<string> linkKeys)
    {
        _live = cards.SelectMany(Keys).Concat(linkKeys).Where(k => !CardMeasure.IsReserved(k)).ToHashSet(StringComparer.Ordinal);
        _selected.IntersectWith(_live);
        if (_hover is not null && !_live.Contains(_hover)) _hover = null;
    }
    public bool Toggle(string key)
    {
        if (!_live.Contains(key)) return false;
        if (!_selected.Add(key)) _selected.Remove(key);
        return true;
    }
    public bool Clear() { if (_selected.Count == 0) return false; _selected.Clear(); return true; }
    public bool Hover(string? key)
    {
        key = key is not null && _live.Contains(key) ? key : null;
        if (_hover == key) return false;
        _hover = key; return true;
    }
    public CardSpec[] Project(IEnumerable<CardSpec> cards) => cards.Select(c =>
    {
        var keys = Keys(c).ToHashSet(StringComparer.Ordinal);
        return c with { SelectedKeys = keys.Where(_selected.Contains).ToArray(), HoverKey = _hover is not null && keys.Contains(_hover) ? _hover : null, OwnerHasSelection = keys.Count > 0 && _selected.Count > 0 };
    }).ToArray();
}

/// <summary>Rhino-free tree mapping. Each branch contains the key index for each of its items.</summary>
public static class LinkKeyMapping
{
    public static int[][] Create(IReadOnlyList<int> branchCounts, IReadOnlyList<string> keys)
    {
        for (var i = 0; i < keys.Count; i++)
            if (string.IsNullOrWhiteSpace(keys[i])) throw new ArgumentException($"Keys[{i}] is empty or whitespace.");
        if (branchCounts.Any(n => n < 0)) throw new ArgumentException("Branch item counts cannot be negative.");
        var perBranch = branchCounts.Count == keys.Count;
        var count = branchCounts.Sum();
        if (!perBranch && count != keys.Count)
            throw new ArgumentException($"Geometry has {branchCounts.Count} branches and {count} items, but Keys has {keys.Count} items. Match branch count or flattened item count.");
        var flat = 0;
        return branchCounts.Select((n, branch) => Enumerable.Range(0, n).Select(_ => perBranch ? branch : flat++).ToArray()).ToArray();
    }
}
