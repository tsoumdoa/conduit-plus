using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitPlus.Core;

public enum CardKind { Metric, Bar, Table, Line, Text, Legend }
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
    public string Text { get; init; } = "";
    public string[] Colors { get; init; } = [];
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double?[] Values { get; init; } = [];
    public string[] Labels { get; init; } = [];
    public string[] Keys { get; init; } = [];
    private readonly ImmutableDictionary<string, GroupDefinition> _groups = ImmutableDictionary<string, GroupDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    /// <summary>Entry keys with explicit group membership; absent entries retain individual-key semantics.</summary>
    public ImmutableDictionary<string, GroupDefinition> Groups
    {
        get => _groups;
        init => _groups = value.WithComparers(StringComparer.Ordinal);
    }
    /// <summary>Effective hovered individual entries; group state lives in GroupStates. A2 consumes these fields.</summary>
    public ImmutableArray<string> HoveredKeys { get; init; } = [];
    public ImmutableDictionary<string, GroupProjection> GroupStates { get; init; } = ImmutableDictionary<string, GroupProjection>.Empty.WithComparers(StringComparer.Ordinal);
    public double Threshold { get; init; } = 1;
    public string[] Headers { get; init; } = [];
    public TableRow[] Rows { get; init; } = [];
    public Slot? WiredSlot { get; init; }
    public Slot? ManualSlot { get; init; }
    public Slot? OrderSlot { get; init; }
    public int? ManualOrder { get; init; }
    public double Width { get; init; } = 280;
    public string? HoverKey { get; init; }
    public string[] SelectedKeys { get; init; } = [];
    public bool OwnerHasSelection { get; init; }
    /// <summary>Table only: rows shown at once; more rows scroll.</summary>
    public int MaxRows { get; init; } = TableScroll.DefaultVisibleRows;
    /// <summary>Table only: first visible row. View state owned by the overlay session, clamped when painted.</summary>
    public int ScrollOffset { get; init; }
    /// <summary>Table only: session-owned display sort; null preserves the incoming row order.</summary>
    public int? SortColumn { get; init; }
    public bool SortDescending { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Card ID is required.");
        if (WiredSlot is { } wired && !Enum.IsDefined(wired) || ManualSlot is { } manual && !Enum.IsDefined(manual) || OrderSlot is { } orderSlot && !Enum.IsDefined(orderSlot) || ManualOrder < 0) throw new ArgumentException("Invalid card layout.");
        if (!double.IsFinite(Width) || Width < 180 || Width > 1200) throw new ArgumentException("Width must be 180–1200 CSS pixels.");
        if (!double.IsFinite(Threshold) || Threshold < 0 || (Kind is not (CardKind.Line or CardKind.Bar) && Threshold == 0)) throw new ArgumentException("Threshold must be positive and finite (Bar/Line also accept zero).");
        if (Values.Any(v => v.HasValue && !double.IsFinite(v.Value))) throw new ArgumentException("Use null for missing values; infinity/NaN are invalid.");
        if (Kind is CardKind.Bar or CardKind.Line && (Values.Length != Keys.Length || Values.Length != Labels.Length)) throw new ArgumentException("Chart values, labels and keys must have equal lengths.");
        if (Kind == CardKind.Line && Values.Length < 2) throw new ArgumentException("Line cards need at least two samples.");
        if (Kind == CardKind.Bar && Colors.Length != 0 && (Colors.Length != Values.Length || Colors.Any(color => color is null || color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))) throw new ArgumentException("Optional bar colors must match values and be #RRGGBB strings.");
        if (Kind == CardKind.Text && (Text is null || Keys.Length > 0)) throw new ArgumentException("Text cards require text and are not keyed.");
        if (Kind == CardKind.Legend)
        {
            if (Colors.Length == 0 || Colors.Any(color => color is null || color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))) throw new ArgumentException("Legend colors must be #RRGGBB strings.");
            if (Min.HasValue != Max.HasValue) throw new ArgumentException("Gradient legends need both Min and Max.");
            if (Min.HasValue)
            {
                if (Colors.Length < 2 || !double.IsFinite(Min.Value) || !double.IsFinite(Max!.Value) || Min.Value >= Max.Value || !double.IsFinite(Max.Value - Min.Value)) throw new ArgumentException("Gradient legends need 2+ colors and finite Min < Max.");
                if (Keys.Length > 0) throw new ArgumentException("Only categorical legends can have keys.");
            }
            else if (Labels.Length != Colors.Length || (Keys.Length != 0 && Keys.Length != Colors.Length)) throw new ArgumentException("Categorical colors, labels and optional keys must match.");
        }
        var keys = Kind == CardKind.Table ? Rows.Select(r => r.Key).ToArray() : Keys;
        if (keys.Any(k => string.IsNullOrWhiteSpace(k) || CardMeasure.IsReserved(k)) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new ArgumentException("Row/bar keys must be nonempty, non-reserved and unique within a card.");
        if (Kind == CardKind.Table && (Headers.Length == 0 || Rows.Any(r => r.Cells.Length != Headers.Length))) throw new ArgumentException("Each row must match the table headers.");
        GroupDefinition.Validate(this);
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

    /// <summary>Minimal scroll to reveal a key in the sorted display order. Missing keys leave the offset alone.</summary>
    public static int Reveal(CardSpec c, string key)
    {
        var offset = Offset(c);
        if (c.Kind != CardKind.Table) return offset;
        var index = Array.FindIndex(TableSort.Rows(c), r => r.Key == key);
        if (index < 0) return offset;
        return Math.Clamp(index < offset ? index : index >= offset + Visible(c) ? index - Visible(c) + 1 : offset, 0, MaxOffset(c));
    }

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
    public string Name { get; init; } = "Paper";
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
    public string Highlight { get; init; } = "#EEF0F3";
    public string TooltipSurface { get; init; } = "#1F2329";
    public string TooltipInk { get; init; } = "#FFFFFF";
    public double TooltipRadius { get; init; } = 4;
    public string Border { get; init; } = "#00000000";
    public double BorderWidth { get; init; }
    // Legacy Paper has no painted shadow; keep its pixels and bitmap extent unchanged.
    public bool Shadow { get; init; }
    public string ShadowColor { get; init; } = "#00000008";
    public double ShadowExtent => Shadow ? 8 : 0;
    public double GhostOpacity { get; init; } = .85;
    public string LayoutOutline => ColorRgba.Parse(Accent).WithAlpha(100).ToHex();
    public string LayoutFill => ColorRgba.Parse(Accent).WithAlpha(15).ToHex();
    public string LayoutActiveFill => ColorRgba.Parse(Accent).WithAlpha(40).ToHex();
    public string Font { get; init; } = "Segoe UI";
    public double Padding { get; init; } = 12;
    public double FontSize { get; init; } = 12;
    public double RowHeight { get; init; } = 22;
    public double Radius { get; init; } = 5;
    public static Theme Paper { get; } = new();
    public static Theme Night { get; } = new()
    {
        Name = "Night", Surface = "#1A1D23", Ink = "#E9EAEE", Muted = "#9AA0AC",
        Line = "#2C3039", Track = "#262A31", Accent = "#6D8CFF",
        Good = "#4CC48E", Bad = "#F06D86", Area = "#252F4F", ThresholdBand = "#3D2733",
        Highlight = "#303C60", TooltipSurface = "#262A31", TooltipInk = "#E9EAEE"
    };
    // Auto is a selection, not a paint palette. Hosts resolve it before measuring/caching/painting.
    public static Theme Auto { get; } = new() { Name = "Auto" };
    public static Theme NewComponentDefault => Auto;
    public static Theme Smoke { get; } = new()
    {
        Name = "Smoke", Surface = "#12151CCC", Ink = "#F2F4F8", Muted = "#A9B0BF",
        Line = "#FFFFFF1F", Track = "#FFFFFF1A", Accent = "#8FB0FF", Radius = 10,
        Good = "#2FAE74", Bad = "#E0546F", Area = "#8FB0FF26", ThresholdBand = "#E0546F26",
        Highlight = "#8FB0FF33", TooltipSurface = "#12151CCC", TooltipInk = "#F2F4F8", TooltipRadius = 6
        // No shadow: stacked outline shadows rendered as a hard grey band in viewport and canvas captures.
    };
    public static Theme Blueprint { get; } = new()
    {
        Name = "Blueprint", Surface = "#0A2654CC", Ink = "#E6F1FF", Muted = "#8FB4E3",
        Line = "#8CBEFF40", Track = "#8CBEFF24", Accent = "#7FD3FF", Radius = 0,
        Good = "#2FAE74", Bad = "#E0546F", Area = "#7FD3FF26", ThresholdBand = "#E0546F2E",
        Highlight = "#7FD3FF33", TooltipSurface = "#0A2654CC", TooltipInk = "#E6F1FF", TooltipRadius = 0,
        Border = "#8CBEFF73", BorderWidth = 1
    };
    public static IReadOnlyList<Theme> Presets { get; } = Array.AsReadOnly(new[] { Auto, Paper, Night, Smoke, Blueprint });
    public Theme Resolve(bool darkMode) => this == Auto ? (darkMode ? Night : Paper) : this;
    /// <summary>Read must override the new-component default: old files without a theme stay Paper.</summary>
    public static Theme ReadSelection(string? savedName)
    {
        TryGet(savedName, out var selection);
        return selection;
    }
    /// <summary>Unknown names return false and the backwards-compatible Paper fallback.</summary>
    public static bool TryGet(string? name, out Theme theme)
    {
        theme = Presets.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Paper;
        return Presets.Any(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
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
    private Theme _theme = Theme.Paper;
    public IReadOnlyList<CardSpec> Update(IEnumerable<CardSpec> specs, Theme? theme = null)
    {
        var incoming = specs.ToArray();
        if (incoming.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Length) throw new ArgumentException("Duplicate card IDs.");
        foreach (var c in incoming) c.Validate();
        theme ??= Theme.Paper;
        var themeChanged = _theme != theme;
        var dirty = new List<CardSpec>();
        foreach (var input in incoming)
        {
            var content = CardJson.Serialize(input with { Rev = 0 });
            if (!themeChanged && _cards.TryGetValue(input.Id, out var old) && old.Content == content) continue;
            var snapshot = CardJson.Deserialize<CardSpec>(content) with { Rev = ++_revision };
            _cards[input.Id] = (content, snapshot);
            dirty.Add(snapshot);
        }
        var live = incoming.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _cards.Keys.Where(k => !live.Contains(k)).ToArray()) _cards.Remove(id);
        _order = incoming.Select(c => c.Id).ToArray();
        _theme = theme;
        return dirty;
    }
    public CardSpec[] Snapshot => _order.Select(id => _cards[id].Spec).ToArray();
}

public static class StructuralDemo
{
    public static CardSpec[] Extras() =>
    [
        new() { Id = "utilisation-legend", Kind = CardKind.Legend, Title = "Utilisation scale", Colors = ["#1F8A5B", "#E5B63D", "#C2334D"], Min = 0, Max = 1.2, Unit = "ratio", WiredSlot = Slot.Lc },
        new() { Id = "result-note", Kind = CardKind.Text, Title = "Design note", Text = "ULS envelope · preliminary results\nReview members above 1.0 utilisation before issuing the design.", WiredSlot = Slot.Lc },
        new() { Id = "status-legend", Kind = CardKind.Legend, Title = "Member status", Colors = ["#1F8A5B", "#C2334D"], Labels = ["Passing", "Requires review"], Keys = ["passes", "fails"], WiredSlot = Slot.Lc }
    ];
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
