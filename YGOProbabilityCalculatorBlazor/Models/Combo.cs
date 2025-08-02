namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(IEnumerable<ComboCategory> categories) {
    public List<ComboCategory> Categories { get; } = categories.ToList();
}
