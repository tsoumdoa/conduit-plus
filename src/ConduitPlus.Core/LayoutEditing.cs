using System.Text.Json;

namespace ConduitPlus.Core;

public sealed record LayoutOverride
{
    public Slot? ManualSlot { get; init; }
    public Slot? OrderSlot { get; init; }
    public int? Order { get; init; }
}
public sealed record LayoutPayload(int Version, Dictionary<string, LayoutOverride> Cards);

public static class LayoutEditing
{
    public static CardSpec[] Apply(IEnumerable<CardSpec> cards, IReadOnlyDictionary<string, LayoutOverride> overrides) => cards.Select(c =>
        overrides.TryGetValue(c.Id, out var value) ? c with { ManualSlot = value.ManualSlot, OrderSlot = value.OrderSlot, ManualOrder = value.Order } : c).ToArray();

    public static CardSpec[] Stack(IReadOnlyList<CardSpec> cards, Slot slot, string? excluding = null) => cards
        .Select((c, i) => (c, i, slot: SlotLayout.Resolve(c, i))).Where(x => x.slot == slot && x.c.Id != excluding)
        .OrderBy(x => x.c.OrderSlot != slot || !x.c.ManualOrder.HasValue).ThenBy(x => x.c.OrderSlot == slot ? x.c.ManualOrder : null).ThenBy(x => x.i).Select(x => x.c).ToArray();

    public static int InsertionIndex(IReadOnlyList<CardSpec> cards, IReadOnlyList<CardLayout> frozen, string dragged, Slot slot, double y) =>
        Stack(cards, slot, dragged).Count(c => frozen.First(l => l.Id == c.Id) is var l && y >= l.Y + l.H / 2);

    /// <summary>Changed entries only. Remove before inserting, and never pin neighbours' automatic/wired slots.</summary>
    public static Dictionary<string, LayoutOverride> Drop(IReadOnlyList<CardSpec> cards, IReadOnlyDictionary<string, LayoutOverride> current, string dragged, Slot slot, int index)
    {
        var cardIndex = Enumerable.Range(0, cards.Count).First(i => cards[i].Id == dragged);
        if (cards[cardIndex].WiredSlot.HasValue) return [];
        var stack = Stack(cards, slot, dragged).Select(c => c.Id).ToList();
        index = Math.Clamp(index, 0, stack.Count);
        if (SlotLayout.Resolve(cards[cardIndex], cardIndex) == slot && Array.FindIndex(Stack(cards, slot), c => c.Id == dragged) == index) return [];
        stack.Insert(index, dragged);
        var changes = new Dictionary<string, LayoutOverride>(StringComparer.Ordinal);
        for (var i = 0; i < stack.Count; i++)
        {
            var id = stack[i]; var spec = cards.First(c => c.Id == id);
            var old = current.GetValueOrDefault(id) ?? new LayoutOverride { ManualSlot = spec.ManualSlot };
            var next = old with { ManualSlot = id == dragged ? slot : old.ManualSlot, OrderSlot = slot, Order = i };
            if (old != next) changes.Add(id, next);
        }
        return changes;
    }

    public static Rect2 Zone(Slot slot, Size2 viewport, double scale = 1)
    {
        var right = slot is Slot.Tr or Slot.Br or Slot.Rc;
        var y = slot is Slot.Bl or Slot.Br ? viewport.Height - 56 * scale : slot is Slot.Lc or Slot.Rc ? viewport.Height / 2 - 22 * scale : 36 * scale;
        return new(right ? viewport.Width - 124 * scale : 12 * scale, y, 112 * scale, 44 * scale);
    }
    public static Slot NearestSlot(Point2 point, Size2 viewport, double scale = 1) => Enum.GetValues<Slot>()
        .OrderBy(s => { var r = Zone(s, viewport, scale); return Math.Pow(point.X - r.X - r.Width / 2, 2) + Math.Pow(point.Y - r.Y - r.Height / 2, 2); }).First();

    public static string Serialize(IReadOnlyDictionary<string, LayoutOverride> cards) => CardJson.Serialize(new LayoutPayload(1, cards.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
    public static Dictionary<string, LayoutOverride> Read(string? json, out bool invalid)
    {
        invalid = false;
        var result = new Dictionary<string, LayoutOverride>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json)) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1 ||
                !root.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Object) { invalid = true; return result; }
            foreach (var entry in cards.EnumerateObject())
            {
                try
                {
                    var value = entry.Value.Deserialize<LayoutOverride>(CardJson.Options);
                    if (string.IsNullOrWhiteSpace(entry.Name) || value is null || value.ManualSlot is { } m && !Enum.IsDefined(m) ||
                        value.OrderSlot is { } s && !Enum.IsDefined(s) || value.Order < 0 || value.Order.HasValue != value.OrderSlot.HasValue)
                    { invalid = true; continue; }
                    result[entry.Name] = value;
                }
                catch (JsonException) { invalid = true; }
            }
        }
        catch (JsonException) { invalid = true; }
        return result;
    }
}
