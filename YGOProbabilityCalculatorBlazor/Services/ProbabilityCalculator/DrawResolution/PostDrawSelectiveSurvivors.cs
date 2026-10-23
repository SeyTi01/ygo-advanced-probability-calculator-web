using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed partial class PostDrawCalculation {
    private readonly struct SurvivorTerminal : ITerminalHand {
        public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) {
            int[] summary = new int[2 * layout.RowCount + 1];
            state.AsSpan(0, 2 * layout.RowCount).CopyTo(summary);
            summary[^1] = layout.HasHiddenCount ? state[layout.HiddenCount] : 0;
            return summary;
        }
    }

    private ExactCalculationResult CountSelectiveSurvivors(
        List<Card> cards,
        List<Card> deferred,
        OrderedResolver resolver,
        BigInteger[] factorial,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        SearchAccessPlan search
    ) {
        Route = SolverRoute.SearchPhysicalDeferred;
        int copies = cards.Sum(card => card.Copies);
        // A lossless encoding of remaining counts, held counts and hidden count.
        // Decode just the terminal currently being counted, not the whole map.
        bool packed = compactSearchTerminals && copies <= byte.MaxValue;
        Dictionary<State, BigInteger> terminal;
        using (budget.SearchDiagnostics?.Measure("selective draw distribution")) {
            terminal = resolver.Resolve<SurvivorTerminal>(handSize, copies, factorial, compactFrontierThreshold, packed);
        }
        TerminalStates = terminal.Count;
        List<Card> explicitRows = cards.Take(cards.Count - 1).ToList();
        List<Card> originals = [.. explicitRows, .. deferred];
        BigInteger[] wins = new BigInteger[1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0))];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(factorial[copies]) + 1), "physical results");
        foreach ((State state, BigInteger mass) in terminal) {
            int[] terminalValues = state.Values;
            if (packed) {
                budget.Spend(terminalValues[0]);
                terminalValues = DrawFrontierPacking.Unpack(terminalValues);
            }
            PhysicalSearchScenario scenario = new(originals, explicitRows, terminalValues,
                terminalValues[2 * cards.Count - 1], terminalValues[^1], budget);
            // The visible trace already integrates hidden skips. Conditional on
            // it, held pool labels are uniform and H hidden positions are uniform
            // among remaining UNOBSERVED copies. Seen-minus-held explicit copies
            // are consumed, never hidden or searchable. This is the same joint
            // kernel as count-state draws, not an extra survival penalty.
            ExactProbability[] values = PhysicalSearchCalculation.CountScenario(scenario, search, combos, groups, totalOnly, budget, conditionObserved: true);
            for (int row = 0; row < wins.Length; row++) {
                ExactProbability value = values[row];
                budget.Spend((WorkBudget.IntegerCells(mass) + value.Cells + 1) * (value.Cells + 1));
                BigInteger contribution = BigInteger.DivRem(mass * value.Numerator, value.Denominator, out BigInteger remainder);
                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Conditional survivor assignments must have integer permutation mass.");
                }
                wins[row] += contribution;
            }
            CheckStorage(wins.Length, wins.Sum(WorkBudget.IntegerCells), "physical results");
        }
        ExactProbability[] results = wins.Select(win => new ExactProbability(win, factorial[copies])).ToArray();
        return new(results[0], totalOnly ? [] : results.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : results.Skip(1 + combos.Count).ToArray());
    }
}
