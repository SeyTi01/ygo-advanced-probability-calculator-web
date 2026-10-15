using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// Transient: the component explicitly disposes its owner, even though a WASM DI
// scope normally outlives navigation. JS object-handle disposal alone is not teardown.
public sealed class BackgroundCalculator(IJSRuntime js) : IBackgroundCalculator {
    private readonly CancellationTokenSource _lifetime = new();
    private Task<IJSObjectReference>? _ownerTask;
    private Task? _disposeTask;
    private bool _disposed;

    public async Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken cancellationToken) {
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

        try {
            ProbabilityCalculationResult result;
            await using (IJSObjectReference job = await owner.InvokeAsync<IJSObjectReference>("createJob")) {
                try {
                    token.ThrowIfCancellationRequested();
                    string response = await job.InvokeAsync<string>("run", token, snapshot.Json);
                    token.ThrowIfCancellationRequested();
                    result = CalculationWire.ReadResult(response, snapshot.RequestedMode);
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
            if (!completed && releasedId is long id && !_disposed) {
                // Cancellation or handle-disposal failure after handoff must
                // still retire this lease, never a later request on that owner.
                // Owner disposal independently terminates the entire runtime.
                await owner.InvokeVoidAsync("discard", id);
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
