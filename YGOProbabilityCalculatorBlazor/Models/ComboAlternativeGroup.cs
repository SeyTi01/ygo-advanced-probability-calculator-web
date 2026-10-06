namespace YGOProbabilityCalculatorBlazor.Models;

// A flat disjunction of complete bounded requirements. The leaves and group are
// immutable; editor changes replace a group, so duplicates never share drafts.
public sealed class ComboAlternative
{
    public string Kind { get; }
    public ComboCategory? Category { get; }
    public ComboCard? Card { get; }

    public ComboAlternative(string kind, ComboCategory? category = null, ComboCard? card = null)
    {
        if (kind == "Category"
                ? category is null || card is not null
                : kind != "Card" || card is null || category is not null)
        {
            throw new ArgumentException(
                "An alternative must contain exactly one matching Category or Card requirement."
            );
        }

        Kind = kind;
        Category = category;
        Card = card;
    }

    public static ComboAlternative For(ComboCategory category) => new("Category", category);

    public static ComboAlternative For(ComboCard card) => new("Card", card: card);
}

public sealed class ComboAlternativeGroup
{
    public IReadOnlyList<ComboAlternative> Alternatives { get; }

    public ComboAlternativeGroup(IReadOnlyList<ComboAlternative> alternatives)
    {
        if (alternatives is null || alternatives.Count == 0 || alternatives.Any(a => a is null))
        {
            throw new ArgumentException("An OR group must contain at least one complete alternative.");
        }

        Alternatives = Array.AsReadOnly(alternatives.ToArray());
    }
}
