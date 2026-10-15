using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed partial class SearchEffectsTest {
    [TestCase(false), TestCase(true)]
    public void DrawAndSearchUseSharedPerEntryFlagWithIndependentCustomDuplicates(bool once) {
        Card first = Preset(80181649).WithOncePerTurn(once);
        Card second = first.Duplicate().WithName(first.Name);
        Card target = Element().WithCopies(2);
        Compare([first.WithCopies(2), target, new([], 2, id: "blank")], AssignmentObjectives(target.Id), 2);
        Compare([first, second, target, new([], 2, id: "blank")], AssignmentObjectives(target.Id), 2);
        Assert.That(first.EffectPlan.OncePerTurn, Is.EqualTo(once));
        Assert.That(first.DrawOncePerTurn, Is.EqualTo(first.OncePerTurn));
        Card drawFirst = first.WithDrawCount(1);
        Card drawSecond = second.WithDrawCount(1);
        List<Card> drawDeck = [drawFirst, drawSecond, target, new([], 2, id: "blank")];
        Compare(drawDeck, AssignmentObjectives(target.Id), 2);
        Assert.That(DrawExecutionAssessment.Assess(drawDeck, AssignmentObjectives(target.Id), 2, true).Path,
            Is.EqualTo(DrawExecutionPath.DrawCountState), "Existing per-entry fast paths remain available.");
    }
}
