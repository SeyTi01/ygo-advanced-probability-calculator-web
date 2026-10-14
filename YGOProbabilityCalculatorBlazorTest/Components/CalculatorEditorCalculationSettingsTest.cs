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
    public async Task SettingsMenuHasThreeExplicitModesAndRestoresFocusContract() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IElement trigger = cut.Find(".calculation-settings-trigger");
        Assert.That(trigger.GetAttribute("aria-label"), Is.EqualTo("Calculation settings"));
        Assert.That(trigger.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        await trigger.ClickAsync(new());
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.False);
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
        Assert.That(cut.FindAll("#calculationModel option"), Has.Count.EqualTo(3));
        Assert.That(cut.Find("#calculationModel").GetAttribute("aria-describedby"), Is.EqualTo("drawEffectsHelp"));
        Assert.That(cut.Find("#drawEffectsHelp").TextContent, Does.Contain("pre-draw costs").And.Contain("post-draw removals"));
        context.JSInterop.VerifyInvoke("workspaceMenus.watchOutside", 1);
        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "High" });
        await cut.Find("#calculationModel").KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Simplified" });
        Assert.That(cut.Find("#drawEffectsHelp").TextContent,
            Does.Contain("Hypothetical").And.Contain("both pre-draw costs and post-draw removals").And.Contain("Trade-In").And.Contain("unconditionally"));
        await cut.InvokeAsync(() => cut.FindComponent<CalculationSettingsMenu>().Instance.Dismiss());
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
        Assert.That(cut.Find(".calculation-settings-trigger").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Simplified"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RestoreHonorsSavedDrawPreferenceAndResetsRuntimeSimplification(bool enabled) {
        SessionState session = ConditionalSession(enabled);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo(enabled ? "Configured" : "OpeningHand"));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Simplified" });
        string source = context.Services.GetRequiredService<ISessionService>().SerializeSession(session);
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(source, "session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo(enabled ? "Configured" : "OpeningHand")));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.PostDrawDiscardCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FreshAndLegacyOnSessionsUseConfiguredMode() {
        IRenderedComponent<ProbabilityCalculatorComponent> fresh = Render();
        Assert.That(fresh.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        JsonObject legacy = JsonNode.Parse(codec.SerializeSession(ConditionalSession()))!.AsObject();
        legacy.Remove(nameof(SessionState.DrawEffectsEnabled));
        legacy[nameof(SessionState.SchemaVersion)] = 3;
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(await codec.LoadSessionAsync(legacy.ToJsonString()));
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
    }

    [Test]
    public async Task AllModesTravelOnSnapshotsLabelResultsAndPreserveCardSettings() {
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
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        foreach (CalculationModel mode in new[] { CalculationModel.Configured, CalculationModel.OpeningHand, CalculationModel.Configured, CalculationModel.Simplified }) {
            await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode.ToString() });
            await Button(cut, "Calculate").ClickAsync(new());
            CalculationInput input = requests[^1];
            Assert.That(input.Options!.ConditionalEffectsEnabled, Is.EqualTo(mode == CalculationModel.Configured));
            Assert.That(input.Cards[0].DrawCount, Is.EqualTo(mode == CalculationModel.OpeningHand ? null : (int?)1));
            Assert.That(input.Cards[0].PostDrawDiscardCount, Is.EqualTo(mode == CalculationModel.OpeningHand ? 0 : 1));
            List<Card> modeled = mode == CalculationModel.OpeningHand
                ? session.Cards.Select(card => card.WithDrawCount(null)).ToList() : session.Cards;
            if (mode == CalculationModel.Simplified) {
                modeled = modeled.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0)).ToList();
            }
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, 5, session.Combos, []);
            double expected = new ExactProbability(oracle.Wins[0], oracle.Total).ToDouble(new(CalculationWorkPolicy.Interactive));
            Assert.That(cut.Find(".probability-total-value").TextContent.Trim(), Is.EqualTo(expected.ToString("P2")));
            Assert.That(cut.Find(".result-model").TextContent, Does.EndWith(mode.Description()));
            Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.ModelDescription, Is.EqualTo(mode.Description()));
        }
        Assert.That(cut.Find(".probability-results").TextContent, Does.Contain("Hypothetical, often optimistic"));
        await Button(cut, "Save Session").ClickAsync(new());
        string saved = SavedSessionJson();
        Assert.That(saved, Does.Not.Contain("ConditionalEffectsEnabled").And.Not.Contain("StorageTier"));
        SessionState stored = await codec.LoadSessionAsync(saved);
        Assert.That(stored.SchemaVersion, Is.EqualTo(5));
        Assert.That(stored.Cards[0].PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(stored.DrawEffectsEnabled, Is.True);
        Assert.That(Render(stored).Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
    }

    [TestCase("OpeningHand")]
    [TestCase("Simplified")]
    public async Task ChangingModelCancelsAndRejectsLateResult(string model) {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => { captured = token; return completion.Task; });
        context.Services.AddSingleton(background.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(ConditionalSession());
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = model });
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(new(0.99, [new(0, "Late result", 0.99)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-total-value"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Late result"));
    }

    private static SessionState ConditionalSession(bool draws = true) {
        CategoryBase role = new("Role");
        return new() {
            Categories = [role], HandSize = 5, DrawEffectsEnabled = draws,
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
        Assert.That(input[nameof(CalculationInput.WorkUnits)]!.GetValue<long>(), Is.EqualTo(150_000_000));
    }
}
