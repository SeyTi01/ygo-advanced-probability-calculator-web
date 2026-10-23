using System.Diagnostics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class DrawEffectFeasibilityBenchmark {
    [Test, Explicit("Warmed 40-card/5-hand before/after feasibility benchmark")]
    public void WarmedFortyCardRequests() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase material = new("Level 8", CategorySource.Metadata, "level:8");
        List<Combo> combos = [new([new(starter, 1, 5), new(extender, 1, 5)], groupId: "g"),
            new([new(starter, 2, 5)], groupId: "g"), new([], cards: [new("target", 1, 3)])];
        List<ComboGroup> groups = [new("g", "Routes")];
        Card pot = CardEffectRegistry.Default.RestoreBuiltIn(new([], 3, id: "pot", externalCardId: 49238328));
        List<Card> Tail(bool eligible) => [new(eligible ? [starter, material] : [starter], 8, id: "target"),
            new([extender], 8, id: "ext"), new([starter, extender], 4, id: "both"), new([], 15, id: "f")];
        List<(string Name, List<Card> Deck)> cases = [
            ("ordinary", [new([], 5, id: "ordinary"), .. Tail(false)]),
            ("unrestricted", [new([], 3, id: "draw2", drawCount: 2), new([], 2, id: "draw1", drawCount: 1), .. Tail(false)]),
            ("feasible-cost", [new([], 3, id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)), new([], 2, id: "draw", drawCount: 1), .. Tail(true)]),
            ("mixed-chain", [pot, new([], 2, id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)), .. Tail(true)]),
            ("pot-inert-cost", [pot, new([], 2, id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)), .. Tail(false)]),
            ("capacity-equal", [new([], 3, id: "hidden", drawCount: 2, preDrawDeckRemovalCount: 33), new([], 2, id: "draw", drawCount: 1), .. Tail(false)]),
            ("capacity-inert", [pot, new([], 2, id: "hidden", drawCount: 2, preDrawDeckRemovalCount: 34), .. Tail(false)])
        ];
        foreach ((string name, List<Card> deck) in cases) {
            Assert.That(deck.Sum(card => card.Copies), Is.EqualTo(40));
            foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default,
                new DrawActivationPriority(DrawActivationMode.CustomPriority, ["cost", "hidden", "draw1", "draw2", "pot", "draw"]) }) {
                List<double> times = [];
                List<long> allocations = [];
                long work = 0;
                ExactProbability[]? expected = null;
                for (int repeat = 0; repeat < 9; repeat++) {
                    WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    Stopwatch timer = Stopwatch.StartNew();
                    ExactCalculationResult result = new DrawEffectCalculation(deck, 5, budget, priority).Calculate(combos, groups);
                    timer.Stop();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
                    expected ??= rows;
                    Assert.That(rows, Is.EqualTo(expected));
                    work = budget.Spent;
                    if (repeat >= 2) {
                        times.Add(timer.Elapsed.TotalMilliseconds);
                        allocations.Add(allocated);
                    }
                }
                times.Sort();
                allocations.Sort();
                DrawExecutionAssessment assessment = DrawExecutionAssessment.Assess(deck, combos, 5, true);
                long peak = 0;
                int states = 0;
                if (assessment.Path is DrawExecutionPath.OrderedFrontier or DrawExecutionPath.ExchangeableConditional) {
                    PostDrawCalculation ordered = new(deck, 5, new(CalculationWorkPolicy.Interactive), priority);
                    ExactCalculationResult reference = ordered.Calculate(combos, groups);
                    Assert.That(new[] { reference.Total }.Concat(reference.Combos).Concat(reference.Groups), Is.EqualTo(expected));
                    peak = ordered.PeakCells;
                    states = ordered.PeakStates;
                }
                TestContext.Out.WriteLine($"{name}|{priority.Mode}|route={assessment.Path}|work={work}|ms={times[0]:F3}/{times[3]:F3}/{times[^1]:F3}|allocated={allocations[3]}|ordered-peak={states}/{peak}|exact={string.Join(';', expected!.Select(row => $"{row.Numerator}/{row.Denominator}"))}");
                Stopwatch preflight = Stopwatch.StartNew();
                for (int repeat = 0; repeat < 10000; repeat++) {
                    _ = DrawExecutionAssessment.Assess(deck, combos, 5, true);
                }
                TestContext.Out.WriteLine($"{name}|{priority.Mode}|assessment-us={preflight.Elapsed.TotalMilliseconds / 10:F3}");
            }
        }
    }
}
