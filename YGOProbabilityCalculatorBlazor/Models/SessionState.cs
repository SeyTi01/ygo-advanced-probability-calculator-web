namespace YGOProbabilityCalculatorBlazor.Models;

public class SessionState
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<CategoryBase> Categories { get; init; } = [];
    public List<Card> Cards { get; init; } = [];
    public List<Combo> Combos { get; init; } = [];
    public List<ComboGroup> ComboGroups { get; init; } = [];
    public int HandSize { get; init; }
    public Dictionary<string, int> CategoryColorIndices { get; init; } = new(StringComparer.Ordinal);
}
