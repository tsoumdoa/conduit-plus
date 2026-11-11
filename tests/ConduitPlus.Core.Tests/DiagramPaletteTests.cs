using System.Text.Json;
using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class DiagramPaletteTests
{
    [Fact]
    public void ExactApprovedTablesAreCategoricalAndRegisteredAfterStatus()
    {
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "diagram-palettes", "palettes.json")))!;
        Assert.Equal(snapshot.Keys, Palettes.Styles.Select(p => p.Name));
        Assert.Equal(Palettes.Styles, Palettes.All.Skip(7));
        Assert.Equal("Status", Palettes.All[6].Name);
        Assert.Equal(Palettes.All.Length, Palettes.All.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var p in Palettes.Styles)
        {
            Assert.Equal(PaletteKind.Categorical, p.Kind);
            Assert.Equal(snapshot[p.Name], p.Colours);
            Assert.Equal(p.Colours.Length, p.Colours.Distinct().Count());
            Assert.Equal(p.Colours, DiagramPalette.Create(p.Name.ToLowerInvariant(), p.Colours.Length).Colours);
        }
    }

    [Fact]
    public void EveryPrefixIsStableAndDeterministicIncludingAcrossRepetition()
    {
        foreach (var p in Palettes.Styles.Append(Palettes.All[0]))
        for (var n = 1; n <= p.Colours.Length * 2; n++)
        {
            Assert.Equal(p.Take(n), p.Take(n));
            Assert.Equal(p.Take(n), p.Take(n + 1).Take(n));
            Assert.Equal(DiagramPalette.Create(p.Name, n).Colours, DiagramPalette.Create(p.Name, n).Colours);
            for (var k = 0; k + n <= p.Colours.Length; k++)
                Assert.Equal(p.Take(n, k), p.Take(n + k).Skip(k));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ShiftWrapsWithoutDuplicatesOrOverflow(int shift)
    {
        foreach (var p in Palettes.Styles)
        {
            var colours = p.Take(p.Colours.Length, shift);
            Assert.Equal(p.Colours.Length, colours.Distinct().Count());
            for (var i = 0; i < colours.Length; i++)
                Assert.Equal(p.Colours[(int)((((long)shift + i) % colours.Length + colours.Length) % colours.Length)], colours[i]);
            Assert.Equal(p.Take(40, shift), p.Take(41, shift).Take(40));
        }
    }

    [Fact]
    public void RepetitionWarnsOnceAndBoundaryDoesNotWarn()
    {
        foreach (var p in Palettes.Styles.Append(Palettes.All[0]))
        {
            Assert.Empty(DiagramPalette.Create(p.Name, p.Colours.Length, -7).Messages);
            var result = DiagramPalette.Create(p.Name, p.Colours.Length * 2 + 1, 3);
            Assert.Equal(result.Colours[0], result.Colours[p.Colours.Length]);
            Assert.Equal(ColourMessageLevel.Warning, Assert.Single(result.Messages).Level);
        }
        Assert.Equal("Pastel has 16 colours; 20 were asked for, so they repeat from the 17th. Programme, Primary, Pop and Cool have 24.",
            Assert.Single(DiagramPalette.Create("Pastel", 20).Messages).Text);
    }

    [Fact]
    public void DefaultsUnknownStyleInvalidCountAndOrderedSampling()
    {
        Assert.Equal(Palettes.Styles[0].Take(8), DiagramPalette.Create(null, 8).Colours);
        var unknown = DiagramPalette.Create("Bold", 8);
        Assert.Equal(Palettes.Styles[0].Take(8), unknown.Colours);
        Assert.Equal("Unknown style 'Bold'; using Programme. Right-click Style for the list.", Assert.Single(unknown.Messages).Text);
        foreach (var p in Palettes.All.Where(p => p.Kind != PaletteKind.Categorical))
        {
            Assert.Equal(p.Take(5), p.Take(5, int.MinValue));
            Assert.Equal(p.ColourAt(.5), Assert.Single(p.Take(1)));
            Assert.Equal(Enumerable.Range(0, 5).Select(i => p.ColourAt(i / 4d)), p.Take(5));
        }
        Assert.Equal("Blue is a scale: sampled evenly; Shift is ignored.", Assert.Single(DiagramPalette.Create("Blue", 5, 3).Messages).Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(257)]
    [InlineData(int.MaxValue)]
    public void CountIsValidatedBeforeAllocation(int count)
    {
        Assert.Equal("Count must be 0–256.", Assert.Throws<ArgumentException>(() => DiagramPalette.Create("Programme", count)).Message);
        foreach (var p in Palettes.All)
            Assert.Equal("Count must be 0–256.", Assert.Throws<ArgumentException>(() => p.Take(count)).Message);
    }

    [Fact]
    public void ZeroIsEmptyWithoutMessagesAndMaximumCountRetainsSamplingAndRepetition()
    {
        foreach (var p in Palettes.All)
        {
            Assert.Empty(p.Take(0));
            var empty = DiagramPalette.Create(p.Name, 0);
            Assert.Empty(empty.Colours); Assert.Empty(empty.Messages);
            var maximum = p.Take(256, int.MaxValue);
            Assert.Equal(256, maximum.Length);
            Assert.Equal(maximum, DiagramPalette.Create(p.Name, 256, int.MaxValue).Colours);
            if (p.Kind == PaletteKind.Categorical) Assert.Equal(maximum[0], maximum[p.Colours.Length]);
            else { Assert.Equal(p.Colours[0], maximum[0]); Assert.Equal(p.Colours[^1], maximum[^1]); }
        }
        Assert.Empty(DiagramPalette.Create("unknown", 0).Messages);
    }

    [Fact]
    public void FullSizeStylesWarnAboutRepetitionWithoutSuggestingLargerStyles()
    {
        foreach (var p in Palettes.Styles.Where(p => p.Colours.Length == 24))
        {
            var message = Assert.Single(DiagramPalette.Create(p.Name, 25).Messages);
            Assert.Equal(ColourMessageLevel.Warning, message.Level);
            Assert.Equal($"{p.Name} has 24 colours; 25 were asked for, so they repeat from the 25th.", message.Text);
            var names = Enumerable.Range(1, 25).Select(i => $"C{i}").ToArray();
            var categoriesMessage = Assert.Single(ColourCategories.Create(names, palette: [p.Name]).Messages);
            Assert.Equal(ColourMessageLevel.Warning, categoriesMessage.Level);
            Assert.Contains("colours repeat from 'C25'", categoriesMessage.Text);
            Assert.DoesNotContain("larger style", categoriesMessage.Text);
        }
    }

    [Fact]
    public void EveryStyleColourIsDistinctFromNoDataGrey()
    {
        foreach (var p in Palettes.Styles)
        foreach (var colour in p.Colours)
            Assert.True(MinimumDistance([colour, Palettes.Missing]) >= 5, $"{p.Name} {colour} is too close to No data");
    }

    [Fact]
    public void ProgrammeSupportsTwentyOneItemLegendRowsAndCategoricalRetainsItsDefault()
    {
        var names = Enumerable.Range(1, 20).Select(i => $"Use {i}").ToArray();
        var result = ColourCategories.Create(names, palette: ["Programme"]);
        Assert.Equal(20, result.Colours.Distinct().Count());
        Assert.Empty(result.Messages); Assert.Empty(result.Legend.ValueLabels);
        Assert.Equal(result.Colours, result.Legend.Colors);
        Assert.Empty(ColourCategories.Create(names, names, ["Programme"]).Legend.ValueLabels);
        var repeated = ColourCategories.Create(names.Take(9).ToArray());
        Assert.Equal(8, repeated.Colours.Distinct().Count());
        Assert.Contains("Programme (24 colours)", Assert.Single(repeated.Messages).Text);
        Assert.Equal(["2", "1"], ColourCategories.Create(["A", "A", "B"]).Legend.ValueLabels);
        Assert.Empty(ColourCategories.Create(["A", "A", "B"], ["a", "a", "b"]).Legend.ValueLabels);
        Assert.Equal(["1", "0"], ColourCategories.Create(["A", "A"], ["a", "a"], order: ["A", "B"]).Legend.ValueLabels);
        Assert.Equal(["1", "0"], ColourCategories.Create(["A"], order: ["A", "B"]).Legend.ValueLabels);
        Assert.Empty(ColourCategories.Create(["A", "A"], showCounts: false).Legend.ValueLabels);
    }

    [Theory]
    [InlineData("Programme", 12, 13.3, 9.8, 6.0)]
    [InlineData("Primary", 10, 17.8, 12.0, 6.4)]
    [InlineData("Pop", 11, 18.3, 7.8, 5.0)]
    [InlineData("Pastel", 10, 7.5, 5.9, 5.1)]
    [InlineData("Earth", 12, 11.2, 5.7, 5.7)]
    [InlineData("Accent", 8, 11.6, 7.9, 5.5)]
    [InlineData("Cool", 9, 11.8, 9.7, 5.1)]
    public void PerceptualDistinctnessMatchesTheApprovedReport(string name, int baseCount, double six, double basis, double all)
    {
        var p = Palettes.Styles.Single(p => p.Name == name);
        Assert.InRange(MinimumDistance(p.Take(6)), six - .051, six + .051);
        Assert.InRange(MinimumDistance(p.Take(baseCount)), basis - .051, basis + .051);
        Assert.InRange(MinimumDistance(p.Take(p.Colours.Length)), all - .051, all + .051);
        Assert.True(MinimumDistance(p.Take(6)) >= (name == "Pastel" ? 7 : 11));
        Assert.True(MinimumDistance(p.Take(baseCount)) >= 5.5);
        Assert.True(MinimumDistance(p.Take(p.Colours.Length)) >= 4.9);
    }

    // OKLab lives only in tests; runtime colours are the approved, fixed RGB tables.
    private static double MinimumDistance(string[] colours)
    {
        static (double L, double A, double B) Lab(string hex)
        {
            static double Linear(byte c) => c / 255d <= .04045 ? c / 255d / 12.92 : Math.Pow((c / 255d + .055) / 1.055, 2.4);
            var c = ColorRgba.Parse(hex); var r = Linear(c.R); var g = Linear(c.G); var b = Linear(c.B);
            var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
            var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
            var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
            return (.2104542553 * l + .7936177850 * m - .0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
                .0259040371 * l + .7827717662 * m - .8086757660 * s);
        }
        var lab = colours.Select(Lab).ToArray(); var minimum = double.MaxValue;
        for (var i = 0; i < lab.Length; i++)
        for (var j = 0; j < i; j++)
            minimum = Math.Min(minimum, 100 * Math.Sqrt(Math.Pow(lab[i].L - lab[j].L, 2) + Math.Pow(lab[i].A - lab[j].A, 2) + Math.Pow(lab[i].B - lab[j].B, 2)));
        return minimum;
    }
}
