using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitPlus.Core;

public enum CardKind { Metric, Bar, Table, Line }
public enum Slot { Tl, Tr, Bl, Br, Lc, Rc }
public sealed record TableRow(string Key, string?[] Cells);
public sealed record CardSpec
{
    public required string Id { get; init; }
    public long Rev { get; init; }
    public CardKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string? Value { get; init; }
    public string Unit { get; init; } = "";
    public double?[] Values { get; init; } = [];
    public string[] Labels { get; init; } = [];
    public string[] Keys { get; init; } = [];
    public double Threshold { get; init; } = 1;
    public string[] Headers { get; init; } = [];
    public TableRow[] Rows { get; init; } = [];
    public Slot? WiredSlot { get; init; }
    public Slot? ManualSlot { get; init; }
    public double Width { get; init; } = 280;
    public string? HoverKey { get; init; }
    public string[] SelectedKeys { get; init; } = [];
    /// <summary>Table only: rows shown at once; more rows scroll.</summary>
    public int MaxRows { get; init; } = TableScroll.DefaultVisibleRows;
    /// <summary>Table only: first visible row. View state owned by the overlay session, clamped when painted.</summary>
    public int ScrollOffset { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Card ID is required.");
        if (!double.IsFinite(Width) || Width < 180 || Width > 1200) throw new ArgumentException("Width must be 180–1200 CSS pixels.");
        if (!double.IsFinite(Threshold) || Threshold <= 0) throw new ArgumentException("Threshold must be positive and finite.");
        if (Values.Any(v => v.HasValue && !double.IsFinite(v.Value))) throw new ArgumentException("Use null for missing values; infinity/NaN are invalid.");
        if (Kind is CardKind.Bar or CardKind.Line && (Values.Length != Keys.Length || Values.Length != Labels.Length)) throw new ArgumentException("Chart values, labels and keys must have equal lengths.");
        if (Kind == CardKind.Line && Values.Length < 2) throw new ArgumentException("Line cards need at least two samples.");
        var keys = Kind == CardKind.Table ? Rows.Select(r => r.Key).ToArray() : Keys;
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new ArgumentException("Row/bar keys must be nonempty and unique within a card.");
        if (Kind == CardKind.Table && (Headers.Length == 0 || Rows.Any(r => r.Cells.Length != Headers.Length))) throw new ArgumentException("Each row must match the table headers.");
        if (MaxRows is < 1 or > 200) throw new ArgumentException("Visible rows must be 1–200.");
    }
}

/// <summary>Scroll geometry for Table cards, shared by painting and hit testing.</summary>
public static class TableScroll
{
    public const int DefaultVisibleRows = 8;
    public const double BarWidth = 6;
    public static int Visible(CardSpec c) => Math.Min(c.Rows.Length, c.MaxRows);
    public static int MaxOffset(CardSpec c) => Math.Max(0, c.Rows.Length - Visible(c));
    public static int Offset(CardSpec c) => Math.Clamp(c.ScrollOffset, 0, MaxOffset(c));
    public static bool CanScroll(CardSpec c) => c.Kind == CardKind.Table && MaxOffset(c) > 0;

    /// <summary>Track and thumb in card-local CSS pixels; null when every row fits.</summary>
    public static (Rect2 Track, Rect2 Thumb)? Bar(CardSpec c, Theme t)
    {
        if (!CanScroll(c)) return null;
        var track = new Rect2(c.Width - t.Padding / 2 - BarWidth / 2, 36 + t.RowHeight, BarWidth, Visible(c) * t.RowHeight);
        var thumbHeight = Math.Max(16, track.Height * Visible(c) / c.Rows.Length);
        var top = track.Y + (track.Height - thumbHeight) * Offset(c) / MaxOffset(c);
        return (track, new Rect2(track.X, top, BarWidth, thumbHeight));
    }

    /// <summary>Row offset that puts the thumb's top edge at <paramref name="thumbTop"/> (card-local CSS pixels).</summary>
    public static int OffsetForThumbTop(CardSpec c, Theme t, double thumbTop)
    {
        if (Bar(c, t) is not { } bar) return 0;
        var travel = bar.Track.Height - bar.Thumb.Height;
        return travel <= 0 ? 0 : (int)Math.Round(Math.Clamp((thumbTop - bar.Track.Y) / travel, 0, 1) * MaxOffset(c));
    }
}

