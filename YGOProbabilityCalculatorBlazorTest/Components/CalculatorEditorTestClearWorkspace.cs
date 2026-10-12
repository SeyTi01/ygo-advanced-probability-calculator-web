using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestClearWorkspace : CalculatorEditorTestBase {
    [Test]
    public async Task PopulatedClearCanBeCancelledAndResetsWorkspaceAndEditorDrafts() {
        SessionState session = Session();
        ComboGroup group = new("group-1", "Tier 1");
        session.ComboGroups.Add(group);
        session.Combos[0].GroupId = group.Id;
        session.CategoryColorIndices["A"] = 3;
        session.CategoryColorIndices["B"] = 6;
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);

        await cut.Find("[aria-label='Category name']").InputAsync(new() { Value = "Uncommitted category" });
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Uncommitted group" });
        await Button(cut, "Clear workspace").ClickAsync(new());

        Assert.That(cut.Find(".clear-workspace-confirmation").TextContent,
            Does.Contain("Clear the current deck, categories, and combos?"));
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Uncommitted category"));
        Assert.That(cut.Find("[aria-label='New combo group name']").GetAttribute("value"), Is.EqualTo("Uncommitted group"));

        await Button(cut, "Cancel").ClickAsync(new());

        Assert.That(cut.FindAll(".clear-workspace-confirmation"), Is.Empty);
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Uncommitted category"));
        Assert.That(cut.Find("[aria-label='New combo group name']").GetAttribute("value"), Is.EqualTo("Uncommitted group"));

        await Button(cut, "Clear workspace").ClickAsync(new());
        await Button(cut, "Clear").ClickAsync(new());

        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.FindComponents<ComboEditor>(), Is.Empty);
        Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases, Is.Empty);
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryColorIndices, Is.Empty);
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("5"));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(cut.Find("[aria-label='New combo group name']").GetAttribute("value"), Is.Null.Or.Empty);

        await Button(cut, "Clear workspace").ClickAsync(new());
        Assert.That(cut.FindAll(".clear-workspace-confirmation"), Is.Empty);
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.FindComponents<ComboEditor>(), Is.Empty);
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("5"));

        await Button(cut, "Add New Card").ClickAsync(new());
        await Button(cut, "Add New Combo").ClickAsync(new());
        IRenderedComponent<CardEditor> freshCard = cut.FindComponent<CardEditor>();
        IRenderedComponent<ComboEditor> freshCombo = cut.FindComponent<ComboEditor>();
        Assert.That(freshCard.Instance.Card.Name, Is.Null.Or.Empty);
        Assert.That(freshCombo.Instance.Combo.Categories, Is.Empty);
        if (freshCard.Find(".accordion-button").GetAttribute("aria-expanded") != "true") {
            await freshCard.Find(".accordion-button").ClickAsync(new());
        }
        if (freshCombo.Find(".accordion-button").GetAttribute("aria-expanded") != "true") {
            await freshCombo.Find(".accordion-button").ClickAsync(new());
        }

        await freshCard.Find("#cardCategory0").ChangeAsync(new() { Value = "metadata:attribute:dark" });
        await Button(freshCard, "Add").ClickAsync(new());
        await freshCombo.Find("#comboTarget0").ChangeAsync(new() { Value = "metadata:level:8" });
        await Button(freshCombo, "Add").ClickAsync(new());
        Assert.That(freshCard.Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { "attribute:dark" }));
        Assert.That(freshCombo.Instance.Combo.Categories.Single().BaseCategory.MetadataKey, Is.EqualTo("level:8"));
    }

    [Test]
    public async Task ClearRemovesResultsAndPinWhilePreservingCalculationPreferences() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await Button(cut, "Calculate").ClickAsync(new());
        await cut.Find(".pin-result-action").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".pinned-result"), Has.Count.EqualTo(1));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "VeryHigh" });
        await Button(cut, "Clear workspace").ClickAsync(new());
        Assert.That(cut.FindAll(".clear-workspace-confirmation"), Has.Count.EqualTo(1));
        await Button(cut, "Clear").ClickAsync(new());

        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        Assert.That(cut.FindAll(".result-comparison"), Is.Empty);
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
        Assert.That(cut.Find("#calculationModel").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("VeryHigh"));

        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.DrawEffectsEnabled, Is.False);
        Assert.That(saved.Categories, Is.Empty);
        Assert.That(saved.Cards, Is.Empty);
        Assert.That(saved.Combos, Is.Empty);
        Assert.That(saved.ComboGroups, Is.Empty);
        Assert.That(saved.HandSize, Is.EqualTo(5));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Configured" });
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Configured" });
        await cut.Find("#storageSafetyTier").ChangeAsync(new() { Value = "High" });
        await Button(cut, "Clear workspace").ClickAsync(new());

        Assert.That(cut.FindAll(".clear-workspace-confirmation"), Is.Empty,
            "runtime preferences alone must not require confirmation");
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
        Assert.That(cut.Find("#storageSafetyTier").GetAttribute("value"), Is.EqualTo("High"));
    }

    [Test]
    public async Task ClearCancelsRunningCalculationAndRejectsItsLateResult() {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IBackgroundCalculator> calculator = new();
        calculator.Setup(service => service.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken cancellationToken) => {
                started.SetResult();
                return completion.Task;
            });
        context.Services.AddSingleton(calculator.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());

        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Button(cut, "Clear workspace").ClickAsync(new());
        await Button(cut, "Clear").ClickAsync(new());

        Assert.That(cut.Find(".calculate-action > button").TextContent, Does.Contain("Calculate"));
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("false"));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);

        completion.SetResult(new(0.9, [new(0, "Late result", 0.8)]));
        await calculation;

        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Late result"));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task ClearRevokesPendingYdkeImportAndDiscardsItsLateDeck() {
        TaskCompletionSource<List<Card>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(It.IsAny<string>()))
            .Returns(() => {
                started.SetResult();
                return completion.Task;
            });
        context.Services.AddSingleton(importer.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());

        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "ydke://pending" });
        Task import = Button(cut, "Import").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Button(cut, "Clear workspace").ClickAsync(new());
        await Button(cut, "Clear").ClickAsync(new());
        completion.SetResult([new([], name: "Late deck")]);
        await import;

        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.FindAll("#ydkeCodeInput"), Is.Empty);
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }
}
