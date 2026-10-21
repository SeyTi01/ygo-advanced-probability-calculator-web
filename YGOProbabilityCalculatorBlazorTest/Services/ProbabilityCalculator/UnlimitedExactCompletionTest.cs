using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

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
        CalculationOptions options = new(Mode: CalculationModel.Exact);
        ProbabilityCalculatorService service = new();
        WorkBudget capped = new(new(100));
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() =>
            service.CalculateWithBudget(cards, combos, 4, [], capped, options))!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        WorkBudget unlimited = new(CalculationWorkPolicy.Unlimited);
        ProbabilityCalculationResult result = service.CalculateWithBudget(cards, combos, 4, [], unlimited, options);
        // Frozen dev reference completed with an explicitly diagnostic 10B budget.
        Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("793381", "959595")));
        Assert.That(result.Contract.Fractions.Combos, Is.EqualTo(new[] {
            new ProbabilityFraction("51103", "73815"), new ProbabilityFraction("851177", "1919190")
        }));
        Assert.That(result.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(unlimited.Remaining, Is.Null);
        WorkBudget interactive = new(CalculationWorkPolicy.Interactive);
        ProbabilityCalculationResult finiteResult = service.CalculateWithBudget(cards, combos, 4, [], interactive, options);
        Assert.That(finiteResult.Contract!.Fractions!.Total, Is.EqualTo(result.Contract.Fractions.Total));
        Assert.That(finiteResult.Contract.Fractions.Combos, Is.EqualTo(result.Contract.Fractions.Combos));
        Assert.That(interactive.Spent, Is.EqualTo(unlimited.Spent));
        Assert.That(interactive.Remaining, Is.GreaterThan(0));
        Assert.That(unlimited.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
        Assert.That(unlimited.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
    }
}
