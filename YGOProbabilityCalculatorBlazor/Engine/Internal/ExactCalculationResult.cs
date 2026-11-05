using CardProbability.Internal.Model;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;

namespace CardProbability.Internal;

internal sealed record ExactCalculationResult(
    ExactProbability Total,
    ExactProbability[] Combos,
    ExactProbability[] Groups
) {
    internal ExactProbability[] ToRows() => [Total, .. Combos, .. Groups];

    internal ProbabilityCalculationResult ToPublic(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, WorkBudget budget) {
        CalculationResultRows rows = new(combos, groups);
        List<ComboProbabilityResult> comboResults = combos.Select((combo, index) =>
            new ComboProbabilityResult(index, combo.Name, Combos[index].ToDouble(budget), combo.GroupId)).ToList();
        List<GroupProbabilityResult> groupResults = (groups ?? []).Select((group, index) =>
            new GroupProbabilityResult(group.Id, group.Name, Groups[index].ToDouble(budget), rows.GroupSize(index))).ToList();

        return new(Total.ToDouble(budget), comboResults.AsReadOnly(), groupResults.AsReadOnly());
    }
}
