using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class HandSizeMaximumAdapterTest {
    [Test]
    public void HandSizeMaximumTracksCardsDrawnAfterOpeningHand() {
        CategoryBase role = new("role");
        List<Card> deck = [new([], id: "source", drawCount: 2), new([role], 2, id: "target")];
        Combo dynamicMaximum = new([new(role, 1, 0, RequirementMaximumMode.HandSize)]);
        Combo fixedMaximum = new([new(role, 1, 1)]);
        ProbabilityCalculatorService service = new();

        ProbabilityCalculationResult dynamicResult = service.CalculateProbabilityResults(deck, [dynamicMaximum], 1);
        ProbabilityCalculationResult fixedResult = service.CalculateProbabilityResults(deck, [fixedMaximum], 1);

        Assert.Multiple(() => {
            Assert.That(dynamicResult.TotalProbability, Is.EqualTo(1).Within(1e-12));
            Assert.That(fixedResult.TotalProbability, Is.EqualTo(2.0 / 3).Within(1e-12));
        });
    }
}
