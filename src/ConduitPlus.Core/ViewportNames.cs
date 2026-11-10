namespace ConduitPlus.Core;

/// <summary>Pure resolution of a requested viewport name. Names only find a viewport; Rhino renames viewports, for example on _SetView.</summary>
public static class ViewportNames
{
    public static string? Requested(string? value) => string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "Active viewport", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();

    /// <param name="views">Open viewports.</param>
    /// <param name="name">Requested name.</param>
    /// <param name="bound">The viewport already serving this same request, whatever it is called now. Pass null when the request changed.</param>
    public static (Guid? Id, bool Duplicate) Resolve(IEnumerable<(Guid Id, string Name)> views, string name, Guid? bound)
    {
        var open = views.ToArray();
        var matches = open.Where(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (bound is { } id && open.Any(v => v.Id == id)) return (id, matches.Length > 1 && matches.Any(v => v.Id == id));
        return (matches.Length == 0 ? (Guid?)null : matches[0].Id, matches.Length > 1);
    }
}
