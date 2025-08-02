namespace YGOProbabilityCalculatorBlazor.Models;

public class Card(IEnumerable<CategoryBase> categories, int copies = 1, string? name = null) {
    public int Copies { get; } = copies;
    public List<CategoryBase> Categories { get; } = categories.ToList();
    public string? Name { get; } = name;

    public Card WithName(string? name) => new(Categories, Copies, name);
    public Card WithCopies(int copies) => new(Categories, copies, Name);
    public Card WithCategories(IEnumerable<CategoryBase> categories) => new(categories, Copies, Name);
}
