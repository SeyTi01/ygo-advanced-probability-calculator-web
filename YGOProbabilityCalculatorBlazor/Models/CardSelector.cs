using System.Globalization;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazor.Models;

public enum CardSelectorKind { Any, Category, AnyOf }

// Flat atomic alternatives deliberately leave room for another operator without
// coupling card eligibility to combo counts or distinct-copy requirements.
[JsonConverter(typeof(CardSelectorConverter))]
public sealed class CardSelector : IEquatable<CardSelector> {
    public static CardSelector Any { get; } = new(CardSelectorKind.Any, []);

    public CardSelectorKind Kind { get; }
    public IReadOnlyList<string> CategoryIdentities { get; }
    public string Signature { get; }

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
            || (kind == CardSelectorKind.AnyOf && identities.Length == 0)) {
            throw new ArgumentException("Any needs no categories, Category needs one, and AnyOf needs at least one.", nameof(categoryIdentities));
        }

        identities = identities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Kind = identities.Length == 1 ? CardSelectorKind.Category : kind;
        CategoryIdentities = Array.AsReadOnly(identities);
        Signature = Kind + ":" + string.Concat(identities.Select(identity =>
            identity.Length.ToString(CultureInfo.InvariantCulture) + ":" + identity));
    }

    public static CardSelector For(CategoryBase category) => new(CardSelectorKind.Category, [category.Identity]);

    public static CardSelector AnyOf(IEnumerable<string> identities) => new(CardSelectorKind.AnyOf, identities);

    public CardSelector RenameCategory(string previousIdentity, string newIdentity) => Kind == CardSelectorKind.Any
        ? this : AnyOf(CategoryIdentities.Select(identity => identity == previousIdentity ? newIdentity : identity));

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
