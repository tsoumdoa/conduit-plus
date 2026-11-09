namespace ConduitPlus.Core;

/// <summary>Pure resolution of a requested viewport name, preserving a matching binding.</summary>
public static class ViewportNames
{
    public static string? Requested(string? value) => string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "Active viewport", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();

    public static (Guid? Id, bool Duplicate) Resolve(IEnumerable<(Guid Id, string Name)> views, string name, Guid? bound)
    {
        var matches = views.Where(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        Guid? id = matches.Length == 0 ? null : matches[0].Id;
        if (bound.HasValue && matches.Any(v => v.Id == bound.Value)) id = bound;
        return (id, matches.Length > 1);
    }
}
