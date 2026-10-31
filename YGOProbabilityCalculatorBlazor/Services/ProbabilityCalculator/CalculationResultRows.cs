using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Every counting route publishes the same order: total, individual combos, then
// groups. Borrow request-owned definitions; do not allocate a second membership
// table or move arithmetic and resource accounting out of the specialized routes.
internal readonly struct CalculationResultRows(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false ) {
    internal int ComboCount => combos.Count;
    internal int GroupCount => groups?.Count ?? 0;
    internal int Count => 1 + (totalOnly ? 0 : ComboCount + GroupCount);

    internal int ComboRow(int index) => 1 + index;

    internal int GroupRow(int index) => 1 + ComboCount + index;

    internal bool GroupContains(int group, int combo) => combos[combo].GroupId == groups![group].Id;

    internal bool GroupMatches(int group, ReadOnlySpan<bool> matches) {
        for (int combo = 0; combo < ComboCount; combo++) {
            if (matches[combo] && GroupContains(group, combo)) {
                return true;
            }
        }

        return false;
    }

    internal int GroupSize(int group) {
        int count = 0;
        for (int combo = 0; combo < ComboCount; combo++) {
            if (GroupContains(group, combo)) {
                count++;
            }
        }

        return count;
    }

    // Streamed certification retains all rows even for a scalar caller. Select
    // only requested output rows here, after that route has completed its work.
    internal ExactCalculationResult ToResult(ExactProbability[] values) => new(
        values[0],
        totalOnly ? [] : [.. values.Skip(1).Take(ComboCount)],
        totalOnly ? [] : [.. values.Skip(1 + ComboCount)]
    );
}
