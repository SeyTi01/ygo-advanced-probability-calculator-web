namespace YGOProbabilityCalculatorBlazor.Models;

/// <summary>Reusable objective fields from YGOPRODeck's cardinfo endpoint.</summary>
public sealed record CardInfo {
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? FrameType { get; init; }
    public string? Race { get; init; }
    public string? Attribute { get; init; }
    public int? Level { get; init; }
    public int? LinkVal { get; init; }
    public int? Scale { get; init; }
    public string? Archetype { get; init; }
}
