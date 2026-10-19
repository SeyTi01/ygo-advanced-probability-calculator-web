using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// One focused form in a recursive selector. This is editor state, never model data.
public sealed class SelectorEditSession {
    internal object? ActiveNode { get; set; }
    internal Func<Task>? CancelActive { get; set; }
    internal CardSelector? ActiveSelector { get; set; }
    internal Func<bool>? HasChanges { get; set; }
    internal Func<Task>? ClearOperatorError { get; set; }
}
