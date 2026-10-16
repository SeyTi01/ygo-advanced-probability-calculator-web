using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class SearchAvailabilityComponentTest : CalculatorEditorTestBase {
    private static SessionState SearchSession() {
        CategoryBase target = new("Target");
        return new() { Cards = [new([], 1, id: "s", search: new(CardSelector.For(target), false)), new([target], 1, id: "t"), new([], 3, id: "f")],
            HandSize = 2, Categories = [target], Combos = [new([new(target, 1, 2)], "Target", groupId: "g")], ComboGroups = [new("g", "Routes")] };
    }

    private static SessionState BoundedSession(CalculationModel mode = CalculationModel.Fast) => new() {
        CalculationModel = mode,
        Cards = Services.ProbabilityCalculator.ThreeCalculationModesTest.Counterexample("consumed"), HandSize = 1,
        Categories = [Services.ProbabilityCalculator.ThreeCalculationModesTest.Target],
        Combos = Services.ProbabilityCalculator.ThreeCalculationModesTest.Combos,
        ComboGroups = Services.ProbabilityCalculator.ThreeCalculationModesTest.Groups
    };

    [Test]
    public async Task OptionalDisplayCopyPinAndReloadOwnTheAcceptedCertificate() {
        SessionState session = BoundedSession();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        string saved = context.Services.GetRequiredService<ISessionService>().SerializeSession(session);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".calculation-settings-menu input[type=checkbox]"), Is.Empty);
        Assert.That(cut.Find(".result-accuracy").TextContent, Does.Contain("Bounded"));
        ProbabilityCalculationResult accepted = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        string copied = ProbabilityResultSummaryFormatter.Format(accepted, 2, CultureInfo.InvariantCulture);
        Assert.That(copied, Does.Contain("fixed modeled draw/removal policy (v1)").And.Contain("Routes").And.Contain("Target").And.Contain("rational endpoints"));
        await Button(cut, "Pin result").ClickAsync(new());
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.SearchAvailability, Is.SameAs(accepted.SearchAvailability));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        AssertPreviousResult(cut);
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.SearchAvailability, Is.SameAs(accepted.SearchAvailability));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results .search-availability"), Is.Empty);
        Assert.That(cut.FindAll(".pinned-result .accuracy-details"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".search-availability table"), Is.Empty);
        for (int load = 0; load < 2; load++) {
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(saved, "certificate-session.json"));
            cut.WaitForAssertion(() => Assert.That(cut.FindAll(".probability-results"), Is.Empty));
        }
        Assert.That(saved, Does.Not.Contain("CertifySearchAvailability").And.Not.Contain("SearchAvailability"));
    }

    [TestCase("edit", false), TestCase("cancel", false), TestCase("opening", false)]
    [TestCase("edit", true), TestCase("cancel", true), TestCase("opening", true)]
    public async Task LateCertifiedResponseCannotReplaceNewerOwnership(string action, bool physical) {
        List<CalculationSnapshot> requests = [];
        List<TaskCompletionSource<ProbabilityCalculationResult>> jobs = [];
        Mock<IBackgroundCalculator> worker = new();
        worker.Setup(value => value.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken token) => {
                TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                requests.Add(snapshot);
                jobs.Add(completion);
                return completion.Task;
            });
        context.Services.AddSingleton(worker.Object);
        SessionState session = physical ? BoundedSession(CalculationModel.Exact) : SearchSession();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Task pending = Button(cut, "Calculate").ClickAsync(new());
        if (action == "edit") {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (action == "opening") {
            await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        }
        else {
            await Button(cut, "Cancel").ClickAsync(new());
        }
        jobs[0].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(requests[0].Json)));
        await pending;
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Task next = Button(cut, "Calculate").ClickAsync(new());
        jobs[1].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(requests[1].Json)));
        await next;
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract!.ExecutedMode,
            Is.EqualTo(action == "opening" ? CalculationModel.OpeningHand : physical ? CalculationModel.Exact : CalculationModel.Fast));
    }

    [Test]
    public void DisplayRoundsOutwardAndNeverLabelsUnavailableAsExact() {
        SearchAvailabilityInterval interval = new("Row", new("1", "3"), new("2", "3"));
        string text = SearchAvailabilityPresentation.Range(interval);
        Assert.That(text, Does.Contain((33.333333m).ToString("0.000000")).And.Contain((66.666667m).ToString("0.000000")));
        SearchAvailabilityCertificate unavailable = SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.NotCertified, "Ordered policy unsupported.");
        IRenderedComponent<SearchAvailabilityResult> cut = context.RenderComponent<SearchAvailabilityResult>(parameters => parameters.Add(value => value.Certificate, unavailable));
        Assert.That(cut.Find("summary").TextContent, Does.Contain("Not certified"));
        Assert.That(cut.FindAll("table"), Is.Empty);
    }

    [Test]
    public void PerRowInformativenessSurvivesWireAndCopyWithoutPromotingTheTotal() {
        SearchAvailabilityCertificate certificate = new(SearchAvailabilityStatus.Bounded,
            "Conservative loss bounds cannot prove a positive lower endpoint for every row.",
            new("Total", new("0", "1"), new("1", "2")),
            [new("Impossible", new("0", "1"), new("0", "1")), new("Useful", new("1", "4"), new("1", "2"))], []);
        string json = System.Text.Json.JsonSerializer.Serialize(certificate,
            CalculationJsonContext.Default.SearchAvailabilityCertificate);
        SearchAvailabilityCertificate restored = System.Text.Json.JsonSerializer.Deserialize(json,
            CalculationJsonContext.Default.SearchAvailabilityCertificate)!;
        Assert.That(restored.Total!.Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Uninformative));
        Assert.That(restored.Combos[0].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Exact));
        Assert.That(restored.Combos[1].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Informative));
        IRenderedComponent<SearchAvailabilityResult> cut = context.RenderComponent<SearchAvailabilityResult>(parameters => parameters.Add(value => value.Certificate, restored));
        Assert.That(cut.Find("summary").TextContent, Does.Contain("uninformative").And.Not.Contain("(exact)"));
        Assert.That(cut.FindAll("table"), Is.Empty);
        string exported = string.Join("\n", SearchAvailabilityPresentation.Export(restored));
        Assert.That(exported, Does.Contain("(uninformative)").And.Contain("(informative)").And.Contain("(exact)"));
    }

    [Test]
    public async Task LimitedExactPreservesAcceptedResultAndPinAndRecoveryKeepsExactPreference() {
        Mock<IBackgroundCalculator> worker = new();
        int exactCalls = 0;
        worker.Setup(value => value.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken token) => {
                if (snapshot.RequestedMode == CalculationModel.Exact && ++exactCalls == 1) {
                    return Task.FromException<ProbabilityCalculationResult>(new ProbabilityCalculationLimitException());
                }
                return Task.FromResult(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
            });
        context.Services.AddSingleton(worker.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = ThreeCalculationModesTest.Counterexample("discard"), HandSize = 2,
            Categories = [ThreeCalculationModesTest.Target], Combos = ThreeCalculationModesTest.Combos, ComboGroups = ThreeCalculationModesTest.Groups });
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        ProbabilityCalculationResult accepted = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("work"));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result, Is.SameAs(accepted));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.Contract, Is.SameAs(accepted.Contract));
        Assert.That(ProbabilityResultSummaryFormatter.Format(accepted, 1, CultureInfo.InvariantCulture),
            Does.Contain("Effects — fast").And.Contain("Exact"));
        await Button(cut, "Calculate opening hand only").ClickAsync(new());
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Exact"));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract!.ExecutedMode, Is.EqualTo(CalculationModel.OpeningHand));
        Assert.That(cut.Find(".comparison-model-difference").TextContent, Does.Contain("different questions"));
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract!.ExecutedMode, Is.EqualTo(CalculationModel.Exact));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "2")));
    }

    [Test]
    public async Task AdaptiveFastPinsAndCopiesExactAccuracyAndComparesWithExactMode() {
        SessionState session = new() {
            Cards = ThreeCalculationModesTest.Counterexample("discard"), HandSize = 2,
            Categories = [ThreeCalculationModesTest.Target], Combos = ThreeCalculationModesTest.Combos,
            ComboGroups = ThreeCalculationModesTest.Groups
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        ProbabilityCalculationResult accepted = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        Assert.That(cut.Find(".result-accuracy").TextContent, Does.Contain("Effects — fast").And.Contain("Exact"));
        Assert.That(accepted.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "2")));
        await Button(cut, "Pin result").ClickAsync(new());
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });
        await Button(cut, "Calculate").ClickAsync(new());
        PinnedResultSnapshot pinned = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Assert.That(pinned.Contract, Is.SameAs(accepted.Contract));
        Assert.That(pinned.Contract!.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(cut.FindAll(".comparison-model-difference"), Is.Empty);
        Assert.That(cut.FindAll(".search-availability"), Is.Empty);
        string copied = ProbabilityResultSummaryFormatter.Format(accepted, 2, CultureInfo.InvariantCulture);
        Assert.That(copied, Does.Contain("Effects — fast").And.Contain("Accuracy: Exact").And.Contain("1/2"));
        Assert.That(copied, Does.Not.Contain("optimistic").And.Not.Contain("interval"));
        Assert.That(cut.FindAll(".probability-results .probability-uncertainty").Select(element => element.TextContent), Is.All.EqualTo("±0%"));
        Assert.That(cut.FindAll(".pinned-result .probability-uncertainty").Select(element => element.TextContent), Is.All.EqualTo("±0%"));
        Assert.That(copied, Does.Contain("±0%"));
    }

    [Test]
    public async Task PhysicalExactReplacesRowsAndKeepsPinnedFastContract() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(BoundedSession());
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        ProbabilityCalculationResult fast = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });
        await Button(cut, "Calculate").ClickAsync(new());
        ProbabilityCalculationResult exact = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        Assert.That(exact.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "3")));
        Assert.That(exact.Contract.SolverRoute, Is.EqualTo("SearchPhysicalCountState"));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.Contract, Is.SameAs(fast.Contract));
        Assert.That(cut.FindAll(".probability-results .search-availability"), Is.Empty);
        Assert.That(cut.FindAll(".pinned-result .accuracy-details"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".search-availability table"), Is.Empty);
        await cut.Find("button[title='Copy results']").ClickAsync(new());
        string copied = (string)context.JSInterop.Invocations["copyText"].Last().Arguments[0]!;
        Assert.That(copied, Does.Contain("Effects — exact").And.Not.Contain("Previous result"));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        AssertPreviousResult(cut);
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.Contract, Is.SameAs(exact.Contract));
    }

    [Test]
    public async Task BoundedRowsNeverDisplayCertifiedPointImprovementsAndCopiedModeIsAccepted() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(BoundedSession());
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(cut.Find(".comparison-model-difference").TextContent, Does.Contain("numeric comparison unavailable"));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });
        await cut.Find("button[title='Copy results']").ClickAsync(new());
        string copied = (string)context.JSInterop.Invocations["copyText"].Last().Arguments[0]!;
        Assert.That(copied, Does.Contain("Mode: Effects — fast").And.Contain("Previous result").And.Contain("contract v1"));
        Assert.That(copied, Does.Not.Contain("Mode: Effects — exact"));
    }

    [Test]
    public async Task LegacyRowsWithoutAccuracyMetadataCannotProduceNumericDeltas() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(SearchSession());
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        PinnedResultSnapshot accepted = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        PinnedResultSnapshot legacy = accepted with { Contract = null };
        Assert.That(legacy.ComparisonStatus(legacy), Does.Contain("Accuracy unavailable"));
        Assert.That(accepted.CompareComboPresentation(accepted.Combos[0], legacy, true).Difference, Is.Null);
    }
}
