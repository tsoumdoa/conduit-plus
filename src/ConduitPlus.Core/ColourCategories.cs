using System.Globalization;

namespace ConduitPlus.Core;

public sealed record ColourCategoriesResult(string[] Colours, CardSpec Legend, string[] Categories,
    string[] CategoryColours, string[] GroupKeys, string[][] Members, ColourMessage[] Messages);

public static class ColourCategories
{
    private static string Category(string? s) => string.IsNullOrWhiteSpace(s) ? "No data" : s;

    public static ColourCategoriesResult Create(IReadOnlyList<string?> categories, IReadOnlyList<string?>? keys = null,
        IReadOnlyList<string>? palette = null, IReadOnlyList<string?>? order = null, string title = "Legend",
        bool showCounts = true, string id = "colour-categories", Slot? slot = null, string groupNamespace = "colour-categories")
    {
        var messages = new List<ColourMessage>();
        var scheme = Palettes.Resolve(palette, "Categorical", messages);
        var items = categories.Select(Category).ToArray();
        var rows = (order ?? []).Select(Category).ToList();
        if (rows.Distinct(StringComparer.Ordinal).Count() != rows.Count) throw new ArgumentException("Order must contain unique categories.");
        var extra = items.Distinct(StringComparer.Ordinal).Except(rows, StringComparer.Ordinal).ToArray();
        var unlisted = extra.Where(s => s != "No data").ToArray();
        if (order is { Count: > 0 } && unlisted.Length > 0)
            messages.Add(new(ColourMessageLevel.Warning, $"{unlisted.Length} {(unlisted.Length == 1 ? "category is" : "categories are")} not in Order ({string.Join(", ", unlisted.Take(2).Select(s => $"'{s}'"))}); added after the listed ones."));
        rows.AddRange(extra);
        if (rows.Remove("No data")) rows.Add("No data");
        if (rows.Count == 0) throw new ArgumentException("Connect Categories (or set Order).");
        var ordinary = rows.Count(s => s != "No data");
        var colours = rows.Select((s, i) => s == "No data" ? Palettes.Missing : scheme.Kind == PaletteKind.Categorical
            ? scheme.Colours[i % scheme.Colours.Length] : scheme.ColourAt(ordinary <= 1 ? .5 : (double)i / (ordinary - 1))).ToArray();
        if (scheme.Kind == PaletteKind.Categorical && ordinary > scheme.Colours.Length)
            messages.Add(new(ColourMessageLevel.Warning, $"{ordinary} categories but {scheme.Name} has {scheme.Colours.Length} colours; colours repeat from '{rows[scheme.Colours.Length]}'. Wire Order to choose which come first, or wire your own colours to Palette."));
        if (scheme.Kind != PaletteKind.Categorical && order is not { Count: > 0 })
            messages.Add(new(scheme.Kind == PaletteKind.Status ? ColourMessageLevel.Warning : ColourMessageLevel.Remark, $"{scheme.Name} is an ordered palette. Wire Order, best first, so colours do not follow data order."));
        if (keys is not null && keys.Count != items.Length)
            throw new ArgumentException($"Keys has {keys.Count} {(keys.Count == 1 ? "item" : "items")} and Categories has {items.Length}; wire one key per item.");
        if (keys is not null)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (string.IsNullOrWhiteSpace(key) || CardMeasure.IsReserved(key)) throw new ArgumentException($"Keys item [{i}] must be nonblank and non-reserved.");
                if (seen.TryGetValue(key, out var category) && category != items[i]) throw new ArgumentException($"Member key '{key}' occurs in different categories.");
                seen[key] = items[i];
            }
        }
        var groupKeys = keys is null ? [] : rows.Select(s => $"{title}:{s}@{groupNamespace}").ToArray();
        if (keys is not null && groupKeys.FirstOrDefault(g => keys.Contains(g, StringComparer.Ordinal)) is { } collision)
            throw new ArgumentException($"Generated group key '{collision}' collides with a member key.");
        var members = keys is null ? [] : rows.Select(s => items.Select((category, i) => (category, i)).Where(x => x.category == s)
            .Select(x => keys[x.i]!).Distinct(StringComparer.Ordinal).ToArray()).ToArray();
        var groups = GroupInputs.Create(groupKeys, rows.Count, keys is not null, keys is null ? null : members.Select((m, i) => new MemberInputBranch($"{{{i}}}", m)).ToArray());
        var legend = new CardSpec { Id = id, Kind = CardKind.Legend, Title = title, Labels = rows.ToArray(), Colors = colours,
            Keys = groupKeys, Groups = groups, WiredSlot = slot,
            ValueLabels = showCounts ? rows.Select((s, i) => (keys is null ? items.Count(c => c == s) : members[i].Length).ToString(CultureInfo.InvariantCulture)).ToArray() : [] };
        legend.Validate();
        var lookup = rows.Select((s, i) => (s, colour: colours[i])).ToDictionary(x => x.s, x => x.colour, StringComparer.Ordinal);
        return new(items.Select(s => lookup[s]).ToArray(), legend, rows.ToArray(), colours, groupKeys, members, messages.ToArray());
    }
}
