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
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<CardInfo>());
        _context.Services.AddSingleton(cardInfo.Object);
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

    private static ProbabilityCalculationResult PreliminaryResult() {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Counterexample("banish"),
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Combos, 2,
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Groups,
            options: new(new(DrawActivationMode.CustomPriority, ["d", "s"])));
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), CalculationModel.Fast);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        return result;
    }

    private Task Progress(int index, ProbabilityCalculationResult result) => _cut.InvokeAsync(() => _calculator.Progress[index](result));

    [Test]
    public async Task PreliminaryIsVisibleBeforeFinalAndKeepsPinExportAndRetryDisabled() {
        Task task = Start();
        Assert.That(_cut.FindAll(".probability-results"), Is.Empty);
        ProbabilityCalculationResult initial = PreliminaryResult();
        await Progress(0, initial);
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(_cut.Find(".probability-result-status").TextContent, Does.Contain("Preliminary"));
        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Preliminary"));
        Assert.That(_cut.Find(".probability-result-status-slot").InnerHtml, Does.Not.Contain("checking Exact"));
        Assert.That(_cut.FindAll(".probability-result-status.probability-result-status-preliminary"), Has.Count.EqualTo(1));
        Assert.That(_cut.Find(".probability-result-status").ClassList, Does.Contain("probability-result-status-chip"));
        Assert.That(_cut.Find(".probability-result-status-slot").GetAttribute("aria-live"), Is.EqualTo("polite"));
        Assert.That(_cut.Find(".calculate-action > button").GetAttribute("aria-label"), Is.EqualTo("Cancel Exact upgrade"));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.IsCalculating, Is.True);
        Assert.That(_cut.FindAll(".exact-retry:not(.exact-retry-placeholder)"), Is.Empty);
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.Result.Contract, Is.SameAs(initial.Contract));
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.Result.ComboProbabilities, Is.EqualTo(initial.ComboProbabilities));
        _calculator.Jobs[0].SetResult(initial with { Contract = initial.Contract! with { AutomaticExact = AutomaticExactOutcome.Deadline } });
        await task;
        Assert.That(_cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.Result.Contract!.AutomaticExact, Is.EqualTo(AutomaticExactOutcome.Deadline));
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.Result.Contract!.Fractions, Is.SameAs(initial.Contract!.Fractions));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelOrEditAfterProgressRetainsFastAndRejectsLateEvents(bool edit) {
        Task first = Start();
        ProbabilityCalculationResult initial = PreliminaryResult();
        await Progress(0, initial);
        if (edit) {
            await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else {
            await _cut.Find(".calculate-action > button").ClickAsync(new());
        }
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(_cut.FindAll(".probability-result-status").Any(status => status.TextContent.Contains("Preliminary")), Is.False);
        Assert.That(_cut.FindAll(".probability-result-status.probability-result-status-preliminary"), Is.Empty);
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.IsStale, Is.EqualTo(edit));
        Assert.That(_cut.FindComponent<ProbabilityResultExport>().Instance.Result.Contract!.Fractions, Is.SameAs(initial.Contract!.Fractions));
        Task second = Start();
        await Progress(0, Result("late progress"));
        _calculator.Jobs[0].SetResult(Result("late final"));
        await first;
        Assert.That(_cut.Markup, Does.Not.Contain("late progress").And.Not.Contain("late final"));
        Assert.That(_cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        _calculator.Jobs[1].SetResult(Result("new accepted"));
        await second;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FinalTransportOrLimitFailureKeepsFastWithoutFalseFailureAlert(bool limit) {
        Task task = Start();
        ProbabilityCalculationResult initial = PreliminaryResult();
        await Progress(0, initial);
        _calculator.Jobs[0].SetException(limit ? new ProbabilityCalculationLimitException() : new InvalidOperationException("invalid final contract"));
        await task;
        Assert.That(_cut.FindAll("[role='alert'], .probability-result-status"), Is.Empty);
        ProbabilityCalculationResult retained = _cut.FindComponent<ProbabilityResultExport>().Instance.Result;
        Assert.That(retained.Contract!.AutomaticExact, Is.EqualTo(limit ? AutomaticExactOutcome.WorkLimit : AutomaticExactOutcome.Error));
        Assert.That(retained.Contract.Fractions, Is.SameAs(initial.Contract!.Fractions));
        Assert.That(retained.SearchAvailability, Is.SameAs(initial.SearchAvailability));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public async Task ExactUpgradeAtomicallyReplacesRowsContractAndSnapshotWithoutChangingManualPin() {
        Task previous = Start();
        _calculator.Jobs[0].SetResult(PreliminaryResult());
        await previous;
        await _cut.Find(".pin-result-action").ClickAsync(new());
        PinnedResultSnapshot pin = _cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Task task = Start();
        await Progress(1, PreliminaryResult());
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Counterexample("banish"),
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Combos, 2,
            Services.ProbabilityCalculator.ThreeCalculationModesTest.Groups,
            options: new(new(DrawActivationMode.CustomPriority, ["d", "s"]), Mode: CalculationModel.Exact));
        ProbabilityCalculationResult exact = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), CalculationModel.Exact);
        ProbabilityCalculationResult automatic = exact with { Contract = exact.Contract! with {
            RequestedMode = CalculationModel.Fast, ExecutedMode = CalculationModel.Fast, AutomaticExact = AutomaticExactOutcome.Completed
        } };
        _calculator.Jobs[1].SetResult(automatic);
        await task;
        ProbabilityCalculationResult actual = _cut.FindComponent<ProbabilityResultExport>().Instance.Result;
        Assert.That(actual.Contract, Is.SameAs(automatic.Contract));
        Assert.That(actual.SearchAvailability, Is.Null);
        Assert.That(actual.ComboProbabilities, Is.EqualTo(exact.ComboProbabilities));
        Assert.That(actual.GroupProbabilities, Is.EqualTo(exact.GroupProbabilities));
        Assert.That(_cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pin));
        Assert.That(_cut.FindAll(".probability-result-status, .exact-retry:not(.exact-retry-placeholder)"), Is.Empty);
        await _cut.Find(".pin-result-action").ClickAsync(new());
        PinnedResultSnapshot replacement = _cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Assert.That(replacement.Contract, Is.SameAs(automatic.Contract));
        Assert.That(replacement.Context.Model, Is.EqualTo(CalculationModel.Fast));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SessionReplacementOrDisposalRevokesProgressOwnership(bool dispose) {
        Task task = Start();
        await Progress(0, PreliminaryResult());
        if (dispose) {
            await _cut.InvokeAsync(() => _cut.Instance.DisposeAsync().AsTask());
        }
        else {
            SessionState replacement = new() {
                Cards = [new([], 4, id: "new")],
                Combos = [new([], "New session", cards: [new("new", 1, 2)])], HandSize = 2
            };
            _cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(_sessions.Object.SerializeSession(replacement), "session.json"));
            _cut.WaitForState(() => _calculator.Tokens[0].IsCancellationRequested);
            Assert.That(_cut.FindAll(".probability-results"), Is.Empty);
        }
        await Progress(0, Result("late progress"));
        _calculator.Jobs[0].SetResult(Result("late completion"));
        await task;
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(_cut.Markup, Does.Not.Contain("late progress").And.Not.Contain("late completion"));
    }

    [Test]
    public async Task RuleChangeCancelsOwnedRequestAndKeepsPinnedResultProvenance() {
        CategoryBase role = new("Role");
        IRenderedComponent<CategoryListEditor> categories = _cut.FindComponent<CategoryListEditor>();
        categories.Find("[aria-label='Category name']").Input(role.Name);
        categories.Find("button.btn-primary").Click();
        Task accepted = Start();
        _calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await _cut.Find(".pin-result-action").ClickAsync(new());
        PinnedResultSnapshot pin = _cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Task pending = Start();
        _cut.Find("[aria-label='Edit category Role']").Click();
        _cut.Find("[aria-label='Define by properties']").Click();
        _cut.Find("[aria-label='Add rule alternative']").Change("metadata:kind:monster");
        _cut.FindComponent<CategoryRuleEditor>().Find("button.btn-primary").Click();
        Assert.That(_calculator.Tokens[1].IsCancellationRequested, Is.True);
        _calculator.Jobs[1].SetResult(Result("late rule result"));
        await pending;
        Assert.That(_cut.Markup, Does.Not.Contain("late rule result"));
        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        Assert.That(_cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pin));
        Task current = Start();
        _calculator.Jobs[2].SetResult(Result("current"));
        await current;
        PinnedResultSnapshot currentSnapshot = (PinnedResultSnapshot)typeof(ProbabilityCalculatorComponent)
            .GetField("_acceptedResultSnapshot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(_cut.Instance)!;
        Assert.That(currentSnapshot, Is.Not.SameAs(pin));
        Assert.That(_cut.FindAll(".result-priority-difference, .comparison-model-difference, .result-difference, .result-row-comparison, .pinned-match"), Is.Empty);
    }

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
        await editor.Find("[data-requirement='card:0']").ClickAsync(new());
        if (close) {
            await editor.Find(".requirement-form-heading button").ClickAsync(new());
        }
        else {
            await editor.Find("[data-action='alternative']").ClickAsync(new());
            await CalculatorEditorTestBase.RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = "card:b" });
            await CalculatorEditorTestBase.RequirementField(editor, "#minCount0").InputAsync(new() { Value = "3" });
            await editor.Find(".requirement-form-heading button").ClickAsync(new());
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
        await editor.Find("[data-requirement='card:0']").ClickAsync(new());
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        await CalculatorEditorTestBase.RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = "card:b" });
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
        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        Task third = Start();
        _calculator.Jobs[2].SetResult(Result("current"));
        await third;
        _calculator.Jobs[1].SetException(new ProbabilityCalculationLimitException());
        await second;
        Assert.That(_cut.Markup, Does.Contain("current").And.Not.Contain("Calculation stopped"));
        Assert.That(_cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task DrawEffectsToggleCancelsPendingCalculationAndCapturesTheNewMode() {
        BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        List<Card> cards = (List<Card>)typeof(ProbabilityCalculatorComponent)
            .GetField("_cards", instanceNonPublic)!.GetValue(_cut.Instance)!;
        cards[0] = cards[0].WithDrawCount(2).WithDrawOncePerTurn(false);

        Task accepted = Start();
        Assert.That(_calculator.Snapshots[0].Json, Does.Contain("\"DrawCount\":2"));
        _calculator.Jobs[0].SetResult(Result("configured result"));
        await accepted;

        Task pending = Start();
        await _cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(_calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));

        Task disabled = Start();
        Assert.That(_calculator.Snapshots[2].Json, Does.Contain("\"DrawCount\":null"));
        _calculator.Jobs[2].SetResult(Result("ordinary result"));
        await disabled;
        _calculator.Jobs[1].SetResult(Result("late configured result"));
        await pending;
        Assert.That(_cut.Markup, Does.Contain("ordinary result").And.Not.Contain("late configured result"));
        Assert.That((cards[0].DrawCount, cards[0].DrawOncePerTurn), Is.EqualTo(((int?)2, false)));
    }

    [Test]
    public async Task IdleComponentExplicitlyDisposesWorkerOwnerOnce() {
        Task accepted = Start();
        _calculator.Jobs[0].SetResult(Result("accepted"));
        await accepted;
        await _cut.InvokeAsync(() => _cut.Instance.DisposeAsync().AsTask());
        await _cut.InvokeAsync(() => _cut.Instance.DisposeAsync().AsTask());
        Assert.That(_calculator.Disposals, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposedComponentCancelsAndIgnoresLateSuccessAndError(bool error) {
        Task first = Start();
        await _cut.InvokeAsync(() => _cut.Instance.DisposeAsync().AsTask());
        Assert.That(_calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(_calculator.Disposals, Is.EqualTo(1));
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

    private sealed class ControlledCalculator : IProgressiveBackgroundCalculator {
        public List<Func<ProbabilityCalculationResult, Task>> Progress { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateProgressivelyAsync(
            CalculationSnapshot snapshot,
            Func<ProbabilityCalculationResult, Task> onPreliminary,
            CancellationToken cancellationToken
        ) {
            Progress.Add(onPreliminary);
            return CalculateAsync(snapshot, cancellationToken);
        }
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() {
            Disposals++;

            return ValueTask.CompletedTask;
        }
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public List<CalculationSnapshot> Snapshots { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
            TaskCompletionSource<ProbabilityCalculationResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Jobs.Add(source);
            Tokens.Add(token);
            Snapshots.Add(snapshot);
            return source.Task; // Deliberately disobey cancellation to test ownership of late callbacks.
        }
    }
}
