namespace YGOProbabilityCalculatorBlazor.Models;

public class Card(IEnumerable<CategoryBase> categories, int copies = 1) {
    public int Copies { get; } = copies;
    public List<CategoryBase> Categories { get; } = categories.ToList();
}
