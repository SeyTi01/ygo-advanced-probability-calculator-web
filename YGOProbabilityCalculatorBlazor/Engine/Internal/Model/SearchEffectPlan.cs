namespace CardProbability.Internal.Model;

// A search transfers exactly one remaining deck copy to hand and consumes
// its source. Activation limits belong to the enclosing effect plan.
internal sealed record SearchEffectMode(CardSelector Selector, int? ActivationLimit = null);

internal sealed record SearchEffectPlan(CardSelector Selector, IReadOnlyList<SearchEffectMode>? Modes = null) {
    private IReadOnlyList<SearchEffectMode>? _modes = Modes is null ? null : Array.AsReadOnly(Modes.ToArray());
    public IReadOnlyList<SearchEffectMode>? Modes {
        get => _modes;
        init => _modes = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    public bool Equals(SearchEffectPlan? other) => other is not null && Selector.Equals(other.Selector)
        && (Modes ?? []).SequenceEqual(other.Modes ?? []);

    public override int GetHashCode() {
        HashCode hash = new();
        hash.Add(Selector);
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

    public static CardSelector ModeTargetUnion(IReadOnlyList<SearchEffectMode> modes) =>
        modes.Any(mode => mode.Selector.Kind == CardSelectorKind.Any)
            ? CardSelector.Any
            : CardSelector.AnyOfSelectors(modes.Select(mode => mode.Selector));

    public void Validate() {
        if (Modes is null) {
            return;
        }
        if (Modes.Count < 1 || Modes.Any(mode => mode is null || mode.Selector is null || mode.ActivationLimit is < 1)
            || !Selector.Equals(ModeTargetUnion(Modes))) {
            throw new ArgumentException("Search modes require bounded allowances and their exact target union.");
        }
    }

    public bool Matches(Card card) => Matches(card, Selector);
    public bool Matches(Card card, CardSelector selector) => card.Active && card.Copies > 0 && selector.Matches(card);
}
