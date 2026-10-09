namespace YGOProbabilityCalculatorBlazor.Models;

// A flat disjunction of complete bounded requirements. The leaves and group are
// immutable; editor changes replace a group, so duplicates never share drafts.
public sealed class ComboAlternative {
    public string Kind { get; }
    public ComboCategory? Category { get; }
    public ComboCard? Card { get; }

    public ComboAlternative(string kind, ComboCategory? category = null, ComboCard? card = null) {
        bool hasMatchingRequirement = kind switch {
            "Category" => category is not null && card is null,
            "Card" => card is not null && category is null,
            _ => false
        };

        if (!hasMatchingRequirement) {
            throw new ArgumentException("An alternative must contain exactly one matching Category or Card requirement.");
        }

        Kind = kind;
        Category = category;
        Card = card;
    }

    public static ComboAlternative For(ComboCategory category) => new("Category", category);

    public static ComboAlternative For(ComboCard card) => new("Card", card: card);
}

public sealed class ComboAlternativeGroup {
    public IReadOnlyList<ComboAlternative> Alternatives { get; }

    public ComboAlternativeGroup(IReadOnlyList<ComboAlternative?>? alternatives) {
        bool hasInvalidAlternative = alternatives is null || alternatives.Count == 0 || alternatives.Any(static alternative => alternative is null);

        if (hasInvalidAlternative) {
            throw new ArgumentException("An OR group must contain at least one complete alternative.");
        }

        Alternatives = Array.AsReadOnly([.. alternatives.Select(static alternative => alternative!)]);
    }
}
