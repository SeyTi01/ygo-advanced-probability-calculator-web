using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class BackgroundCalculationComponentTest {
    private TestContext context = null!;
    private ControlledCalculator calculator = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> cut = null!;
    private Mock<ISessionService> sessions = null!;

    [SetUp]
    public void Setup() {
        context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        calculator = new();
        context.Services.AddSingleton<IBackgroundCalculator>(calculator);
        context.Services.AddSingleton(Mock.Of<IDeckImportService>());
        sessions = new();
        var codec = new SessionService(context.JSInterop.JSRuntime, new JsonSerializer());
        sessions.Setup(s => s.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        sessions.Setup(s => s.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        context.Services.AddSingleton(sessions.Object);
        context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        context.Services.AddSingleton<IPendingSessionService>(new PendingSessionService { PendingSession = new SessionState {
            Cards = [new([], 4, "A", id: "a"), new([], 4, "B", id: "b")],
            Combos = [new([], "Direct", cards: [new("a", 1, 5)])], HandSize = 2
        } });
        cut = context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    [TearDown]
    public void TearDown() { cut?.Dispose(); context.Dispose(); }

    private Task Start() => cut.Find(".calculate-action > button").ClickAsync(new());

    [TestCase(false)]
    [TestCase(true)]
    public async Task AdvancedDraftCancellationKeepsAcceptedResultPinAndRecoveryBytes(bool close) {
        var accepted = Start();
        calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await cut.Find(".pin-result-action").ClickAsync(new());
        var editor = cut.FindComponent<ComboEditor>();
        var codec = new SessionService(context.JSInterop.JSRuntime, new JsonSerializer());
        var before = codec.SerializeSession(new SessionState { Cards = editor.Instance.Cards.ToList(), Combos = [editor.Instance.Combo], HandSize = 2 });
        var recoveryWrites = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        var result = cut.Find(".probability-results").OuterHtml;
        var pin = cut.Find(".pinned-result").OuterHtml;
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find(".alternative-authoring > button").ClickAsync(new());
        if (close) {
            await editor.Find(".alternative-target-instruction button").ClickAsync(new());
        }
        else {
            await editor.Find("button[data-alternative-target='card:0']").ClickAsync(new());
            await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
            await editor.Find("#comboCard0").ChangeAsync(new() { Value = "b" });
            await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
            await editor.Find(".alternative-scope button").ClickAsync(new());
        }
        Assert.That(codec.SerializeSession(new SessionState { Cards = editor.Instance.Cards.ToList(), Combos = [editor.Instance.Combo], HandSize = 2 }), Is.EqualTo(before));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"].Count, Is.EqualTo(recoveryWrites));
        Assert.That(cut.Find(".probability-results").OuterHtml, Is.EqualTo(result));
        Assert.That(cut.Find(".pinned-result").OuterHtml, Is.EqualTo(pin));
        Assert.That(cut.Markup, Does.Not.Contain("Previous result").And.Not.Contain("Definition changed"));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task OrEditCancelsPendingRequestAndRejectsLateCompletion(bool error) {
        var accepted = Start();
        calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await cut.Find(".pin-result-action").ClickAsync(new());
        var pending = Start();
        Assert.That(calculator.Jobs, Has.Count.EqualTo(2));
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find(".alternative-authoring > button").ClickAsync(new());
        await editor.Find("button[data-alternative-target='card:0']").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = "b" });
        await editor.Find(".requirement-submit button").ClickAsync(new());
        Assert.That(calculator.Tokens[1].IsCancellationRequested, Is.True);
        if (error) calculator.Jobs[1].SetException(new InvalidOperationException("late OR error"));
        else calculator.Jobs[1].SetResult(Result("late OR success"));
        await pending;
        Assert.That(cut.Markup, Does.Contain("accepted").And.Not.Contain("late OR"));
        Assert.That(cut.FindAll(".pinned-result"), Has.Count.EqualTo(1));
        var next = Start();
        calculator.Jobs[2].SetResult(Result("new OR result"));
        await next;
        Assert.That(cut.Markup, Does.Contain("new OR result"));
    }
    private Task Cancel() => cut.Find(".calculate-action > button[aria-label='Cancel calculation']").ClickAsync(new());
    private Task SetHandSizeWithoutInvalidating(int value) => cut.InvokeAsync(() => {
        typeof(ProbabilityCalculatorComponent).GetField("handSize",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(cut.Instance, value);
        typeof(Microsoft.AspNetCore.Components.ComponentBase).GetMethod("StateHasChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(cut.Instance, null);
    });
    private static ProbabilityCalculationResult Result(string name) => new(0.5, [new(0, name, 0.5)]);

    [TestCase(false)]
    [TestCase(true)]
    public async Task OldCompletionCannotClearNewBusyStateOrReplaceNewResult(bool oldError) {
        var first = Start();
        await Cancel();
        Assert.That(calculator.Tokens[0].IsCancellationRequested, Is.True);
        var second = Start();
        if (oldError) calculator.Jobs[0].SetException(new InvalidOperationException("old failure"));
        else calculator.Jobs[0].SetResult(Result("old success"));
        await first;
        Assert.That(cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".calculate-action > button[aria-label='Cancel calculation']"), Has.Count.EqualTo(1));
        Assert.That(cut.Markup, Does.Not.Contain("old failure").And.Not.Contain("old success"));
        calculator.Jobs[1].SetResult(Result("new success"));
        await second;
        Assert.That(cut.Markup, Does.Contain("new success"));
        Assert.That(cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public async Task IdleInvalidInputsDisableCalculateButRunningActionRemainsUsableForCancellation() {
        await SetHandSizeWithoutInvalidating(0);
        Assert.That(cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.True);

        await SetHandSizeWithoutInvalidating(2);
        var calculation = Start();
        var runningButton = cut.Find(".calculate-action > button");
        Assert.That(runningButton.GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
        Assert.That(runningButton.GetAttribute("title"), Is.EqualTo("Cancel calculation"));
        Assert.That(runningButton.GetAttribute("type"), Is.EqualTo("button"));
        Assert.That(runningButton.GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(runningButton.HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));

        // Keep the controlled request active while validation changes, so the in-progress cancel action
        // must stay enabled even though the idle Calculate action would now be disabled.
        await SetHandSizeWithoutInvalidating(0);
        Assert.That(cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
        await Cancel();
        Assert.That(calculator.Jobs, Has.Count.EqualTo(1));
        Assert.That(calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.True);

        calculator.Jobs[0].SetCanceled();
        await calculation;
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Calculation was cancelled."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UserCancellationPreservesAcceptedResultAndDoesNotCreateCancellationNotice(bool stale) {
        var accepted = Start();
        calculator.Jobs[0].SetResult(Result("previous result"));
        await accepted;
        if (stale)
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });

        var wasStale = cut.FindAll(".probability-result-status").Count == 1;
        Assert.That(wasStale, Is.EqualTo(stale));
        var priorValue = cut.Find(".probability-total-value").TextContent;
        var cancellation = Start();

        Assert.That(cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
        await Cancel();
        Assert.That(calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(cut.Find(".calculate-action > button").TextContent, Does.Contain("Calculate"));
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(priorValue));
        Assert.That(cut.FindAll(".probability-result-status"), Has.Count.EqualTo(stale ? 1 : 0));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Calculation was cancelled.").And.Not.Contain("Calculation was canceled."));
        Assert.That(cut.FindAll("[role='status']").Any(status => status.TextContent.Contains("cancel", StringComparison.OrdinalIgnoreCase)), Is.False);

        // The worker may report cancellation after the user has already returned the control to idle.
        calculator.Jobs[1].SetCanceled();
        await cancellation;
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(priorValue));
        Assert.That(cut.FindAll(".probability-result-status"), Has.Count.EqualTo(stale ? 1 : 0));

        var next = Start();
        calculator.Jobs[2].SetResult(Result("after cancellation"));
        await next;
        Assert.That(cut.Markup, Does.Contain("after cancellation"));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task CalculationLimitErrorsRemainVisible() {
        var calculation = Start();
        var exception = new ProbabilityCalculationLimitException();
        calculator.Jobs[0].SetException(exception);
        await calculation;

        Assert.That(cut.Find("[role='alert']").TextContent, Does.Contain(exception.Message));
    }

    [Test]
    public async Task GenuineCalculationFailuresRemainVisible() {
        var calculation = Start();
        calculator.Jobs[0].SetException(new InvalidOperationException("worker transport failed"));
        await calculation;

        Assert.That(cut.Find("[role='alert']").TextContent, Does.Contain("Calculation failed: worker transport failed"));
    }

    [Test]
    public async Task DuplicateClickDoesNotStartAnotherJob() {
        var first = Start();
        await cut.InvokeAsync(() => cut.Instance.GetType().GetMethod("Calculate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(cut.Instance, null));
        Assert.That(calculator.Jobs, Has.Count.EqualTo(1));
        calculator.Jobs[0].SetResult(Result("done"));
        await first;
    }

    [Test]
    public async Task EditCancelsAndMarksPreviousResultsStaleThenNewJobSucceeds() {
        var first = Start(); calculator.Jobs[0].SetResult(Result("previous")); await first;
        var second = Start();
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        var third = Start();
        calculator.Jobs[2].SetResult(Result("current")); await third;
        calculator.Jobs[1].SetException(new ProbabilityCalculationLimitException()); await second;
        Assert.That(cut.Markup, Does.Contain("current").And.Not.Contain("Calculation stopped"));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposedComponentCancelsAndIgnoresLateSuccessAndError(bool error) {
        var first = Start();
        await cut.InvokeAsync(cut.Instance.Dispose);
        Assert.That(calculator.Tokens[0].IsCancellationRequested, Is.True);
        if (error) calculator.Jobs[0].SetException(new InvalidOperationException("late"));
        else calculator.Jobs[0].SetResult(Result("late"));
        await first;
    }

    [Test]
    public async Task AcceptingAnotherSessionCancelsThePreviousWorkspaceCalculation() {
        var replacement = new SessionState {
            Cards = [new([], 4, id: "new")], Combos = [new([], "New combo", cards: [new("new", 1, 2)])], HandSize = 2
        };
        var first = Start();
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(sessions.Object.SerializeSession(replacement), "session.json"));
        cut.WaitForState(() => calculator.Tokens[0].IsCancellationRequested);
        Assert.That(calculator.Tokens[0].IsCancellationRequested, Is.True);
        calculator.Jobs[0].SetResult(Result("old workspace"));
        await first;
        Assert.That(cut.Markup, Does.Contain("New combo").And.Not.Contain("old workspace"));
        var next = Start(); calculator.Jobs[1].SetResult(Result("new workspace")); await next;
        Assert.That(cut.Markup, Does.Contain("new workspace"));
    }

    [Test]
    public async Task RapidCancelRestartCancelsEveryPriorRequest() {
        var events = new List<Task>();
        for (var i = 0; i < 5; i++) { events.Add(Start()); await Cancel(); }
        var last = Start();
        for (var i = 0; i < 5; i++) {
            Assert.That(calculator.Tokens[i].IsCancellationRequested, Is.True);
            calculator.Jobs[i].SetResult(Result("old"));
        }
        await Task.WhenAll(events);
        Assert.That(cut.FindAll("[aria-label='Cancel calculation']"), Has.Count.EqualTo(1));
        calculator.Jobs[5].SetResult(Result("last")); await last;
        Assert.That(cut.Markup, Does.Contain("last"));
    }

    private sealed class ControlledCalculator : IBackgroundCalculator {
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
            var source = new TaskCompletionSource<ProbabilityCalculationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Jobs.Add(source); Tokens.Add(token);
            return source.Task; // Deliberately disobey cancellation to test ownership of late callbacks.
        }
    }
}
