using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace YGOProbabilityCalculatorBlazor.Models;

public enum NumericPropertyKind { Level, Rank, Link }

public sealed record NumericPropertyRange {
    public NumericPropertyKind Property { get; }
    public int? Minimum { get; }
    public int? Maximum { get; }
    public bool ObjectiveOnly { get; }
    public string Key => Property.ToString().ToLowerInvariant();
    public string Identity => $"metadata:numeric:{Key}:{Minimum?.ToString(CultureInfo.InvariantCulture) ?? "*"}:{Maximum?.ToString(CultureInfo.InvariantCulture) ?? "*"}:{(ObjectiveOnly ? "objective" : "any")}";
    public string Description => Minimum == Maximum ? $"{Property} {Minimum}"
        : Maximum is null ? $"{Property} >= {Minimum}" : Minimum is null ? $"{Property} <= {Maximum}" : $"{Property} {Minimum}–{Maximum}";

    public NumericPropertyRange(NumericPropertyKind property, int? minimum, int? maximum, bool objectiveOnly = false) {
        if (!Enum.IsDefined(property) || minimum is < 0 || maximum is < 0 || minimum is null && maximum is null || minimum > maximum) {
            throw new ArgumentException("Choose a non-negative number or an ordered inclusive range.");
        }

        Property = property;
        Minimum = minimum;
        Maximum = maximum;
        ObjectiveOnly = objectiveOnly;
    }

    // Reserved serialized requirement definitions are not assignable card metadata.
    public CategoryBase ToCategory() => new(Description, CategorySource.Metadata, Identity[9..]);
    public CardSelector ToSelector() => Property == NumericPropertyKind.Level && ObjectiveOnly && Maximum is null
        ? CardSelector.LevelAtLeast(Minimum!.Value) : CardSelector.ForNumericRange(this);
    public static CardSelector SelectorFor(CategoryBase category) => category.MetadataKey?.StartsWith("numeric:", StringComparison.Ordinal) == true
        && TryFromIdentity(category.Identity, out NumericPropertyRange? range) ? range.ToSelector() : CardSelector.For(category);

    public static bool TryFromSelector(CardSelector selector, [NotNullWhen(true)] out NumericPropertyRange? range) {
        range = selector.NumericRange;
        if (range is not null) { return true; }
        if (selector.Kind == CardSelectorKind.LevelAtLeast) {
            range = new(NumericPropertyKind.Level, selector.MinimumLevel, null, true);
            return true;
        }
        return selector.Kind == CardSelectorKind.Category && TryFromIdentity(selector.CategoryIdentities[0], out range);
    }

    public static bool TryFromIdentity(string identity, [NotNullWhen(true)] out NumericPropertyRange? range) {
        range = null;
        if (!identity.StartsWith("metadata:", StringComparison.Ordinal)) { return false; }
        string[] parts = identity[9..].Split(':');
        bool encoded = parts.Length == 5 && parts[0] == "numeric";
        if (!encoded && parts.Length != 2) { return false; }
        string property = parts[encoded ? 1 : 0];
        if (!Enum.TryParse(property, true, out NumericPropertyKind kind) || !Enum.IsDefined(kind)
            || !string.Equals(kind.ToString(), property, StringComparison.OrdinalIgnoreCase)) { return false; }
        if (!encoded) {
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) { return false; }
            range = new(kind, number, number);
            return true;
        }
        if (parts[4] is not ("objective" or "any")) { return false; }
        int? minimum = null;
        int? maximum = null;
        if (parts[2] != "*") {
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) { return false; }
            minimum = number;
        }
        if (parts[3] != "*") {
            if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) { return false; }
            maximum = number;
        }
        if (minimum is null && maximum is null || minimum > maximum) { return false; }
        range = new(kind, minimum, maximum, parts[4] == "objective");
        return true;
    }

    internal bool Matches(IReadOnlyCollection<CategoryBase> categories, Card? card) {
        if (ObjectiveOnly && card is not null && card.ExternalCardId is not > 0) { return false; }
        bool monster = false;
        int? number = null;
        foreach (CategoryBase category in categories) {
            if (category.Source != CategorySource.Metadata || ObjectiveOnly && card?.ManualMetadataCategoryKeys.Contains(category.MetadataKey!) == true) { continue; }
            string key = category.MetadataKey!;
            if (key == "kind:monster") { monster = true; }
            if (key.StartsWith("level:", StringComparison.Ordinal) || key.StartsWith("rank:", StringComparison.Ordinal) || key.StartsWith("link:", StringComparison.Ordinal)) {
                if (!key.StartsWith(Key + ":", StringComparison.Ordinal) || number is not null
                    || !int.TryParse(key.AsSpan(Key.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int value)) { return false; }
                number = value;
            }
            bool wrongType = Property switch {
                NumericPropertyKind.Level => key is "monster-type:xyz" or "monster-type:link",
                NumericPropertyKind.Rank => key == "monster-type:link",
                _ => key == "monster-type:xyz"
            };
            if (wrongType) { return false; }
        }
        return monster && number is not null && (Minimum is null || number >= Minimum) && (Maximum is null || number <= Maximum);
    }
}
