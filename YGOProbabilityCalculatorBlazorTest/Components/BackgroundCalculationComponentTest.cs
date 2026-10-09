using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
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
    private TestContext _context = null!;
    private ControlledCalculator _calculator = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> _cut = null!;
    private Mock<ISessionService> _sessions = null!;

    [SetUp]
    public void Setup() {
        _context = new();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _calculator = new();
        _context.Services.AddSingleton<IBackgroundCalculator>(_calculator);
        _context.Services.AddSingleton(Mock.Of<IDeckImportService>());
        _sessions = new();
        SessionService codec = new(_context.JSInterop.JSRuntime, new JsonSerializer());
        _sessions.Setup(s => s.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        _sessions.Setup(s => s.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        _context.Services.AddSingleton(_sessions.Object);
        _context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        SessionState initialSession = new() {
            Cards = [new([], 4, "A", id: "a"), new([], 4, "B", id: "b")],
            Combos = [new([], "Direct", cards: [new("a", 1, 5)])],
            HandSize = 2
        };
        _context.Services.AddSingleton<IPendingSessionService>(new PendingSessionService { PendingSession = initialSession });
        _cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    [TearDown]
    public void TearDown() {
        _cut?.Dispose();
        _context.Dispose();
    }

    private Task Start() => _cut.Find(".calculate-action > button").ClickAsync(new());

    [TestCase(false)]
    [TestCase(true)]
    public async Task AdvancedDraftCancellationKeepsAcceptedResultPinAndRecoveryBytes(bool close) {
        Task accepted = Start();
        _calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await _cut.Find(".pin-result-action").ClickAsync(new());
        IRenderedComponent<ComboEditor> editor = _cut.FindComponent<ComboEditor>();
        SessionService codec = new(_context.JSInterop.JSRuntime, new JsonSerializer());
        string before = codec.SerializeSession(new SessionState { Cards = editor.Instance.Cards.ToList(), Combos = [editor.Instance.Combo], HandSize = 2 });
        int recoveryWrites = _context.JSInterop.Invocations["sessionRecovery.update"].Count;
        string result = _cut.Find(".probability-results").OuterHtml;
        string pin = _cut.Find(".pinned-result").OuterHtml;
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
        string serializedSession = codec.SerializeSession(new SessionState { Cards = editor.Instance.Cards.ToList(), Combos = [editor.Instance.Combo], HandSize = 2 });
        Assert.That(serializedSession, Is.EqualTo(before));
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.update"].Count, Is.EqualTo(recoveryWrites));
        Assert.That(_cut.Find(".probability-results").OuterHtml, Is.EqualTo(result));
        Assert.That(_cut.Find(".pinned-result").OuterHtml, Is.EqualTo(pin));
        Assert.That(_cut.Markup, Does.Not.Contain("Previous result").And.Not.Contain("Definition changed"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrEditCancelsPendingRequestAndRejectsLateCompletion(bool error) {
        Task accepted = Start();
        _calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await _cut.Find(".pin-result-action").ClickAsync(new());
        Task pending = Start();
        Assert.That(_calculator.Jobs, Has.Count.EqualTo(2));
        IRenderedComponent<ComboEditor> editor = _cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find(".alternative-authoring > button").ClickAsync(new());
        await editor.Find("button[data-alternative-target='card:0']").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = "b" });
        await editor.Find(".requirement-submit button").ClickAsync(new());
        Assert.That(_calculator.Tokens[1].IsCancellationRequested, Is.True);
        if (error) {
            _calculator.Jobs[1].SetException(new InvalidOperationException("late OR error"));
        }
        else {
            _calculator.Jobs[1].SetResult(Result("late OR success"));
        }
        await pending;
        Assert.That(_cut.Markup, Does.Contain("accepted").And.Not.Contain("late OR"));
        Assert.That(_cut.FindAll(".pinned-result"), Has.Count.EqualTo(1));
        Task next = Start();
        _calculator.Jobs[2].SetResult(Result("new OR result"));
        await next;
        Assert.That(_cut.Markup, Does.Contain("new OR result"));
    }

    private Task Cancel() => _cut.Find(".calculate-action > button[aria-label='Cancel calculation']").ClickAsync(new());
    private Task SetHandSizeWithoutInvalidating(int value) => _cut.InvokeAsync(() => {
        BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo handSizeField = typeof(ProbabilityCalculatorComponent).GetField("_handSize", instanceNonPublic)!;
        handSizeField.SetValue(_cut.Instance, value);
        MethodInfo stateHasChangedMethod = typeof(ComponentBase).GetMethod("StateHasChanged", instanceNonPublic)!;
        stateHasChangedMethod.Invoke(_cut.Instance, null);
    });
    private static ProbabilityCalculationResult Result(string name) => new(0.5, [new(0, name, 0.5)]);

    [TestCase(false)]
    [TestCase(true)]
    public async Task OldCompletionCannotClearNewBusyStateOrReplaceNewResult(bool oldError) {
        Task first = Start();
        await Cancel();
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        Task second = Start();
        if (oldError) {
            _calculator.Jobs[0].SetException(new InvalidOperationException("old failure"));
        }
        else {
            _calculator.Jobs[0].SetResult(Result("old success"));
        }
        await first;
        Assert.That(_cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
        Assert.That(_cut.FindAll(".calculate-action > button[aria-label='Cancel calculation']"), Has.Count.EqualTo(1));
        Assert.That(_cut.Markup, Does.Not.Contain("old failure").And.Not.Contain("old success"));
        _calculator.Jobs[1].SetResult(Result("new success"));
        await second;
        Assert.That(_cut.Markup, Does.Contain("new success"));
        Assert.That(_cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public async Task IdleInvalidInputsDisableCalculateButRunningActionRemainsUsableForCancellation() {
        await SetHandSizeWithoutInvalidating(0);
        Assert.That(_cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.True);

        await SetHandSizeWithoutInvalidating(2);
        Task calculation = Start();
        AngleSharp.Dom.IElement runningButton = _cut.Find(".calculate-action > button");
        Assert.That(runningButton.GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
        Assert.That(runningButton.GetAttribute("title"), Is.EqualTo("Cancel calculation"));
        Assert.That(runningButton.GetAttribute("type"), Is.EqualTo("button"));
        Assert.That(runningButton.GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(runningButton.HasAttribute("disabled"), Is.False);
        Assert.That(_cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));

        // Keep the controlled request active while validation changes, so the in-progress cancel action
        // must stay enabled even though the idle Calculate action would now be disabled.
        await SetHandSizeWithoutInvalidating(0);
        Assert.That(_cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.False);
        await Cancel();
        Assert.That(_calculator.Jobs, Has.Count.EqualTo(1));
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(_cut.Find(".calculate-action > button").HasAttribute("disabled"), Is.True);

        _calculator.Jobs[0].SetCanceled();
        await calculation;
        Assert.That(_cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(_cut.Markup, Does.Not.Contain("Calculation was cancelled."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UserCancellationPreservesAcceptedResultAndDoesNotCreateCancellationNotice(bool stale) {
        Task accepted = Start();
        _calculator.Jobs[0].SetResult(Result("previous result"));
        await accepted;
        if (stale) {
            await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }

        bool wasStale = _cut.FindAll(".probability-result-status").Count == 1;
        Assert.That(wasStale, Is.EqualTo(stale));
        string priorValue = _cut.Find(".probability-total-value").TextContent;
        Task cancellation = Start();

        Assert.That(_cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));
        Assert.That(_cut.Find(".calculate-action > button").GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
        await Cancel();
        Assert.That(_calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(_cut.Find(".calculate-action > button").TextContent, Does.Contain("Calculate"));
        Assert.That(_cut.Find(".probability-total-value").TextContent, Is.EqualTo(priorValue));
        Assert.That(_cut.FindAll(".probability-result-status"), Has.Count.EqualTo(stale ? 1 : 0));
        Assert.That(_cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(_cut.Markup, Does.Not.Contain("Calculation was cancelled.").And.Not.Contain("Calculation was canceled."));
        Assert.That(_cut.FindAll("[role='status']").Any(status => status.TextContent.Contains("cancel", StringComparison.OrdinalIgnoreCase)), Is.False);

        // The worker may report cancellation after the user has already returned the control to idle.
        _calculator.Jobs[1].SetCanceled();
        await cancellation;
        Assert.That(_cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(_cut.Find(".probability-total-value").TextContent, Is.EqualTo(priorValue));
        Assert.That(_cut.FindAll(".probability-result-status"), Has.Count.EqualTo(stale ? 1 : 0));

        Task next = Start();
        _calculator.Jobs[2].SetResult(Result("after cancellation"));
        await next;
        Assert.That(_cut.Markup, Does.Contain("after cancellation"));
        Assert.That(_cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(_cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task CalculationLimitErrorsRemainVisible() {
        Task calculation = Start();
        ProbabilityCalculationLimitException exception = new();
        _calculator.Jobs[0].SetException(exception);
        await calculation;

        Assert.That(_cut.Find("[role='alert']").TextContent, Does.Contain(exception.Message));
    }

    [Test]
    public async Task GenuineCalculationFailuresRemainVisible() {
        Task calculation = Start();
        _calculator.Jobs[0].SetException(new InvalidOperationException("worker transport failed"));
        await calculation;

        Assert.That(_cut.Find("[role='alert']").TextContent, Does.Contain("Calculation failed: worker transport failed"));
    }

    [Test]
    public async Task DuplicateClickDoesNotStartAnotherJob() {
        Task first = Start();
        await _cut.InvokeAsync(() => _cut.Instance.GetType().GetMethod("Calculate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(_cut.Instance, null));
        Assert.That(_calculator.Jobs, Has.Count.EqualTo(1));
        _calculator.Jobs[0].SetResult(Result("done"));
        await first;
    }

    [Test]
    public async Task EditCancelsAndMarksPreviousResultsStaleThenNewJobSucceeds() {
        Task first = Start();
        _calculator.Jobs[0].SetResult(Result("previous"));
        await first;

        Task second = Start();
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(_calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(_cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        Task third = Start();
        _calculator.Jobs[2].SetResult(Result("current"));
        await third;
        _calculator.Jobs[1].SetException(new ProbabilityCalculationLimitException());
        await second;
        Assert.That(_cut.Markup, Does.Contain("current").And.Not.Contain("Calculation stopped"));
        Assert.That(_cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposedComponentCancelsAndIgnoresLateSuccessAndError(bool error) {
        Task first = Start();
        await _cut.InvokeAsync(_cut.Instance.Dispose);
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        if (error) {
            _calculator.Jobs[0].SetException(new InvalidOperationException("late"));
        }
        else {
            _calculator.Jobs[0].SetResult(Result("late"));
        }
        await first;
    }

    [Test]
    public async Task AcceptingAnotherSessionCancelsThePreviousWorkspaceCalculation() {
        SessionState replacement = new() {
            Cards = [new([], 4, id: "new")],
            Combos = [new([], "New combo", cards: [new("new", 1, 2)])],
            HandSize = 2
        };
        Task first = Start();
        _cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(_sessions.Object.SerializeSession(replacement), "session.json"));
        _cut.WaitForState(() => _calculator.Tokens[0].IsCancellationRequested);
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        _calculator.Jobs[0].SetResult(Result("old workspace"));
        await first;
        Assert.That(_cut.Markup, Does.Contain("New combo").And.Not.Contain("old workspace"));
        Task next = Start();
        _calculator.Jobs[1].SetResult(Result("new workspace"));
        await next;
        Assert.That(_cut.Markup, Does.Contain("new workspace"));
    }

    [Test]
    public async Task RapidCancelRestartCancelsEveryPriorRequest() {
        List<Task> priorCalculations = [];
        for (int i = 0; i < 5; i++) {
            priorCalculations.Add(Start());
            await Cancel();
        }

        Task last = Start();
        for (int i = 0; i < 5; i++) {
            Assert.That(_calculator.Tokens[i].IsCancellationRequested, Is.True);
            _calculator.Jobs[i].SetResult(Result("old"));
        }
        await Task.WhenAll(priorCalculations);
        Assert.That(_cut.FindAll("[aria-label='Cancel calculation']"), Has.Count.EqualTo(1));
        _calculator.Jobs[5].SetResult(Result("last"));
        await last;
        Assert.That(_cut.Markup, Does.Contain("last"));
    }

    private sealed class ControlledCalculator : IBackgroundCalculator {
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
            TaskCompletionSource<ProbabilityCalculationResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Jobs.Add(source);
            Tokens.Add(token);
            return source.Task; // Deliberately disobey cancellation to test ownership of late callbacks.
        }
    }
}
