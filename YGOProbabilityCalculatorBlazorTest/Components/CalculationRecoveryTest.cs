using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Components.Shared;
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
    public async Task TypedFailureRecoveryUsesExactSnapshotPreservesPreferenceAndPinsExecutedMode(ProbabilityCalculationLimitReason reason) {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPreDrawRemovalCount(1);
        session.ComboGroups.Add(new("g", "Group"));
        session.Combos[0].GroupId = "g";
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled(session);
        Task initial = Button(cut, "Calculate").ClickAsync(new());
        Complete(0);
        await initial;
        await Button(cut, "Pin result").ClickAsync(new());
        string oldValue = cut.Find(".probability-total-value").TextContent;
        Task failure = Button(cut, "Calculate").ClickAsync(new());
        _jobs[1].SetException(new ProbabilityCalculationLimitException(reason));
        await failure;
        Assert.That(cut.Find("[role='alert']").TextContent,
            Does.Contain(reason == ProbabilityCalculationLimitReason.Work ? "work allowance" : "storage safety limit").And.Contain("No probability was calculated"));
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(oldValue));
        Assert.That(cut.FindAll(".calculation-recovery p"), Is.Empty);
        string saved = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        Task recovery = Button(cut, "Calculate opening hand only").ClickAsync(new());
        CalculationInput input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_requests[2].Json)!;
        Assert.That(input.Cards.All(card => card.DrawCount is null && card.PreDrawRemovalCount == 0), Is.True);
        Complete(2);
        await recovery;
        List<Card> ordinary = session.Cards.Select(card => card.WithDrawCount(null)).ToList();
        double expected = SmallDeckOracle.EnumerateProbability(ordinary, session.Combos, session.HandSize);
        Assert.That(cut.Find(".probability-total-value").TextContent.Trim(), Is.EqualTo(expected.ToString("P2")));
        Assert.That(cut.FindAll(".probability-group"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(2));
        Assert.That(cut.Find(".result-model").TextContent, Does.Contain(CalculationModel.OpeningHand.Description()));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.ModelDescription, Is.EqualTo(CalculationModel.OpeningHand.Description()));
        Assert.That(cut.FindAll(".comparison-model-difference, .result-difference, .result-row-comparison, .pinned-match"), Is.Empty);
        Assert.That(cut.Find(".result-model").TextContent, Does.Contain(CalculationModel.OpeningHand.Description()));
        Assert.That(cut.Find(".pinned-result > p.small").TextContent, Does.Contain(CalculationModel.Fast.Description()));
        Assert.That(cut.FindComponent<SessionShareButton>().Instance.Snapshot, Is.EqualTo(saved));
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Fast"));
        await Button(cut, "Replace pin with current result").ClickAsync(new());
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.Context.Model, Is.EqualTo(CalculationModel.OpeningHand));
        Task normal = Button(cut, "Calculate").ClickAsync(new());
        Complete(3);
        await normal;
        Assert.That(cut.Find(".result-model").TextContent, Does.Contain("Effects — fast"));
    }

    [TestCase("complete")]
    [TestCase("cancel")]
    [TestCase("edit")]
    [TestCase("mode")]
    [TestCase("session")]
    [TestCase("dispose")]
    public async Task DelayedRecoveryActionOnlyBelongsToActiveRequest(string ending) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled();
        ManualTimeProvider clock = new();
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.CalculationTimeProvider, clock));
        Task active = Button(cut, "Calculate").ClickAsync(new());
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(999)));
        Assert.That(cut.FindAll(".calculation-elapsed"), Is.Empty);
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.That(cut.FindAll(".calculation-elapsed"), Is.Empty);
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(2)));
        cut.WaitForState(() => cut.FindAll(".calculation-recovery").Count == 1);
        if (ending == "cancel") {
            await Button(cut, "Cancel").ClickAsync(new());
        }
        else if (ending == "edit") {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (ending == "mode") {
            await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        }
        else if (ending == "session") {
            string source = context.Services.GetRequiredService<ISessionService>().SerializeSession(Session());
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(source, "replacement.json"));
            cut.WaitForState(() => _tokens[0].IsCancellationRequested);
        }
        else if (ending == "dispose") {
            await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
        }
        Complete(0);
        await active;
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(10)));
        Assert.That(clock.ActiveTimerCount, Is.Zero);
        if (ending != "dispose") {
            Assert.That(cut.FindAll(".calculation-elapsed, .calculation-recovery"), Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SlowRecoveryWaitsForCleanupAndRejectsLateFullCompletion(bool editWhileWaiting) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Controlled();
        ManualTimeProvider clock = new();
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.CalculationTimeProvider, clock));
        Task full = Button(cut, "Calculate").ClickAsync(new());
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(1)));
        Assert.That(cut.FindAll(".calculation-elapsed"), Is.Empty);
        await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(2)));
        cut.WaitForState(() => cut.FindAll(".calculation-recovery .btn-outline-primary").Count == 1);
        Task recovery = Button(cut, "Calculate opening hand only").ClickAsync(new());
        Assert.That(_tokens[0].IsCancellationRequested, Is.True);
        Assert.That(_jobs, Has.Count.EqualTo(1), "cleanup must finish before creating the alternate worker");
        if (editWhileWaiting) {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        _jobs[0].SetResult(new(.99, [new(0, "late full", .99)]));
        await full;
        if (editWhileWaiting) {
            await recovery;
            Assert.That(_jobs, Has.Count.EqualTo(1));
        }
        else {
            cut.WaitForState(() => _jobs.Count == 2);
            Complete(1);
            await recovery;
            Assert.That(cut.Find(".result-model").TextContent, Does.Contain(CalculationModel.OpeningHand.Description()));
        }
        Assert.That(cut.Markup, Does.Not.Contain("late full"));
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Fast"));
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
    private sealed class ManualTimeProvider : TimeProvider {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public int ActiveTimerCount => _timers.Count(timer => timer.IsActive);
        public int CallbackCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            ManualTimer timer = new(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan amount) {
            if (amount < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            _utcNow += amount;
            foreach (ManualTimer timer in _timers.ToArray()) {
                if (!timer.FireIfDue(_utcNow)) {
                    continue;
                }

                CallbackCount++;
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer {
            private DateTimeOffset? _dueAt;
            private TimeSpan _period;
            private bool _disposed;

            public bool IsActive => !_disposed && _dueAt.HasValue;

            public bool Change(TimeSpan dueTime, TimeSpan period) {
                if (_disposed) {
                    return false;
                }

                if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan) {
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                }

                if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan) {
                    throw new ArgumentOutOfRangeException(nameof(period));
                }

                _period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
                return true;
            }

            public void Dispose() {
                _disposed = true;
                _dueAt = null;
            }

            public ValueTask DisposeAsync() {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool FireIfDue(DateTimeOffset now) {
                if (!IsActive || _dueAt > now) {
                    return false;
                }

                if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero) {
                    _dueAt = null;
                }
                else {
                    _dueAt += _period;
                }

                callback(state);
                return true;
            }
        }
    }


}
