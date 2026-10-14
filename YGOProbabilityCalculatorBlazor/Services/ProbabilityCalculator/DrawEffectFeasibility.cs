using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal static class DrawEffectFeasibility {
    // Only exclude activations when no surviving effect selects hand material.
    // ReduceDeck and RemoveCard distinguish effects from ordinary cards, including
    // globally unpayable, cost-deferred sources. Keep the original ordered model
    // in those cases rather than changing its reduction or deterministic tiers.
    internal static HashSet<Card>? FindInert(
        IReadOnlyList<Card> deck,
        int totalCopies,
        int handSize,
        WorkBudget? budget = null
    ) {
        if (handSize < 0 || handSize > totalCopies) {
            return null;
        }

        HashSet<Card>? inert = null;
        foreach (Card source in deck) {
            budget?.Spend(1);
            // Calculator callers normally supply an already active deck. If a
            // direct caller has not filtered it, retain that caller's old model.
            if (!source.Active && source.Copies > 0) {
                return null;
            }
            if (!source.Active || source.Copies <= 0 || source.DrawCount is not int drawCount) {
                continue;
            }
            // Never hide unsupported snapshots behind a feasibility proof.
            if (source.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1) {
                return null;
            }

            int consumption = drawCount + (source.PreDrawDeckRemovalCount + source.PostDrawDeckMillCount);
            bool impossible = consumption > totalCopies - handSize;
            if (!impossible && source.PreDrawRemovalCount > 0) {
                // Count the entire physical deck, reserving exactly ONE source copy.
                // Stop as soon as sufficient material exists; an opening-hand cost
                // failure alone cannot prove that a later draw will not supply it.
                long selectorCells = source.PreDrawSelector.CategoryIdentities.Count
                    + source.PreDrawSelector.Children.Sum(child => (long)child.CategoryIdentities.Count);
                budget?.Spend(1 + selectorCells * (source.Categories.Count + 1L));
                int needed = source.PreDrawRemovalCount + (source.PreDrawSelector.Matches(source.Categories) ? 1 : 0);
                long available = 0;
                foreach (Card material in deck) {
                    if (!material.Active || material.Copies <= 0) {
                        continue;
                    }
                    budget?.Spend(1 + selectorCells * (material.Categories.Count + 1L));
                    if (source.PreDrawSelector.Matches(material.Categories)) {
                        available += material.Copies;
                        if (available >= needed) {
                            break;
                        }
                    }
                }
                impossible = available < needed;
            }

            if (impossible) {
                inert ??= [];
                inert.Add(source);
                budget?.CheckStorage(inert.Count, inert.Count);
            }
            else if (source.EffectPlan.RequiresHandResolution) {
                return null;
            }
        }

        return inert;
    }
}
