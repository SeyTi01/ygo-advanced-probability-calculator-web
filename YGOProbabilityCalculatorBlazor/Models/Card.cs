namespace YGOProbabilityCalculatorBlazor.Models;

public class Card(IEnumerable<CategoryBase> categories, int copies = 1, string? name = null) {
    public int Copies { get; } = copies;
    public List<CategoryBase> Categories { get; } = categories.ToList();
    public string? Name { get; } = name;
}
