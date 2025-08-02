namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(IEnumerable<Category> categories) {
    public List<Category> Categories { get; } = categories.ToList();
}
