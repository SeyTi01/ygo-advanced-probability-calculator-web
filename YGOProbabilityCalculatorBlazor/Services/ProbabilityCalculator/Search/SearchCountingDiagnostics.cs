using System.Diagnostics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Explicit test/benchmark probe only. Normal requests never allocate a probe,
// read a clock, or retain diagnostic maps. Counts aggregate across draw scenarios.
internal sealed class SearchCountingDiagnostics {
    private readonly Dictionary<string, HashSet<object>> _visited = new();
    private readonly Dictionary<string, HashSet<object>> _templateKeys = new();
    private string _stage = "primary";

    internal Dictionary<string, long> Counts { get; } = new();
    internal Dictionary<string, double> Milliseconds { get; } = new();

    internal void Add(string name, long count = 1) {
        string key = _stage + "/" + name;
        Counts[key] = Counts.GetValueOrDefault(key) + count;
    }

    internal void Visit(string name, object key) {
        if (!_visited.TryGetValue(name, out HashSet<object>? values)) {
            values = [];
            _visited.Add(name, values);
        }
        Add(values.Add(key) ? name + " unique" : name + " repeated");
    }

    internal void Template(object key) {
        if (!_templateKeys.TryGetValue(_stage, out HashSet<object>? keys)) {
            keys = [];
            _templateKeys.Add(_stage, keys);
        }
        Add(keys.Add(key) ? "template keys" : "template recompilations");
    }

    internal void BeginScenario() => _visited.Clear();

    internal void Predicates(IEnumerable<CompiledEvent?> predicates) {
        HashSet<CompiledEvent> distinct = [];
        foreach (CompiledEvent? predicate in predicates) {
            Add(predicate is null ? "rejected predicates" : "projected predicates");
            if (predicate is not null) {
                Add(distinct.Add(predicate) ? "distinct residual predicates" : "duplicate residual predicates");
            }
        }
    }

    internal IDisposable Stage(string name) {
        string previous = _stage;
        _stage = name;
        return new RestoreStage(this, previous);
    }

    internal IDisposable Measure(string name) => new Measurement(this, _stage + "/" + name);

    private sealed class RestoreStage(SearchCountingDiagnostics owner, string previous) : IDisposable {
        public void Dispose() => owner._stage = previous;
    }

    private sealed class Measurement(SearchCountingDiagnostics owner, string name) : IDisposable {
        private readonly long _start = Stopwatch.GetTimestamp();
        private bool _disposed;
        public void Dispose() {
            if (!_disposed) {
                owner.Milliseconds[name] = owner.Milliseconds.GetValueOrDefault(name)
                    + Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
                _disposed = true;
            }
        }
    }
}
