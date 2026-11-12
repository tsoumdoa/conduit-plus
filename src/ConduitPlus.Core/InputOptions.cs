namespace ConduitPlus.Core;

public sealed record InputOption(string Label, string? Value, string? Group = null)
{
    public bool Matches(string? value) => string.Equals(Value ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
}

public static class InputOptions
{
    public static IReadOnlyList<InputOption> BarGoodWhen { get; } = Array.AsReadOnly(new InputOption[] {
        new("Below", null), new("Above", "Above"), new("Within", "Within") });
    public static IReadOnlyList<InputOption> BarSort { get; } = Array.AsReadOnly(new InputOption[] {
        new("Original", null), new("Worst first", "Worst first"), new("Best first", "Best first") });
    public static IReadOnlyList<InputOption> Slots { get; } = Array.AsReadOnly(new InputOption[] {
        new("Automatic", null), new("Top left", "tl"), new("Top right", "tr"),
        new("Bottom left", "bl"), new("Bottom right", "br"), new("Left centre", "lc"), new("Right centre", "rc") });
    public static IReadOnlyList<InputOption> TitlePositions { get; } = Array.AsReadOnly(new InputOption[] {
        new("Top left", "tl"), new("Top middle", "tc"), new("Top right", "tr"),
        new("Bottom left", "bl"), new("Bottom middle", "bc"), new("Bottom right", "br") });
    public static IReadOnlyList<InputOption> Themes { get; } = Array.AsReadOnly(new[] { new InputOption("Use component menu", null) }
        .Concat(Theme.Presets.Select(t => new InputOption(t.Name, t.Name))).ToArray());
}
