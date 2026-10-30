using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[SetCulture("en-US")]
public sealed class CalculationAccuracyResultTest : CalculatorEditorTestBase {
    [TestCase(AutomaticExactOutcome.Completed, CalculationAccuracy.Exact)]
    [TestCase(AutomaticExactOutcome.Deadline, CalculationAccuracy.Bounded)]
    [TestCase(AutomaticExactOutcome.WorkLimit, CalculationAccuracy.Uncertified)]
    [TestCase(AutomaticExactOutcome.StorageLimit, CalculationAccuracy.Bounded)]
    [TestCase(AutomaticExactOutcome.Unsupported, CalculationAccuracy.Uncertified)]
    [TestCase(AutomaticExactOutcome.Error, CalculationAccuracy.Bounded)]
    [TestCase(AutomaticExactOutcome.NoTime, CalculationAccuracy.Bounded)]
    public void AutomaticOutcomeStaysWithAcceptedPinAndCopyWithoutRelabelingFast(AutomaticExactOutcome outcome, CalculationAccuracy accuracy) {
        CalculationResultContract contract = ProbabilityPresentationTest.Contract(accuracy) with { AutomaticExact = outcome };
        ProbabilityCalculationResult result = new(.5, [new(0, "Route", .5)]) { Contract = contract };
        PinnedResultSnapshot snapshot = PinnedResultSnapshot.Capture(new(5, 40, 20), result);
        IRenderedComponent<CalculationAccuracyResult> cut = context.RenderComponent<CalculationAccuracyResult>(parameters =>
            parameters.Add(component => component.Contract, snapshot.Contract));
        string expected = ProbabilityPresentation.AutomaticExactDetail(outcome)!;
        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo("Mode: Effects — fast"));
        Assert.That(cut.FindAll("details"), Is.Empty);
        string copied = ProbabilityResultSummaryFormatter.Format(result, 5, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(copied, Does.Contain("Mode: Effects — fast").And.Contain(expected));
        Assert.That(snapshot.Contract, Is.SameAs(contract));
    }

    [TestCase(CalculationModel.Fast, CalculationModel.Fast, CalculationAccuracy.Bounded, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.Exact, CalculationModel.Exact, CalculationAccuracy.Exact, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.OpeningHand, CalculationModel.OpeningHand, CalculationAccuracy.Exact, CalculationRuleScope.OpeningHand)]
    [TestCase(CalculationModel.Fast, CalculationModel.Exact, CalculationAccuracy.Exact, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.Fast, CalculationModel.Fast, CalculationAccuracy.Uncertified, CalculationRuleScope.SavedEffectsFixedPolicy)]
    public void KeepsModeAndAccuracyWithoutRepeatingScopeProse(
        CalculationModel requestedMode,
        CalculationModel executedMode,
        CalculationAccuracy accuracy,
        CalculationRuleScope scope
    ) {
        CalculationResultContract contract = new(
            CalculationResultContract.CurrentVersion,
            requestedMode,
            executedMode,
            "Internal solver reference (v1)",
            scope,
            CalculationCompletion.Success,
            accuracy,
            "Optional refinement stopped at Work limit; Storage diagnostics are available.",
            "internal solver route",
            "internal-proof-id-v1"
        );

        IRenderedComponent<CalculationAccuracyResult> cut = context.RenderComponent<CalculationAccuracyResult>(parameters =>
            parameters.Add(component => component.Contract, contract));

        AssertStatus(cut, contract);
        Assert.That(cut.FindAll("details"), Is.Empty);
    }

    [Test]
    public void MissingContractStillExplainsUncertifiedAccuracyWithoutDiagnostics() {
        IRenderedComponent<CalculationAccuracyResult> cut = context.RenderComponent<CalculationAccuracyResult>();

        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo("Uncertified · accuracy unavailable"));
        Assert.That(cut.FindAll("details"), Is.Empty);
    }

    [Test]
    public void ContextUsesPhysicalCopiesAndKeepsModeAndAccuracyDisclosure() {
        PinnedCalculationContext input = new(5, 40, 24);
        IRenderedComponent<CalculationAccuracyResult> cut = context.RenderComponent<CalculationAccuracyResult>(parameters => parameters
            .AddChildContent("<h5>Results</h5>")
            .Add(component => component.Context, input)
            .Add(component => component.ModeDescription, input.ModeDescription)
            .Add(component => component.Contract, ProbabilityPresentationTest.Contract(CalculationAccuracy.Exact)));
        Assert.That(cut.Find("h5").TextContent, Is.EqualTo("Results"));
        Assert.That(cut.Find(".result-model").Children.Select(child => child.TextContent),
            Is.EqualTo(new[] { "Hand size: 5", "Deck size: 40" }));
        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo("Mode: Progressive"));
        Assert.That(cut.FindAll("details"), Is.Empty);
    }

    private static void AssertStatus(IRenderedFragment cut, CalculationResultContract contract) {
        string expected = $"Mode: {contract.RequestedMode.Description()}";

        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo(expected));
        Assert.That(cut.Find(".result-accuracy").GetAttribute("role"), Is.EqualTo("status"));
    }

    [Test]
    public void AccuracyDetailsRemainAnInitiallyCollapsedDisclosureOnTheirOwn() {
        IRenderedComponent<CalculationAccuracyDetails> cut = context.RenderComponent<CalculationAccuracyDetails>();
        AssertDisclosure(cut);
    }

    private static void AssertDisclosure(IRenderedFragment cut) {
        AngleSharp.Dom.IElement disclosure = cut.Find("details.accuracy-details");

        Assert.That(disclosure.GetAttribute("open"), Is.Null, "Accuracy details should be collapsed by default.");
        Assert.That(disclosure.QuerySelector("summary"), Is.Not.Null);
        Assert.That(disclosure.TextContent, Does.Contain(ProbabilityPresentation.AccuracyDetails));
    }
}
