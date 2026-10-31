using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public class BackgroundCalculatorTest {
    private Mock<IJSRuntime> _js = null!;
    private Mock<IJSObjectReference> _module = null!;
    private Mock<IJSObjectReference> _job = null!;
    private Mock<IJSObjectReference> _owner = null!;
    private BackgroundCalculator _service = null!;

    private static CalculationSnapshot Snapshot() => CalculationSnapshot.Capture(
        [new Card([], 2, id: "card")], [new Combo([], cards: [new("card", 1, 1)])], 1, []);

    private static CalculationSnapshot BoundedSnapshot() => CalculationSnapshot.Capture(
        ProbabilityCalculator.ThreeCalculationModesTest.Counterexample("banish"),
        ProbabilityCalculator.ThreeCalculationModesTest.Combos, 2, ProbabilityCalculator.ThreeCalculationModesTest.Groups);

    [Test]
    public async Task FastOnlyUsesOneOrdinaryFastRequestAndKeepsItsBounds() {
        CalculationSnapshot snapshot = BoundedSnapshot() with { AllowAutomaticExact = false };
        string response = CalculationWire.Execute(snapshot.Json);
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>())).ReturnsAsync(response);
        ProbabilityCalculationResult result = await _service.CalculateProgressivelyAsync(snapshot,
            _ => throw new AssertionException("Fast only cannot publish preliminary results."), default);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        Assert.That(result.Contract.AutomaticExact, Is.Null);
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(),
            It.Is<object?[]?>(args => args!.Length == 3 && Equals(args[0], snapshot.Json) && Equals(args[1], false) && Equals(args[2], false))), Times.Once);
        _job.Verify(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
        _job.Verify(j => j.InvokeAsync<string>("finish", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Once);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    [TestCase(CalculationModel.OpeningHand)]
    public async Task ProvenAndExplicitModesNeverPublishProgress(CalculationModel mode) {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(
            [new Card([], 2, id: "card")], [new Combo([], cards: [new("card", 1, 1)])], 1, [], options: new(Mode: mode));
        string response = CalculationWire.Execute(snapshot.Json);
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(response, false));
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>())).ReturnsAsync(response);
        int calls = 0;
        ProbabilityCalculationResult result = await _service.CalculateProgressivelyAsync(snapshot, _ => {
            calls++;
            return Task.CompletedTask;
        }, default);
        Assert.That(calls, Is.Zero);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        _job.Verify(j => j.InvokeAsync<string>("finish", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Once);
    }

    [Test]
    public void ContradictoryProgressCannotPresentAnAlreadyExactResult() {
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(CalculationWire.Execute(Snapshot().Json), true));
        CalculationProtocolException exception = Assert.ThrowsAsync<CalculationProtocolException>(() => _service.CalculateProgressivelyAsync(Snapshot(),
            _ => throw new AssertionException("Contradictory progress must not be presented"), default))!;
        Assert.That(exception.Reason, Is.EqualTo(CalculationProtocolFailureReason.InvalidPreliminaryResult));
        VerifyDisposed();
    }

    [Test]
    public async Task DisposalDuringProgressCallbackPreventsFinalInteropAndRetiresLease() {
        CalculationSnapshot snapshot = BoundedSnapshot();
        TaskCompletionSource callback = new();
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(CalculationWire.Execute(snapshot.Json), true));
        Task<ProbabilityCalculationResult> calculation = _service.CalculateProgressivelyAsync(snapshot, _ => callback.Task, default);
        await _service.DisposeAsync();
        Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        _job.Verify(j => j.InvokeAsync<string>("finish", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
        VerifyDisposed();
        callback.SetResult();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProgressiveDeliveryValidatesBeforeCallbackAndReleasesOnlyAfterFinalValidation(bool uncertified) {
        CalculationSnapshot snapshot = BoundedSnapshot();
        string initial = CalculationWire.Execute(snapshot.Json);
        if (uncertified) {
            List<Card> cards = ProbabilityCalculator.ThreeCalculationModesTest.Counterexample("banish");
            WorkBudget budget = new(CalculationWorkPolicy.Default);
            ExactCalculationResult rows = new DrawEffectCalculation(cards, 2, budget).Calculate(
                ProbabilityCalculator.ThreeCalculationModesTest.Combos, ProbabilityCalculator.ThreeCalculationModesTest.Groups);
            // The real result producer retains a completed primary when its
            // secondary proof fails, just as in ThreeCalculationModesTest.
            ProbabilityCalculationResult result = ProbabilityCalculatorService.CompleteResult(
                ProbabilityCalculator.ThreeCalculationModesTest.Combos, ProbabilityCalculator.ThreeCalculationModesTest.Groups,
                CalculationModel.Fast, rows, budget, new DrawEffectCalculation(cards, 2, new(CalculationWorkPolicy.Default)));
            initial = System.Text.Json.JsonSerializer.Serialize(new CalculationResponse(result, null), CalculationJsonContext.Default.CalculationResponse);
        }
        TaskCompletionSource<string> final = new();
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(initial, true));
        _job.Setup(j => j.InvokeAsync<string>("finish", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .Returns(new ValueTask<string>(final.Task));
        ProbabilityCalculationResult? shown = null;
        Task<ProbabilityCalculationResult> task = _service.CalculateProgressivelyAsync(snapshot, result => {
            shown = result;
            return Task.CompletedTask;
        }, default);
        Assert.That(shown!.Contract!.Accuracy, Is.EqualTo(uncertified ? CalculationAccuracy.Uncertified : CalculationAccuracy.Bounded));
        Assert.That(task.IsCompleted, Is.False);
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Never);
        System.Text.Json.Nodes.JsonNode fallback = System.Text.Json.Nodes.JsonNode.Parse(initial)!;
        fallback["Result"]!["Contract"]!["AutomaticExact"] = 1;
        final.SetResult(fallback.ToJsonString());
        ProbabilityCalculationResult settled = await task;
        Assert.That(settled.Contract!.AutomaticExact, Is.EqualTo(AutomaticExactOutcome.Deadline));
        Assert.That(settled.Contract.Fractions, Is.EqualTo(shown.Contract.Fractions).Using<CalculationFractions>((a, b) =>
            System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b)));
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Once);
    }

    [TestCase("{}")]
    [TestCase("not json")]
    public void InvalidProgressNeverReachesTheCallbackOrAcknowledgesLease(string response) {
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(response, true));
        int calls = 0;
        Assert.That(async () => await _service.CalculateProgressivelyAsync(BoundedSnapshot(), _ => {
            calls++;
            return Task.CompletedTask;
        }, default), Throws.Exception);
        Assert.That(calls, Is.Zero);
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidFinalOrCancelledProgressRetiresLease(bool cancel) {
        CalculationSnapshot snapshot = BoundedSnapshot();
        using CancellationTokenSource cancellation = new();
        _job.Setup(j => j.InvokeAsync<BackgroundCalculator.PresentationStage>("runProgressive", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(new BackgroundCalculator.PresentationStage(CalculationWire.Execute(snapshot.Json), true));
        _job.Setup(j => j.InvokeAsync<string>("finish", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>())).ReturnsAsync("{}");
        int calls = 0;
        Assert.That(async () => await _service.CalculateProgressivelyAsync(snapshot, _ => {
            calls++;
            if (cancel) { cancellation.Cancel(); }
            return Task.CompletedTask;
        }, cancellation.Token), Throws.Exception);
        Assert.That(calls, Is.EqualTo(1));
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Never);
    }

    [Test]
    public async Task NewRequestWaitsForCancelledLeaseCleanup() {
        TaskCompletionSource<Microsoft.JSInterop.Infrastructure.IJSVoidResult> cleanup = new();
        _job.SetupSequence(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ThrowsAsync(new OperationCanceledException()).ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        _job.Setup(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()))
            .Returns(new ValueTask<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(cleanup.Task));
        Task<ProbabilityCalculationResult> first = _service.CalculateAsync(Snapshot(), default);
        Task<ProbabilityCalculationResult> second = _service.CalculateAsync(Snapshot(), default);
        Assert.That(second.IsCompleted, Is.False);
        _owner.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Once);
        cleanup.SetResult(default!);
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await second).TotalProbability, Is.EqualTo(1));
    }

    [SetUp]
    public void Setup() {
        _js = new();
        _module = new();
        _job = new();
        _owner = new();
        _service = new(_js.Object);

        _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>())).ReturnsAsync(_module.Object);
        _module.Setup(j => j.InvokeAsync<IJSObjectReference>("createOwner", It.IsAny<object?[]?>())).ReturnsAsync(_owner.Object);
        _owner.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>())).ReturnsAsync(_job.Object);
    }

    [TearDown]
    public async Task TearDown() => await _service.DisposeAsync();

    [Test]
    public async Task SuccessPassesTheSnapshotThroughReleasesItsLeaseAndDisposesTemporaryHandles() {
        CalculationSnapshot snapshot = Snapshot();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(snapshot.Json));
        ProbabilityCalculationResult result = await _service.CalculateAsync(snapshot, default);
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(1));
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(),
            It.Is<object?[]?>(a => (string)a![0]! == snapshot.Json)), Times.Once);
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Once);
        _job.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Never);
        _job.Verify(j => j.DisposeAsync(), Times.Once);
        _module.Verify(j => j.DisposeAsync(), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), Times.Never);
    }

    [TestCase(CalculationModel.Fast, true)]
    [TestCase(CalculationModel.Exact, false)]
    [TestCase(CalculationModel.OpeningHand, false)]
    public async Task OnlyFastSelectsTheAutomaticDeadline(CalculationModel mode, bool automatic) {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([], 2, id: "a")],
            [new([], cards: [new("a", 1, 1)])], 1, [], options: new(Mode: mode));
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(snapshot.Json));
        await _service.CalculateAsync(snapshot, default);
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(),
            It.Is<object?[]?>(args => args!.Length == 3 && (bool)args[1]! == automatic && (bool)args[2]! == (mode == CalculationModel.Exact))), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InitializationOrTransportFailurePropagates(bool importing) {
        if (importing) {
            _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()))
                .ThrowsAsync(new JSException("startup failed"));
        } else {
            _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
                .ThrowsAsync(new JSException("transport failed"));
        }

        Assert.ThrowsAsync<JSException>(() => _service.CalculateAsync(Snapshot(), default));

        if (!importing) {
            VerifyDisposed();
        }
    }

    [Test]
    public async Task CancellationDuringImportDoesNotCreateAWorker() {
        TaskCompletionSource<IJSObjectReference> completion = new();
        _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(completion.Task));
        using CancellationTokenSource cancellation = new();
        Task<ProbabilityCalculationResult> task = _service.CalculateAsync(Snapshot(), cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(_module.Object);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        _owner.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Never);
        _module.Verify(j => j.DisposeAsync(), Times.Once);
        await Task.CompletedTask;
    }

    [Test]
    public void CancellationDuringCreationDisposesTheLateWorkerHandle() {
        using CancellationTokenSource cancellation = new();
        _owner.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()))
            .Returns(() => {
                cancellation.Cancel();

                return ValueTask.FromResult(_job.Object);
            });
        Assert.ThrowsAsync<OperationCanceledException>(() => _service.CalculateAsync(Snapshot(), cancellation.Token));
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
    }

    [Test]
    public async Task CancellationDuringRunWaitsForActualTerminationBeforeSettlingAndAllowsRestart() {
        TaskCompletionSource termination = new();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .Returns((string _, CancellationToken token, object?[]? _) => new ValueTask<string>(WaitForCancellation(token)));
        _job.Setup(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()))
            .Returns(new ValueTask<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(FinishTermination()));
        async Task<Microsoft.JSInterop.Infrastructure.IJSVoidResult> FinishTermination() {
            await termination.Task;
            return default!;
        }
        using CancellationTokenSource cancellation = new();
        BackgroundCalculator service = _service;
        Task<ProbabilityCalculationResult> task = service.CalculateAsync(Snapshot(), cancellation.Token);
        cancellation.Cancel();
        Assert.That(task.IsCompleted, Is.False);
        termination.SetResult();
        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        Assert.That((await service.CalculateAsync(Snapshot(), default)).ComboProbabilities, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SequentialRequestsShareOwnerButKeepSnapshotsResultsAndHandlesSeparate() {
        List<string> inputs = [];
        List<Mock<IJSObjectReference>> jobs = [];
        _owner.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()))
            .Returns(() => {
                Mock<IJSObjectReference> job = new();
                job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
                    .Returns((string _, CancellationToken _, object?[]? args) => {
                        string input = (string)args![0]!;
                        inputs.Add(input);

                        return ValueTask.FromResult(CalculationWire.Execute(input));
                    });
                jobs.Add(job);

                return ValueTask.FromResult(job.Object);
            });
        CalculationSnapshot first = Snapshot();
        CalculationSnapshot second = CalculationSnapshot.Capture(
            [new Card([], 1, id: "target"), new Card([], 3, id: "blank")],
            [new Combo([], "Changed", cards: [new("target", 1, 1)])], 1, []);
        ProbabilityCalculationResult a = await _service.CalculateAsync(first, default);
        ProbabilityCalculationResult b = await _service.CalculateAsync(second, default);
        Assert.Multiple(() => {
            Assert.That(inputs, Is.EqualTo(new[] { first.Json, second.Json }));
            Assert.That(a.TotalProbability, Is.EqualTo(1));
            Assert.That(b.TotalProbability, Is.EqualTo(0.25));
            Assert.That(a.ComboProbabilities, Is.Not.SameAs(b.ComboProbabilities));
        });
        _js.Verify(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()), Times.Once);
        _module.Verify(j => j.InvokeAsync<IJSObjectReference>("createOwner", It.IsAny<object?[]?>()), Times.Once);
        foreach (Mock<IJSObjectReference> job in jobs) {
            job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Once);
            job.Verify(j => j.DisposeAsync(), Times.Once);
        }
        await _service.DisposeAsync();
        await _service.DisposeAsync();
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), Times.Once);
        Assert.ThrowsAsync<ObjectDisposedException>(() => _service.CalculateAsync(first, default));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedInitializationCanBeRetried(bool creating) {
        if (creating) {
            _module.SetupSequence(j => j.InvokeAsync<IJSObjectReference>("createOwner", It.IsAny<object?[]?>()))
                .ThrowsAsync(new JSException("creation failed")).ReturnsAsync(_owner.Object);
        } else {
            _js.SetupSequence(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()))
                .ThrowsAsync(new JSException("import failed")).ReturnsAsync(_module.Object);
        }
        Assert.ThrowsAsync<JSException>(() => _service.CalculateAsync(Snapshot(), default));
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        Assert.That((await _service.CalculateAsync(Snapshot(), default)).TotalProbability, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposalDuringInitializationCleansLateOwnerAndNeverStartsWorker(bool creating) {
        TaskCompletionSource<IJSObjectReference> pending = new();
        if (creating) {
            _module.Setup(j => j.InvokeAsync<IJSObjectReference>("createOwner", It.IsAny<object?[]?>()))
                .Returns(new ValueTask<IJSObjectReference>(pending.Task));
        } else {
            _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()))
                .Returns(new ValueTask<IJSObjectReference>(pending.Task));
        }
        Task<ProbabilityCalculationResult> calculation = _service.CalculateAsync(Snapshot(), default);
        Task disposal = _service.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        pending.SetResult(creating ? _owner.Object : _module.Object);
        Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        await disposal;
        _owner.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Never);
        _module.Verify(j => j.DisposeAsync(), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), creating ? Times.Once() : Times.Never());
    }

    [Test]
    public async Task ModuleHandleCleanupFailureDisposesUnreturnedOwnerAndAllowsRetry() {
        _module.SetupSequence(j => j.DisposeAsync())
            .ThrowsAsync(new JSException("module cleanup failed")).Returns(ValueTask.CompletedTask);
        Assert.ThrowsAsync<JSException>(() => _service.CalculateAsync(Snapshot(), default));
        _owner.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Never);
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), Times.Once);

        Mock<IJSObjectReference> replacement = new();
        replacement.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>())).ReturnsAsync(_job.Object);
        _module.Setup(j => j.InvokeAsync<IJSObjectReference>("createOwner", It.IsAny<object?[]?>())).ReturnsAsync(replacement.Object);
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        Assert.That((await _service.CalculateAsync(Snapshot(), default)).TotalProbability, Is.EqualTo(1));
        await _service.DisposeAsync();
        replacement.Verify(j => j.DisposeAsync(), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task OwnerDisposalCancelsActiveRunAndTerminatesIdleOrBusyRuntime() {
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .Returns((string _, CancellationToken token, object?[]? _) => new ValueTask<string>(WaitForCancellation(token)));
        Task<ProbabilityCalculationResult> calculation = _service.CalculateAsync(Snapshot(), default);
        await _service.DisposeAsync();
        Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        _owner.Verify(j => j.DisposeAsync(), Times.Once);
        VerifyDisposed();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationAtResponseOrIdleHandoffDoesNotRetainTheWorker(bool handoff) {
        using CancellationTokenSource cancellation = new();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .Returns(() => {
                if (!handoff) {
                    cancellation.Cancel();
                }

                return ValueTask.FromResult(CalculationWire.Execute(Snapshot().Json));
            });
        if (handoff) {
            _job.Setup(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()))
                .Callback(() => cancellation.Cancel());
        }
        Assert.ThrowsAsync<OperationCanceledException>(() => _service.CalculateAsync(Snapshot(), cancellation.Token));
        if (handoff) {
            _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("discard", It.IsAny<object?[]?>()), Times.Once);
            _job.Verify(j => j.DisposeAsync(), Times.Once);
        } else {
            VerifyDisposed();
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void TypedLimitsRemainDistinctAndRetireWorker(int reason) {
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync($"{{\"Result\":null,\"Error\":\"limit\",\"LimitReason\":{reason}}}");
        ProbabilityCalculationLimitException error = Assert.ThrowsAsync<ProbabilityCalculationLimitException>(
            () => _service.CalculateAsync(Snapshot(), default))!;
        Assert.That(error.Reason, Is.EqualTo((ProbabilityCalculationLimitReason)reason));
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Never);
    }

    [TestCase("not json", typeof(System.Text.Json.JsonException))]
    [TestCase("{}", typeof(CalculationProtocolException))]
    [TestCase("{\"Error\":\"bad input\",\"FailureKind\":1}", typeof(ArgumentException))]
    [TestCase("{\"Error\":\"engine error\",\"FailureKind\":2}", typeof(InvalidOperationException))]
    public void DecodeAndValidWireFailuresRetireWorker(string response, Type exceptionType) {
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>())).ReturnsAsync(response);
        Assert.ThrowsAsync(exceptionType, () => _service.CalculateAsync(Snapshot(), default));
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>()), Times.Never);
    }

    [Test]
    public async Task DisposalDuringJobCreationCleansLateHandleWithoutRunningIt() {
        TaskCompletionSource<IJSObjectReference> pending = new();
        _owner.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(pending.Task));
        Task<ProbabilityCalculationResult> calculation = _service.CalculateAsync(Snapshot(), default);
        await _service.DisposeAsync();
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        pending.SetResult(_job.Object);
        Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        VerifyDisposed();
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()), Times.Never);
    }

    [Test]
    public async Task CancelledImporterDoesNotPoisonNextRequestSharingInitialization() {
        TaskCompletionSource<IJSObjectReference> pending = new();
        _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(pending.Task));
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        using CancellationTokenSource cancellation = new();
        Task<ProbabilityCalculationResult> old = _service.CalculateAsync(Snapshot(), cancellation.Token);
        cancellation.Cancel();
        Task<ProbabilityCalculationResult> next = _service.CalculateAsync(Snapshot(), default);
        pending.SetResult(_module.Object);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await old);
        Assert.That((await next).TotalProbability, Is.EqualTo(1));
        _owner.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LateHandleCleanupRevokesReleasedLeaseOnCancellationOrFailure(bool failure) {
        TaskCompletionSource disposed = new();
        using CancellationTokenSource cancellation = new();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        _job.Setup(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>())).ReturnsAsync(7L);
        _job.Setup(j => j.DisposeAsync()).Returns(new ValueTask(disposed.Task));
        Task<ProbabilityCalculationResult> calculation = _service.CalculateAsync(Snapshot(), cancellation.Token);
        if (failure) {
            disposed.SetException(new JSException("handle cleanup failed"));
            Assert.ThrowsAsync<JSException>(async () => await calculation);
        } else {
            cancellation.Cancel();
            disposed.SetResult();
            Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        }
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("discard",
            It.Is<object?[]?>(args => (long)args![0]! == 7L)), Times.Once);
        await _service.DisposeAsync();
    }

    [Test]
    public async Task OwnerDisposalDuringLateJobCleanupDoesNotReuseDisposedOwnerHandle() {
        TaskCompletionSource disposed = new();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        _job.Setup(j => j.InvokeAsync<long>("release", It.IsAny<object?[]?>())).ReturnsAsync(7L);
        _job.Setup(j => j.DisposeAsync()).Returns(new ValueTask(disposed.Task));
        Task<ProbabilityCalculationResult> calculation = _service.CalculateAsync(Snapshot(), default);
        await _service.DisposeAsync();
        disposed.SetResult();
        Assert.That(async () => await calculation, Throws.InstanceOf<OperationCanceledException>());
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        _owner.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("discard", It.IsAny<object?[]?>()), Times.Never);
    }

    private static async Task<string> WaitForCancellation(CancellationToken token) {
        await Task.Delay(Timeout.Infinite, token);
        return "unreachable";
    }

    private void VerifyDisposed() {
        _job.Verify(j => j.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("dispose", It.IsAny<object?[]?>()), Times.Once);
        _job.Verify(j => j.DisposeAsync(), Times.Once);
        _module.Verify(j => j.DisposeAsync(), Times.Once);
    }
}
