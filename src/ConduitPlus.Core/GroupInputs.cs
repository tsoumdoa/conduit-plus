using System.Collections.Immutable;

namespace ConduitPlus.Core;

/// <summary>A path-ordered text branch, including empty branches. Hosts supply paths for diagnostics.</summary>
public sealed record MemberInputBranch(string Path, IReadOnlyList<string?> Items);

public static class GroupInputs
{
    public static ImmutableDictionary<string, GroupDefinition> Create(IReadOnlyList<string> keys,
        int entryCount, bool explicitKeys, IReadOnlyList<MemberInputBranch>? branches, bool gradient = false)
    {
        var empty = ImmutableDictionary<string, GroupDefinition>.Empty.WithComparers(StringComparer.Ordinal);
        if (branches is null) return empty;
        if (gradient) throw new ArgumentException("Gradient Legend does not accept Members.");
        if (!explicitKeys || keys.Count != entryCount)
            throw new ArgumentException($"Members requires {entryCount} explicit Keys (Metric requires Key).");
        if (branches.Count != entryCount)
            throw new ArgumentException($"Members has {branches.Count} branches; expected exactly {entryCount}, one per entry in path order (preserve empty branches). Paths: {string.Join(", ", branches.Select(b => b.Path))}.");
        var builder = empty.ToBuilder();
        for (var i = 0; i < branches.Count; i++)
        {
            var branch = branches[i];
            if (string.IsNullOrWhiteSpace(keys[i]) || CardMeasure.IsReserved(keys[i]))
                throw new ArgumentException($"Keys item [{i}] for Members path {branch.Path} must be nonblank and non-reserved.");
            if (builder.ContainsKey(keys[i])) throw new ArgumentException($"Keys item [{i}] for Members path {branch.Path} duplicates '{keys[i]}'.");
            for (var item = 0; item < branch.Items.Count; item++)
                if (string.IsNullOrWhiteSpace(branch.Items[item]) || CardMeasure.IsReserved(branch.Items[item]!))
                    throw new ArgumentException($"Members path {branch.Path}, item [{item}] must be nonblank and non-reserved.");
            builder.Add(keys[i], new(branch.Items.Select(k => k!).ToImmutableArray()));
        }
        // Report local nesting at the originating branch/item, before CardSpec validation.
        foreach (var branch in branches)
            for (var item = 0; item < branch.Items.Count; item++)
                if (builder.ContainsKey(branch.Items[item]!))
                    throw new ArgumentException($"Members path {branch.Path}, item [{item}] contains group key '{branch.Items[item]}'; nested groups are not supported.");
        return builder.ToImmutable();
    }
}
