namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(IEnumerable<ComboCategory> categories, string? name = null) {
    public List<ComboCategory> Categories { get; } = categories.ToList();
    public string? Name { get; } = name;

    public Combo WithName(string? name) => new(Categories, name);

    public Combo WithCategories(IEnumerable<ComboCategory> categories) {
        ArgumentNullException.ThrowIfNull(categories);

        return new Combo(categories, Name);
    }
}
