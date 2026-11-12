namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Lets sibling routes inspect the retained editor without copying its model or drafts.
public sealed class WorkspaceState {
    public Func<bool>? HasChanges { get; set; }
    public bool IsModified => HasChanges?.Invoke() == true;
}
