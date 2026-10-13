using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

internal static class EffectProperties {
    internal static CategoryBase Property(string name, string key) => new(name, CategorySource.Metadata, key);
    internal static CardSelector Selector(string key) => CardSelector.For(Property(key, key));

    internal static IReadOnlyList<CategoryBase> All { get; } = Array.AsReadOnly(new[] {
        Property("Attribute: DARK", "attribute:dark"), Property("Quick-Play Spell", "spell-type:quick-play"),
        Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon"), Property("Monster", "kind:monster"),
        Property("Level 8", "level:8"), Property("Archetype: Orcust", "archetype:orcust"),
        Property("Archetype: World Legacy", "archetype:world%20legacy")
    });
}
