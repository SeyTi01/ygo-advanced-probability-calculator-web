using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

internal sealed class ControlledBackgroundCalculator : IBackgroundCalculator {
    internal sealed record Call(
        CalculationSnapshot Snapshot,
        CancellationToken Token,
        TaskCompletionSource<ProbabilityCalculationResult> Completion
    );

    public List<Call> Calls { get; } = [];

    public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Calls.Add(new(snapshot, token, completion));
        // Tests explicitly complete calls, including late results after cancellation.

        return completion.Task;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
