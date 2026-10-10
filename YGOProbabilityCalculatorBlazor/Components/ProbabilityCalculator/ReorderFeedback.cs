namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public sealed class ReorderFeedback<TKey> : IDisposable where TKey : notnull {
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromMilliseconds(850);

    private readonly Func<Action, Task> _invokeAsync;
    private readonly Action _stateChanged;
    private readonly Func<CancellationToken, Task> _delay;
    private readonly IEqualityComparer<TKey> _comparer;
    private CancellationTokenSource? _timeout;
    private TKey? _movedItem;
    private long _version;
    private bool _hasMovedItem;
    private bool _disposed;

    public ReorderFeedback(
        Func<Action, Task> invokeAsync,
        Action stateChanged,
        IEqualityComparer<TKey>? comparer = null,
        Func<CancellationToken, Task>? delay = null) {
        _invokeAsync = invokeAsync;
        _stateChanged = stateChanged;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _delay = delay ?? (cancellationToken => Task.Delay(FeedbackDuration, cancellationToken));
    }

    public bool IsActive(TKey item) =>
        !_disposed && _hasMovedItem && _comparer.Equals(_movedItem!, item);

    public Task MarkMoved(TKey item) {
        if (_disposed) {
            return Task.CompletedTask;
        }

        _timeout?.Cancel();
        CancellationTokenSource timeout = new();
        _timeout = timeout;
        _movedItem = item;
        _hasMovedItem = true;
        long version = ++_version;

        return ClearAfterDelay(version, timeout);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _version++;
        _hasMovedItem = false;
        _movedItem = default;

        CancellationTokenSource? timeout = _timeout;
        _timeout = null;
        timeout?.Cancel();
    }

    private async Task ClearAfterDelay(long version, CancellationTokenSource timeout) {
        try {
            await _delay(timeout.Token);

            if (_disposed || version != _version) {
                return;
            }

            await _invokeAsync(() => {
                if (_disposed || version != _version) {
                    return;
                }

                _timeout = null;
                _hasMovedItem = false;
                _movedItem = default;
                _stateChanged();
            });
        }
        catch (OperationCanceledException) {
        }
        catch (ObjectDisposedException) when (_disposed) {
        }
        catch (InvalidOperationException) when (_disposed) {
        }
        finally {
            timeout.Dispose();
        }
    }
}
