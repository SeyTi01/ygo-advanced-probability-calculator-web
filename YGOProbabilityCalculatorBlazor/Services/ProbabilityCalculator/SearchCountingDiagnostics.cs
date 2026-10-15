using System.Diagnostics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Explicit test/benchmark probe only. Normal requests never allocate a probe,
// read a clock, or retain diagnostic maps. Counts aggregate across draw scenarios.
internal sealed class SearchCountingDiagnostics {
    internal Dictionary<string, long> Counts { get; } = new();
    internal Dictionary<string, double> Milliseconds { get; } = new();

    internal void Add(string name, long count = 1) => Counts[name] = Counts.GetValueOrDefault(name) + count;
    internal IDisposable Measure(string name) => new Measurement(this, name);

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
