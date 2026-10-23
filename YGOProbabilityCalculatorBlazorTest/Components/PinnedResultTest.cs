using System.Globalization;
using System.Reflection;
using System.Text;
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
[SetCulture("en-US")]
public partial class PinnedResultTest {
    private TestContext _context = null!;
    private ControlledCalculator _worker = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> _cut = null!;
    private ISessionService _sessions = null!;
    private BunitJSModuleInterop _clipboard = null!;
    private Mock<IDeckImportService> _imports = null!;
    private static readonly CategoryBase role = new("Role");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void Setup() {
        _context = new();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<ISerializer, JsonSerializer>();
        _context.Services.AddSingleton<ISessionService, SessionService>();
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<CardInfo>());
        _context.Services.AddSingleton(cardInfo.Object);
        _context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        _context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        _imports = new();
        _context.Services.AddSingleton(_imports.Object);
        _worker = new();
        _context.Services.AddSingleton<IBackgroundCalculator>(_worker);
        _context.Services.AddSingleton<IPendingSessionService>(new PendingSessionService { PendingSession = Workspace() });
        _clipboard = _context.JSInterop.SetupModule("./js/probabilityResultExport.js");
        _clipboard.Mode = JSRuntimeMode.Loose;
        _clipboard.Setup<bool>("copyText", _ => true).SetResult(true);
        _sessions = _context.Services.GetRequiredService<ISessionService>();
        _cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    [TearDown]
    public void Cleanup() {
        _cut.Dispose();
        _context.Dispose();
    }

    private static SessionState Workspace(int handSize = 2) => new() {
        Cards = [new([role], 4, "A", id: "a"), new([], 4, "B", id: "b")],
        Categories = [role], HandSize = handSize,
        ComboGroups = [new("g", "Group")],
        Combos = [new([], "Duplicate", groupId: "g", cards: [new("a", 1, 5)]),
            new([new(role, 1, 5)], "Duplicate"), new([], null, cards: [new("b", 1, 5)]),
            new([], "Inactive", false, cards: [new("b", 1, 5)])]
    };

    private T Field<T>(string name) => (T)typeof(ProbabilityCalculatorComponent).GetField(name, Private)!.GetValue(_cut.Instance)!;

    private Task Call(string name, params object[] args) => _cut.InvokeAsync(async () => {
        object? result = typeof(ProbabilityCalculatorComponent).GetMethod(name, Private)!.Invoke(_cut.Instance, args);
        if (result is Task task) {
            await task;
        }

        typeof(Microsoft.AspNetCore.Components.ComponentBase).GetMethod("StateHasChanged", Private)!.Invoke(_cut.Instance, null);
    });

    private Task Start() => _cut.Find(".calculate-action > button").ClickAsync(new());

    private Task Pin() => _cut.Find(".pin-result-action").ClickAsync(new());

    private Task Clear() => _cut.Find(".pinned-result button").ClickAsync(new());

    private PinnedResultSnapshot Pinned => _cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;

    private PinnedResultSnapshot Accepted => Field<PinnedResultSnapshot>("_acceptedResultSnapshot");

    private (string Current, string Pinned) ContextDescriptions() => (
        _cut.Find(".probability-results > p.result-model").TextContent.Trim(),
        _cut.FindComponent<PinnedResultPanel>().Find(".pinned-result > p.small").TextContent.Trim());

    private void AssertContextDescriptions(string current, string pinned) {
        (string Current, string Pinned) actual = ContextDescriptions();
        Assert.That(actual.Current, Is.EqualTo(current));
        Assert.That(actual.Pinned, Is.EqualTo(pinned));
    }

    private ProbabilityCalculationResult Result(int job, double total) {
        CalculationInput input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_worker.Inputs[job].Json)!;
        List<ComboProbabilityResult> comboResults = input.Combos
            .Select((combo, index) => new ComboProbabilityResult(index, combo.Name, total, combo.GroupId))
            .ToList();
        List<GroupProbabilityResult> groupResults = input.Groups
            .Select(group => new GroupProbabilityResult(
                group.Id,
                group.Name,
                total,
                input.Combos.Count(combo => combo.GroupId == group.Id)))
            .ToList();

