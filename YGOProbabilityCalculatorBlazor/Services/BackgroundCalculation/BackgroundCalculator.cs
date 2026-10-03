using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public sealed class BackgroundCalculator(IJSRuntime js) : IBackgroundCalculator {
    public async Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        // Do not abandon an import/create invocation: a late handle still needs disposing.
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/background-calculation.js");
        cancellationToken.ThrowIfCancellationRequested();
        await using var job = await module.InvokeAsync<IJSObjectReference>("createJob");
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await job.InvokeAsync<string>("run", cancellationToken, snapshot.Json);
            cancellationToken.ThrowIfCancellationRequested();
            return CalculationWire.ReadResult(response);
        }
        finally {
            // Termination runs on the UI thread, even while the worker is inside synchronous C#.
            // Await it before this request settles, including cancellation during runtime startup.
            await job.InvokeVoidAsync("dispose");
        }
    }
}
