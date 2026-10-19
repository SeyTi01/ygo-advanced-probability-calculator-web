using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal static class OperatorReversibility {
    // Semantic selector equality sorts operands. Reversing an editor operation
    // must also restore the authored order and every grouping boundary.
    internal static bool SameExpression(CardSelector left, CardSelector right) {
        if (left.Kind != right.Kind || !left.Equals(right)) { return false; }
        if (left.Kind is not (CardSelectorKind.AllOf or CardSelectorKind.AnyOf)) { return true; }
        CardSelector[] original = left.AuthoredTerms.ToArray();
        CardSelector[] restored = right.AuthoredTerms.ToArray();
        return original.Length == restored.Length
            && original.Zip(restored).All(pair => SameExpression(pair.First, pair.Second));
    }
}
