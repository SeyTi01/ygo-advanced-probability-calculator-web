// Disposable, externally compiled research only. No production routing or contract changes.
using System.Diagnostics;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

internal static class ResearchRefinement {
    internal sealed record Checkpoint(long Work, double Milliseconds, ProbabilityFraction Mass, ProbabilityFraction[] Lower, ProbabilityFraction[] Upper);
    internal sealed record Result(string Status, long Work, long Entries, long Cells, int Completed, int Regions,
        ProbabilityFraction[] Lower, ProbabilityFraction[] Upper, List<Checkpoint> Checkpoints);

    internal static Result Run(List<Card> cards, List<Combo> combos, IReadOnlyList<ComboGroup> groups, int handSize,
        DrawEffectCalculation? execution, ProbabilityCalculationResult baseline, WorkBudget request, long cap) {
        SearchAvailabilityCertificate? certificate = baseline.SearchAvailability;
        SearchAvailabilityInterval[] old = certificate?.Total is null ? [] : [certificate.Total, .. certificate.Combos, .. certificate.Groups];
        ProbabilityFraction[] lower = old.Select(row => row.Lower).ToArray();
        ProbabilityFraction[] upper = old.Select(row => row.Upper).ToArray();
        List<Checkpoint> checkpoints = [];
        // Cheap gate on completed capability. No repeated applicability tree traversal.
        if (execution?.DescribeCapability().Route != SolverRoute.SearchCountState || old.Length == 0 || old.All(row => row.IsExact)) {
            return new("Ineligible", 0, 0, 0, 0, 0, lower, upper, checkpoints);
        }
        long allowance = Math.Min(cap, request.Allowance - request.Spent - 4096);
        if (allowance <= 0) { return new("NoRemainingBudget", 0, 0, 0, 0, 0, lower, upper, checkpoints); }
        WorkBudget budget = new(new(allowance), request.StoragePolicy);
        long started = Stopwatch.GetTimestamp();
        int completed = 0;
        int regions = 0;
        string status = "Completed";
        try {
            SearchAccessCalculation original = execution.SearchExecution!;
            List<Card> effects = original.Effects;
            // Fast terminals can merge different observed-source counts. Rebuild the
            // physical distribution with the SAME guarded resolver; charge all work.
            DrawResolution resolution = DrawEffectResolver.Resolve<PhysicalSearchComposition>(
                cards.Where(card => !effects.Contains(card)).Sum(card => card.Copies), effects, handSize, budget);
            regions = resolution.Scenarios.Count;
            budget.CheckStorage(regions, resolution.Scenarios.Sum(pair => (long)pair.Key.EffectCounts.Length + pair.Value.Cells + 1));
            KeyValuePair<DrawComposition, ExactProbability>[] ordered = resolution.Scenarios.ToArray();
            Array.Sort(ordered, (left, right) => {
                long cells = left.Value.Cells + right.Value.Cells;
                budget.Spend(cells * cells + 1);
                return (right.Value.Numerator * left.Value.Denominator).CompareTo(left.Value.Numerator * right.Value.Denominator);
            });
            SearchAccessPlan search = new(cards, budget);
            ExactProbability[] wins = Enumerable.Repeat(ExactProbability.Zero, old.Length).ToArray();
            ExactProbability mass = ExactProbability.Zero;
            foreach ((DrawComposition composition, ExactProbability weight) in ordered) {
                PhysicalSearchScenario scenario = new(cards, effects, composition, budget);
                List<List<CompiledEvent?>> events = search.Compile(combos, scenario.HandSize, scenario.Cards, scenario.Capacity, physical: scenario);
                SearchDecisionCounter diagram = new(scenario.Domains, scenario.Domains.DefaultIfEmpty(0).Max(), budget, singleSample: false);
                int[] roots = events.Select(diagram.Union).ToArray();
                int union = roots.Aggregate(0, diagram.Or);
                int[] groupRoots = groups.Select(group => roots.Where((_, row) => combos[row].GroupId == group.Id).Aggregate(0, diagram.Or)).ToArray();
                int[] all = [union, .. roots, .. groupRoots];
                SearchDecisionCounter.SurvivorCounter counter = new(diagram, scenario);
                BigInteger denominator = counter.Count(1);
                ExactProbability[] next = new ExactProbability[wins.Length];
                for (int row = 0; row < next.Length; row++) {
                    next[row] = wins[row].Add(weight.Multiply(new(counter.Count(all[row]), denominator), budget), budget);
                }
                ExactProbability nextMass = mass.Add(weight, budget);
                ProbabilityFraction[] nextLower = new ProbabilityFraction[next.Length];
                ProbabilityFraction[] nextUpper = new ProbabilityFraction[next.Length];
                for (int row = 0; row < next.Length; row++) {
                    ExactProbability lo = Parse(lower[row]);
                    ExactProbability hi = Parse(upper[row]);
                    ExactProbability remainderUpper = ExactProbability.One.Add(new(-nextMass.Numerator, nextMass.Denominator), budget).Add(next[row], budget);
                    budget.Spend((lo.Cells + hi.Cells + next[row].Cells + remainderUpper.Cells) * 8L);
                    nextLower[row] = (Compare(lo, next[row]) >= 0 ? lo : next[row]).ToFraction();
                    nextUpper[row] = (Compare(hi, remainderUpper) <= 0 ? hi : remainderUpper).ToFraction();
                    if (Compare(Parse(nextLower[row]), Parse(nextUpper[row])) > 0) { throw new InvalidOperationException("Interval invariant"); }
                }
                // Check retained diagnostics BEFORE committing any new endpoint.
                long cells = next.Sum(value => value.Cells) * 5L + 8L * next.Length;
                foreach (Checkpoint point in checkpoints) {
                    cells += point.Lower.Concat(point.Upper).Sum(value => (long)Parse(value).Cells) + Parse(point.Mass).Cells + 4;
                }
                budget.Spend(cells + 1);
                budget.CheckStorage((checkpoints.Count + 1L) * (2 * next.Length + 1), cells);
                Checkpoint checkpoint = new(budget.Spent, Stopwatch.GetElapsedTime(started).TotalMilliseconds, nextMass.ToFraction(), nextLower, nextUpper);
                checkpoints.Add(checkpoint);
                wins = next; mass = nextMass; lower = nextLower; upper = nextUpper; completed++;
            }
        }
        catch (ProbabilityCalculationLimitException exception) { status = exception.Reason + "Limit"; }
        catch (OperationCanceledException) { status = "Cancelled"; }
        catch (Exception exception) { status = "ProofFailure:" + exception.GetType().Name; }
        finally {
            request.Spend(budget.Spent);
            // Failed child storage checks may exceed the parent bound. Report those
            // attempted peaks separately, without converting completed Fast to failure.
        }
        return new(status, budget.Spent, budget.PeakEntries, budget.PeakCells, completed, regions, lower, upper, checkpoints);
    }

    private static ExactProbability Parse(ProbabilityFraction fraction) => new(BigInteger.Parse(fraction.Numerator), BigInteger.Parse(fraction.Denominator));
    private static int Compare(ExactProbability left, ExactProbability right) => (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);
}
