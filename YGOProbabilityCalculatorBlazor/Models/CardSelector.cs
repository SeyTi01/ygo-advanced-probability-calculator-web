using System.Globalization;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazor.Models;

public enum CardSelectorKind { Any, Category, AnyOf, AllOf, LevelAtLeast, Objective, Not }

// Bounded, immutable Boolean predicates over one physical card.
// Counts and distinct-copy roles belong to combo requirements, not selectors.
[JsonConverter(typeof(CardSelectorConverter))]
public sealed class CardSelector : IEquatable<CardSelector> {
    public const int MaximumDepth = 8;
    public const int MaximumNodes = 4096;
    public const int MaximumCategoryReferences = 2048;
    public static CardSelector Any { get; } = new(CardSelectorKind.Any, Array.Empty<string>());

    public CardSelectorKind Kind { get; }
    public IReadOnlyList<string> CategoryIdentities { get; }
    public string Signature { get; }
    public IReadOnlyList<CardSelector> Children { get; } = [];
    public int? MinimumLevel { get; }
    public int Depth { get; } = 1;
    public int NodeCount { get; } = 1;
    public int CategoryReferenceCount { get; }
    public bool HasLevelPredicate { get; }
    public bool IsGrouped => HasLevelPredicate || Kind is CardSelectorKind.Objective or CardSelectorKind.Not
        || Kind == CardSelectorKind.AllOf && Children.Count > 0 || Children.Any(child => child.Children.Count > 0);
    public int EvaluationSize => IsGrouped ? NodeCount + CategoryReferenceCount : CategoryIdentities.Count;

    public CardSelector(CardSelectorKind kind, IEnumerable<string> categoryIdentities) {
        ArgumentNullException.ThrowIfNull(categoryIdentities);
        if (!Enum.IsDefined(kind) || kind is CardSelectorKind.LevelAtLeast or CardSelectorKind.Objective or CardSelectorKind.Not) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        string[] identities = categoryIdentities.ToArray();
        if (identities.Length > MaximumCategoryReferences) {
            throw new ArgumentException("A selector supports at most 2048 category references.", nameof(categoryIdentities));
        }
        if (identities.Any(identity => !ValidIdentity(identity))) {
            throw new ArgumentException("Selectors require stable category identities.", nameof(categoryIdentities));
        }
        if ((kind == CardSelectorKind.Any && identities.Length != 0)
            || (kind == CardSelectorKind.Category && identities.Length != 1)
            || (kind is CardSelectorKind.AnyOf or CardSelectorKind.AllOf && identities.Length == 0)) {
            throw new ArgumentException("Any needs no categories, Category needs one, and AnyOf/AllOf need at least one.", nameof(categoryIdentities));
        }

        identities = identities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Kind = identities.Length == 1 ? CardSelectorKind.Category : kind;
        CategoryIdentities = Array.AsReadOnly(identities);
        CategoryReferenceCount = identities.Length;
        Signature = Kind + ":" + string.Concat(identities.Select(identity =>
            identity.Length.ToString(CultureInfo.InvariantCulture) + ":" + identity));
    }

    public static CardSelector For(CategoryBase category) => new(CardSelectorKind.Category, [category.Identity]);

    public static CardSelector AnyOf(IEnumerable<string> identities) => new(CardSelectorKind.AnyOf, identities);

    public static CardSelector AllOf(IEnumerable<string> identities) => new(CardSelectorKind.AllOf, identities);

    public static CardSelector AnyOfSelectors(IEnumerable<CardSelector> selectors) => Combine(CardSelectorKind.AnyOf, selectors);

    public static CardSelector AllOfSelectors(IEnumerable<CardSelector> selectors) => Combine(CardSelectorKind.AllOf, selectors);

    public static CardSelector Objective(CardSelector selector) => new(CardSelectorKind.Objective, new[] { selector ?? throw new ArgumentNullException(nameof(selector)) });

    public static CardSelector Not(CardSelector selector) => new(CardSelectorKind.Not, new[] { selector ?? throw new ArgumentNullException(nameof(selector)) });

    // Names here are verified catalog identities, including permanent name clauses.
    // They never inspect a workspace entry's mutable display name.
    public static CardSelector CatalogIdentity(int canonicalId) => Objective(new(CardSelectorKind.Category,
        ["metadata:catalog-id:" + canonicalId.ToString(CultureInfo.InvariantCulture)]));

    public static CardSelector CatalogName(string name) => Objective(new(CardSelectorKind.Category,
        ["metadata:card-name:" + Uri.EscapeDataString(name.ToLowerInvariant())]));

    public static CardSelector Mentions(string name) => Objective(new(CardSelectorKind.Category,
        ["metadata:mentions:" + Uri.EscapeDataString(name.ToLowerInvariant())]));

    public static CardSelector LevelAtLeast(int minimum) => new(minimum);

    private CardSelector(int minimum) {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        Kind = CardSelectorKind.LevelAtLeast;
        MinimumLevel = minimum;
        HasLevelPredicate = true;
        CategoryIdentities = Array.Empty<string>();
        Signature = "LevelAtLeast:" + minimum.ToString(CultureInfo.InvariantCulture);
    }

