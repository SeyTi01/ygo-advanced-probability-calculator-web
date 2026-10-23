using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public class CalculatorEditorDrawPriorityTest : CalculatorEditorTestBase {
    private static SessionState PrioritySession() => new() {
        Cards = [new([], name: "Duplicate name", id: "a", drawCount: 1), new([], name: "Duplicate name", id: "b", drawCount: 2), new([], 2, "Filler", id: "f")],
        Combos = [new([], "Retained B", cards: [new("b", 1, 1)])], HandSize = 2
    };

    [Test]
    public async Task SettingsDiscloseAccessibleOrderingAndPreserveFocusAndDisabledPreference() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(PrioritySession());
        Assert.That(cut.FindAll(".draw-priority-list"), Is.Empty);
        Assert.That(cut.Find("#drawActivationPriority").GetAttribute("value"), Is.EqualTo("EncounterOrder"));
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        Assert.That(cut.FindAll(".draw-priority-list li"), Has.Count.EqualTo(2));
        Assert.That(cut.FindComponent<SessionRecovery>().Instance.Session.DrawActivationPriority.CardIds, Is.EqualTo(Rows(cut)));
        Assert.That(Button(cut, "Move Duplicate name (entry 1) up").HasAttribute("disabled"), Is.True);
        Assert.That(Button(cut, "Move Duplicate name (entry 2) down").HasAttribute("disabled"), Is.True);
        int focusCalls = context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        await Button(cut, "Move Duplicate name (entry 2) up").ClickAsync(new());
        Assert.That(Rows(cut), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count, Is.GreaterThan(focusCalls));
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "EncounterOrder" });
        Assert.That(cut.FindAll(".draw-priority-list"), Is.Empty);
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        Assert.That(Rows(cut), Is.EqualTo(new[] { "b", "a" }));
        await cut.Find(".draw-priority-list button").KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
    }

    [TestCase(0), TestCase(1)]
    public async Task EmptyAndSingleEntryNeedNoExtraControls(int effects) {
        SessionState session = new() { Cards = effects == 0 ? [] : [new([], id: "single", drawCount: 1)] };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        Assert.That(cut.FindAll(".draw-priority-list li"), Has.Count.EqualTo(effects));
        Assert.That(cut.FindAll(".draw-priority-list button").All(button => button.HasAttribute("disabled")), Is.True);
    }

    [Test]
    public async Task PriorityInvalidatesPinnedResultAndTravelsThroughWorkerSaveShareAndRecovery() {
        List<CalculationSnapshot> requests = [];
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken _) => {
                requests.Add(snapshot);
                return Task.FromResult(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
            });
        context.Services.AddSingleton(background.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(PrioritySession());
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        await Button(cut, "Move Duplicate name (entry 2) up").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(Button(cut, "Replace pin with current result").HasAttribute("disabled"), Is.False);
        Assert.That(Button(cut, "Replace pin with current result").HasAttribute("aria-describedby"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".result-model").TextContent, Does.StartWith("Hand size ").And.EndWith(" cards"));
        Assert.That(cut.Find(".result-accuracy").TextContent, Does.StartWith("Mode: Progressive"));
        Assert.That(cut.Find(".pinned-result > p.small").TextContent, Does.StartWith("Hand size ").And.EndWith(" cards"));
        Assert.That(cut.FindAll(".result-priority-difference, .result-difference, .result-row-comparison, .pinned-match"), Is.Empty);
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(requests[^1].Json)!.Options!.DrawActivationPriority!.CardIds, Is.EqualTo(new[] { "b", "a" }));
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        SessionState loaded = await codec.LoadSessionAsync(SavedSessionJson());
        Assert.That(loaded.DrawActivationPriority.CardIds, Is.EqualTo(new[] { "b", "a" }));
        SessionState shared = await codec.LoadSessionAsync(cut.FindComponent<SessionShareButton>().Instance.Snapshot!);
        Assert.That(shared.DrawActivationPriority.CardIds, Is.EqualTo(loaded.DrawActivationPriority.CardIds));
        SessionState recovery = cut.FindComponent<SessionRecovery>().Instance.Session;
        Assert.That(recovery.DrawActivationPriority.CardIds, Is.EqualTo(loaded.DrawActivationPriority.CardIds));
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "EncounterOrder" });
        Assert.That(await cut.InvokeAsync(() => cut.FindComponent<SessionRecovery>().Instance.ApplyRecovery(loaded)), Is.True);
        Assert.That(Rows(cut), Is.EqualTo(new[] { "b", "a" }));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(codec.SerializeSession(PrioritySession()), "old.json"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#drawActivationPriority").GetAttribute("value"), Is.EqualTo("EncounterOrder")));
    }

    [Test]
    public async Task EntryLifecycleUsesWorkspaceIdsIndependentlyOfNamesAndVisualOrder() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(PrioritySession());
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        await Button(cut, "Move Duplicate name (entry 2) up").ClickAsync(new());
        CardListEditor list = cut.FindComponent<CardListEditor>().Instance;
        await cut.InvokeAsync(() => list.OnMoveCard.InvokeAsync((0, 1)));
        Assert.That(Rows(cut), Is.EqualTo(new[] { "b", "a" }));
        Card original = list.Cards.Single(card => card.Id == "b");
        await cut.InvokeAsync(() => list.OnReplaceCard.InvokeAsync((0, original.WithName("Renamed").WithActive(false))));
        Assert.That(Rows(cut), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(cut.Find("[data-priority-id='b']").TextContent, Does.Contain("Renamed"));
        await cut.InvokeAsync(() => list.OnDuplicateCard.InvokeAsync(0));
        Assert.That(Rows(cut).Take(2), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(cut.FindComponent<SessionRecovery>().Instance.Session.DrawActivationPriority.CardIds, Is.EqualTo(Rows(cut)));
        await cut.InvokeAsync(() => list.OnReplaceCard.InvokeAsync((0, original.WithDrawCount(null))));
        Assert.That(Rows(cut), Does.Not.Contain("b"));
        await cut.InvokeAsync(() => list.OnReplaceCard.InvokeAsync((0, original)));
        Assert.That(Rows(cut)[0], Is.EqualTo("b"));
        await cut.InvokeAsync(() => list.OnRemoveCard.InvokeAsync(0));
        Assert.That(cut.FindComponent<SessionRecovery>().Instance.Session.DrawActivationPriority.CardIds, Does.Not.Contain("b"));
    }

    [Test]
    public async Task PriorityChangeCancelsInFlightWorkerAndRejectsItsLateResult() {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => { captured = token; return completion.Task; });
        context.Services.AddSingleton(background.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(PrioritySession());
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(new(0.99, [new(0, "Late result", 0.99)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-total-value"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Late result"));
    }

    [Test]
    public async Task ImportClearsPriorityEvenWhenImporterReusesIds() {
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(PrioritySession().Cards);
        context.Services.AddSingleton(importer.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(PrioritySession());
        await cut.Find("#drawActivationPriority").ChangeAsync(new() { Value = "CustomPriority" });
        await Button(cut, "Move Duplicate name (entry 2) up").ClickAsync(new());
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123\n!side", "deck.ydk"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#drawActivationPriority").GetAttribute("value"), Is.EqualTo("EncounterOrder")));
    }

    private static string[] Rows(IRenderedFragment cut) => cut.FindAll(".draw-priority-list li").Select(row => row.GetAttribute("data-priority-id")!).ToArray();
}
