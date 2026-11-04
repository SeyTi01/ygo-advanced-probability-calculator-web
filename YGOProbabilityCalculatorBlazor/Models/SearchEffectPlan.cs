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

    public IEnumerable<CardSelector> EnumerateSelectors() {
        yield return Selector;
        foreach (SearchEffectMode mode in Modes ?? []) {
            yield return mode.Selector;
        }
    }

    public SearchEffectPlan TransformSelectors(Func<CardSelector, CardSelector> transform) {
        if (Modes is null) {
            return this with { Selector = transform(Selector) };
        }

        SearchEffectMode[] modes = Modes.Select(mode => mode with { Selector = transform(mode.Selector) }).ToArray();

        return this with { Selector = ModeTargetUnion(modes), Modes = modes };
    }

    public SearchEffectPlan RenameCategory(string previous, string replacement) =>
        TransformSelectors(selector => selector.RenameCategory(previous, replacement));

    public static CardSelector ModeTargetUnion(IReadOnlyList<SearchEffectMode> modes) =>
        modes.Any(mode => mode.Selector.Kind == CardSelectorKind.Any)
            ? CardSelector.Any
            : CardSelector.AnyOfSelectors(modes.Select(mode => mode.Selector));

    public void Validate() {
        if (Modes is null) {
            return;
        }
        if (Modes.Count is < 1 or > 8 || Modes.Any(mode => mode is null || mode.Selector is null || mode.ActivationLimit is < 1 or > 60)
            || !Selector.Equals(ModeTargetUnion(Modes))) {
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
