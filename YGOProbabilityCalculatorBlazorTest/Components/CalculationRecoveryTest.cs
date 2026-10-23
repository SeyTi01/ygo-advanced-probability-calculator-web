using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculationRecoveryTest : CalculatorEditorTestBase {
    private readonly List<CalculationSnapshot> _requests = [];
    private readonly List<TaskCompletionSource<ProbabilityCalculationResult>> _jobs = [];
    private readonly List<CancellationToken> _tokens = [];

    private IRenderedComponent<ProbabilityCalculatorComponent> Controlled(SessionState? session = null) {
        Mock<IBackgroundCalculator> worker = new();
        worker.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken token) => {
                TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _requests.Add(snapshot);
                _tokens.Add(token);
                _jobs.Add(completion);
                return completion.Task;
            });
        context.Services.AddSingleton(worker.Object);
        return Render(session ?? Session());
    }

    [SetUp]
    public void ResetRequests() {
        _requests.Clear();
        _jobs.Clear();
        _tokens.Clear();
    }

    private void Complete(int index) => _jobs[index].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_requests[index].Json)));

    [TestCase(ProbabilityCalculationLimitReason.Work)]
    [TestCase(ProbabilityCalculationLimitReason.Storage)]
    public async Task TypedFailureKeepsAcceptedResultAndPinWithoutRecoverySolicitation(ProbabilityCalculationLimitReason reason) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled();
        Task initial = Button(cut, "Calculate").ClickAsync(new());
        Complete(0);
        await initial;
        await Button(cut, "Pin result").ClickAsync(new());
        ProbabilityCalculationResult accepted = cut.FindComponent<ProbabilityResultExport>().Instance.Result!;
        PinnedResultSnapshot pinned = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Task failure = Button(cut, "Calculate").ClickAsync(new());
        _jobs[1].SetException(new ProbabilityCalculationLimitException(reason));
        await failure;
        Assert.That(cut.Find("[role='alert']").TextContent,
            Does.Contain(reason == ProbabilityCalculationLimitReason.Work ? "work allowance" : "storage safety limit").And.Contain("No probability was calculated"));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result, Is.SameAs(accepted));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pinned));
    }

    [Test]
    public async Task PendingCancellationAndManualModeSelectionPreserveConfiguredEffects() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPreDrawRemovalCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled(session);
        Task active = Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Cancel").ClickAsync(new());
        Assert.That(_tokens[0].IsCancellationRequested, Is.True);
        _jobs[0].SetCanceled();
        await active;

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Task manual = Button(cut, "Calculate").ClickAsync(new());
        CalculationInput input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_requests[1].Json)!;
        Assert.That(_requests[1].RequestedMode, Is.EqualTo(CalculationModel.OpeningHand));
        Assert.That(input.Cards.All(card => card.DrawCount is null && card.PreDrawRemovalCount == 0), Is.True);
        Complete(1);
        await manual;
        double expected = SmallDeckOracle.EnumerateProbability(session.Cards.Select(card => card.WithDrawCount(null)).ToList(), session.Combos, session.HandSize);
        Assert.That(cut.Find(".probability-total-value").TextContent.Trim(), Is.EqualTo(expected.ToString("P2")));
        Assert.That(cut.Find(".result-accuracy").TextContent, Is.EqualTo("Mode: No effect simulation"));
    }

    [Test]
    public async Task TransportFailureHasNoModelRecoveryAction() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled();
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        _jobs[0].SetException(new Microsoft.JSInterop.JSException("worker startup failed"));
        await calculation;
        Assert.That(cut.Find("[role='alert']").TextContent, Does.Contain("worker startup failed"));
        Assert.That(cut.FindAll(".calculation-recovery"), Is.Empty);
    }

    [Test]
    public async Task ComplexityWarningReassessesStructuralEditsWithoutStartingWorkers() {
        Mock<IProbabilityCalculatorService> compiler = new(MockBehavior.Strict);
        context.Services.AddSingleton(compiler.Object);
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        session.Cards[session.Cards.IndexOf(graceful)] = new([], 1, "Pot", id: graceful.Id, drawCount: 2);
        session.Cards.Add(new([], 1, "Allure", id: "allure", drawCount: 2, postDrawDiscardCount: 1));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled(session);
        Assert.That(cut.FindAll(".draw-complexity-notice"), Has.Count.EqualTo(1));
        Assert.That(_jobs, Is.Empty);
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Fast" });
        Assert.That(cut.FindAll(".draw-complexity-notice"), Has.Count.EqualTo(1));
        Assert.That(_jobs, Is.Empty);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(cut.FindAll(".draw-complexity-notice"), Has.Count.EqualTo(1));
        await cut.Find("#cardName0").InputAsync(new() { Value = "Renamed only" });
        for (int index = 0; index < 5; index++) {
            await SetActiveAsync(cut, $"#comboActive{index}", false);
        }
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        compiler.VerifyNoOtherCalls();
        Assert.That(_jobs, Is.Empty);
    }

}
