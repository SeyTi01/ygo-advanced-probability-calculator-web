namespace YGOProbabilityCalculatorBlazor.Models;

public class Card(IEnumerable<Category> categories, int copies = 1) {
    public int Copies { get; } = copies;
    public List<Category> Categories { get; } = categories.ToList();
}
