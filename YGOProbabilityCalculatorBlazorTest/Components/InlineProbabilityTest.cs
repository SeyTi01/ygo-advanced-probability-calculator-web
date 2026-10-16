using System.Collections.Immutable;
using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[SetCulture("en-US")]
public sealed class InlineProbabilityTest : CalculatorEditorTestBase {
    private static ProbabilityCalculationResult Mixed() {
        SearchAvailabilityInterval exact = new("Duplicate", new("0", "1"), new("0", "1"));
        SearchAvailabilityInterval informative = new("Duplicate", new("1", "2"), new("17", "30"));
        SearchAvailabilityInterval uninformative = new("Duplicate", new("0", "1"), new("1", "2"));
        return new(17d / 30,
            [new(2, "Duplicate", .5), new(0, "Duplicate", 0, "left"), new(1, "Duplicate", 17d / 30, "right")],
            [new("right", "Duplicate group", 17d / 30, 1), new("left", "Duplicate group", 0, 1)],
            new(SearchAvailabilityStatus.Limited, "Optional refinement stopped at Work limit; accepted bound retained.", informative,
                [exact, informative, uninformative], [informative, exact]),
            ProbabilityPresentationTest.Contract(CalculationAccuracy.Bounded) with {
                Reference = "Internal solver reference (v1)",
                Reason = "Optional refinement stopped at Work limit; Storage diagnostics are available.",
                Fractions = new(new("17", "30"), [new("0", "1"), new("17", "30"), new("1", "2")], [new("17", "30"), new("0", "1")])
            });
    }

    [Test]
    public async Task CurrentAndPinUseAcceptedIndicesAndGroupOrderThroughEditsAndFailedRecalculation() {
        ProbabilityCalculationResult accepted = Mixed();
        Mock<IBackgroundCalculator> worker = new();
        int calls = 0;
        worker.Setup(value => value.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1 ? Task.FromResult(accepted) : Task.FromException<ProbabilityCalculationResult>(new InvalidOperationException("Test failure")));
        context.Services.AddSingleton(worker.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Cards = [new([a], 4)], Categories = [a], HandSize = 1,
            Combos = [new([new(a, 1, 1)], "First"), new([new(a, 1, 1)], "Second"), new([new(a, 1, 1)], "Third")],
            ComboGroups = [new("left", "First"), new("right", "Second")]
        });
        await Button(cut, "Calculate").ClickAsync(new());
        AssertRows(".probability-results");
        AssertAccuracyDisclosure(cut, ".probability-results");
        ProbabilityCalculationResult owned = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        await Button(cut, "Pin result").ClickAsync(new());
        AssertRows(".pinned-result");
        AssertAccuracyDisclosure(cut, ".probability-results");
        AssertAccuracyDisclosure(cut, ".pinned-result");
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });
        await Button(cut, "Calculate").ClickAsync(new());
        AssertRows(".probability-results");
        AssertRows(".pinned-result");
        AssertAccuracyDisclosure(cut, ".probability-results");
        AssertAccuracyDisclosure(cut, ".pinned-result");
        Assert.That(cut.Find(".probability-results .probability-result-status").TextContent,
            Does.Contain("Previous result · inputs changed"));
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result, Is.SameAs(owned));
        await cut.Find("button[title='Copy results']").ClickAsync(new());
        string copied = (string)context.JSInterop.Invocations["copyText"].Last().Arguments[0]!;
        Assert.That(copied, Does.Contain("53.33% ±3.34%").And.Contain("[1/2, 17/30]").And.Contain("optimistic U").And.Contain("17/30"));
        Assert.That(accepted.TotalProbability, Is.EqualTo(17d / 30));
        Assert.That(accepted.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("17", "30")));

        void AssertRows(string root) {
            // Render order: total, right group + combo 1, left group + combo 0, ungrouped combo 2.
            Assert.That(cut.FindAll(root + " .probability-uncertainty").Select(element => element.TextContent),
                Is.EqualTo(new[] { "±3.34%", "±3.34%", "±3.34%", "±0%", "±0%", "±25.00%" }));
            Assert.That(cut.FindAll(root + " .probability-uninformative"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll(root + " table"), Is.Empty);
            Assert.That(cut.Find(root + " .probability-with-accuracy").GetAttribute("aria-label"), Does.Contain("[1/2, 17/30]"));
        }
    }

    [TestCase(CalculationAccuracy.Exact, SearchAvailabilityStatus.NotApplicable)]
    [TestCase(CalculationAccuracy.Uncertified, SearchAvailabilityStatus.NotCertified)]
    [TestCase(CalculationAccuracy.Uncertified, SearchAvailabilityStatus.Limited)]
    [TestCase(CalculationAccuracy.Uncertified, SearchAvailabilityStatus.Error)]
    [TestCase(null, SearchAvailabilityStatus.NotCertified)]
    public void SharedCurrentAndPinnedRowsRespectActualAccuracy(CalculationAccuracy? accuracy, SearchAvailabilityStatus status) {
        CalculationResultContract? contract = accuracy is null ? null : ProbabilityPresentationTest.Contract(accuracy);
        SearchAvailabilityCertificate? certificate = status == SearchAvailabilityStatus.NotApplicable ? null : new(status, "Specific accepted reason");
        IRenderedComponent<ProbabilityValue> current = context.RenderComponent<ProbabilityValue>(parameters => parameters
            .Add(value => value.Probability, .5).Add(value => value.Contract, contract).Add(value => value.Certificate, certificate));
        PinnedCalculationContext pinContext = new(1, 2, 2, CalculationModel.Fast);
        PinnedResultSnapshot snapshot = new(pinContext, .5, [new(0, "Combo", .5, "g")], [new("g", "Group", .5, 1)]) {
            Contract = contract, SearchAvailability = certificate
        };
        IRenderedComponent<PinnedResultPanel> pin = context.RenderComponent<PinnedResultPanel>(parameters => parameters.Add(value => value.Snapshot, snapshot));
        string expected = accuracy == CalculationAccuracy.Exact ? "±0%" : "⚠ Uncertified";
        Assert.That(current.Find(".probability-uncertainty").TextContent, Is.EqualTo(expected));
        Assert.That(pin.FindAll(".probability-uncertainty").Select(element => element.TextContent), Is.All.EqualTo(expected));
        Assert.That(pin.FindAll("table"), Is.Empty);
        Assert.That(pin.Find(".accuracy-details summary").TextContent, Is.EqualTo("Accuracy details"));
    }

    private static void AssertAccuracyDisclosure(IRenderedFragment cut, string resultSelector) {
        AngleSharp.Dom.IElement disclosure = cut.Find(resultSelector + " .accuracy-details");

        Assert.That(disclosure.QuerySelectorAll("p"), Has.Count.EqualTo(1));
        Assert.That(disclosure.QuerySelector("p")!.TextContent, Is.EqualTo(ProbabilityPresentation.AccuracyDetails));
        Assert.That(disclosure.TextContent, Does.Not.Contain("Internal solver reference")
            .And.Not.Contain("Certification Limited")
            .And.Not.Contain("(v1)")
            .And.Not.Contain("Work limit")
            .And.Not.Contain("Storage diagnostics")
            .And.Not.Contain("accepted bound retained"));
    }
}
