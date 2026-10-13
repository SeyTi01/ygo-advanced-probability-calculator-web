using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public abstract class CalculatorEditorTestBase {
    protected TestContext context = null!;
    protected readonly CategoryBase a = new("A");
    protected readonly CategoryBase b = new("B");

    [SetUp]
    public void SetUp() {
        context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IBackgroundCalculator, BackgroundCalculatorTestAdapter>();
        context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.SearchCardsAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<CardInfo>());
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo>(StringComparer.Ordinal));
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<ICardArtworkService, CardArtworkService>();
        context.Services.AddSingleton<ILegacyCardMetadataEnricher, LegacyCardMetadataEnricher>();
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    protected IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState? session = null) {
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    // Synchronous bUnit events discard their dispatcher task; calculation tests await events
    // before asserting or editing again, and retain pending calculation tasks until release.
    // Patched AngleSharp is binary-incompatible with bUnit 1.x's FindAll indexer.
    // Use Single for unique queries or ToArray before indexing; requery after edits.
    protected static IElement Button(IRenderedFragment fragment, string text) =>
        fragment.FindAll("button").Single(element => element.TextContent.Trim() == text || element.GetAttribute("aria-label") == text);

    protected static void OpenSimulation(IRenderedFragment editor) {
        if (editor.FindAll(".simulation-editor").Count == 0) {
            editor.Find(".simulation-edit").Click();
        }
    }

    protected static async Task SaveSimulation(IRenderedFragment editor) {
        if (!editor.Find(".simulation-save").HasAttribute("disabled")) {
            await editor.Find(".simulation-save").ClickAsync(new());
            OpenSimulation(editor);
        }
    }

    protected static string SimulationDetails(IRenderedFragment editor) {
        if (editor.FindAll(".simulation-details").Count == 0) {
            editor.Find(".simulation-details-toggle").Click();
        }

        return editor.Find(".simulation-details").TextContent;
    }

    protected static string CardTarget(string id) => string.IsNullOrEmpty(id) ? string.Empty : $"card:{id}";

    protected static Task EditRequirement(IRenderedFragment editor, string target) =>
        editor.Find($"button[data-requirement='{target}']").ClickAsync(new());

    protected static async Task ExtendRequirement(IRenderedFragment editor, string target, bool sameCard = false) {
        if (target.StartsWith("group:", StringComparison.Ordinal)) {
            target = $"alternative:{target[6..]}:0";
        }
        await EditRequirement(editor, target);
        await editor.Find(sameCard ? "[data-action='property']" : "[data-action='alternative']").ClickAsync(new());
    }

    protected static async Task DeleteRequirement(IRenderedFragment editor, string target) {
        if (editor.FindAll(".alternative-scope").Count > 0) {
            await Button(editor, "Cancel").ClickAsync(new());
        }
        await EditRequirement(editor, target);
        await Button(editor, target.StartsWith("alternative:", StringComparison.Ordinal) ? "Remove alternative" : "Remove requirement").ClickAsync(new());
    }

    protected static async Task RemoveProperty(IRenderedFragment editor, string target, int property) {
        await EditRequirement(editor, target);
        await editor.Find($"[aria-label='Remove property {property}']").ClickAsync(new());
        await Button(editor, "Save").ClickAsync(new());
    }

    protected static void AssertAnyMaximumDraft(IRenderedFragment fragment, int index = 0) {
        IElement maximum = fragment.Find($"#maxCount{index}");
        Assert.That(maximum.GetAttribute("value"), Is.EqualTo(string.Empty));
        Assert.That(maximum.GetAttribute("placeholder"), Is.EqualTo("Any"));
        Assert.That(maximum.GetAttribute("title"), Is.EqualTo("Leave empty for Any"));
        Assert.That(maximum.HasAttribute("disabled"), Is.False);
        Assert.That(fragment.FindAll($"#maxAny{index}"), Is.Empty);
    }

    protected static async Task DuplicateComboAsync(IRenderedFragment fragment, int index) {
        IRenderedComponent<ComboEditor> editor = fragment.FindComponents<ComboEditor>()[index];

        await FirstRowAction(editor, "combo", "Duplicate").ClickAsync(new());
    }

    protected static void AssertPreviousResult(IRenderedFragment fragment) {
        Assert.That(fragment.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(fragment.Find(".probability-result-status .visually-hidden").TextContent.Trim(),
            Is.EqualTo("Previous result · inputs changed"));
    }

    protected string SavedSessionJson(int saveNumber = 0) {
        JSRuntimeInvocation invocation = context.JSInterop.Invocations
            .Where(invocation => invocation.Arguments.Count == 2 &&
                invocation.Arguments[0] is string fileName && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                invocation.Arguments[1] is string)
            .ElementAt(saveNumber);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
    }

    protected static async Task RenameGroupWithEnter(IRenderedFragment fragment, string oldName, string newName) {
        await fragment.Find($"[aria-label='Edit group {oldName}']").ClickAsync(new());
        IElement input = fragment.Find($"[aria-label='New name for group {oldName}']");
        await input.InputAsync(new() { Value = newName });
        await input.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }

    protected SessionState Session() => new() {
        Categories = [a, b],
        Cards = [new([a], 2, "First"), new([b], 2, "Second")],
        Combos = [new([new(a, 1, 2)], "First combo"), new([new(b, 1, 2)], "Second combo")],
        HandSize = 2
    };

    protected sealed class CountingProbabilityCalculator : IProbabilityCalculatorService {
        public int CallCount { get; private set; }

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.25;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck,
            List<Combo> combos,
            int handSize,
            IReadOnlyList<ComboGroup>? groups = null
        ) {
            CallCount++;
            return new ProbabilityCalculationResult(
                0.25,
                [new ComboProbabilityResult(0, "Any A", 0.25)]);
        }
    }

    protected sealed class DelayedProbabilityCalculator : IProbabilityCalculatorService {
        public bool ExceedLimit { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Continue { get; } = new(false);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck,
            List<Combo> combos,
            int handSize,
            IReadOnlyList<ComboGroup>? groups = null
        ) {
            Started.SetResult();
            if (!Continue.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The test did not release the delayed calculation.");
            }

            if (ExceedLimit) {
                throw new ProbabilityCalculationLimitException();
            }

            return new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Stale combo", 0.5)]);
        }
    }

    protected sealed class SequencedProbabilityCalculator(
        ProbabilityCalculationResult secondResult,
        bool failSecond = false
    ) : IProbabilityCalculatorService {
        private int _callCount;

        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ContinueSecond { get; } = new(false);
        public int CallCount => Volatile.Read(ref _callCount);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck,
            List<Combo> combos,
            int handSize,
            IReadOnlyList<ComboGroup>? groups = null
        ) {
            int call = Interlocked.Increment(ref _callCount);

            if (call == 1) {
                return new ProbabilityCalculationResult(
                    0.25,
                    [new ComboProbabilityResult(0, "Original combo", 0.2)]);
            }

            SecondStarted.SetResult();

            if (!ContinueSecond.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The test did not release the second calculation.");
            }

            if (failSecond) {
                throw new InvalidOperationException("expected test failure");
            }

            return secondResult;
        }
    }
}
