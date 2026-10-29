using System.Globalization;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

// Maintained definitions share the canonical resource convention. Their plans,
// revisions and support claims remain explicit; saved profiles never consult it.
public abstract class CardEffectDefinition : ICardEffectDefinition {
    public abstract int CanonicalCardId { get; }
    public abstract string Name { get; }
    public abstract int Revision { get; }
    public abstract EffectSupport Support { get; }
    public abstract DrawEffectPlan Compile();
    public virtual IReadOnlyList<int> Aliases => [];
    public virtual IReadOnlyList<CategoryBase> FilterProperties => [];
    public string Summary => ResourceText("Summary");
    public virtual string Limitations => "";
    public virtual EffectExecutionModel ExecutionModel => EffectExecutionModel.DrawSequenceV1;

    protected string ResourceText(string field) =>
        UiText.English($"Effects.{CanonicalCardId.ToString(CultureInfo.InvariantCulture)}.{field}");
}
