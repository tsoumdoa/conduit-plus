using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class ColourHelpersTests
{
    [Fact]
    public void PaletteSnapshotAndNames()
    {
        Assert.Equal("Categorical:#2466C3 #9A9606 #B54303 #15A2B6 #8E7DEC #93518E #E36586 #2E7A50\n" +
            "Blue:#CFE8FE #8CC0F2 #5196DF #296CBA #184687\n" +
            "Teal:#C8EEE8 #7BCBC6 #31A5A5 #017B80 #095258\n" +
            "Heat:#EFE6B1 #E3B25A #D37812 #B4410D #831826\n" +
            "Blue–Red:#0B4C92 #6D9BD5 #DFDEDA #CF7F79 #862726\n" +
            "Purple–Green:#633481 #AB87C7 #DFDEDA #69AA77 #015E28\n" +
            "Status:#209993 #EBC75E #C83846", string.Join("\n", Palettes.All.Take(7).Select(p => p.Name + ":" + string.Join(" ", p.Colours))));
        Assert.Equal(Palettes.All.Length, Palettes.All.Select(p => p.Name.ToUpperInvariant()).Distinct().Count());
        foreach (var p in Palettes.All)
        {
            Assert.All(p.Colours, c => { Assert.Equal(7, c.Length); Assert.Equal(255, ColorRgba.Parse(c).A); });
            var reversed = Palettes.Resolve([p.Name.ToLowerInvariant().Replace('–', '-') + " reversed"], "Blue", []);
            Assert.Equal(p.Colours.Reverse(), reversed.Colours);
        }
    }

    [Fact]
    public void CustomPaletteAcceptsHexAndGrasshopperRgbTextAndIgnoresAlpha()
    {
        var p = Palettes.Resolve(["#ff000080", "0, 255, 0 (128)", "64,0,0,255"], "Blue", []);
        Assert.Equal(new[] { "#FF0000", "#00FF00", "#0000FF" }, p.Colours.ToArray());
        Assert.Equal("#808080", Palettes.Resolve(["#000000", "#FFFFFF"], "Blue", []).ColourAt(.5));
    }

    [Fact]
    public void UnknownPaletteWarnsAndUsesTheComponentsDefault()
    {
        var scale = ColourScale.Create([1, 2], ["Blues"]);
        Assert.Equal("Blue", scale.Palette.Name);
        Assert.Equal("Unknown palette 'Blues'; using Blue. Right-click Palette for the list.", Assert.Single(scale.Messages).Text);
        var cat = ColourCategories.Create(["A"], palette: ["Blues"]);
        Assert.Equal(Palettes.All[0].Colours[0], cat.Colours[0]);
        Assert.Contains(cat.Messages, m => m.Level == ColourMessageLevel.Warning && m.Text.Contains("using Categorical"));
    }

    [Fact]
    public void ScaleRejectsEveryNamedCategoryPaletteIncludingReversedAndCaseVariants()
    {
        foreach (var p in Palettes.All.Where(p => p.Kind == PaletteKind.Categorical))
        foreach (var reversed in new[] { false, true })
        {
            var name = p.Name + (reversed ? " reversed" : "");
            var result = ColourScale.Create([0, .5, 1], [name.ToUpperInvariant()], min: 0, max: 1);
            Assert.Equal("Blue", result.Palette.Name);
            Assert.Equal(ColourScale.Create([0, .5, 1], min: 0, max: 1).Colours, result.Colours);
            var warning = Assert.Single(result.Messages);
            Assert.Equal(ColourMessageLevel.Warning, warning.Level);
            Assert.Equal($"'{name}' is a category palette; using Blue. Choose a sequential, diverging or Status palette.", warning.Text);
        }
        var custom = ColourScale.Create([0, .5, 1], ["#000000", "#FFFFFF"], min: 0, max: 1);
        Assert.Equal("Custom", custom.Palette.Name);
        Assert.Equal(["#000000", "#808080", "#FFFFFF"], custom.Colours);
        Assert.Empty(custom.Messages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    public void CustomPaletteSizeIsValidated(int count) => Assert.Throws<ArgumentException>(() =>
        Palettes.Resolve(Enumerable.Repeat("#000000", count).ToArray(), "Blue", []));

    [Fact]
    public void InvalidColourDoesNotBecomeAnUnknownPalette() => Assert.Throws<ArgumentException>(() => Palettes.Resolve(["#oops"], "Blue", []));

    [Fact]
    public void NiceRangeConstantValuesAndLegendOnly()
    {
        var result = ColourScale.Create([3.2, 37]);
        Assert.Equal(0, result.Min); Assert.Equal(40, result.Max);
        var constant = ColourScale.Create([5, 5]);
        Assert.Equal(5, constant.Min); Assert.Equal(5, constant.Max);
        Assert.Equal(constant.Palette.ColourAt(.5), constant.Colours[0]);
        var empty = ColourScale.Create([], min: 0, max: 40);
        Assert.Empty(empty.Colours); empty.Legend.Validate();
        Assert.Empty(GroupInputs.Create([], 0, false, null, gradient: true));
    }

    [Fact]
    public void BoundsAreIndependentAndExplicitNumbersAreKept()
    {
        var lower = ColourScale.Create([3.2, 37], min: 2.1);
        Assert.Equal(2.1, lower.Min); Assert.Equal(40, lower.Max);
        var upper = ColourScale.Create([3.2, 37], max: 38.4);
        Assert.Equal(0, upper.Min); Assert.Equal(38.4, upper.Max);
    }

    [Fact]
    public void ClampingMissingValuesAndMessages()
    {
        var result = ColourScale.Create([-1, 0, .5, 1, 2, null, double.NaN, double.PositiveInfinity], min: 0, max: 1);
        Assert.Equal(result.Palette.Colours[0], result.Colours[0]);
        Assert.Equal(result.Palette.Colours[^1], result.Colours[4]);
        Assert.All(result.Colours.Skip(5), c => Assert.Equal(Palettes.Missing, c));
        Assert.Contains(result.Messages, m => m.Text == "2 of 8 values are outside 0–1 and use the end colours.");
        Assert.Contains(result.Messages, m => m.Text.StartsWith("3 missing"));
    }

    [Theory]
    [InlineData("Status")]
    [InlineData("Blue-Red")]
    [InlineData("Purple-Green reversed")]
    public void DivergingAndStatusUseZeroAndHonourExplicitMid(string name)
    {
        var result = ColourScale.Create([-4, 0, 8], [name], min: -4, max: 8);
        Assert.Equal(0, result.Mid);
        Assert.Equal(result.Palette.ColourAt(.5), result.ColourAt(0));
        var status = ColourScale.Create([0, 1, 1.2], [name], min: 0, max: 1.2, mid: 1);
        Assert.Equal(status.Palette.Colours[0], status.Colours[0]);
        Assert.Equal(status.Palette.ColourAt(.5), status.Colours[1]);
        Assert.Equal(status.Palette.Colours[^1], status.Colours[2]);
    }

    [Fact]
    public void ScaleRejectsMissingRangeReversedEqualAndInvalidMid()
    {
        Assert.Equal("Connect Values (or set Min and Max).", Assert.Throws<ArgumentException>(() => ColourScale.Create([null, double.NaN])).Message);
        Assert.Throws<ArgumentException>(() => ColourScale.Create([], min: 0));
        Assert.Equal("Min (40) must be less than Max (12).", Assert.Throws<ArgumentException>(() => ColourScale.Create([], min: 40, max: 12)).Message);
        Assert.Throws<ArgumentException>(() => ColourScale.Create([], min: 1, max: 1));
        foreach (var mid in new[] { 0, 1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentException>(() => ColourScale.Create([], min: 0, max: 1, mid: mid));
        Assert.Throws<ArgumentException>(() => ColourScale.Create([], min: 0, max: double.PositiveInfinity));
    }

    [Fact]
    public void FirstAppearanceIsCaseSensitiveAndNoDataIsLast()
    {
        var r = ColourCategories.Create(["B", null, "A", "B", "a", " "]);
        Assert.Equal(["B", "A", "a", "No data"], r.Categories);
        Assert.Equal(["2", "1", "1", "2"], r.Legend.ValueLabels);
        Assert.Equal(r.Colours[0], r.Colours[3]);
        Assert.Equal(Palettes.Missing, r.Colours[1]);
        Assert.Equal(Palettes.Missing, r.Colours[5]);
        Assert.Empty(r.GroupKeys); Assert.Empty(r.Members); Assert.Empty(r.Legend.Keys);
        Assert.All(r.Legend.Colors.Select((colour, i) => (colour, i)), x => Assert.Equal(r.CategoryColours[x.i], x.colour));
        r.Legend.Validate();
    }

    [Fact]
    public void OrderPinsSlotsAndRetainsEmptyGroupsAcrossFiltering()
    {
        string[] order = ["A", "B", "C"];
        var full = ColourCategories.Create(["B", "A", "B", "C"], ["b1", "a1", "b2", "c1"], order: order, title: "Types");
        var filtered = ColourCategories.Create(["C", "A"], ["c1", "a1"], order: order, title: "Types");
        Assert.Equal(full.CategoryColours, filtered.CategoryColours);
        Assert.Equal(["Types:A@colour-categories", "Types:B@colour-categories", "Types:C@colour-categories"], filtered.GroupKeys);
        Assert.Empty(filtered.Members[1]); Assert.Empty(filtered.Legend.Groups["Types:B@colour-categories"].Members);
        Assert.Equal(["1", "0", "1"], filtered.Legend.ValueLabels);
        Assert.Equal(full.CategoryColours[2], filtered.Colours[0]);
        filtered.Legend.Validate();
        var groups = GroupInputs.Create(filtered.GroupKeys, 3, true, filtered.Members.Select((m, i) => new MemberInputBranch($"{{{i}}}", m)).ToArray());
        Assert.Equal(filtered.Legend.Groups["Types:A@colour-categories"].Members.ToArray(), groups["Types:A@colour-categories"].Members.ToArray());
    }

    [Fact]
    public void UnknownCategoriesAppendWithFableWarning()
    {
        var r = ColourCategories.Create(["Plant", "Store", "A"], order: ["A"]);
        Assert.Equal(["A", "Plant", "Store"], r.Categories);
        Assert.Contains(r.Messages, m => m.Text == "2 categories are not in Order ('Plant', 'Store'); added after the listed ones.");
    }

    [Fact]
    public void NinthCategoryRepeatsAndWarnsButMissingDoesNotUseAPaletteSlot()
    {
        var names = Enumerable.Range(1, 9).Select(i => "C" + i).ToArray();
        var r = ColourCategories.Create(names.Append(null).ToArray());
        Assert.Equal(r.CategoryColours[0], r.CategoryColours[8]);
        Assert.Contains(r.Messages, m => m.Level == ColourMessageLevel.Warning && m.Text.Contains("colours repeat from 'C9'"));
        Assert.DoesNotContain(ColourCategories.Create(names.Take(8).Append(null).ToArray()).Messages, m => m.Level == ColourMessageLevel.Warning);
    }

    [Fact]
    public void StatusIsSampledFromGoodToBadAndOrderSuppressesWarning()
    {
        var two = ColourCategories.Create(["Good", "Bad"], palette: ["Status"]);
        Assert.Equal(["#209993", "#C83846"], two.CategoryColours);
        Assert.Contains(two.Messages, m => m.Level == ColourMessageLevel.Warning);
        var three = ColourCategories.Create(["Bad", "Good", "Warning"], palette: ["Status"], order: ["Good", "Warning", "Bad"]);
        Assert.Equal(["#209993", "#EBC75E", "#C83846"], three.CategoryColours);
        Assert.Empty(three.Messages);
    }

    [Fact]
    public void MembersDeduplicateAndCountsCanBeHidden()
    {
        var r = ColourCategories.Create(["A", "A", "B"], ["a", "a", "b"]);
        Assert.Equal(["a"], r.Members[0]); Assert.Empty(r.Legend.ValueLabels);
        Assert.Equal(["2", "1"], ColourCategories.Create(["A", "A", "B"], ["a1", "a2", "b"]).Legend.ValueLabels);
        Assert.Empty(ColourCategories.Create(["A"], showCounts: false).Legend.ValueLabels);
        var empty = ColourCategories.Create([], [], order: ["A"]);
        Assert.Empty(empty.Members[0]); empty.Legend.Validate();
    }

    [Fact]
    public void GroupAndKeyErrorsAreRejectedBeforeAnyOutputs()
    {
        Assert.Equal("Keys has 1 item and Categories has 2; wire one key per item.", Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A", "B"], ["a"])).Message);
        Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A"], [" "]));
        Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A"], ["\u0001clear"]));
        Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A", "B"], ["a", "a"]));
        Assert.Contains("collides", Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A"], ["Legend:A@colour-categories"])).Message);
        Assert.Throws<ArgumentException>(() => ColourCategories.Create(["A"], order: ["A", "A"]));
        Assert.Throws<ArgumentException>(() => ColourCategories.Create([]));
    }
}
