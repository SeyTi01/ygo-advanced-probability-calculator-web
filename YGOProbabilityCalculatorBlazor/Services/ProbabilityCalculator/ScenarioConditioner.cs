using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed class ScenarioConditioner(int ordinaryRows, int randomHandSize, List<Card> retained, WorkBudget budget) {
    internal CompiledEvent? Project(CompiledEvent? compiled) {
        if (compiled is null) {
            return null;
        }

        budget.Spend(1 + ordinaryRows / 32);
        BigInteger ordinaryMask = (BigInteger.One << ordinaryRows) - 1;
        Dictionary<BigInteger, CountBound> projected = new();
        foreach (CountBound bound in compiled.Constraints) {
            budget.Spend(retained.Count + 1L + bound.EligibleRows.GetBitLength() / 32);
            int fixedCount = 0;
            for (int i = 0; i < retained.Count; i++) {
                if ((bound.EligibleRows & (BigInteger.One << (ordinaryRows + i))) != 0) {
                    fixedCount = checked(fixedCount + retained[i].Copies);
                }
            }

            BigInteger eligible = bound.EligibleRows & ordinaryMask;
            int min = Math.Max(0, bound.MinCount - fixedCount);
            int max = Math.Min(randomHandSize, bound.MaxCount - fixedCount);
            if (projected.TryGetValue(eligible, out CountBound previous)) {
                min = Math.Max(min, previous.MinCount);
                max = Math.Min(max, previous.MaxCount);
            }

            if (min > max || (eligible.IsZero && min > 0)) {
                return null;
            }

            if (eligible.IsZero || (min == 0 && max == randomHandSize)) {
                continue;
            }

            projected[eligible] = new(eligible, min, max);
            WorkBudget.CheckStorage(projected.Count, (long)projected.Count * (3 + ordinaryRows / 32));
        }

        // Residual bounds include every Hall subset. Original role metadata is not
        // valid after fixing copies, so optional role factoring must stay disabled.
        return new(projected.Values.OrderBy(bound => bound.EligibleRows).ToArray());
    }
}
