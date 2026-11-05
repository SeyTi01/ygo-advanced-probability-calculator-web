using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CalculatorActiveInputAdapterTest {
    [Test]
    public void NativeOrdinaryAndDrawInputsRetainInactiveCopies() {
        CategoryBase target = new("target");
        ProbabilityCalculatorService service = new();
        List<Combo> goals = [new([new(target, 1, 2)])];
        ProbabilityCalculationResult ordinary = service.CalculateProbabilityResults(
            [new([target], active: false), new([])], goals, 1);
        ProbabilityCalculationResult draw = service.CalculateProbabilityResults(
            [new([], active: false, drawCount: 1), new([target]), new([])], goals, 1);

        Assert.That(ordinary.TotalProbability, Is.EqualTo(0.5));
        // Target initially: 1/3; inactive draw initially then target: 1/3 * 1/2.
        Assert.That(draw.TotalProbability, Is.EqualTo(0.5));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InactiveSearchOrExcavationRemainsAnOrdinaryPhysicalEntry(bool excavation) {
        CategoryBase target = new("target");
        Card source = excavation
            ? new([], active: false, excavation: new(1))
            : new([], active: false, search: new(CardSelector.For(target), false));
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            [source, new([target]), new([])], [new([new(target, 1, 2)])], 1);

        // The inactive source stays in the three-copy denominator and never acts.
        Assert.That(result.TotalProbability, Is.EqualTo(1.0 / 3).Within(1e-12));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActiveSearchOrExcavationUsesOnlyActivePositiveEntries(bool excavation) {
        CategoryBase target = new("target");
        Card source = excavation ? new([], excavation: new(1)) : new([], search: new(CardSelector.For(target), false));
        List<Card> deck = [source, new([target]), new([]), new([target], 2, active: false), new([target], 0)];
        List<Combo> goals = [new([new(target, 1, 2)])];
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult native = service.CalculateProbabilityResults(deck, goals, 1);
        ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(
            CalculationSnapshot.Capture(deck, goals, 1, []).Json));

        // Of three eligible opening copies, the target succeeds directly; the
        // source succeeds always for search, or on one of two reveals for excavation.
        double expected = excavation ? 0.5 : 2.0 / 3;
        Assert.That(native.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(worker.TotalProbability, Is.EqualTo(native.TotalProbability));
        ProbabilityCalculationResult opening = service.CalculateProbabilityResults(deck, goals, 1, [],
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.OpeningHand));
        Assert.That(opening.TotalProbability, Is.EqualTo(3.0 / 5).Within(1e-12));
    }
}
