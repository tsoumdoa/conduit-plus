namespace ConduitPlus.Core;

public sealed record DemoBlock(string Key, string Use, double X, double Y, double Width, double Depth, int Floors, double FloorHeight, double HeightLimit)
{
    public double Footprint => Width * Depth;
    public double Gfa => Footprint * Floors;
    public double Height => Floors * FloorHeight;
    public double HeightRatio => Height / HeightLimit;
    public bool Pass => Height <= HeightLimit;
}

public sealed record DemoMassingResult(DemoBlock[] Blocks, double SiteWidth, double SiteDepth, double AllowedFar)
{
    public double SiteArea => SiteWidth * SiteDepth;
    public double TotalGfa => Blocks.Sum(b => b.Gfa);
    public double AllowedGfa => SiteArea * AllowedFar;
    public double Far => TotalGfa / SiteArea;
    public bool Pass => TotalGfa <= AllowedGfa && Blocks.All(b => b.Pass);
    public int HeightFailures => Blocks.Count(b => !b.Pass);
    public string[] Uses => ["Residential", "Office", "Retail"];
    public double[] GfaByUse => Uses.Select(use => Blocks.Where(b => b.Use == use).Sum(b => b.Gfa)).ToArray();
}

/// <summary>Metre-based zoning study. GFA counts whole floorplates; limits are fictitious, not planning advice.</summary>
public static class DemoMassing
{
    public static DemoMassingResult Create(int extraFloors = 0)
    {
        if (extraFloors is < -1 or > 5) throw new ArgumentOutOfRangeException(nameof(extraFloors), "Extra floors must be -1–5.");
        (string Use, double X, double Y, double W, double D, int Floors)[] data =
        [
            ("Residential", 8, 8, 20, 18, 8), ("Office", 38, 8, 24, 16, 10), ("Retail", 72, 8, 18, 14, 3),
            ("Residential", 8, 45, 26, 18, 7), ("Office", 44, 44, 20, 16, 12), ("Retail", 74, 46, 16, 12, 2)
        ];
        return new(data.Select((b, i) => new DemoBlock($"B{i + 1:00}", b.Use, b.X, b.Y, b.W, b.D,
            b.Floors + extraFloors, b.Use == "Retail" ? 4.2 : 3.3, 30)).ToArray(), 100, 80, 2);
    }
}
