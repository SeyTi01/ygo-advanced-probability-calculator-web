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

        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "High" });
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));

        await cut.Find("#storageSafetyTier").KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
        Assert.That(cut.Find(".calculation-settings-trigger").GetAttribute("aria-expanded"), Is.EqualTo("false"));

        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));
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
