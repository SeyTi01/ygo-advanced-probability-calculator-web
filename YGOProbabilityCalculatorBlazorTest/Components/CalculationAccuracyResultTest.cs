using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[SetCulture("en-US")]
public sealed class CalculationAccuracyResultTest : CalculatorEditorTestBase {
    [TestCase(CalculationModel.Fast, CalculationModel.Fast, CalculationAccuracy.Bounded, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.Exact, CalculationModel.Exact, CalculationAccuracy.Exact, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.OpeningHand, CalculationModel.OpeningHand, CalculationAccuracy.Exact, CalculationRuleScope.OpeningHand)]
    [TestCase(CalculationModel.Fast, CalculationModel.Exact, CalculationAccuracy.Exact, CalculationRuleScope.SavedEffectsFixedPolicy)]
    [TestCase(CalculationModel.Fast, CalculationModel.Fast, CalculationAccuracy.Uncertified, CalculationRuleScope.SavedEffectsFixedPolicy)]
    public void KeepsOneConciseDisclosureAcrossAccuracyAndExecutionModes(
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
        AssertDisclosure(cut);
    }

    [Test]
    public void MissingContractStillExplainsUncertifiedAccuracyWithoutDiagnostics() {
        IRenderedComponent<CalculationAccuracyResult> cut = context.RenderComponent<CalculationAccuracyResult>();

        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo("Uncertified · accuracy unavailable"));
        AssertDisclosure(cut);
    }

    private static void AssertStatus(IRenderedFragment cut, CalculationResultContract contract) {
        string scope = contract.Scope == CalculationRuleScope.OpeningHand
            ? "dealt hand; effects ignored"
            : "saved effects and fixed policy";
        string expected = $"{contract.ExecutedMode.Description()} · {contract.Accuracy} — {scope}";

        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo(expected));
        Assert.That(cut.Find(".result-accuracy").GetAttribute("role"), Is.EqualTo("status"));
    }

    private static void AssertDisclosure(IRenderedFragment cut) {
        AngleSharp.Dom.IElement disclosure = cut.Find("details.accuracy-details");

        Assert.That(disclosure.GetAttribute("open"), Is.Null, "Accuracy details should be collapsed by default.");
        Assert.That(disclosure.QuerySelector("summary")!.TextContent, Is.EqualTo("Accuracy details"));
        Assert.That(disclosure.QuerySelectorAll("p"), Has.Count.EqualTo(1));
        Assert.That(disclosure.QuerySelector("p")!.TextContent, Is.EqualTo(ProbabilityPresentation.AccuracyDetails));
        Assert.That(disclosure.TextContent, Does.Not.Contain("Internal solver reference")
            .And.Not.Contain("Reason")
            .And.Not.Contain("Certification Bounded")
            .And.Not.Contain("(v1)")
            .And.Not.Contain("Work limit")
            .And.Not.Contain("Storage diagnostics")
            .And.Not.Contain("internal-proof-id"));
        Assert.That(disclosure.QuerySelectorAll("table"), Is.Empty);
    }
}
