namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// One focused form in a recursive selector. This is editor state, never model data.
public sealed class SelectorEditSession {
    internal object? ActiveNode { get; set; }
    internal Func<Task>? CancelActive { get; set; }
}
