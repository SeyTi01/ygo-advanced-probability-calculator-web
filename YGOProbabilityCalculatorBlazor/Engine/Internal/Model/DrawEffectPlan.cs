namespace CardProbability.Internal.Model;

internal readonly record struct DrawEffectPlan(
    int? DrawCount,
    bool OncePerTurn,
    int HandCost,
    CardSelector HandCostSelector,
    int HiddenDeckCost,
    int RemovalCount,
    CardSelector RemovalSelector,
    PostDrawFailureBehavior FailureBehavior = PostDrawFailureBehavior.FailBranch,
    int HiddenPostDrawMill = 0,
    bool FirstActivationOnly = false,
    bool BlocksLaterDraws = false,
    SearchEffectPlan? Search = null,
    int? ActivationLimit = null,
    int MinimumHandSize = 0,
    ExcavationEffectPlan? Excavation = null
) {
    public static DrawEffectPlan Draw(int count, bool oncePerTurn) => new(count, oncePerTurn, 0, CardSelector.Any, 0, 0, CardSelector.Any);

    public static DrawEffectPlan SearchCards(SearchEffectPlan search, bool oncePerTurn) =>
        new(null, oncePerTurn, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: search);

    public static DrawEffectPlan Excavate(int count, bool requiresNoPriorEffectDraw = false) =>
        new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any, Excavation: new(count, requiresNoPriorEffectDraw));

    public int MaximumActivations => ActivationLimit ?? (OncePerTurn ? 1 : int.MaxValue);

    public bool HasActivationRestrictions => FirstActivationOnly || BlocksLaterDraws || MinimumHandSize > 0;

    // These rules inspect the changing search hand or per-mode allowances;
    // they cannot use the simple optional-substitution projection.
    public bool RequiresSearchResolution => Search is not null
        && (RequiresHandResolution || Search.Modes is { Count: > 0 } || MinimumHandSize > 0);

    public bool RequiresHandResolution => HandCost > 0 || RemovalCount > 0;

    // Mode selectors remain authored inputs even when their derived union is Any.
    public IEnumerable<CardSelector> EnumerateSelectors() {
        yield return HandCostSelector;
        yield return RemovalSelector;
        if (Search is { } search) {
            foreach (CardSelector selector in search.EnumerateSelectors()) {
                yield return selector;
            }
        }
    }

    public IEnumerable<string> EnumerateCategoryIdentities() => EnumerateSelectors()
        .SelectMany(selector => selector.CategoryIdentities)
        .Distinct(StringComparer.Ordinal);

}
