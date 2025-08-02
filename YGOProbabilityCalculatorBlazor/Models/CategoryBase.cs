namespace YGOProbabilityCalculatorBlazor.Models;

public record CategoryBase {
    public string Name { get; }

    public CategoryBase(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category cannot be empty.", nameof(name));
        Name = name;
    }
}
