using System.Globalization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Localization;

public static class DisplayLabels {
    public static string Color(string name) => Optional("Color." + name, name);

    // Localized labels are presentation only; stored names are never rewritten.
    public static string Card(CardInfo info, string? localizedName = null) =>
        Optional($"Cards.{CardEffectId(info)}.Name", localizedName ?? info.Name);
    public static string Card(Card card, string? localizedName = null) => localizedName ?? card.Name ?? "";
    public static string Card(Card card, ICardInfoService service) => Card(card, service.GetLocalizedCardName(card));
    public static string Card(CardInfo card, ICardInfoService service) => Card(card, service.GetLocalizedCardName(card));

    public static string Category(CategoryBase category) {
        if (category.Source != CategorySource.Metadata) { return category.Name; }
        string fallback = NumericPropertyRange.TryFromIdentity(category.Identity, out NumericPropertyRange? range)
            ? NumericRange(range) : AutomaticCategoryName(category);
        return Optional("Metadata." + category.MetadataKey, fallback);
    }

    private static string AutomaticCategoryName(CategoryBase category) {
        const string archetypePrefix = "Archetype: ";
        if (category.MetadataKey!.StartsWith("archetype:", StringComparison.Ordinal)
            && category.Name.StartsWith(archetypePrefix, StringComparison.Ordinal)) {
            string canonicalName = category.Name[archetypePrefix.Length..];
            bool matchesCanonicalKey = canonicalName.Length > 0
                && string.Equals(canonicalName, canonicalName.Trim(), StringComparison.Ordinal)
                && string.Equals(category.MetadataKey, "archetype:" + Uri.EscapeDataString(canonicalName.ToLowerInvariant()), StringComparison.Ordinal);
            if (matchesCanonicalKey) {
                return UiText.Format("Property.Archetype", Archetype(canonicalName, canonicalName));
            }
        }

        (string Prefix, string Resource)? label = category.MetadataKey switch {
            string key when key.StartsWith("catalog-id:", StringComparison.Ordinal) => ("Card identity: ", "Property.Identity"),
            string key when key.StartsWith("card-name:", StringComparison.Ordinal) => ("Card name: ", "Property.Name"),
            string key when key.StartsWith("monster-race:", StringComparison.Ordinal) => ("Monster Type: ", "Property.Type"),
            string key when key.StartsWith("attribute:", StringComparison.Ordinal) => ("Attribute: ", "Property.Attribute"),
            string key when key.StartsWith("mentions:", StringComparison.Ordinal) => ("Mentions: ", "Property.Mentions"),
            _ => null
        };
        return label is { } presentation && category.Name.StartsWith(presentation.Prefix, StringComparison.Ordinal)
            ? UiText.Format(presentation.Resource, category.Name[presentation.Prefix.Length..]) : category.Name;
    }

    public static string? CategoryOrNull(CategoryBase? category) => category is null ? null : Category(category);

    public static string Requirement(ComboCategory category) => category.CategoryBases.Count == 1
        ? Category(category.BaseCategory)
        : "(" + string.Join(UiText.Get("Expression.AndSeparator"), category.CategoryBases.Select(Category)) + ")";

    public static string NumericProperty(NumericPropertyKind property) =>
        Optional("NumericProperty." + property, property.ToString());

    public static string NumericRange(NumericPropertyRange range) {
        string label = NumericProperty(range.Property);
        if (range.Minimum == range.Maximum) { return UiText.Format("NumericRange.Exact", label, range.Minimum); }
        if (range.Maximum is null) { return UiText.Format("NumericRange.Minimum", label, range.Minimum); }
        if (range.Minimum is null) { return UiText.Format("NumericRange.Maximum", label, range.Maximum); }
        return UiText.Format("NumericRange.Between", label, range.Minimum, range.Maximum);
    }

    public static string Archetype(string canonicalName, string english) =>
        Optional("Archetypes." + canonicalName, english);

    private static int CardEffectId(CardInfo info) =>
        info.CanonicalCardId is > 0 ? info.CanonicalCardId.Value : info.Id;

    private static string Optional(string key, string fallback) =>
        UiText.Resources.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
}
