using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

internal static class EffectProperties {
    internal static CategoryBase Property(string name, string key) => new(name, CategorySource.Metadata, key);
}
