namespace YGOProbabilityCalculatorBlazor.Models;

public class SessionState {
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<CategoryBase> Categories { get; init; } = [];
    public List<Card> Cards { get; init; } = [];
    public List<Combo> Combos { get; init; } = [];
    public List<ComboGroup> ComboGroups { get; init; } = [];
    public int HandSize { get; init; }
    public CalculationModel CalculationModel { get; init; } = CalculationModel.Fast;
    public DrawActivationPriority DrawActivationPriority { get; init; } = DrawActivationPriority.Default;
    public Dictionary<string, int> CategoryColorIndices { get; init; } = new(StringComparer.Ordinal);
}
