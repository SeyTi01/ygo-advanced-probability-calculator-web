using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
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

[TestFixture]
public class CategoryRenameTest {
    private TestContext context = null!;

    [SetUp]
    public void SetUp() {
        context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    private IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState? session = null) {
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    private static SessionState SessionWithOverlappingReferences() {
        var category = new CategoryBase("Old");
        var otherCategory = new CategoryBase("Other");

        // Reference objects are deliberately independent of the definitions, as they are
        // after deserializing a saved session with the existing JSON converters.
        return new SessionState {
            Categories = [category, otherCategory],
            Cards = [
                new([new("Old"), new("Other")], 2, "Shared"),
                new([new("Old")], 1, "Old only"),
                new([new("Other")], 2, "Other only"),
                new([], 1, "Uncategorized")
            ],
            Combos = [
                new([new(new("Old"), 1, 2), new(new("Other"), 1, 3)], "Overlap"),
                new([new(new("Old"), 2, 2)], "Old only"),
                new([new(new("Old"), 0, 2), new(new("Old"), 1, 2)], "Duplicate constraints")
            ],
            HandSize = 3
        };
    }

    [Test]
    public void RenameReplacesLogicalReferencesAndPreservesProbabilityAndEditorDrafts() {
        var session = SessionWithOverlappingReferences();
        var originalCards = session.Cards.ToArray();
        var originalCombos = session.Combos.ToArray();
        var expectedConstraints = session.Combos.Select(combo => combo.Categories
            .Select(category => (category.BaseCategory.Name, category.MinCount, category.MaxCount)).ToArray()).ToArray();
        var oracleProbability = SmallDeckOracleTest.EnumerateProbability(session.Cards, session.Combos, session.HandSize);
        var calculator = new ProbabilityCalculatorService();
        var probabilityBefore = calculator.CalculateProbabilityForCombos(session.Cards, session.Combos, session.HandSize);
        Assert.That(probabilityBefore, Is.EqualTo(oracleProbability).Within(1e-12));

        var cut = Render(session);
        var card = cut.FindComponents<CardEditor>()[0];
        var combo = cut.FindComponents<ComboEditor>()[0];
        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select").Change("Old");
        combo.Find("select").Change("Old");
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("1");

        RenameCategory(cut, "Old", "Renamed");

        Assert.That(cut.FindComponents<CardEditor>()[0], Is.SameAs(card));
        Assert.That(cut.FindComponents<ComboEditor>()[0], Is.SameAs(combo));
        Assert.That(card.Instance.Card, Is.SameAs(originalCards[0]));
        Assert.That(combo.Instance.Combo, Is.SameAs(originalCombos[0]));
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("Renamed"));
        Assert.That(combo.Find("select").GetAttribute("value"), Is.EqualTo("Renamed"));
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));

        var definitions = cut.FindComponent<CategoryListEditor>().Instance.CategoryBases;
        Assert.That(definitions.Select(category => category.Name), Is.EqualTo(new[] { "Renamed", "Other" }));
        Assert.That(session.Cards.SelectMany(item => item.Categories).Any(category => category.Name == "Old"), Is.False);
        Assert.That(session.Combos.SelectMany(item => item.Categories).Any(category => category.BaseCategory.Name == "Old"), Is.False);
        Assert.That(session.Cards.SelectMany(item => item.Categories).Count(category => category.Name == "Renamed"), Is.EqualTo(2));
        Assert.That(session.Cards[0].Categories.Select(category => category.Name), Is.EqualTo(new[] { "Renamed", "Other" }));
        Assert.That(session.Combos.Select(comboItem => comboItem.Categories
            .Select(category => (category.BaseCategory.Name, category.MinCount, category.MaxCount)).ToArray()),
            Is.EqualTo(expectedConstraints.Select(constraints => constraints
                .Select(category => (category.Name == "Old" ? "Renamed" : category.Name, category.MinCount, category.MaxCount)).ToArray())));
        Assert.That(session.Combos[2].Categories.Select(category => category.BaseCategory.Name),
            Is.EqualTo(new[] { "Renamed", "Renamed" }));

        var probabilityAfter = calculator.CalculateProbabilityForCombos(session.Cards, session.Combos, session.HandSize);
        Assert.That(probabilityAfter, Is.EqualTo(oracleProbability).Within(1e-12));
        Assert.That(probabilityAfter, Is.EqualTo(probabilityBefore).Within(1e-12));

        Button(combo, "Update").Click();
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("Renamed (0–1)"));
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void RenameRejectsBlankAndCaseInsensitiveDuplicateNamesButAllowsCancelAndCaseOnlyRename() {
        var cut = Render(SessionWithOverlappingReferences());
        BeginRename(cut, "Old");

        AssertIconButton(cut, "Save category name");
        AssertIconButton(cut, "Cancel category rename");

        cut.Find("[aria-label='New name for category Old']").Input("  ");
        Button(cut, "Save category name").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));

        cut.Find("[aria-label='New name for category Old']").Input("OTHER");
        Button(cut, "Save category name").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        Assert.That(cut.Find("[aria-label='New name for category Old']"), Is.Not.Null);

        cut.Find("[aria-label='New name for category Old']").Input("Discarded");
        Button(cut, "Cancel category rename").Click();
        Assert.That(cut.FindAll("[aria-label='Rename category Old']"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Rename category Discarded']"), Is.Empty);

        BeginRename(cut, "Old");
        cut.Find("[aria-label='New name for category Old']").Input("old");
        Button(cut, "Save category name").Click();
        Assert.That(cut.FindAll("[aria-label='Rename category old']"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Rename category Other']"), Has.Count.EqualTo(1));
    }

    [Test]
    public void CategoryNameStartsRenameAndDeleteCrossRemainsASeparateAction() {
        var cut = Render(SessionWithOverlappingReferences());
        var categoryName = cut.Find("[aria-label='Rename category Old']");
        Assert.That(categoryName.TextContent.Trim(), Is.EqualTo("Old"));
        Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Rename"), Is.False);

        cut.Find("[aria-label='Remove category Old']").Click();
        Assert.That(cut.FindAll("[aria-label='New name for category Old']"), Is.Empty);
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        Assert.That(cut.Find("[aria-label='Remove category Old']").GetAttribute("title"),
            Is.EqualTo("Remove category Old"));

        categoryName = cut.Find("[aria-label='Rename category Old']");
        categoryName.Click();
        Assert.That(cut.Find("[aria-label='New name for category Old']"), Is.Not.Null);
        AssertIconButton(cut, "Save category name");
        AssertIconButton(cut, "Cancel category rename");
        Assert.That(cut.Find("[aria-label='Remove category Old']"), Is.Not.Null);
    }

    [Test]
    public void EnterSavesAndEscapeReturnsToTheCategoryNameTrigger() {
        var cut = Render(SessionWithOverlappingReferences());
        BeginRename(cut, "Old");
        var input = cut.Find("[aria-label='New name for category Old']");
        input.Input("Saved");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.Find("[aria-label='Rename category Saved']").TextContent.Trim(), Is.EqualTo("Saved"));

        cut.Find("[aria-label='Rename category Other']").Click();
        cut.Find("[aria-label='New name for category Other']").Input("Discarded");
        cut.Find("[aria-label='New name for category Other']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.That(cut.Find("[aria-label='Rename category Other']").TextContent.Trim(), Is.EqualTo("Other"));
        Assert.That(cut.FindAll("[aria-label='New name for category Other']"), Is.Empty);
    }

    [Test]
    public void RenamedLegacySessionRoundTripsWithoutStaleReferencesAndStillBlocksDeletion() {
        var cut = Render();
        const string legacySession = """
            {
              "Categories": [{ "Name": "Old" }],
              "Cards": [{ "Categories": [{ "Name": "Old" }], "Copies": 1, "Name": "Legacy card" }],
              "Combos": [{ "Categories": [{ "BaseCategory": { "Name": "Old" }, "MinCount": 1, "MaxCount": 1 }] }],
              "HandSize": 1
            }
            """;

        LoadSession(cut, legacySession);
        RenameCategory(cut, "Old", "Renamed");
        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(savedJson, Does.Contain("Renamed"));
        Assert.That(savedJson, Does.Not.Contain("\"Name\": \"Old\""));

        LoadSession(cut, savedJson);
        var loadedCard = cut.FindComponent<CardEditor>().Instance.Card;
        var loadedCombo = cut.FindComponent<ComboEditor>().Instance.Combo;
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Single().Name, Is.EqualTo("Renamed"));
        Assert.That(loadedCard.Categories.Select(category => category.Name), Is.EqualTo(new[] { "Renamed" }));
        Assert.That(loadedCombo.Categories.Select(category => category.BaseCategory.Name), Is.EqualTo(new[] { "Renamed" }));
        Assert.That(loadedCombo.Name, Is.Null);
        Assert.That(ReferenceEquals(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Single(), loadedCard.Categories[0]), Is.False);
        Assert.That(ReferenceEquals(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Single(), loadedCombo.Categories[0].BaseCategory), Is.False);

        cut.Find("[aria-label='Remove category Renamed']").Click();
        Assert.That(cut.FindComponent<CategoryListEditor>().Find("[role=alert]").TextContent, Does.Contain("still used"));
    }

    private static void RenameCategory(IRenderedFragment cut, string oldName, string newName) {
        BeginRename(cut, oldName);
        cut.Find($"[aria-label='New name for category {oldName}']").Input(newName);
        Button(cut, "Save category name").Click();
    }

    private static void BeginRename(IRenderedFragment cut, string name) =>
        cut.Find($"[aria-label='Rename category {name}']").Click();

    private static IElement Button(IRenderedFragment cut, string accessibleName) =>
        cut.FindAll("button").Single(button =>
            button.GetAttribute("aria-label") == accessibleName || button.TextContent.Trim() == accessibleName);

    private static void AssertIconButton(IRenderedFragment cut, string accessibleName) {
        var button = Button(cut, accessibleName);
        Assert.That(button.TextContent.Trim(), Is.Empty);
        Assert.That(button.GetAttribute("title"), Is.EqualTo(accessibleName));
        Assert.That(button.QuerySelector("svg[aria-hidden='true']"), Is.Not.Null);
    }

    private static void LoadSession(IRenderedComponent<ProbabilityCalculatorComponent> cut, string json) {
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Legacy card")));
    }
}
