using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// Transient: the component explicitly disposes its owner, even though a WASM DI
// scope normally outlives navigation. JS object-handle disposal alone is not teardown.
public sealed class BackgroundCalculator(IJSRuntime js) : IProgressiveBackgroundCalculator {
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _leaseGate = new(1, 1);
    private Task<IJSObjectReference>? _ownerTask;
    private Task? _disposeTask;
    private bool _disposed;

    public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken cancellationToken) =>
        CalculateCoreAsync(snapshot, null, cancellationToken);

    public Task<ProbabilityCalculationResult> CalculateProgressivelyAsync(
        CalculationSnapshot snapshot,
        Func<ProbabilityCalculationResult, Task> onPreliminary,
        CancellationToken cancellationToken
    ) => CalculateCoreAsync(snapshot, onPreliminary, cancellationToken);

    public sealed record PresentationStage(string Response, bool IsPreliminary);

    private async Task<ProbabilityCalculationResult> CalculateCoreAsync(
        CalculationSnapshot snapshot,
        Func<ProbabilityCalculationResult, Task>? onPreliminary,
        CancellationToken cancellationToken
    ) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        CancellationToken token = request.Token;
        // Initialization is shared, but starts no physical worker. Do not abandon
        // late interop handles when a request is cancelled during import/creation.
        Task<IJSObjectReference> initialization = _ownerTask ??= CreateOwnerAsync();
        IJSObjectReference owner;
        try {
            owner = await initialization;
        }
        catch {
            if (ReferenceEquals(_ownerTask, initialization)) {
                _ownerTask = null;
            }
            throw;
        }
        token.ThrowIfCancellationRequested();
        long? releasedId = null;
        bool completed = false;

        // A cancelled interop await resumes before its JS lease cleanup finishes.
        // Serialize acquisition through cleanup, never through abandoned worker work.
        await _leaseGate.WaitAsync(token);
        try {
            token.ThrowIfCancellationRequested();
            ProbabilityCalculationResult result;
            await using (IJSObjectReference job = await owner.InvokeAsync<IJSObjectReference>("createJob")) {
                try {
                    token.ThrowIfCancellationRequested();
                    if (onPreliminary is not null && snapshot.AllowAutomaticExact && snapshot.RequestedMode == CalculationModel.Fast) {
                        PresentationStage stage = await job.InvokeAsync<PresentationStage>("runProgressive", token, snapshot.Json, true);
                        token.ThrowIfCancellationRequested();
                        result = CalculationWire.ReadResult(stage.Response, snapshot.RequestedMode);
                        if (stage.IsPreliminary) {
                            CalculationResponseReader.ValidatePreliminary(result);
                            await onPreliminary(result).WaitAsync(token);
                            token.ThrowIfCancellationRequested();
                            string response = await job.InvokeAsync<string>("finish", token);
                            token.ThrowIfCancellationRequested();
                            result = CalculationWire.ReadResult(response, snapshot.RequestedMode);
                        }
                    }
                    else {
                        string response = await job.InvokeAsync<string>("run", token, snapshot.Json, snapshot.AllowAutomaticExact && snapshot.RequestedMode == CalculationModel.Fast,
                            snapshot.RequestedMode == CalculationModel.Exact);
                        token.ThrowIfCancellationRequested();
                        result = CalculationWire.ReadResult(response, snapshot.RequestedMode);
                    }
                    token.ThrowIfCancellationRequested();
                    // Errors (including valid Work/Storage/Input responses) never
                    // acknowledge the lease or retain a possibly enlarged heap.
                    releasedId = await job.InvokeAsync<long>("release");
                }
                finally {
                    if (releasedId is null) {
                        // UI-thread termination interrupts synchronous worker C#.
                        await job.InvokeVoidAsync("dispose");
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            completed = true;

            return result;
        }
        finally {
            try {
                if (!completed && releasedId is long id && !_disposed) {
                    // Cancellation or handle-disposal failure after handoff must
                    // still retire this lease, never a later request on that owner.
                    // Owner disposal independently terminates the entire runtime.
                    await owner.InvokeVoidAsync("discard", id);
                }
            }
            finally {
                _leaseGate.Release();
            }
        }
    }

    private async Task<IJSObjectReference> CreateOwnerAsync() {
        IJSObjectReference? owner = null;
        try {
            await using (IJSObjectReference module = await js.InvokeAsync<IJSObjectReference>("import", "./js/background-calculation.js")) {
                _lifetime.Token.ThrowIfCancellationRequested();
                owner = await module.InvokeAsync<IJSObjectReference>("createOwner");
            }
        }
        catch {
            // If temporary module-handle cleanup fails, initialization cannot
            // return its owner. Release that otherwise unreachable handle too.
            if (owner is not null) {
                try {
                    await owner.InvokeVoidAsync("dispose");
                }
                finally {
                    await owner.DisposeAsync();
                }
            }
            throw;
        }

        return owner;
    }

    public ValueTask DisposeAsync() {
        if (_disposeTask is not null) {
            return new(_disposeTask);
        }
        _disposed = true;
        _lifetime.Cancel();
        _disposeTask = DisposeOwnerAsync();

        return new(_disposeTask);
    }

    private async Task DisposeOwnerAsync() {
        try {
            if (_ownerTask is not null) {
                IJSObjectReference owner;
                try {
                    owner = await _ownerTask;
                }
                catch {
                    // Initialization owns its error and disposes its module.
                    return;
                }
                try {
                    await owner.InvokeVoidAsync("dispose");
                }
                finally {
                    await owner.DisposeAsync();
                }
            }
        }
        finally {
            _ownerTask = null;
            _lifetime.Dispose();
        }
    }
}