public sealed record Theme
{
    public string Surface { get; init; } = "#FFFFFF";
    public string Ink { get; init; } = "#17191E";
    public string Muted { get; init; } = "#626875";
    public string Line { get; init; } = "#E3E5E9";
    public string Track { get; init; } = "#EEF0F3";
    public string Accent { get; init; } = "#2B59F0";
    public string Bad { get; init; } = "#C2334D";
    public string Good { get; init; } = "#1F8A5B";
    public string Area { get; init; } = "#E7EDFF";
    public string ThresholdBand { get; init; } = "#FCECEF";
    public string Font { get; init; } = "Segoe UI";
    public double Padding { get; init; } = 12;
    public double FontSize { get; init; } = 12;
    public double RowHeight { get; init; } = 22;
    public double Radius { get; init; } = 5;
    public static Theme Paper { get; } = new();
}

public static class CardJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Null spec.");
}

/// <summary>Owns revision numbers; compares serialized content so caller-owned arrays cannot defeat dirty tracking.</summary>
public sealed class CardStore
{
    private readonly Dictionary<string, (string Content, CardSpec Spec)> _cards = new(StringComparer.Ordinal);
    private long _revision;
    private string[] _order = [];
    public IReadOnlyList<CardSpec> Update(IEnumerable<CardSpec> specs)
    {
        var incoming = specs.ToArray();
        if (incoming.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Length) throw new ArgumentException("Duplicate card IDs.");
        foreach (var c in incoming) c.Validate();
        var dirty = new List<CardSpec>();
        foreach (var input in incoming)
        {
            var content = CardJson.Serialize(input with { Rev = 0 });
            if (_cards.TryGetValue(input.Id, out var old) && old.Content == content) continue;
            var snapshot = CardJson.Deserialize<CardSpec>(content) with { Rev = ++_revision };
            _cards[input.Id] = (content, snapshot);
            dirty.Add(snapshot);
        }
        var live = incoming.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _cards.Keys.Where(k => !live.Contains(k)).ToArray()) _cards.Remove(id);
        _order = incoming.Select(c => c.Id).ToArray();
        return dirty;
    }
    public CardSpec[] Snapshot => _order.Select(id => _cards[id].Spec).ToArray();
}

public static class StructuralDemo
{
    public static CardSpec Line() => new() { Id = "load-cases", Kind = CardKind.Line, Title = "M01 utilisation / load case", Width = 340, Values = [.32, .58, .91, 1.12, null, .73, .86, 1.04], Keys = Enumerable.Range(1, 8).Select(i => $"LC{i:00}").ToArray(), Labels = Enumerable.Range(1, 8).Select(i => $"LC{i:00}").ToArray(), WiredSlot = Slot.Bl };
    public static CardSpec[] Create(int metricCount = 4)
    {
        var cards = new List<CardSpec>();
        string[] titles = ["Max deflection", "Max utilisation", "Total mass", "Members passing"];
        string[] values = ["18.4", "1.12", "24.8", "17 / 20"];
        string[] units = ["mm", "ratio", "t", ""];
        for (var i = 0; i < metricCount; i++) cards.Add(new CardSpec { Id = $"metric-{i}", Kind = CardKind.Metric, Title = titles[i % 4], Value = values[i % 4], Unit = units[i % 4], WiredSlot = Slot.Tl });
        var v = Enumerable.Range(0, 20).Select(i => (double?)(1.12 - i * .043)).ToArray();
        cards.Add(new CardSpec { Id = "members", Kind = CardKind.Bar, Title = "Member utilisation", Values = v, Keys = Enumerable.Range(1, 20).Select(i => $"M{i:00}").ToArray(), Labels = Enumerable.Range(1, 20).Select(i => $"M{i:00}").ToArray(), WiredSlot = Slot.Tr });
        cards.Add(new CardSpec { Id = "results", Kind = CardKind.Table, Title = "Structural results", Width = 380, Headers = ["Member", "Util.", "Defl. mm", "Case"], Rows = Enumerable.Range(1, 12).Select(i => new TableRow($"M{i:00}", [$"M{i:00}", v[i - 1]!.Value.ToString("0.00", CultureInfo.InvariantCulture), (18.4 - (i - 1) * .7).ToString("0.0", CultureInfo.InvariantCulture), "ULS-01"])).ToArray(), WiredSlot = Slot.Br });
        return cards.ToArray();
    }
}
