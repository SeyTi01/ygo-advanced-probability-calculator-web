using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Component-local ownership of displayed results and in-flight work. Session loads,
// recovery and the worker transport retain their separate lifetimes.
internal sealed class CalculationResultState {
    private Run? _current;
    private AcceptedResult? _accepted;

    public long InputVersion { get; private set; }
    public bool IsCalculating => _current is not null;
    public bool IsPreliminary => _current?.HasPreliminary == true;
    public bool IsStale { get; private set; }
    public ProbabilityCalculationResult? Result => _accepted?.Result;
    public PinnedResultSnapshot? Snapshot => _accepted?.Snapshot;

    public Run Begin() {
        if (_current is not null) {
            throw new InvalidOperationException("A calculation already owns this workspace.");
        }

        // Starting another calculation retains the previous accepted display,
        // including its stale marker, until this run publishes a result.
        _current = new(this, InputVersion);

        return _current;
    }

    public bool Owns(Run run) => ReferenceEquals(_current, run) && run.InputVersion == InputVersion;

    public bool TryAcceptPreliminary(Run run, PinnedCalculationContext context, ProbabilityCalculationResult result) {
        if (!Owns(run) || run.Token.IsCancellationRequested || run.HasPreliminary || context.Model != CalculationModel.Fast) {
            return false;
        }

        Accept(context, result);
        run.Preliminary = _accepted;

        return true;
    }

    public void AcceptFinal(Run run, PinnedCalculationContext context, ProbabilityCalculationResult result) {
        if (Owns(run)) {
            Accept(context, result);
        }
    }

    public void RetainPreliminaryFailure(Run run, AutomaticExactOutcome outcome) {
        if (!Owns(run) || run.Preliminary is not { } preliminary) {
            return;
        }

        ProbabilityCalculationResult retained = preliminary.Result! with {
            Contract = preliminary.Result.Contract! with { AutomaticExact = outcome }
        };
        _accepted = new(retained, PinnedResultSnapshot.Capture(preliminary.Snapshot!.Context, retained));
    }

    private void Accept(PinnedCalculationContext context, ProbabilityCalculationResult result) {
        ProbabilityCalculationResult accepted = result with {
            ComboProbabilities = result.ComboProbabilities.ToArray(),
            GroupProbabilities = result.GroupProbabilities?.ToArray()
        };
        _accepted = new(accepted, PinnedResultSnapshot.Capture(context, accepted));
        IsStale = false;
    }

    public void Stop() {
        Run? previous = _current;
        _current = null;
        previous?.Cancel();
    }

    public void Invalidate(bool clearPreviousResult) {
        Stop();
        InputVersion++;
        if (clearPreviousResult) {
            _accepted = null;
            IsStale = false;
        }
        else if (Result is not null) {
            IsStale = true;
        }
    }

    public void Restore(ProbabilityCalculationResult? result, bool stale, PinnedResultSnapshot? snapshot) {
        // Language handoff restores the already accepted context verbatim. Do not
        // recapture names or infer a missing context from the current workspace.
        _accepted = new(result, snapshot);
        IsStale = stale;
    }

    private void Finish(Run run) {
        if (Owns(run)) {
            _current = null;
        }
    }

    internal sealed record AcceptedResult(ProbabilityCalculationResult? Result, PinnedResultSnapshot? Snapshot);

    internal sealed class Run(CalculationResultState owner, long inputVersion) : IDisposable {
        private readonly CancellationTokenSource _cancellation = new();

        internal long InputVersion { get; } = inputVersion;
        internal AcceptedResult? Preliminary { get; set; }
        public bool HasPreliminary => Preliminary is not null;
        public CancellationToken Token => _cancellation.Token;

        internal void Cancel() => _cancellation.Cancel();

        public void Dispose() {
            try {
                _cancellation.Cancel();
                owner.Finish(this);
            }
            finally {
                _cancellation.Dispose();
            }
        }
    }
}
