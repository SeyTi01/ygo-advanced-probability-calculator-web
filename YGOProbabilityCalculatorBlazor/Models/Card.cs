namespace YGOProbabilityCalculatorBlazor.Models;

public class Card(IEnumerable<CategoryBase> categories, int copies = 1, string? name = null, bool active = true) {
    public int Copies { get; } = copies;
    public List<CategoryBase> Categories { get; } = categories.ToList();
    public string? Name { get; } = name;
    public bool Active { get; } = active;

    public Card WithName(string? name) => new(Categories, Copies, name, Active);
    public Card WithCopies(int copies) => new(Categories, copies, Name, Active);
    public Card WithCategories(IEnumerable<CategoryBase> categories) => new(categories, Copies, Name, Active);
    public Card WithActive(bool active) => new(Categories, Copies, Name, active);
}
