using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Intrinsic guidance, independent of active copies, modes, routes and deck capacity.
// This is a sufficient reason to need detailed hand resolution, not a runtime estimate.
public static class DrawEffectPerformance {
    public static bool RequiresDetailedHandState(DrawEffectPlan plan, EffectExecutionModel executionModel) {
        if (executionModel != EffectExecutionModel.DrawSequenceV1) {
            return false;
        }

        if (plan.Operation == DrawOperation.DangerReveal) {
            return true;
        }

        if (plan.DrawCount is null) {
            return plan.RequiresSearchResolution;
        }

        // OPT can make activation restrictions redundant in a single-entry workspace.
        // Multiple entries and deck capacity remain workspace assessment facts.
        if (plan.HandCost > 0 || (plan.MinimumHandSize > 0 && plan.MinimumHandSize > plan.DrawCount)
            || (plan.HasActivationRestrictions && plan.MaximumActivations > 1)) {
            return true;
        }

        // PostDrawCalculation can specialize an unfiltered, non-OPT, hand-neutral
        // effect. Relevance, homogeneous filler and capacity remain workspace facts;
        // failure of those proofs must not turn this intrinsic hint into a card flag.
        bool exchangeableShape = plan.MaximumActivations == int.MaxValue && plan.DrawCount == plan.RemovalCount + 1
            && plan.RemovalSelector.Kind == CardSelectorKind.Any;

        return plan.RemovalCount > 0 && !exchangeableShape;
    }
}
