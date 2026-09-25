namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(IEnumerable<ComboCategory> categories, string? name = null, bool active = true) {
    public List<ComboCategory> Categories { get; } = categories.ToList();
    public string? Name { get; } = name;
    public bool Active { get; } = active;

    public Combo WithName(string? name) => new(Categories, name, Active);
    public Combo WithActive(bool active) => new(Categories, Name, active);

    public Combo WithCategories(IEnumerable<ComboCategory> categories) {
        ArgumentNullException.ThrowIfNull(categories);

        return new Combo(categories, Name, Active);
    }
}
