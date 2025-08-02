namespace YGOProbabilityCalculatorBlazor.Models;

public class SessionState {
    public List<CategoryBase> Categories { get; init; } = [];
    public List<Card> Cards { get; init; } = [];
    public List<Combo> Combos { get; init; } = [];
    public int HandSize { get; init; }
}
