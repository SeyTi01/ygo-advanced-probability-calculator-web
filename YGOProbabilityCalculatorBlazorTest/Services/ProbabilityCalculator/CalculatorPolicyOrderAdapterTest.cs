using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CalculatorPolicyOrderAdapterTest {
    [TestCase(false)]
    [TestCase(true)]
    public void GoalOrderPreservesAtomicKeepPolicyAndStandaloneRows(bool composite) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        CategoryBase c = new("C");
        List<Card> deck = [
            new(composite ? [a, c] : [a], id: "a"),
            new([b], id: "b"),
            new([], id: "draw", drawCount: 1, postDrawDiscardCount: 1)
        ];
        ComboCategory aRequirement = composite ? ComboCategory.SameCard([a, c], 1, 2) : new(a, 1, 2);
        Combo goalA = new([aRequirement], "A");
        Combo goalB = new([new(b, 1, 2)], "B");
        ProbabilityCalculatorService service = new();

        // Three equally likely opening hands: A+B already succeeds for both;
        // A+draw and B+draw reveal the missing card and retain B under the fixed
        // ordinal atomic-role policy. Hence B succeeds always, A only once.
        foreach (List<Combo> goals in new List<Combo>[] { [goalB, goalA], [goalA, goalB] }) {
            ProbabilityCalculationResult native = service.CalculateProbabilityResults(deck, goals, 2);
            ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(
                CalculationSnapshot.Capture(deck, goals, 2, []).Json));

            foreach (ProbabilityCalculationResult result in new[] { native, worker }) {
                Assert.Multiple(() => {
                    Assert.That(result.TotalProbability, Is.EqualTo(1));
                    Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
                    Assert.That(result.ComboProbabilities.Single(row => row.ComboName == "B").Probability, Is.EqualTo(1));
                    Assert.That(result.ComboProbabilities.Single(row => row.ComboName == "A").Probability, Is.EqualTo(1.0 / 3).Within(1e-12));
                });
            }
        }
    }
}
