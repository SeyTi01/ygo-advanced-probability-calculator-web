using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using CardProbability;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class UnlimitedExactCompletionTest {
    [Test]
    public void FormerlyWorkLimitedExactCompletesUnderStandard() {
        // Existing selector-edge-9 family, with one draw-source copy and the
        // removed copy returned to filler. Same 40-card deck and four-card hand.
        CategoryBase target = new("Target");
        List<Card> cards = Enumerable.Range(0, 6).Select(index => new Card([target], 3, id: "t" + index)).ToList();
        cards.Add(new([], 2, id: "s", search: new(CardSelector.For(target), false)));
        cards.Add(new([], 1, id: "d", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, postDrawDeckMillCount: 1));
        cards.Add(new([], 19, id: "f"));
        List<Combo> combos = [new([new(target, 1, 2)], "One or two targets"), new([], "Direct", cards: [new("t0", 1, 5)])];
        CalculationRequest request = new CalculationRequestAdapter(cards, true).Create(combos, 4, []);
        CalculationResult capped = CalculationEngine.Calculate(request, new CardProbability.CalculationOptions(
            CalculationMode.Exact, new ExecutionPolicy(100)));
        Assert.That(capped.Status, Is.EqualTo(CalculationStatus.WorkLimit));
        CalculationResult result = CalculationEngine.Calculate(request);
        Assert.That(result.Status, Is.EqualTo(CalculationStatus.Completed));
        // Frozen dev reference completed with an explicitly diagnostic 10B budget.
        Assert.That(result.Total!.Primary, Is.EqualTo(new RationalProbability("793381", "959595")));
        Assert.That(result.Goals.Select(row => row.Value.Primary), Is.EqualTo(new[] {
            new RationalProbability("51103", "73815"), new RationalProbability("851177", "1919190")
        }));
        Assert.That(result.Accuracy, Is.EqualTo(ProbabilityAccuracy.Exact));
        Assert.That(result.Metrics.RemainingWork, Is.Null);
        CalculationResult finite = CalculationEngine.Calculate(request, new CardProbability.CalculationOptions(
            CalculationMode.Exact, new ExecutionPolicy(150_000_000)));
        Assert.That(finite.Status, Is.EqualTo(CalculationStatus.Completed));
        Assert.That(finite.Total!.Primary, Is.EqualTo(result.Total.Primary));
        Assert.That(finite.Goals.Select(row => row.Value.Primary), Is.EqualTo(result.Goals.Select(row => row.Value.Primary)));
        Assert.That(finite.Metrics.WorkSpent, Is.EqualTo(result.Metrics.WorkSpent));
        Assert.That(finite.Metrics.RemainingWork, Is.GreaterThan(0));
        Assert.That(result.Metrics.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
        Assert.That(result.Metrics.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
    }
}