    private static CardSelector Combine(CardSelectorKind operation, IEnumerable<CardSelector> selectors) {
        ArgumentNullException.ThrowIfNull(selectors);
        CardSelector[] input = selectors.ToArray();
        if (input.Length == 0 || input.Any(selector => selector is null || selector.Kind == CardSelectorKind.Any)) {
            throw new ArgumentException("A Boolean filter requires nonempty clauses other than Any.", nameof(selectors));
        }
        if (input.Length >= MaximumNodes || input.Sum(selector => selector.NodeCount) >= MaximumNodes
            || input.Sum(selector => selector.CategoryReferenceCount) > MaximumCategoryReferences) {
            throw new ArgumentException("Selector exceeds its node or category-reference limit.", nameof(selectors));
        }

        CardSelector[] children = input.SelectMany(selector => selector.Kind == operation ? selector.Terms : [selector])
            .Distinct().OrderBy(selector => selector.Signature, StringComparer.Ordinal).ToArray();
        if (children.Length == 1) {
            return children[0];
        }
        if (children.All(selector => selector.Kind == CardSelectorKind.Category)) {
            return new(operation, children.SelectMany(selector => selector.CategoryIdentities));
        }
        return new CardSelector(operation, children);
    }

    private CardSelector(CardSelectorKind operation, CardSelector[] children) {
        Depth = 1 + children.Max(child => child.Depth);
        NodeCount = 1 + children.Sum(child => child.NodeCount);
        CategoryReferenceCount = children.Sum(child => child.CategoryReferenceCount);
        if (Depth > MaximumDepth || NodeCount > MaximumNodes || CategoryReferenceCount > MaximumCategoryReferences) {
            throw new ArgumentException("Selector exceeds its depth, node or category-reference limit.", nameof(children));
        }
        Kind = operation;
        Children = Array.AsReadOnly(children);
        HasLevelPredicate = children.Any(child => child.HasLevelPredicate);
        CategoryIdentities = Array.AsReadOnly(children.SelectMany(child => child.CategoryIdentities)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Signature = operation + "Children:" + string.Concat(children.Select(child =>
            child.Signature.Length.ToString(CultureInfo.InvariantCulture) + ":" + child.Signature));
    }

    // Direct operands, never a distributive expansion. Legacy category arrays are
    // equivalent operands and keep their original wire shape and signatures.
    public IEnumerable<CardSelector> Terms => Children.Count > 0 ? Children
        : Kind is CardSelectorKind.AnyOf or CardSelectorKind.AllOf
            ? CategoryIdentities.Select(identity => new CardSelector(CardSelectorKind.Category, [identity]))
            : [this];

    public IEnumerable<CardSelector> Alternatives {
        get {
            if (Kind != CardSelectorKind.AnyOf) {
                return [this];
            }
            return Children.Count > 0 ? Children
                : CategoryIdentities.Select(identity => new CardSelector(CardSelectorKind.Category, [identity]));
        }
    }

    public bool Matches(Card card) => Matches(card.Categories, card);

    public bool Matches(IReadOnlyCollection<CategoryBase> categories) => Matches(categories, null);

    internal bool Matches(IReadOnlyCollection<CategoryBase> categories, Card? card) => Kind switch {
        CardSelectorKind.Any => true,
        CardSelectorKind.Objective => card?.ExternalCardId is > 0 && Children[0].Matches(categories
            .Where(category => category.Source == CategorySource.Metadata
                && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)).ToArray(), card),
        CardSelectorKind.Not => !Children[0].Matches(categories, card),
        CardSelectorKind.LevelAtLeast => MatchesLevel(categories, card),
        CardSelectorKind.AllOf when Children.Count > 0 => Children.All(child => child.Matches(categories, card)),
        CardSelectorKind.AllOf => CategoryIdentities.All(identity => categories.Any(category => category.Identity == identity)),
        CardSelectorKind.AnyOf when Children.Count > 0 => Children.Any(child => child.Matches(categories, card)),
        _ => CategoryIdentities.Any(identity => categories.Any(category => category.Identity == identity))
    };

    private bool MatchesLevel(IReadOnlyCollection<CategoryBase> categories, Card? card) {
        if (card is not null && card.ExternalCardId is not > 0) {
            return false;
        }
        bool Objective(CategoryBase category) => category.Source == CategorySource.Metadata
            && (card is null || !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!));
        bool monster = false;
        int? level = null;
        foreach (CategoryBase category in categories) {
            if (!Objective(category)) {
                continue;
            }
            string key = category.MetadataKey!;
            if (key == "kind:monster") {
                monster = true;
            }
            if (key is "monster-type:xyz" or "monster-type:link" || key.StartsWith("rank:", StringComparison.Ordinal)
                || key.StartsWith("link:", StringComparison.Ordinal)) {
                return false;
            }
            if (key.StartsWith("level:", StringComparison.Ordinal)) {
                if (level is not null || !int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int value)) {
                    return false;
                }
                level = value;
            }
        }
        return monster && level >= MinimumLevel;
    }

    public CardSelector RenameCategory(string previousIdentity, string newIdentity) => Children.Count > 0
        ? Kind is CardSelectorKind.Objective or CardSelectorKind.Not
            ? new(Kind, Children.Select(child => child.RenameCategory(previousIdentity, newIdentity)).ToArray())
            : Combine(Kind, Children.Select(child => child.RenameCategory(previousIdentity, newIdentity)))
        : Kind == CardSelectorKind.LevelAtLeast ? this
            : new(Kind, CategoryIdentities.Select(identity => identity == previousIdentity ? newIdentity : identity));

    public bool Equals(CardSelector? other) => other is not null && Signature == other.Signature;

    public override bool Equals(object? other) => other is CardSelector selector && Equals(selector);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Signature);

    private static bool ValidIdentity(string? identity) {
        if (identity is null) {
            return false;
        }
        int prefix = identity switch {
            _ when identity.StartsWith("user:", StringComparison.Ordinal) => 5,
            _ when identity.StartsWith("metadata:", StringComparison.Ordinal) => 9,
            _ => 0
        };
        return prefix > 0 && !string.IsNullOrWhiteSpace(identity[prefix..]);
    }
}

public enum PostDrawFailureBehavior { FailBranch }
