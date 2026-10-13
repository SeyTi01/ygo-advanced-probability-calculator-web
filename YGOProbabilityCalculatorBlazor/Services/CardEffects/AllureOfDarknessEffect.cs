using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class AllureOfDarknessEffect : ICardEffectDefinition {
    public int CanonicalCardId => 1475311;
    public string Name => "Allure of Darkness";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        RemovalCount = 1, RemovalSelector = EffectProperties.Selector("attribute:dark")
    };
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 2, then remove a DARK card from hand; missing material fails the branch.";
    public string Limitations => "Uses deterministic removal and failed-branch semantics instead of sending the entire hand to the graveyard.";
}
