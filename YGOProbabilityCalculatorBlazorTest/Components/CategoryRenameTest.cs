using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
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
    private TestContext _context = null!;

    [SetUp]
    public void SetUp() {
        _context = new TestContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<IBackgroundCalculator, BackgroundCalculatorTestAdapter>();
        _context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        _context.Services.AddSingleton<ISerializer, JsonSerializer>();
        _context.Services.AddSingleton<ISessionService, SessionService>();
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<CardInfo>());
        _context.Services.AddSingleton(cardInfo.Object);
        _context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        _context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        _context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState? session = null) {
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return _context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    private static SessionState SessionWithOverlappingReferences() {
        CategoryBase category = new("Old");
        CategoryBase otherCategory = new("Other");

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
        SessionState session = SessionWithOverlappingReferences();
        Card[] originalCards = session.Cards.ToArray();
        Combo[] originalCombos = session.Combos.ToArray();
        (string Name, int MinCount, int MaxCount)[][] expectedConstraints = session.Combos
            .Select(combo => combo.Categories
                .Select(category => (category.BaseCategory.Name, category.MinCount, category.MaxCount))
                .ToArray())
            .ToArray();
        (string Name, int MinCount, int MaxCount)[][] expectedRenamedConstraints = expectedConstraints
            .Select(constraints => constraints
                .Select(constraint => (
                    constraint.Name == "Old" ? "Renamed" : constraint.Name,
                    constraint.MinCount,
                    constraint.MaxCount))
                .ToArray())
            .ToArray();
        double oracleProbability = SmallDeckOracle.EnumerateProbability(session.Cards, session.Combos, session.HandSize);
        ProbabilityCalculatorService calculator = new();
        double probabilityBefore = calculator.CalculateProbabilityForCombos(session.Cards, session.Combos, session.HandSize);
        Assert.That(probabilityBefore, Is.EqualTo(oracleProbability).Within(1e-12));

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>()[0];
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>()[0];
        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select[id^='cardCategory']").Change("user:Old");
        combo.Find("select").Change("user:Old");
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("1");

        RenameCategory(cut, "Old", "Renamed");

        Assert.That(cut.FindComponents<CardEditor>()[0], Is.SameAs(card));
        Assert.That(cut.FindComponents<ComboEditor>()[0], Is.SameAs(combo));
        Assert.That(card.Instance.Card, Is.SameAs(originalCards[0]));
        Assert.That(combo.Instance.Combo, Is.SameAs(originalCombos[0]));
        Assert.That(card.Find("select[id^='cardCategory']").GetAttribute("value"), Is.EqualTo("user:Renamed"));
        Assert.That(combo.Find("select").GetAttribute("value"), Is.EqualTo("user:Renamed"));
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));

        IReadOnlyList<CategoryBase> definitions = cut.FindComponent<CategoryListEditor>().Instance.CategoryBases;
        Assert.That(definitions.Select(category => category.Name), Is.EqualTo(new[] { "Renamed", "Other" }));
        Assert.That(session.Cards.SelectMany(item => item.Categories).Any(category => category.Name == "Old"), Is.False);
        Assert.That(session.Combos.SelectMany(item => item.Categories).Any(category => category.BaseCategory.Name == "Old"), Is.False);
        Assert.That(session.Cards.SelectMany(item => item.Categories).Count(category => category.Name == "Renamed"), Is.EqualTo(2));
        Assert.That(session.Cards[0].Categories.Select(category => category.Name), Is.EqualTo(new[] { "Renamed", "Other" }));
        (string Name, int MinCount, int MaxCount)[][] actualConstraints = session.Combos
            .Select(combo => combo.Categories
                .Select(category => (category.BaseCategory.Name, category.MinCount, category.MaxCount))
                .ToArray())
            .ToArray();
        Assert.That(actualConstraints, Is.EqualTo(expectedRenamedConstraints));
        Assert.That(session.Combos[2].Categories.Select(category => category.BaseCategory.Name),
            Is.EqualTo(new[] { "Renamed", "Renamed" }));

        double probabilityAfter = calculator.CalculateProbabilityForCombos(session.Cards, session.Combos, session.HandSize);
        Assert.That(probabilityAfter, Is.EqualTo(oracleProbability).Within(1e-12));
        Assert.That(probabilityAfter, Is.EqualTo(probabilityBefore).Within(1e-12));

        Button(combo, "Update").Click();
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("Renamed (1 Max)"));
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void RenameRejectsBlankAndCaseInsensitiveDuplicateNamesButAllowsCancelAndCaseOnlyRename() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(SessionWithOverlappingReferences());
        BeginRename(cut, "Old");

        AssertIconButton(cut, "Save category name");
        AssertIconButton(cut, "Exit category edit mode");

        cut.Find("[aria-label='New name for category Old']").Input("  ");
        Button(cut, "Save category name").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));

        cut.Find("[aria-label='New name for category Old']").Input("OTHER");
        Button(cut, "Save category name").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        Assert.That(cut.Find("[aria-label='New name for category Old']"), Is.Not.Null);

        cut.Find("[aria-label='New name for category Old']").Input("Discarded");
        Button(cut, "Exit category edit mode").Click();
        Assert.That(cut.FindAll("[aria-label='Edit category Old']"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Edit category Discarded']"), Is.Empty);

        BeginRename(cut, "Old");
        cut.Find("[aria-label='New name for category Old']").Input("old");
        Button(cut, "Save category name").Click();
        Assert.That(cut.FindAll("[aria-label='Edit category old']"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Edit category Other']"), Has.Count.EqualTo(1));
    }

    [Test]
    public void CategoryNameStartsRenameAndDeleteCrossRemainsASeparateAction() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(SessionWithOverlappingReferences());
        IElement categoryName = cut.Find("[aria-label='Edit category Old']");
        Assert.That(categoryName.TextContent.Trim(), Is.EqualTo("Old"));
        Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Rename"), Is.False);

        cut.Find("[aria-label='Remove category Old']").Click();
        Assert.That(cut.FindAll("[aria-label='New name for category Old']"), Is.Empty);
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        Assert.That(cut.Find("[aria-label='Remove category Old']").GetAttribute("title"),
            Is.EqualTo("Remove category Old"));

        categoryName = cut.Find("[aria-label='Edit category Old']");
        categoryName.Click();
        Assert.That(cut.Find("[aria-label='New name for category Old']"), Is.Not.Null);
        AssertIconButton(cut, "Save category name");
        AssertIconButton(cut, "Exit category edit mode");
        Assert.That(cut.Find("[aria-label='Remove category Old']"), Is.Not.Null);
    }

    [Test]
    public void EnterSavesAndEscapeReturnsToTheCategoryNameTrigger() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(SessionWithOverlappingReferences());
        BeginRename(cut, "Old");
        IElement input = cut.Find("[aria-label='New name for category Old']");
        input.Input("Saved");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.Find("[aria-label='Edit category Saved']").TextContent.Trim(), Is.EqualTo("Saved"));

        cut.Find("[aria-label='Edit category Other']").Click();
        cut.Find("[aria-label='New name for category Other']").Input("Discarded");
        cut.Find("[aria-label='New name for category Other']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.That(cut.Find("[aria-label='Edit category Other']").TextContent.Trim(), Is.EqualTo("Other"));
        Assert.That(cut.FindAll("[aria-label='New name for category Other']"), Is.Empty);
    }

    [Test]
    public void RenamedLegacySessionRoundTripsWithoutStaleReferencesAndStillBlocksDeletion() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
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
        string savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)_context.JSInterop.Invocations["saveSessionFile"].Single().Arguments[1]!));
        Assert.That(savedJson, Does.Contain("Renamed"));
        Assert.That(savedJson, Does.Not.Contain("\"Name\": \"Old\""));

        LoadSession(cut, savedJson);
        Card loadedCard = cut.FindComponent<CardEditor>().Instance.Card;
        Combo loadedCombo = cut.FindComponent<ComboEditor>().Instance.Combo;
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
        cut.Find($"[aria-label='Edit category {name}']").Click();

    private static IElement Button(IRenderedFragment cut, string accessibleName) =>
        cut.FindAll("button").Single(button =>
            button.GetAttribute("aria-label") == accessibleName || button.TextContent.Trim() == accessibleName);

    private static void AssertIconButton(IRenderedFragment cut, string accessibleName) {
        IElement button = Button(cut, accessibleName);
        Assert.That(button.TextContent.Trim(), Is.Empty);
        Assert.That(button.GetAttribute("title"), Is.EqualTo(accessibleName));
        Assert.That(button.QuerySelector("svg[aria-hidden='true']"), Is.Not.Null);
    }

    private static void LoadSession(IRenderedComponent<ProbabilityCalculatorComponent> cut, string json) {
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Legacy card")));
    }
}
