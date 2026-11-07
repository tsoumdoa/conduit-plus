namespace ConduitPlus.Core;

public sealed record DemoRoom(string Key, string Name, string Type, double X, double Y, double Width, double Depth, double Required)
{
    public double Area => Width * Depth;
    public double Delta => Area - Required;
    public bool Pass => Delta >= 0;
}

public sealed record DemoRoomsResult(DemoRoom[] Rooms)
{
    // Schematic GIA: tiled footprint, including circulation; wall thickness is omitted.
    public double GrossArea => Rooms.Sum(r => r.Area);
    public double CirculationArea => Rooms.Where(r => r.Type == "Circulation").Sum(r => r.Area);
    public double NetArea => GrossArea - CirculationArea;
    public double NetToGrossPercent => 100 * NetArea / GrossArea;
    public double CirculationPercent => 100 * CirculationArea / GrossArea;
    public int Failing => Rooms.Count(r => !r.Pass);
}

/// <summary>Metre-based schematic accommodation schedule. Minima are illustrative, not building regulations.</summary>
public static class DemoRooms
{
    public static DemoRoomsResult Create(double scale = 1)
    {
        if (!double.IsFinite(scale) || scale is < .5 or > 2) throw new ArgumentOutOfRangeException(nameof(scale), "Plan scale must be 0.5–2.");
        double[] widths = [4, 6, 4, 6], depths = [3, 4, 4, 3], starts = [0, 3, 9, 13];
        string[] types = ["Bedroom", "Living", "Kitchen", "Bath", "Study", "Bedroom", "Kitchen", "Living",
            "Bedroom", "Living", "Bath", "Bedroom", "Study", "Kitchen", "Bedroom", "Living"];
        var rooms = new List<DemoRoom>();
        for (var row = 0; row < 4; row++)
        {
            double x = 0;
            for (var col = 0; col < 4; col++)
            {
                var type = types[row * 4 + col];
                var required = type switch { "Bedroom" => 16, "Living" => 25, "Kitchen" => 18, "Bath" => 6, _ => 12 };
                var key = $"R{rooms.Count + 1:00}";
                var name = $"{(type == "Bedroom" ? "Bed" : type == "Living" ? "Liv" : type == "Kitchen" ? "Kit" : type)} {rooms.Count + 1:00}";
                rooms.Add(new(key, name, type, x * scale, starts[row] * scale, widths[col] * scale, depths[row] * scale, required));
                x += widths[col];
            }
        }
        rooms.Add(new("R17", "Hall", "Circulation", 0, 7 * scale, 20 * scale, 2 * scale, 0));
        return new(rooms.ToArray());
    }
}