        CalculationResultContract contract = CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[job].Json)).Contract!;
        return new(total, comboResults, groupResults, Contract: contract);
    }

    private async Task Accept(double probability = .814) {
        Task action = Start();
        int job = _worker.Jobs.Count - 1;
        _worker.Jobs[job].SetResult(Result(job, probability));
        await action;
    }

    private async Task<PinnedResultSnapshot> AcceptWithAccuracy(CalculationAccuracy accuracy, double probability) {
        Task action = Start();
        int job = _worker.Jobs.Count - 1;
        ProbabilityCalculationResult result = Result(job, probability);
        AutomaticExactOutcome automaticExact = accuracy == CalculationAccuracy.Exact
            ? AutomaticExactOutcome.Completed
            : AutomaticExactOutcome.Deadline;
        CalculationResultContract contract = result.Contract! with {
            Accuracy = accuracy,
            AutomaticExact = automaticExact
        };
        SearchAvailabilityCertificate certificate = new(
            SearchAvailabilityStatus.NotApplicable,
            "No search effects are modeled in this result.");
        _worker.Jobs[job].SetResult(result with { Contract = contract, SearchAvailability = certificate });
        await action;
        return Accepted;
    }

    private Task Upload(SessionState session) => _cut.InvokeAsync(() => {
        InputFile input = _cut.FindComponents<InputFile>()[1].Instance;
        string json = _sessions.SerializeSession(session);
        InputFileChangeEventArgs args = new([new SessionFile(json)]);
        return input.OnChange.InvokeAsync(args);
    });

    [Test]
    public async Task ExactResultsWithChangedDefinitionsKeepIndependentRowsAndZeroUncertainty() {
        await _cut.Find("#calculationModel").ChangeAsync(new() { Value = "Exact" });

        Task initialCalculation = Start();
        int firstJob = _worker.Jobs.Count - 1;
        _worker.Jobs[firstJob].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[firstJob].Json)));
        await initialCalculation;

        PinnedResultSnapshot initial = Accepted;
        Assert.That(initial.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        await Pin();
        PinnedResultSnapshot pinned = Pinned;

        List<Combo> combos = Field<List<Combo>>("_combos");
        await Call("ReplaceCombo", (0, combos[0].WithCards([new("a", 2, 5)])));
        await Call("ReplaceCombo", (1, combos[1].WithActive(false)));
        await Call("ReplaceCombo", (2, combos[2].WithActive(false)));

        Task currentCalculation = Start();
        int currentJob = _worker.Jobs.Count - 1;
        _worker.Jobs[currentJob].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[currentJob].Json)));
        await currentCalculation;

        PinnedResultSnapshot current = Accepted;
        Assert.That(current.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(current.Total, Is.Not.EqualTo(initial.Total));
        Assert.That(current.Combos[0].Probability, Is.Not.EqualTo(initial.Combos[0].Probability));
        Assert.That(current.Contract.Fractions!.Combos[0], Is.Not.EqualTo(initial.Contract.Fractions!.Combos[0]));
        Assert.That(Pinned, Is.SameAs(pinned));
        Assert.That(pinned.Combos[0].Probability, Is.EqualTo(initial.Combos[0].Probability));
        Assert.That(_cut.FindAll(".probability-results .probability-uncertainty").Select(element => element.TextContent.Trim()), Is.All.EqualTo("±0%"));
        Assert.That(_cut.FindAll(".pinned-result .probability-uncertainty").Select(element => element.TextContent.Trim()), Is.All.EqualTo("±0%"));
    }

    [Test]
    public async Task PinReplaceClearAreExplicitAndDoNotChangeSessionAutosaveOrCalculationVersion() {
        Assert.That(_cut.FindAll(".pin-result-action"), Is.Empty);
        Assert.That(_cut.FindAll(".pinned-result"), Is.Empty);
        await Accept();
        Assert.That(_cut.FindAll(".pinned-result"), Is.Empty);
        string before = _sessions.SerializeSession(FieldSession());
        long version = Field<long>("_calculationVersion");
        int writes = _context.JSInterop.Invocations["sessionRecovery.update"].Count;
        await Pin();
        Assert.That(_cut.Find(".probability-results-heading .pin-result-action").GetAttribute("title"), Is.EqualTo("Replace pin with current result"));
        Assert.That(_cut.Find(".probability-results-heading .pin-result-action").TextContent.Trim(), Is.Empty);
        Assert.That(_cut.Find(".probability-results-heading [aria-label='Copy results'] .visually-hidden").TextContent, Is.EqualTo("Copy results"));
        Assert.That(_cut.Find(".pinned-heading [aria-label='Unpin result']").GetAttribute("title"), Is.EqualTo("Unpin result"));
        Assert.That(_cut.FindAll(".pinned-heading svg path"), Has.Count.EqualTo(2));
        PinnedResultSnapshot first = Pinned;
        AssertContextDescriptions("Hand size 2 · 8 cards", "Hand size 2 · 8 cards");
        await Accept(.842);
        Assert.That(Pinned, Is.SameAs(first));
        Assert.That(Accepted.Total, Is.EqualTo(.842));
        Assert.That(Pinned.Total, Is.EqualTo(.814));
        await Pin();
        Assert.That(Pinned.Total, Is.EqualTo(.842));
        await Clear();
        Assert.That(_cut.FindAll(".pinned-result"), Is.Empty);
        Assert.That(_sessions.SerializeSession(FieldSession()), Is.EqualTo(before));
        Assert.That(Field<long>("_calculationVersion"), Is.EqualTo(version));
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
    }

    private SessionState FieldSession() =>
        (SessionState)typeof(ProbabilityCalculatorComponent)
            .GetMethod("CaptureSession", Private)!
            .Invoke(_cut.Instance, null)!;

    [Test]
    public async Task PinOwnsRequestContextAndCopiesMutableResultRows() {
        Task action = Start();
        CalculationInput input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_worker.Inputs[0].Json)!;
        Assert.That(input.WorkUnits, Is.EqualTo(150_000_000), "ordinary Calculate selects the interactive policy on its first request");
        Assert.That(input.HandSize, Is.EqualTo(2));
        ProbabilityCalculationResult result = Result(0, .814);
        // An unannounced edit specifically verifies capture timing, independent of invalidation.
        Field<List<Card>>("_cards")[0].Categories.Clear();
        Field<List<Combo>>("_combos")[0].Cards.Clear();
        typeof(ProbabilityCalculatorComponent).GetField("_handSize", Private)!.SetValue(_cut.Instance, 3);
        _worker.Jobs[0].SetResult(result);
        await action;
        await Pin();
        PinnedResultSnapshot frozen = Pinned;
        ((List<ComboProbabilityResult>)result.ComboProbabilities).Clear();
        ((List<GroupProbabilityResult>)result.GroupProbabilities!).Clear();
        await Call("RenameGroup", ("g", "Changed group"));
        Assert.That(Pinned, Is.SameAs(frozen));
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
        Assert.That(Pinned.Context.Copies, Is.EqualTo(8));
        Assert.That(Pinned.Combos, Has.Length.EqualTo(3));
        Assert.That(Pinned.Combos[2].Label, Is.EqualTo("Unnamed combo 3"));
        Assert.That(Pinned.Groups[0].Label, Is.EqualTo("Group"));
        Assert.That(Pinned.Total, Is.EqualTo(.814));
        Assert.That(Pinned.Combos.Select(combo => combo.Probability), Is.All.EqualTo(.814));
        Assert.That(Pinned.Groups[0].Probability, Is.EqualTo(.814));
    }

    [Test]
    public async Task InvalidResultCannotReplaceTheLastAcceptedPin() {
        await Accept();
        await Pin();
        await Accept(double.NaN);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(Pinned.Total, Is.EqualTo(.814));
    }

    [TestCase(CalculationAccuracy.Exact, "settings")]
    [TestCase(CalculationAccuracy.Exact, "card-count")]
    [TestCase(CalculationAccuracy.Exact, "combo-rename")]
    [TestCase(CalculationAccuracy.Exact, "combo-removal")]
    [TestCase(CalculationAccuracy.Bounded, "settings")]
    [TestCase(CalculationAccuracy.Bounded, "card-count")]
    [TestCase(CalculationAccuracy.Bounded, "combo-rename")]
    [TestCase(CalculationAccuracy.Bounded, "combo-removal")]
    public async Task StaleAcceptedSnapshotCanReplacePinWithoutRecalculation(CalculationAccuracy accuracy, string edit) {
        PinnedResultSnapshot initial = await AcceptWithAccuracy(accuracy, .814);
        await Pin();
        Assert.That(Pinned, Is.SameAs(initial));

        PinnedResultSnapshot accepted = await AcceptWithAccuracy(accuracy, .842);
        Assert.That(Pinned, Is.SameAs(initial));
        Assert.That(accepted, Is.Not.SameAs(initial));

        switch (edit) {
            case "settings":
                await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
                await _cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
                break;
            case "card-count":
                await Call("ReplaceCard", (0, Field<List<Card>>("_cards")[0].WithCopies(3)));
                break;
            case "combo-rename":
                await Call("ReplaceCombo", (0, Field<List<Combo>>("_combos")[0].WithName("Edited route")));
                await Call("RenameGroup", ("g", "Edited group"));
                break;
            case "combo-removal":
                await Call("RemoveCombo", 0);
                break;
        }

        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        Assert.That(Accepted, Is.SameAs(accepted));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("aria-describedby"), Is.False);
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2), "pinning a stale result must not start another calculation");

        await Pin();

        Assert.That(Pinned, Is.SameAs(accepted), "the existing pin is replaced by the already accepted snapshot");
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
        Assert.That(Pinned.Context.Copies, Is.EqualTo(8));
        Assert.That(Pinned.Context.CardCount, Is.EqualTo(2));
        Assert.That(Pinned.Context.Model, Is.EqualTo(CalculationModel.Fast));
        Assert.That(Pinned.Combos.Select(combo => combo.Label), Is.EqualTo(accepted.Combos.Select(combo => combo.Label)));
        Assert.That(Pinned.Groups.Select(group => group.Label), Is.EqualTo(accepted.Groups.Select(group => group.Label)));
        Assert.That(Pinned.Contract, Is.SameAs(accepted.Contract));
        Assert.That(Pinned.Contract!.Accuracy, Is.EqualTo(accuracy));
        Assert.That(Pinned.Contract.AutomaticExact, Is.EqualTo(accuracy == CalculationAccuracy.Exact
            ? AutomaticExactOutcome.Completed
            : AutomaticExactOutcome.Deadline));
        Assert.That(Pinned.Contract.Fractions, Is.SameAs(accepted.Contract.Fractions));
        Assert.That(Pinned.SearchAvailability, Is.SameAs(accepted.SearchAvailability));
        Assert.That(Pinned.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.NotApplicable));

        await Call("RenameGroup", ("g", "Edited after pin"));
        Assert.That(Pinned, Is.SameAs(accepted), "later workspace edits cannot mutate the pinned snapshot");
    }

    [TestCase(.842)]
    [TestCase(.786)]
    public async Task AcceptedResultAndPinRemainVisibleWhenDisplayedResultBecomesStale(double probability) {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        await Accept(probability);
        PinnedResultSnapshot accepted = Accepted;

        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });

        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(Accepted, Is.SameAs(accepted));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("aria-describedby"), Is.False);

        await Pin();
        Assert.That(Pinned, Is.SameAs(accepted));
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2));

        Task action = Start();
        Assert.That(_cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        _worker.Jobs[^1].SetCanceled();
        await action;
        Assert.That(Accepted, Is.SameAs(accepted));
    }

    [Test]
    public async Task SuccessfulStaleRecalculationReplacesCurrentSnapshotAndKeepsPinFrozen() {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        await Accept(.842);
        PinnedResultSnapshot previous = Accepted;
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Pin();
        PinnedResultSnapshot stalePin = Pinned;
        Assert.That(stalePin, Is.SameAs(previous));
        Task action = Start();

        _worker.Jobs[^1].SetResult(Result(_worker.Jobs.Count - 1, .786));
        await action;

        PinnedResultSnapshot accepted = Accepted;
        Assert.That(accepted, Is.Not.SameAs(previous));
        Assert.That(Pinned, Is.SameAs(stalePin));
        Assert.That(_cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(accepted.Total, Is.EqualTo(.786));
        Assert.That(Pinned.Total, Is.EqualTo(.842));
        Assert.That(pin.Total, Is.EqualTo(.814));
    }

    [TestCase("late-success")]
    [TestCase("late-error")]
    [TestCase("cancelled")]
    [TestCase("failure")]
    [TestCase("work-limit")]
    [TestCase("storage-limit")]
    public async Task BusyStaleCancelledFailedAndLateResultsCannotReplacePinOrAcceptedResult(string completion) {
        await Accept();
        await Pin();
        PinnedResultSnapshot initialPin = Pinned;
        await Accept(.842);
        PinnedResultSnapshot accepted = Accepted;
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("aria-describedby"), Is.False);
        await Pin();
        PinnedResultSnapshot baseline = Pinned;
        Assert.That(baseline, Is.SameAs(accepted));
        Assert.That(baseline, Is.Not.SameAs(initialPin));
        Task action = Start();
        await Call("PinCurrentResult");
        Assert.That(Pinned, Is.SameAs(baseline), "the event handler also rejects busy/stale pinning");
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        if (completion.StartsWith("late")) {
            await _cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
            await Accept(.842);
            if (completion == "late-success") {
                _worker.Jobs[2].SetResult(Result(2, .1));
            }
            else {
                _worker.Jobs[2].SetException(new Exception("Obsolete error"));
            }
        }
        else if (completion == "cancelled") {
            _worker.Jobs[2].SetCanceled();
        }
        else if (completion.EndsWith("limit")) {
            _worker.Jobs[2].SetException(new ProbabilityCalculationLimitException(
                completion == "work-limit" ? ProbabilityCalculationLimitReason.Work : ProbabilityCalculationLimitReason.Storage));
        }
        else {
            _worker.Jobs[2].SetException(new Exception("Current failure"));
        }

        await action;
        Assert.That(Pinned, Is.SameAs(baseline));
        if (completion.StartsWith("late")) {
            Assert.That(_cut.Markup, Does.Not.Contain("Obsolete error"));
        }
        else {
            Assert.That(Accepted, Is.SameAs(accepted));
            Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ObsoleteCompletionCannotUnlockNewBusyRequestOrReplaceItsAcceptedContext(bool limit) {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        PinnedResultSnapshot accepted = Accepted;
        Task old = Start();
        await _cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Task current = Start();
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_worker.Inputs[2].Json)!.WorkUnits, Is.EqualTo(150_000_000));
        if (limit) {
            _worker.Jobs[1].SetException(new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage));
        }
        else {
            _worker.Jobs[1].SetResult(Result(1, .1));
        }
        await old;
        Assert.That(Accepted, Is.SameAs(accepted));
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(_cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(_cut.Find("button[title='Copy results']").HasAttribute("disabled"), Is.True);
        Assert.That(_cut.FindAll("[role='alert']"), Is.Empty);
        _worker.Jobs[2].SetResult(Result(2, .842));
        await current;
        Assert.That(Field<PinnedResultSnapshot>("_acceptedResultSnapshot").Context.HandSize, Is.EqualTo(6));
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
    }

    [Test]
    public async Task ReorderRenameAndCountChangesKeepIndependentCurrentAndPinnedRows() {
        await Accept();
        await Pin();
        PinnedResultSnapshot original = Pinned;
        await Call("MoveCombo", (0, 2));
        await Call("ReplaceCombo", (2, Field<List<Combo>>("_combos")[2].WithName("Renamed")));
        await Call("RenameGroup", ("g", "Renamed group"));
        await Call("ReplaceCard", (0, Field<List<Card>>("_cards")[0].WithCopies(3)));
        await Accept(.842);
        PinnedResultSnapshot current = Accepted;
        Assert.That(current.Combos[2].Label, Is.EqualTo("Renamed"));
        Assert.That(current.Combos[2].Probability, Is.EqualTo(.842));
        Assert.That(current.Total, Is.EqualTo(.842));
        Assert.That(Pinned, Is.SameAs(original));
        AssertContextDescriptions("Hand size 2 · 7 cards", "Hand size 2 · 8 cards");
        Assert.That(Pinned.Combos.Select(c => c.Label), Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
    }

    [Test]
    public async Task CurrentAndPinnedContextSummariesKeepTheSameCapturedFieldsAcrossEditsAndRecalculation() {
        await Accept(.814);
        await Pin();
        AssertContextDescriptions("Hand size 2 · 8 cards", "Hand size 2 · 8 cards");
        Assert.That(Pinned.Context.Copies, Is.EqualTo(8));
        Assert.That(Pinned.Context.CardCount, Is.EqualTo(2));

        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertContextDescriptions("Hand size 2 · 8 cards", "Hand size 2 · 8 cards");
        await Accept(.842);
        AssertContextDescriptions("Hand size 3 · 8 cards", "Hand size 2 · 8 cards");

        await Call("ReplaceCard", (0, Field<List<Card>>("_cards")[0].WithCopies(3)));
        await Call("ReplaceCard", (1, Field<List<Card>>("_cards")[1].WithActive(false)));
        // Pending edits do not leak into the last accepted result or the explicit pin.
        AssertContextDescriptions("Hand size 3 · 8 cards", "Hand size 2 · 8 cards");
        await Accept(.786);
        AssertContextDescriptions("Hand size 3 · 3 cards", "Hand size 2 · 8 cards");

        await Pin();
        AssertContextDescriptions("Hand size 3 · 3 cards", "Hand size 3 · 3 cards");
    }

    [TestCase("requirement")]
    [TestCase("mode")]
    [TestCase("multiplicity")]
    [TestCase("kind")]
    [TestCase("membership")]
    [TestCase("regroup")]
    public async Task StructuralChangesRemainIndependentSnapshots(string edit) {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        Combo route = Field<List<Combo>>("_combos")[0];
        Combo replacement = edit switch {
            "requirement" => route.WithCards([new("a", 0, 0)]),
            "mode" => route.WithCards([new("a", 1, 5, RequirementMaximumMode.HandSize)]),
            "multiplicity" => route.WithCards([new("a", 1, 5), new("a", 1, 5)]),
            "kind" => route.WithCards([]).WithCategories([new(role, 1, 5)]),
            "regroup" => route.WithGroup(null),
            _ => route.WithCategories([new(role, 1, 5)]).WithCards([])
        };
        await Call("ReplaceCombo", (0, replacement));
        if (edit == "membership") {
            await Call("ReplaceCard", (1, Field<List<Card>>("_cards")[1].WithCategories([role])));
        }
        await Accept(.842);
        PinnedResultSnapshot current = Accepted;
        Assert.That(current.Total, Is.EqualTo(.842));
        Assert.That(current.Combos[0].Probability, Is.EqualTo(.842));
        Assert.That(pin.Total, Is.EqualTo(.814));
        Assert.That(Pinned, Is.SameAs(pin));
    }

    [TestCase("file")]
    [TestCase("recovery")]
    [TestCase("accepted-example-path")]
    [TestCase("ydk")]
    public async Task AcceptedLoadsClearCurrentAndPreservePinnedSnapshot(string load) {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        SessionState replacement = Workspace(3);
        if (load == "file") {
            await Upload(replacement);
        }
        else if (load == "ydk") {
            _imports.Setup(x => x.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(replacement.Cards);
            await _cut.InvokeAsync(() => _cut.FindComponents<InputFile>()[0].Instance.OnChange.InvokeAsync(
                new InputFileChangeEventArgs([new SessionFile("fixture")])));
        }
        else if (load == "recovery") {
            await Call("ApplyRecoveryAsync", replacement);
        }
        else {
            object request = typeof(ProbabilityCalculatorComponent).GetMethod("BeginSessionLoad", Private)!.Invoke(_cut.Instance, [true])!;
            await Call("RestoreSessionDataAsync", replacement, request);
        }
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(_cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(_cut.FindAll(".pin-result-action"), Is.Empty);
        await Accept(.842);
        PinnedResultSnapshot current = Accepted;
        Assert.That(current.Total, Is.EqualTo(.842));
        Assert.That(Pinned, Is.SameAs(pin));
        string currentContext = load == "ydk"
            ? "Hand size 2 · 8 cards"
            : "Hand size 3 · 8 cards";
        AssertContextDescriptions(currentContext, "Hand size 2 · 8 cards");
    }

    [Test]
    public async Task CopyStillExportsOnlyTheAcceptedCurrentResultAndCapturedHandSize() {
        await Accept();
        await Pin();
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Accept(.842);
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        string text = (string)_clipboard.Invocations["copyText"].Single().Arguments[0]!;
        Assert.That(text, Does.Contain(.842.ToString("P2")).And.Not.Contain(.814.ToString("P2")));
        Assert.That(text, Does.Contain("Hand size: 3").And.Not.Contain("Pinned").And.Not.Contain("\\n"));
        Assert.That(_cut.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StaleCopyOwnsHandSizeRowsAndGroupsWithoutChangingEditedSession(bool pin) {
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "5" });
        Task calculation = Start();
        ProbabilityCalculationResult original = Result(0, .814);
        _worker.Jobs[0].SetResult(original);
        await calculation;
        if (pin) {
            await Pin();
        }
        string expected = "Previous result — current inputs have changed.\n" + ProbabilityResultSummaryFormatter.Format(
            _cut.FindComponent<ProbabilityResultExport>().Instance.Result!, 5, CultureInfo.GetCultureInfo("en-US"), "Progressive");
        // Even mutation of the _worker's returned lists cannot alter accepted display/export rows.
        ((List<ComboProbabilityResult>)original.ComboProbabilities).Clear();
        ((List<GroupProbabilityResult>)original.GroupProbabilities!).Clear();
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        await Call("MoveCombo", (0, 2));
        await Call("ReplaceCombo", (2, Field<List<Combo>>("_combos")[2].WithName("Edited route").WithGroup(null)));
        await Call("RenameGroup", ("g", "Edited group"));
        await Call("RemoveCombo", 0);
        await Call("ReplaceCard", (0, Field<List<Card>>("_cards")[0].WithCopies(3)));
        string session = _sessions.SerializeSession(FieldSession());
        int writes = _context.JSInterop.Invocations["sessionRecovery.update"].Count;
        AngleSharp.Dom.IElement copy = _cut.Find("button[title='Copy results']");
        Assert.That(copy.HasAttribute("disabled"), Is.False);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("aria-describedby"), Is.False);
        Assert.That(_cut.Find("#pinUnavailableReason").ClassList, Does.Contain("visually-hidden"));
        await copy.ClickAsync(new());
        Assert.That(_clipboard.Invocations["copyText"].Last().Arguments[0], Is.EqualTo(expected));
        Assert.That(_sessions.SerializeSession(FieldSession()), Is.EqualTo(session));
        Assert.That(_cut.FindComponent<SessionShareButton>().Instance.Snapshot, Is.EqualTo(session));
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
        Task running = Start();
        Assert.That(_cut.Find("button[title='Copy results']").HasAttribute("disabled"), Is.True);
        await _cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
        Assert.That(_cut.Find("button[title='Copy results']").HasAttribute("disabled"), Is.False);
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        Assert.That(_clipboard.Invocations["copyText"].Last().Arguments[0], Is.EqualTo(expected));
        _worker.Jobs[1].SetResult(Result(1, .1));
        await running;
        await Accept(.842);
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        string fresh = (string)_clipboard.Invocations["copyText"].Last().Arguments[0]!;
        Assert.That(fresh, Does.StartWith("Probability results\nMode: Progressive").And.Contain("Hand size: 6\nAny active combo: 84.20%"));
        Assert.That(fresh, Does.Contain("Edited route").And.Not.Contain("Previous result"));
    }

    [Test]
    public async Task SharedOfferDismissalAndGatedAcceptanceKeepPinAndFenceOldClipboardAndWorker() {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        NavigationManager navigation = _context.Services.GetRequiredService<NavigationManager>();
        SessionState replacement = Workspace(6);
        replacement.Combos[0] = replacement.Combos[0].WithName("Shared route");
        string link = SessionShareCodec.CreateLink(navigation.BaseUri, _sessions.SerializeSession(replacement));
        string before = _sessions.SerializeSession(FieldSession());
        await _cut.InvokeAsync(() => navigation.NavigateTo(link));
        await _cut.Find("section[aria-label='Shared session'] .btn-secondary").ClickAsync(new());
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(_sessions.SerializeSession(FieldSession()), Is.EqualTo(before));
        Assert.That(_cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Bunit.JSRuntimeInvocationHandler<bool> delayed = _clipboard.Setup<bool>("copyText", _ => true);
        Task copying = _cut.Find("button[title='Copy results']").ClickAsync(new());
        Task oldWork = Start();
        await _cut.InvokeAsync(() => navigation.NavigateTo(link));
        IRenderedComponent<SessionRecovery> recovery = _cut.FindComponent<SessionRecovery>();
        _cut.WaitForState(() => recovery.Instance.InspectionComplete);
        await Call("LoadSharedSessionAsync");
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(_cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(typeof(ProbabilityCalculatorComponent).GetField("_acceptedResultSnapshot", Private)!.GetValue(_cut.Instance), Is.Null);
        _worker.Jobs[1].SetResult(Result(1, .1));
        await oldWork;
        delayed.SetResult(false);
        await copying;
        Assert.That(_cut.FindAll(".probability-result-copy-fallback"), Is.Empty);
        Assert.That(_cut.FindAll(".probability-results"), Is.Empty);
        await Accept(.842);
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        Assert.That(_clipboard.Invocations["copyText"].Last().Arguments[0], Does.StartWith("Probability results\nMode: Progressive").And.Contain("Hand size: 6\nAny active combo: 84.20%"));
        string? shared = _cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        Assert.That(shared, Is.EqualTo(_sessions.SerializeSession(FieldSession())));
        Assert.That(shared, Does.Not.Contain("acceptedResultSnapshot").And.Not.Contain("pinnedResult").And.Not.Contain("Previous result").And.Not.Contain("84.20%"));
    }

    [TestCase("edit")] [TestCase("dismiss")] [TestCase("failure")]
    public async Task ObsoleteOrFailedSharedLoadRetainsPinAndAcceptedExportContext(string action) {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("_acceptedResultSnapshot");
        NavigationManager navigation = _context.Services.GetRequiredService<NavigationManager>();
        SessionState replacement = Workspace(6);
        replacement.Combos[0] = replacement.Combos[0].WithName("Obsolete shared route");
        await _cut.InvokeAsync(() => navigation.NavigateTo(SessionShareCodec.CreateLink(navigation.BaseUri, _sessions.SerializeSession(replacement))));
        _cut.WaitForState(() => _cut.FindComponent<SessionRecovery>().Instance.InspectionComplete);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock.Get(_context.Services.GetRequiredService<ILegacyCardMetadataEnricher>())
            .Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(() => {
                started.SetResult();
                return gate.Task;
            });
        Task loading = Call("LoadSharedSessionAsync");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        if (action == "edit") {
            await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (action == "dismiss") {
            await Call("DismissSharedSession");
        }
        // Enrichment failures are recoverable and intentionally do not block a valid load.
        // A failed session decode exercises the actual failure path before acceptance.
        else {
            await _cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + "#ygo-session=v1.%ZZ"));
        }
        gate.SetResult();
        await loading;
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(Field<PinnedResultSnapshot>("_acceptedResultSnapshot"), Is.SameAs(accepted));
        Assert.That(_cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(_sessions.SerializeSession(FieldSession()), Does.Not.Contain("Obsolete shared route"));
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        Assert.That(_clipboard.Invocations["copyText"].Last().Arguments[0], Does.Contain("Hand size: 2").And.Not.Contain("Obsolete shared route"));
    }

    [Test]
    public async Task MissingAcceptedContextFailsClosedRatherThanExportingLiveHandSize() {
        await Accept();
        typeof(ProbabilityCalculatorComponent).GetField("_acceptedResultSnapshot", Private)!.SetValue(_cut.Instance, null);
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(_cut.Find("button[title='Copy results']").HasAttribute("disabled"), Is.True);
        Assert.That(_clipboard.Invocations["copyText"], Is.Empty);
    }

    [Test]
    public async Task DrawEffectsModeAndPriorityDescriptionsStayWithTheirOwnSnapshots() {
        List<Card> cards = Field<List<Card>>("_cards");
        cards[0] = cards[0].WithDrawCount(2).WithDrawOncePerTurn(false);

        await Accept(.6);
        await Pin();
        PinnedResultSnapshot pinnedOn = Pinned;
        Assert.That((pinnedOn.Context.Model != CalculationModel.OpeningHand), Is.True);
        AssertContextDescriptions("Hand size 2 · 8 cards",
            "Hand size 2 · 8 cards");

        await _cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(_cut.Find(".probability-result-status").TextContent.Trim(), Is.EqualTo("Inputs changed"));
        Assert.That(_cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        await Accept(.75);

        CalculationInput offInput = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(_worker.Inputs.Last().Json)!;
        Assert.That(offInput.Cards[0].DrawCount, Is.Null);
        Assert.That((cards[0].DrawCount, cards[0].DrawOncePerTurn), Is.EqualTo(((int?)2, false)));
        AssertContextDescriptions("Hand size 2 · 8 cards",
            "Hand size 2 · 8 cards");
        Assert.That(Pinned.Context, Is.SameAs(pinnedOn.Context));
        Assert.That((Pinned.Context.Model != CalculationModel.OpeningHand), Is.True);
        Assert.That((Accepted.Context.Model != CalculationModel.OpeningHand), Is.False);
    }

    private sealed class SessionFile(string json) : IBrowserFile {
        public string Name => "fixture.json";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => Encoding.UTF8.GetByteCount(json);
        public string ContentType => "application/json";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ControlledCalculator : IBackgroundCalculator {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CalculationSnapshot> Inputs { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
            TaskCompletionSource<ProbabilityCalculationResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Inputs.Add(snapshot);
            Jobs.Add(source);
            return source.Task;
        }
    }

}
