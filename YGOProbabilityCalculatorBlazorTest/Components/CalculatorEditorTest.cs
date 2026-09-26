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
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorTest {
    private TestContext context = null!;
    private readonly CategoryBase a = new("A");
    private readonly CategoryBase b = new("B");

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

    private static IElement Button(IRenderedFragment fragment, string text) =>
        fragment.FindAll("button").Single(element => element.TextContent.Trim() == text);

    private SessionState Session() => new() {
        Categories = [a, b], Cards = [new([a], 2, "First"), new([b], 2, "Second")],
        Combos = [new([new(a, 1, 2)], "First combo"), new([new(b, 1, 2)], "Second combo")], HandSize = 2
    };

    [Test]
    public void CategoryColorsStayConsistentAcrossViewsThroughRenameAndUnrelatedDeletion() {
        var categoryA = new CategoryBase("A");
        var categoryB = new CategoryBase("B");
        var categoryC = new CategoryBase("C");
        var session = new SessionState {
            Categories = [categoryA, categoryB, categoryC],
            Cards = [new([categoryA, categoryC], 3, "Card")],
            Combos = [new([new(categoryA, 1, 3), new(categoryC, 1, 3)], "Combo")],
            HandSize = 3
        };

        var cut = Render(session);
        var categoryList = cut.FindComponent<CategoryListEditor>();
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();
        var colorA = CategoryColorClass(categoryList, ".category-chip", "A");
        var colorC = CategoryColorClass(categoryList, ".category-chip", "C");

        Assert.That(colorA, Is.Not.EqualTo(colorC));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-button .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));

        cut.Find("[aria-label='Rename category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed");
        cut.Find("[aria-label='Save category name']").Click();

        Assert.That(CategoryColorClass(categoryList, ".category-chip", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "C"), Is.EqualTo(colorC));

        cut.Find("[aria-label='Remove category B']").Click();
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));

        cut.Find("[placeholder='Category name']").Input("D");
        Button(cut, "Add Category").Click();
        var colorD = CategoryColorClass(categoryList, ".category-chip", "D");
        Assert.That(colorD, Is.Not.EqualTo(colorA).And.Not.EqualTo(colorC));
    }

    [Test]
    public void CategoryColorsRoundTripAndLegacySessionsReceiveDistinctAssignments() {
        var cut = Render(Session());
        var categories = cut.FindComponent<CategoryListEditor>();
        var firstColor = CategoryColorClass(categories, ".category-chip", "A");
        var secondColor = CategoryColorClass(categories, ".category-chip", "B");
        Assert.That(firstColor, Is.Not.EqualTo(secondColor));

        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(savedJson)) {
            var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
            Assert.That(savedColors.GetProperty("A").GetInt32(), Is.Not.EqualTo(savedColors.GetProperty("B").GetInt32()));
        }

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(savedJson, "session.json"));
        cut.WaitForAssertion(() => {
            var loadedCategories = cut.FindComponent<CategoryListEditor>();
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "A"), Is.EqualTo(firstColor));
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "B"), Is.EqualTo(secondColor));
            Assert.That(CategoryColorClass(cut.FindComponent<CardEditor>(), ".accordion-body .category-tag", "A"), Is.EqualTo(firstColor));
            Assert.That(CategoryColorClass(cut.FindComponents<ComboEditor>()[1], ".accordion-body .category-tag", "B"), Is.EqualTo(secondColor));
        });

        const string legacySession = """
            {
              "Categories": [{ "Name": "Legacy A" }, { "Name": "Legacy B" }],
              "Cards": [],
              "Combos": [],
              "HandSize": 5
            }
            """;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(legacySession, "legacy-session.json"));
        cut.WaitForAssertion(() => {
            var legacyCategories = cut.FindComponent<CategoryListEditor>();
            var legacyAColor = CategoryColorClass(legacyCategories, ".category-chip", "Legacy A");
            var legacyBColor = CategoryColorClass(legacyCategories, ".category-chip", "Legacy B");
            Assert.That(legacyAColor, Is.Not.EqualTo(legacyBColor));
        });
    }

    private static string CategoryColorClass(IRenderedFragment fragment, string selector, string categoryName) =>
        fragment.FindAll(selector)
            .Single(element => element.TextContent.Trim().StartsWith(categoryName, StringComparison.Ordinal))
            .ClassList.Single(className => className.StartsWith("category-color-", StringComparison.Ordinal));

    [Test]
    public void CategoriesRefreshSiblingSelectorsAndRejectEmptyDuplicateOrUsedNames() {
        var cut = Render();
        Button(cut, "Add New Card").Click();
        Button(cut, "Add New Combo").Click();
        Button(cut, "Add Category").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[placeholder='Category name']").Input("A");
        Button(cut, "Add Category").Click();
        Assert.That(cut.FindAll("select option[value=A]"), Has.Count.EqualTo(2));
        cut.Find("[placeholder='Category name']").Input("a");
        Button(cut, "Add Category").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        var card = cut.FindComponent<CardEditor>();
        card.Find("select").Change("A");
        Button(card, "Add").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        card.Find(".accordion-body .badge button").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.FindAll("select option[value=A]"), Is.Empty);
    }

    [Test]
    public void LoadedComboAlonePreventsCategoryDeletionUntilConstraintIsRemoved() {
        var session = Session();
        session.Cards.Clear();
        var cut = Render(session);
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-body .badge button").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.FindAll("select option[value=A]"), Is.Empty);
    }

    [Test]
    public void CardValidationRetainsPositiveCopyCountsAndReportsSelectionErrors() {
        var cut = Render(Session());
        var card = cut.FindComponents<CardEditor>()[0];
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        foreach (var value in new[] { "0", "-1", "", "nonsense" }) {
            card.Find("input[type=number]").Input(value);
            Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("at least 1"));
            Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("(2)"));
        }
        card.Find("select").Change("A");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("already added"));
        card.Find("select").Change("missing");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("not found"));
        card.Find("select").Change("B");
        card.Find("input[type=number]").Input("6");
        card.Find("#cardName0").Input("Renamed");
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("B"));
        Button(card, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Renamed").And.Contain("(6)"));
        Assert.That(card.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ZeroMaximumSurvivesRenameParentRerenderAndHandSizeChangeAndCanBeUpdated() {
        var cut = Render(Session());
        var combo = cut.FindComponents<ComboEditor>()[0];
        combo.Find("select").Change("A");
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("0");
        combo.Find("#comboName0").Input("Zero A");
        cut.Find("#handSize").Change("3");
        combo.Find(".accordion-button").Click();
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Button(combo, "Update").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (0–0)"));
        combo.Find("select").Change("A");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        combo.Find("#maxCount0").Input("2");
        Button(combo, "Update").Click();
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (0–2)"));
    }

    [Test]
    public void ComboRejectsInvalidRangesWithoutClampingAndUsesHandSizeForNewDrafts() {
        var cut = Render(Session());
        var combo = cut.FindComponents<ComboEditor>()[0];
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        combo.Find("select").Change("B");
        combo.Find("#minCount0").Input("-1");
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Minimum"));
        combo.Find("#minCount0").Input("2");
        combo.Find("#maxCount0").Input("1");
        cut.Find("#handSize").Change("3");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Maximum"));
        combo.Find("#maxCount0").Input("");
        Button(combo, "Add").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        combo.Find("select").Change("");
        cut.Find("#handSize").Change("4");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
        combo.Find("select").Change("B");
        Button(combo, "Add").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void DeletingEarlierRowsPreservesSurvivingDraftsAndActiveEditors() {
        var cut = Render(Session());
        var card = cut.FindComponents<CardEditor>()[1];
        var combo = cut.FindComponents<ComboEditor>()[1];
        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select").Change("A");
        combo.Find("select").Change("A");
        combo.Find("#minCount1").Input("0");
        combo.Find("#maxCount1").Input("0");
        cut.FindComponents<CardEditor>()[0].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[0].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(1));
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("A"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Button(card, "Add").Click();
        Button(combo, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Second"));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (0–0)"));
        card.Find("[title='Remove card']").Click();
        combo.Find("[title='Remove combo']").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void ReorderingRowsKeepsDraftsWithTheirModels() {
        var session = Session();
        var cards = context.RenderComponent<CardListEditor>(p => p.Add(x => x.Cards, session.Cards)
            .Add(x => x.CategoryBases, session.Categories));
        var combos = context.RenderComponent<ComboListEditor>(p => p.Add(x => x.Combos, session.Combos)
            .Add(x => x.CategoryBases, session.Categories));
        cards.FindComponents<CardEditor>()[0].Find("select").Change("B");
        combos.FindComponents<ComboEditor>()[0].Find("select").Change("B");
        combos.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");
        combos.FindComponents<ComboEditor>()[0].Find("#maxCount0").Input("0");
        session.Cards.Reverse();
        session.Combos.Reverse();
        cards.SetParametersAndRender(p => p.Add(x => x.Cards, session.Cards));
        combos.SetParametersAndRender(p => p.Add(x => x.Combos, session.Combos));
        Assert.That(cards.FindComponents<CardEditor>()[1].Find("select").GetAttribute("value"), Is.EqualTo("B"));
        Assert.That(combos.FindComponents<ComboEditor>()[1].Find("#maxCount1").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cards.FindComponents<CardEditor>()[0].Find("select").GetAttribute("value"), Is.Null.Or.Empty);
    }

    [Test]
    public void RemovingConstraintPreservesOtherConstraintsFromLoadedSession() {
        // Session JSON can contain repeated category constraints. Removing B must
        // not silently discard either A constraint (together they require exactly one A).
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 1, 2), new(a, 0, 1), new(b, 0, 2)]));
        var cut = Render(session);
        var combo = cut.FindComponent<ComboEditor>();
        combo.FindAll(".accordion-body .badge button")[2].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (1–2)").And.Contain("A (0–1)"));
        combo.FindAll(".accordion-body .badge button")[0].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (0–1)"));
    }

    [Test]
    public void SessionSaveLoadRoundTripPreservesZeroAndResetsOldEditorDrafts() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 0, 0)], "No A"));
        var cut = Render(session);
        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"MaxCount\": 0"));
        var combo = cut.FindComponent<ComboEditor>();
        combo.Find("select").Change("B");
        combo.Find("#minCount0").Input("2");
        combo.Find(".accordion-button").Click();
        cut.Find("[placeholder='Category name']").Input("Old draft");
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        cut.WaitForAssertion(() => {
            var loaded = cut.FindComponent<ComboEditor>();
            Assert.That(loaded.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(loaded.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(loaded.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
            Assert.That(cut.Find("[placeholder='Category name']").GetAttribute("value"), Is.Null.Or.Empty);
        });
        Button(cut, "Calculate").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Find(".alert-primary").TextContent, Does.Contain((1.0 / 6).ToString("P2"))));
        cut.Find("[aria-label='Remove category A']").Click();
        cut.WaitForAssertion(() => Assert.That(
            cut.FindComponent<CategoryListEditor>().Find("[role=alert]").TextContent, Does.Contain("still used")));
    }

    [Test]
    public void DeckImportReplacesRowsWithoutReusingDraftsAndUpdatesCalculationEligibility() {
        // Exercise the real YDK parser; only the external card-name lookup is stubbed.
        context.Services.AddSingleton<IFileService, FileService>();
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(x => x.GetCardNameAsync(123)).ReturnsAsync("Imported");
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<IDeckImportService, DeckImportService>();
        var cut = Render(Session());
        cut.FindComponents<CardEditor>()[0].Find("select").Change("B");
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123\n123\n#extra\n456", "deck.ydk"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1)));
        var card = cut.FindComponent<CardEditor>();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Imported").And.Contain("(2)"));
        Assert.That(card.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        cut.Find("#handSize").Change("3");
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }
}
