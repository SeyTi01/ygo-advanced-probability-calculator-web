using System.Globalization;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazor.Models;

public enum CardSelectorKind { Any, Category, AnyOf, AllOf }

// Bounded disjunction of atomic categories or same-card conjunctions.
// Counts and distinct-copy roles belong to combo requirements, not selectors.
[JsonConverter(typeof(CardSelectorConverter))]
public sealed class CardSelector : IEquatable<CardSelector> {
    public static CardSelector Any { get; } = new(CardSelectorKind.Any, []);

    public CardSelectorKind Kind { get; }
    public IReadOnlyList<string> CategoryIdentities { get; }
    public string Signature { get; }
    public IReadOnlyList<CardSelector> Children { get; } = [];

    public CardSelector(CardSelectorKind kind, IEnumerable<string> categoryIdentities) {
        ArgumentNullException.ThrowIfNull(categoryIdentities);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        string[] identities = categoryIdentities.ToArray();
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
        Signature = Kind + ":" + string.Concat(identities.Select(identity =>
            identity.Length.ToString(CultureInfo.InvariantCulture) + ":" + identity));
    }

    public static CardSelector For(CategoryBase category) => new(CardSelectorKind.Category, [category.Identity]);

    public static CardSelector AnyOf(IEnumerable<string> identities) => new(CardSelectorKind.AnyOf, identities);

    public static CardSelector AllOf(IEnumerable<string> identities) => new(CardSelectorKind.AllOf, identities);

    public static CardSelector AnyOfSelectors(IEnumerable<CardSelector> selectors) {
        ArgumentNullException.ThrowIfNull(selectors);
        CardSelector[] input = selectors.ToArray();
        if (input.Length == 0 || input.Any(selector => selector is null || selector.Kind == CardSelectorKind.Any)) {
            throw new ArgumentException("An OR filter requires nonempty category alternatives.", nameof(selectors));
        }

        CardSelector[] children = input.SelectMany(selector => selector.Alternatives)
            .Distinct().OrderBy(selector => selector.Signature, StringComparer.Ordinal).ToArray();
        if (children.Length == 1) {
            return children[0];
        }
        if (children.All(selector => selector.Kind == CardSelectorKind.Category)) {
            return AnyOf(children.SelectMany(selector => selector.CategoryIdentities));
        }
        return new CardSelector(children);
    }

    private CardSelector(CardSelector[] children) {
        Kind = CardSelectorKind.AnyOf;
        Children = Array.AsReadOnly(children);
        CategoryIdentities = Array.AsReadOnly(children.SelectMany(child => child.CategoryIdentities)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Signature = "AnyOfChildren:" + string.Concat(children.Select(child =>
            child.Signature.Length.ToString(CultureInfo.InvariantCulture) + ":" + child.Signature));
    }

    public IEnumerable<CardSelector> Alternatives {
        get {
            if (Kind != CardSelectorKind.AnyOf) {
                return [this];
            }
            return Children.Count > 0 ? Children
                : CategoryIdentities.Select(identity => new CardSelector(CardSelectorKind.Category, [identity]));
        }
    }

    public bool Matches(IReadOnlyCollection<CategoryBase> categories) => Kind switch {
        CardSelectorKind.Any => true,
        CardSelectorKind.AllOf => CategoryIdentities.All(identity => categories.Any(category => category.Identity == identity)),
        CardSelectorKind.AnyOf when Children.Count > 0 => Children.Any(child => child.Matches(categories)),
        _ => CategoryIdentities.Any(identity => categories.Any(category => category.Identity == identity))
    };

    public CardSelector RenameCategory(string previousIdentity, string newIdentity) => Children.Count > 0
        ? AnyOfSelectors(Children.Select(child => child.RenameCategory(previousIdentity, newIdentity)))
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
