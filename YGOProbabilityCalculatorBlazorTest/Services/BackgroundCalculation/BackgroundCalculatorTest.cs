using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public class BackgroundCalculatorTest {
    private Mock<IJSRuntime> _js = null!;
    private Mock<IJSObjectReference> _module = null!;
    private Mock<IJSObjectReference> _job = null!;

    private static CalculationSnapshot Snapshot() => CalculationSnapshot.Capture(
        [new Card([], 2, id: "card")], [new Combo([], cards: [new("card", 1, 1)])], 1, []);

    [SetUp]
    public void Setup() {
        _js = new();
        _module = new();
        _job = new();

        _js.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]?>())).ReturnsAsync(_module.Object);
        _module.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>())).ReturnsAsync(_job.Object);
    }

    [Test]
    public async Task SuccessPassesTheSnapshotThroughAndDisposesBothHandles() {
        CalculationSnapshot snapshot = Snapshot();
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(snapshot.Json));
        ProbabilityCalculationResult result = await new BackgroundCalculator(_js.Object).CalculateAsync(snapshot, default);
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(1));
        _job.Verify(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(),
            It.Is<object?[]?>(a => (string)a![0]! == snapshot.Json)), Times.Once);
        VerifyDisposed();
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

        Assert.ThrowsAsync<JSException>(() => new BackgroundCalculator(_js.Object).CalculateAsync(Snapshot(), default));

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
        Task<ProbabilityCalculationResult> task = new BackgroundCalculator(_js.Object).CalculateAsync(Snapshot(), cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(_module.Object);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        _module.Verify(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()), Times.Never);
        _module.Verify(j => j.DisposeAsync(), Times.Once);
        await Task.CompletedTask;
    }

    [Test]
    public void CancellationDuringCreationDisposesTheLateWorkerHandle() {
        using CancellationTokenSource cancellation = new();
        _module.Setup(j => j.InvokeAsync<IJSObjectReference>("createJob", It.IsAny<object?[]?>()))
            .Returns(() => {
                cancellation.Cancel();

                return ValueTask.FromResult(_job.Object);
            });
        Assert.ThrowsAsync<OperationCanceledException>(() => new BackgroundCalculator(_js.Object).CalculateAsync(Snapshot(), cancellation.Token));
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
        BackgroundCalculator service = new(_js.Object);
        Task<ProbabilityCalculationResult> task = service.CalculateAsync(Snapshot(), cancellation.Token);
        cancellation.Cancel();
        Assert.That(task.IsCompleted, Is.False);
        termination.SetResult();
        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        _job.Setup(j => j.InvokeAsync<string>("run", It.IsAny<CancellationToken>(), It.IsAny<object?[]?>()))
            .ReturnsAsync(CalculationWire.Execute(Snapshot().Json));
        Assert.That((await service.CalculateAsync(Snapshot(), default)).ComboProbabilities, Has.Count.EqualTo(1));
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
