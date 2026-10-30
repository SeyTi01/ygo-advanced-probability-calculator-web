using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

// The nine maintained cards share one operation; their separate discard triggers
// are deliberately absent. IDs are canonical passcodes from the checked-in catalog.
public sealed class DangerBaseEffect : CardEffectDefinition {
    public static IReadOnlyList<ICardEffectDefinition> Definitions { get; } = Array.AsReadOnly<ICardEffectDefinition>([
        new DangerBaseEffect(43316238, "Danger! Bigfoot!", EffectSupport.ModeledWithinRules),
        new DangerBaseEffect(90807199, "Danger! Thunderbird!", EffectSupport.ModeledWithinRules),
        new DangerBaseEffect(26302107, "Danger! Dogman!", EffectSupport.ModeledWithinRules),
        new DangerBaseEffect(99745551, "Danger!? Tsuchinoko?", EffectSupport.ModeledWithinRules),
        new DangerBaseEffect(16209941, "Danger! Chupacabra!", EffectSupport.ModeledWithinRules),
        new DangerBaseEffect(52350806, "Danger! Mothman!", EffectSupport.Partial),
        new DangerBaseEffect(70711847, "Danger! Nessie!", EffectSupport.Partial),
        new DangerBaseEffect(43694650, "Danger!? Jackalope?", EffectSupport.Partial),
        new DangerBaseEffect(83518674, "Danger! Ogopogo!", EffectSupport.Partial)
    ]);

    private DangerBaseEffect(int canonicalCardId, string name, EffectSupport support) {
        CanonicalCardId = canonicalCardId;
        Name = name;
        Support = support;
    }

    public override int CanonicalCardId { get; }
    public override string Name { get; }
    public override int Revision => 1;
    public override EffectSupport Support { get; }
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.DangerReveal();
}
