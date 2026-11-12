namespace CardProbability.Internal.Model;

internal sealed record CategoryBase(string Identity);

internal enum CardSelectorKind { Any, Category, AnyOf }

internal sealed class CardSelector : IEquatable<CardSelector> {
    public CardSelectorKind Kind { get; }
    public IReadOnlyList<string> CategoryIdentities { get; }
    public IReadOnlyList<CardSelector> Children { get; }
    public bool IsGrouped => Kind == CardSelectorKind.AnyOf;
    public int EvaluationSize { get; }
    public int NodeCount => EvaluationSize;
    public int CategoryReferenceCount => CategoryIdentities.Count;
    public string Signature { get; }

    private CardSelector(CardSelectorKind kind, IReadOnlyList<string> identities, IReadOnlyList<CardSelector> children) {
        Kind = kind;
        CategoryIdentities = identities;
        Children = children;
        EvaluationSize = 1 + children.Sum(child => child.EvaluationSize);
        Signature = kind switch {
            CardSelectorKind.Any => "*",
            CardSelectorKind.Category => $"c:{identities[0].Length}:{identities[0]}",
            _ => "o:" + string.Join("|", children.Select(child => child.Signature))
        };
    }

    public static CardSelector Any { get; } = new(CardSelectorKind.Any, [], []);
    public static CardSelector Category(string identity) => new(CardSelectorKind.Category, [identity], []);

    public static CardSelector AnyOfSelectors(IEnumerable<CardSelector> selectors) {
        CardSelector[] children = selectors.Distinct().OrderBy(selector => selector.Signature, StringComparer.Ordinal).ToArray();
        if (children.Any(selector => selector.Kind == CardSelectorKind.Any)) {
            return Any;
        }
        if (children.Length == 1) {
            return children[0];
        }

        string[] identities = children.SelectMany(selector => selector.CategoryIdentities).Distinct(StringComparer.Ordinal).ToArray();
        return new(CardSelectorKind.AnyOf, identities, children);
    }

    public bool Matches(Card card) {
        if (Kind == CardSelectorKind.Any) {
            return true;
        }
        if (Kind == CardSelectorKind.Category) {
            string identity = CategoryIdentities[0];
            foreach (CategoryBase category in card.Categories) {
                if (category.Identity == identity) {
                    return true;
                }
            }

            return false;
        }
        for (int index = 0; index < Children.Count; index++) {
            if (Children[index].Matches(card)) {
                return true;
            }
        }

        return false;
    }

    public bool Equals(CardSelector? other) => other is not null && Signature == other.Signature;
    public override bool Equals(object? other) => other is CardSelector selector && Equals(selector);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Signature);
}

internal sealed class Card {
    public string Id { get; }
    public int Copies { get; }
    public List<CategoryBase> Categories { get; }
    public DrawEffectPlan EffectPlan { get; }
    public string? PolicyOrderKey { get; }

    public Card(string id, int copies, IEnumerable<CategoryBase> categories, DrawEffectPlan effectPlan, string? policyOrderKey = null) {
        Id = id;
        Copies = copies;
        Categories = [.. categories];
        EffectPlan = effectPlan;
        PolicyOrderKey = policyOrderKey;
    }

    public Card WithCopies(int copies) => new(Id, copies, Categories, EffectPlan, PolicyOrderKey);
    public bool Active => true;
    public bool HasEffect => DrawCount is not null || Search is not null || Excavation is not null;
    public SearchEffectPlan? Search => EffectPlan.Search;
    public ExcavationEffectPlan? Excavation => EffectPlan.Excavation;
    public bool FirstActivationOnly => EffectPlan.FirstActivationOnly;
    public bool BlocksLaterDraws => EffectPlan.BlocksLaterDraws;
    public int? DrawCount => EffectPlan.DrawCount;
    public bool OncePerTurn => EffectPlan.OncePerTurn;
    public bool DrawOncePerTurn => OncePerTurn;
    public int MaximumActivations => EffectPlan.MaximumActivations;
    public int MinimumHandSize => EffectPlan.MinimumHandSize;
    public int PreDrawDeckRemovalCount => EffectPlan.HiddenDeckCost;
    public int PostDrawDeckMillCount => EffectPlan.HiddenPostDrawMill;
    public int PreDrawRemovalCount => EffectPlan.HandCost;
    public CardSelector PreDrawSelector => EffectPlan.HandCostSelector;
    public int PostDrawDiscardCount => EffectPlan.RemovalCount;
    public CardSelector PostDrawSelector => EffectPlan.RemovalSelector;
    public PostDrawFailureBehavior PostDrawFailureBehavior => EffectPlan.FailureBehavior;
}

internal sealed class Combo {
    public List<ComboCategory> Categories { get; }
    public List<ComboCard> Cards { get; }
    public List<ComboAlternativeGroup> AlternativeGroups { get; }
    public string? Name { get; }
    public bool Active { get; }
    public string? GroupId { get; }

    public Combo(
        IEnumerable<ComboCategory> categories,
        string? name = null,
        bool active = true,
        string? groupId = null,
        IEnumerable<ComboCard>? cards = null,
        IEnumerable<ComboAlternativeGroup>? alternativeGroups = null
    ) {
        Categories = [.. categories];
        Name = name;
        Active = active;
        GroupId = groupId;
        Cards = cards?.ToList() ?? [];
        AlternativeGroups = alternativeGroups?.ToList() ?? [];
    }

    public IEnumerable<ComboCategory> AllCategories => Categories.Concat(
        AlternativeGroups.SelectMany(group => group.Alternatives)
            .Where(alternative => alternative.Category is not null).Select(alternative => alternative.Category!)
    );

    public IEnumerable<ComboCard> AllCards => Cards.Concat(
        AlternativeGroups.SelectMany(group => group.Alternatives)
            .Where(alternative => alternative.Card is not null).Select(alternative => alternative.Card!)
    );

    public int RequirementCount => Categories.Count + Cards.Count + AlternativeGroups.Count;
}

internal sealed class ComboCategory {
    public CardSelector Selector { get; }
    public IReadOnlyList<CategoryBase> CategoryBases { get; }
    public int MinCount { get; }
    public int MaxCount { get; }
    public RequirementMaximumMode MaximumMode { get; }

    public ComboCategory(CardSelector selector, int minCount, int maxCount, RequirementMaximumMode maximumMode = RequirementMaximumMode.Fixed) {
        Selector = selector;
        CategoryBases = selector.CategoryIdentities.Select(identity => new CategoryBase(identity)).ToArray();
        MinCount = minCount;
        MaxCount = maxCount;
        MaximumMode = maximumMode;
    }

    public int GetEffectiveMaximum(int handSize) => MaximumMode == RequirementMaximumMode.HandSize ? handSize : MaxCount;
}

internal sealed record ComboGroup(string Id, string Name);
