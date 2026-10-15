using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class SearchAvailabilityComponentTest : CalculatorEditorTestBase {
    private static SessionState SearchSession() {
        CategoryBase target = new("Target");
        return new() { Cards = [new([], 1, id: "s", search: new(CardSelector.For(target), false)), new([target], 1, id: "t"), new([], 3, id: "f")],
            HandSize = 2, Categories = [target], Combos = [new([new(target, 1, 2)], "Target", groupId: "g")], ComboGroups = [new("g", "Routes")] };
    }

    [Test]
    public async Task OptionalDisplayCopyPinAndReloadOwnTheAcceptedCertificate() {
        SessionState session = SearchSession();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        string saved = context.Services.GetRequiredService<ISessionService>().SerializeSession(session);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".search-availability"), Is.Empty);
        await cut.Find(".calculation-settings-menu input[type=checkbox]").ChangeAsync(new() { Value = true });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".search-availability summary").TextContent, Does.Contain("Exact with respect"));
        ProbabilityCalculationResult accepted = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        string copied = ProbabilityResultSummaryFormatter.Format(accepted, 2, CultureInfo.InvariantCulture);
        Assert.That(copied, Does.Contain("fixed modeled draw/removal policy (v1)").And.Contain("Routes:").And.Contain("Target:"));
        await Button(cut, "Pin result").ClickAsync(new());
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.SearchAvailability, Is.SameAs(accepted.SearchAvailability));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.SearchAvailability, Is.SameAs(accepted.SearchAvailability));
        await cut.Find(".calculation-settings-menu input[type=checkbox]").ChangeAsync(new() { Value = false });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results .search-availability"), Is.Empty);
        Assert.That(cut.FindAll(".pinned-result .search-availability"), Has.Count.EqualTo(1));
        for (int load = 0; load < 2; load++) {
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(saved, "certificate-session.json"));
            cut.WaitForAssertion(() => Assert.That(cut.FindAll(".probability-results"), Is.Empty));
        }
        Assert.That(saved, Does.Not.Contain("CertifySearchAvailability").And.Not.Contain("SearchAvailability"));
    }

    [TestCase("edit")]
    [TestCase("cancel")]
    [TestCase("opening")]
    public async Task LateCertifiedResponseCannotReplaceNewerOwnership(string action) {
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
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(SearchSession());
        await cut.Find(".calculation-settings-menu input[type=checkbox]").ChangeAsync(new() { Value = true });
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
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.SearchAvailability!.Status,
            Is.EqualTo(action == "opening" ? SearchAvailabilityStatus.NotApplicable : SearchAvailabilityStatus.Exact));
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
}
