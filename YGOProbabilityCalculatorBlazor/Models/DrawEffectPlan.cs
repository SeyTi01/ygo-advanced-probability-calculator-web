namespace YGOProbabilityCalculatorBlazor.Models;

// Versioned, bounded instruction set. V1 reserves the source, checks the complete
// batch, pays hand/hidden-deck costs, draws FIFO, then performs mandatory removal.
// New execution semantics require an explicit compiler/interpreter version; a
// definition cannot smuggle callbacks into count-state equivalence or worker JSON.
public enum EffectExecutionModel { DrawSequenceV1 = 1, Unsupported = 2 }
[Flags]
public enum EffectCapabilities { None = 0, Draw = 1, OncePerTurn = 2, HandCost = 4, HiddenDeckCost = 8, MandatoryRemoval = 16 }
public enum EffectSource { LegacySnapshot, BuiltIn, Custom }
public enum EffectSupport { ModeledWithinRules, Partial, Unmodeled }

public sealed record EffectProfile(
    EffectSource Source = EffectSource.LegacySnapshot,
    int? CanonicalCardId = null,
    int? Revision = null,
    string? Summary = null,
    EffectSupport Support = EffectSupport.Unmodeled,
    string? Limitations = null,
    EffectExecutionModel ExecutionModel = EffectExecutionModel.DrawSequenceV1
) {
    public static EffectProfile Legacy { get; } = new();

    public EffectProfile AsCustom() => new(EffectSource.Custom, CanonicalCardId, ExecutionModel: ExecutionModel);
}

public readonly record struct DrawEffectPlan(
    int? DrawCount,
    bool OncePerTurn,
    int HandCost,
    CardSelector HandCostSelector,
    int HiddenDeckCost,
    int RemovalCount,
    CardSelector RemovalSelector,
    PostDrawFailureBehavior FailureBehavior = PostDrawFailureBehavior.FailBranch
) {
    public static DrawEffectPlan Draw(int count, bool oncePerTurn) => new(count, oncePerTurn, 0, CardSelector.Any, 0, 0, CardSelector.Any);

    public EffectCapabilities Capabilities => (DrawCount is null ? EffectCapabilities.None : EffectCapabilities.Draw)
        | (DrawCount is not null && OncePerTurn ? EffectCapabilities.OncePerTurn : EffectCapabilities.None)
        | (HandCost > 0 ? EffectCapabilities.HandCost : EffectCapabilities.None)
        | (HiddenDeckCost > 0 ? EffectCapabilities.HiddenDeckCost : EffectCapabilities.None)
        | (RemovalCount > 0 ? EffectCapabilities.MandatoryRemoval : EffectCapabilities.None);

    public bool RequiresHandResolution => HandCost > 0 || RemovalCount > 0;

    public string Describe() {
        if (DrawCount is null) {
            return "No draw effect";
        }

        string summary = $"Draw {DrawCount}";
        if (HandCost > 0) {
            summary = $"Remove {HandCost} eligible card(s) from hand; " + summary;
        }
        if (HiddenDeckCost > 0) {
            summary = $"Banish {HiddenDeckCost} unseen deck cards; " + summary;
        }
        if (RemovalCount > 0) {
            summary += $"; remove {RemovalCount} eligible card(s) from hand";
        }

        return summary + (OncePerTurn ? "; once per turn" : "");
    }
}
