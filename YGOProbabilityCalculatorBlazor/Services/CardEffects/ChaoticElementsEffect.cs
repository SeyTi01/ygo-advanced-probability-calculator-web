using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChaoticElementsEffect : ICardEffectDefinition {
    public int CanonicalCardId => 92221402;
    public string Name => "Chaotic Elements";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Search 1 Level 5+ LIGHT/DARK Pyro/Aqua monster";
    public string Limitations => "Main Deck search only; GY retrieval, optional field destruction and the GY effect are not modeled.";

    // Cover printed Levels 5–13 using existing equality properties. Expand the two OR
    // groups conjunctively into 36 branches (144 atoms), within the existing
    // selector wire limit. Rank never supplies a Level property.
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AnyOfSelectors(
            from level in Enumerable.Range(5, 9)
            from attribute in new[] { "light", "dark" }
            from race in new[] { "pyro", "aqua" }
            select CardSelector.AllOf([
                "metadata:kind:monster", $"metadata:level:{level}",
                $"metadata:attribute:{attribute}", $"metadata:monster-race:{race}"
            ]))));
}
