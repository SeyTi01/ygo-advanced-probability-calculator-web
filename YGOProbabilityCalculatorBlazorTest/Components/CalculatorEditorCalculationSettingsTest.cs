using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Components.Shared;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorCalculationSettingsTest : CalculatorEditorTestBase {
    [Test]
    public async Task SettingsMenuOpensClosesWithEscapeAndKeepsItsValues() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IElement trigger = cut.Find(".calculation-settings-trigger");

        Assert.That(cut.FindAll(".calculation-settings-trigger").ToArray(), Has.Length.EqualTo(1));
        Assert.That(trigger.GetAttribute("aria-label"), Is.EqualTo("Calculation settings"));
        Assert.That(trigger.GetAttribute("title"), Is.EqualTo("Calculation settings"));
        Assert.That(trigger.GetAttribute("aria-haspopup"), Is.EqualTo("dialog"));
        Assert.That(trigger.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(cut.FindAll(".draw-effects-control, .draw-effects-help").ToArray(), Is.Empty);
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("2"));

        await trigger.ClickAsync(new());
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.False);
        Assert.That(cut.Find("#drawEffectsEnabled").HasAttribute("checked"), Is.True);
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("Standard"));
        Assert.That(cut.Find("[aria-labelledby='drawSimulationHeading'] #drawEffectsEnabled"), Is.Not.Null);
        Assert.That(cut.Find("#drawSimulationHeading").TextContent, Is.EqualTo("Draw simulation"));
        Assert.That(cut.Find("[aria-labelledby='drawSimulationHeading'] .advanced-draw-option #conditionalEffectsEnabled"), Is.Not.Null);
        Assert.That(cut.Find("label[for='conditionalEffectsEnabled']").TextContent, Is.EqualTo("Advanced draw simulation"));
        Assert.That(cut.Find("#conditionalEffectsHelp").TextContent, Does.Contain("Adds post-draw discards to Basic simulation").And.Contain("Requires Draw effects"));
        Assert.That(cut.FindAll("[aria-labelledby='drawSimulationHeading'] #storageSafetyTier").ToArray(), Is.Empty);
        Assert.That(cut.Find("#storageSafetyTier").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find("#conditionalEffectsEnabled").GetAttribute("role"), Is.EqualTo("switch"));
        Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
        Assert.That(cut.Find("#conditionalEffectsEnabled").GetAttribute("aria-describedby"), Is.EqualTo("conditionalEffectsHelp"));

        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "High" });
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));

        await cut.Find("#storageSafetyTier").KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
        Assert.That(cut.Find(".calculation-settings-trigger").GetAttribute("aria-expanded"), Is.EqualTo("false"));

        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));
        Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
    }

    [Test]
    public async Task BasicAdvancedAndGlobalModesPreserveCardsInvalidateResultsAndKeepPinnedLineage() {
        SessionState session = ConditionalSession();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await Button(cut, "Calculate").ClickAsync(new());
        AssertProbability(false, true);
        await cut.Find(".pin-result-action").ClickAsync(new());
        PinnedResultSnapshot pinned = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        Assert.That(pinned.Context.Description, Does.EndWith("Draw effects on · Basic"));

        foreach ((bool global, bool advanced) in new[] { (true, true), (false, false), (true, false), (true, true) }) {
            await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = global });
            await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = advanced });
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.EqualTo(advanced));
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("disabled"), Is.EqualTo(!global));
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
            Assert.That(cut.FindComponents<CardEditor>().ToArray()[0].Instance.Card.PostDrawDiscardCount, Is.EqualTo(1));
            await Button(cut, "Calculate").ClickAsync(new());
            AssertProbability(advanced, global);
            Assert.That(cut.Find(".probability-results > p.small").TextContent.Trim(),
                Does.EndWith($"Draw effects {(global ? "on" : "off")} · {(advanced ? "Advanced" : "Basic")}"));
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pinned));
            Assert.That(cut.Find(".probability-results").TextContent,
                Does.Not.Contain("Unrelated session").And.Not.Contain("Definition changed").And.Not.Contain("New route"));
        }

        void AssertProbability(bool advanced, bool global) {
            List<Card> modeled = global ? session.Cards : session.Cards.Select(card => card.WithDrawCount(null)).ToList();
            if (!advanced) {
                modeled = modeled.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
            }
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, 5, session.Combos, []);
            double expected = new ExactProbability(oracle.Wins[0], oracle.Total).ToDouble(new(CalculationWorkPolicy.Interactive));
            Assert.That(cut.Find(".probability-total-value").TextContent.Trim(), Is.EqualTo(expected.ToString("P2")));
        }
    }

    [Test]
    public async Task TurningDrawEffectsOffClearsAdvancedAndReenablingKeepsBasicMode() {
        List<CalculationInput> requests = [];
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken _) => {
                requests.Add(System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!);
                return Task.FromResult(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
            });
        context.Services.AddSingleton(background.Object);
        SessionState session = ConditionalSession();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(requests[^1].Options!.ConditionalEffectsEnabled, Is.True);

        await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = false });
        AssertAdvancedUnavailable();
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("#storageSafetyTier").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindComponents<CardEditor>().ToArray()[0].Instance.Card.PostDrawDiscardCount, Is.EqualTo(1));
        await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        AssertAdvancedUnavailable();
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(requests[^1].Options!.ConditionalEffectsEnabled, Is.False);
        Assert.That(requests[^1].Cards.All(card => card.DrawCount is null && card.PostDrawDiscardCount == 0), Is.True);

        await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = true });
        Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(requests[^1].Options!.ConditionalEffectsEnabled, Is.False);
        Assert.That(requests[^1].Cards[0].PostDrawDiscardCount, Is.EqualTo(1));

        await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        JsonObject offSession = JsonNode.Parse(context.Services.GetRequiredService<ISessionService>().SerializeSession(session))!.AsObject();
        offSession[nameof(SessionState.DrawEffectsEnabled)] = false;
        cut.FindComponents<InputFile>().ToArray()[1].UploadFiles(InputFileContent.CreateFromText(offSession.ToJsonString(), "off.json"));
        cut.WaitForAssertion(AssertAdvancedUnavailable);

        void AssertAdvancedUnavailable() {
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("disabled"), Is.True);
        }
    }

    [Test]
    public async Task AdvancedIsRuntimeOnlyAndSessionLoadsAndImportsCannotEnableIt() {
        SessionState session = ConditionalSession();
        Mock<IDeckImportService> imports = new();
        imports.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(session.Cards);
        context.Services.AddSingleton(imports.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string source = codec.SerializeSession(session);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = false });
        await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = true });
        Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
        cut.FindComponents<InputFile>().ToArray()[1].UploadFiles(InputFileContent.CreateFromText(source, "session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False));
        cut.FindComponents<InputFile>().ToArray()[0].UploadFiles(InputFileContent.CreateFromText("#main\n79571449", "deck.ydk"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False));
        await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        Assert.That(cut.Find("#storageSafetyTier").HasAttribute("disabled"), Is.False);
        string shared = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        Assert.That(shared, Does.Not.Contain("ConditionalEffectsEnabled").And.Not.Contain("AdvancedCalculation"));
        await Button(cut, "Save Session").ClickAsync(new());
        string saved = SavedSessionJson();
        Assert.That(saved, Does.Not.Contain("ConditionalEffectsEnabled").And.Not.Contain("AdvancedCalculation"));
        SessionState stored = await codec.LoadSessionAsync(saved);
        Assert.That(stored.SchemaVersion, Is.EqualTo(4));
        Assert.That(stored.Cards[0].PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(stored.DrawEffectsEnabled, Is.True);
        IRenderedComponent<ProbabilityCalculatorComponent> fresh = Render(stored);
        await fresh.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(fresh.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangingDrawSimulationSettingsCancelsRunningRequestAndRejectsItsLateResult(bool disableDrawEffects) {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => {
                captured = token;
                return completion.Task;
            });
        context.Services.AddSingleton(background.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(ConditionalSession());
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        if (disableDrawEffects) {
            await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        }

        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        if (disableDrawEffects) {
            await cut.Find("#drawEffectsEnabled").ChangeAsync(new() { Value = false });
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("checked"), Is.False);
            Assert.That(cut.Find("#conditionalEffectsEnabled").HasAttribute("disabled"), Is.True);
        }
        else {
            await cut.Find("#conditionalEffectsEnabled").ChangeAsync(new() { Value = true });
        }
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(new(0.99, [new(0, "Late result", 0.99)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-total-value").ToArray(), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Late result"));
        Assert.That(cut.FindComponents<CardEditor>().ToArray()[0].Instance.Card.PostDrawDiscardCount, Is.EqualTo(1));
    }

    private static SessionState ConditionalSession() {
        CategoryBase role = new("Role");
        return new() {
            Categories = [role], HandSize = 5,
            Cards = [new([], name: "Source", id: "source", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1),
                new([role], 5, "Ordinary", id: "ordinary")],
            Combos = [new([new(role, 5, 5)], "Five ordinary")]
        };
    }

    [Test]
    public async Task StorageTierInvalidatesResultsTravelsOnTheNextWorkerRequestAndStaysOutOfSessions() {
        List<CalculationSnapshot> snapshots = [];
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken _) => {
                snapshots.Add(snapshot);
                string response = CalculationWire.Execute(snapshot.Json);
                return Task.FromResult(CalculationWire.ReadResult(response));
            });
        context.Services.AddSingleton(background.Object);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(snapshots, Has.Count.EqualTo(1));
        string standardRequest = snapshots[0].Json;
        AssertTier(standardRequest, CalculationStorageTier.Standard);

        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "High" });
        AssertPreviousResult(cut);
        Assert.That(snapshots, Has.Count.EqualTo(1));
        AssertTier(standardRequest, CalculationStorageTier.Standard);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(snapshots, Has.Count.EqualTo(2));
        AssertTier(snapshots[1].Json, CalculationStorageTier.High);
        AssertWorkAllowance(snapshots[1].Json);

        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "VeryHigh" });
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(snapshots, Has.Count.EqualTo(3));
        AssertTier(snapshots[2].Json, CalculationStorageTier.VeryHigh);
        AssertWorkAllowance(snapshots[2].Json);

        string sharedJson = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        Assert.That(sharedJson, Does.Not.Contain("StorageTier"));
        Assert.That(sharedJson, Does.Not.Contain("StorageSafety"));

        await Button(cut, "Save Session").ClickAsync(new());
        string savedJson = SavedSessionJson();
        Assert.That(savedJson, Does.Not.Contain("StorageTier"));
        Assert.That(savedJson, Does.Not.Contain("StorageSafety"));
        Assert.That(JsonNode.Parse(savedJson)![nameof(SessionState.SchemaVersion)]!.GetValue<int>(),
            Is.EqualTo(SessionState.CurrentSchemaVersion));

        string replacementSession = context.Services.GetRequiredService<ISessionService>().SerializeSession(Session());
        cut.FindComponents<InputFile>().ToArray()[1]
            .UploadFiles(InputFileContent.CreateFromText(replacementSession, "replacement.json"));
        cut.WaitForAssertion(() =>
            Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("VeryHigh")));
    }

    private static void AssertTier(string request, CalculationStorageTier expected) {
        JsonObject input = JsonNode.Parse(request)!.AsObject();
        Assert.That(input[nameof(CalculationInput.StorageTier)]!.GetValue<int>(), Is.EqualTo((int)expected));
    }

    private static void AssertWorkAllowance(string request) {
        JsonObject input = JsonNode.Parse(request)!.AsObject();
        Assert.That(input[nameof(CalculationInput.WorkUnits)]!.GetValue<long>(), Is.EqualTo(50_000_000));
    }
}
