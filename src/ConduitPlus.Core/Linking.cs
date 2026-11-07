namespace ConduitPlus.Core;

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
