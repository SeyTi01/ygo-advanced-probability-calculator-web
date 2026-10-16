namespace YGOProbabilityCalculatorBlazor.Models;

// A search transfers exactly one remaining Main Deck copy to hand and consumes
// its source. Activation limits belong to the enclosing effect plan.
public sealed record SearchEffectMode(CardSelector Selector, int? ActivationLimit = null);

public sealed record SearchEffectPlan(CardSelector Selector, bool ObjectiveMetadataOnly = true, IReadOnlyList<SearchEffectMode>? Modes = null) {
    private IReadOnlyList<SearchEffectMode>? _modes = Modes is null ? null : Array.AsReadOnly(Modes.ToArray());
    public IReadOnlyList<SearchEffectMode>? Modes {
        get => _modes;
        init => _modes = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    public bool Equals(SearchEffectPlan? other) => other is not null && Selector.Equals(other.Selector)
        && ObjectiveMetadataOnly == other.ObjectiveMetadataOnly && (Modes ?? []).SequenceEqual(other.Modes ?? []);

    public override int GetHashCode() {
        HashCode hash = new();
        hash.Add(Selector);
        hash.Add(ObjectiveMetadataOnly);
        foreach (SearchEffectMode mode in Modes ?? []) {
            hash.Add(mode);
        }

        return hash.ToHashCode();
    }

    public SearchEffectPlan RenameCategory(string previous, string replacement) => this with {
        Selector = Selector.RenameCategory(previous, replacement),
        Modes = Modes?.Select(mode => mode with { Selector = mode.Selector.RenameCategory(previous, replacement) }).ToArray()
    };

    public void Validate() {
        if (Modes is null) {
            return;
        }
        if (Modes.Count is < 1 or > 8 || Modes.Any(mode => mode is null || mode.Selector is null || mode.ActivationLimit is < 1 or > 60)
            || !Selector.Equals(CardSelector.AnyOfSelectors(Modes.Select(mode => mode.Selector)))) {
            throw new ArgumentException("Search modes require bounded allowances and their exact target union.");
        }
    }

    public bool Matches(Card card) => Matches(card, Selector);

    public bool Matches(Card card, CardSelector selector) {
        if (!card.Active || card.Copies <= 0) {
            return false;
        }
        if (!ObjectiveMetadataOnly) {
            return selector.Matches(card);
        }

        return card.ExternalCardId is > 0 && selector.Matches(card.Categories
            .Where(category => category.Source == CategorySource.Metadata
                && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)).ToArray(), card);
    }
}
