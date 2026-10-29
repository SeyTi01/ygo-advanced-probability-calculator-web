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
            ? NumericRange(range) : category.Name;
        return Optional("Metadata." + category.MetadataKey, fallback);
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
