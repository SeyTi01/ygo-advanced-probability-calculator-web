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

[TestFixture]
public sealed class CalculatorEditorTestCategories : CalculatorEditorTestBase {

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

        cut.Find("[aria-label='Edit category A']").Click();
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
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
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
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
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

    [Test]
    public async Task CategoryColorPickerUpdatesEveryViewPreservesRenameDraftAndRoundTripsDuplicates() {
        var vsStarter = new CategoryBase("VS Starter");
        var k9Starter = new CategoryBase("K9 Starter");
        var cut = Render(new SessionState {
            Categories = [vsStarter, k9Starter],
            Cards = [new([vsStarter, k9Starter], 2, "Starter card")],
            Combos = [new([new(vsStarter, 1, 2), new(k9Starter, 1, 2)], "Starter route")],
            HandSize = 2,
            CategoryColorIndices = new(StringComparer.Ordinal) { [vsStarter.Name] = 0, [k9Starter.Name] = 1 }
        });
        var categories = cut.FindComponent<CategoryListEditor>();
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();

        Assert.That(categories.FindAll(".category-color-swatch"), Is.Empty);
        Assert.That(CategoryColorClass(categories, ".category-chip", vsStarter.Name), Is.EqualTo("category-color-0"));
        await categories.Find("[aria-label='Edit category K9 Starter']").ClickAsync(new());
        await categories.Find("[aria-label='Choose color for category K9 Starter']").ClickAsync(new());
        var k9Swatches = categories.FindAll(".category-color-swatch");
        Assert.That(k9Swatches, Has.Count.EqualTo(CategoryColorPalette.PaletteSize));
        Assert.That(k9Swatches.Select(button => button.GetAttribute("aria-label")),
            Is.EqualTo(CategoryColorPalette.Options.Select(color => $"Set K9 Starter color to {color.Name}")));
        Assert.That(categories.Find("[aria-label='Set K9 Starter color to Orange']").GetAttribute("aria-pressed"), Is.EqualTo("true"));
        await categories.Find("[aria-label='Set K9 Starter color to Green']").ClickAsync(new());

        Assert.That(CategoryColorClass(categories, ".category-chip", vsStarter.Name), Is.EqualTo("category-color-0"),
            "changing one category leaves the other category unchanged");
        Assert.That(categories.Find("[aria-label='New name for category K9 Starter']").ParentElement!.ClassList,
            Does.Contain("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-button .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));

        categories = cut.FindComponent<CategoryListEditor>();
        await categories.Find("[aria-label='Exit category edit mode']").ClickAsync(new());
        await categories.Find("[aria-label='Edit category VS Starter']").ClickAsync(new());
        await categories.Find("[aria-label='Choose color for category VS Starter']").ClickAsync(new());
        Assert.That(categories.Find("[aria-label='Set VS Starter color to Blue']").GetAttribute("aria-pressed"), Is.EqualTo("true"));
        await categories.Find("[aria-label='New name for category VS Starter']").InputAsync(new() { Value = "VS Starter renamed" });
        await categories.Find("[aria-label='Set VS Starter color to Green']").ClickAsync(new());

        categories = cut.FindComponent<CategoryListEditor>();
        var renameInput = categories.Find("[aria-label='New name for category VS Starter']");
        Assert.That(renameInput.GetAttribute("value"), Is.EqualTo("VS Starter renamed"));
        Assert.That(categories.FindAll("[aria-label='Edit category VS Starter renamed']"), Is.Empty,
            "choosing a color must not commit the pending rename");
        Assert.That(renameInput.ParentElement!.ClassList, Does.Contain("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", vsStarter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", vsStarter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(categories, ".category-chip", k9Starter.Name), Is.EqualTo("category-color-2"));

        await categories.Find("[aria-label='Save category name']").ClickAsync(new());
        categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(CategoryColorClass(categories, ".category-chip", "VS Starter renamed"), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(savedJson)) {
            var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
            Assert.That(savedColors.GetProperty("VS Starter renamed").GetInt32(), Is.EqualTo(2));
            Assert.That(savedColors.GetProperty("K9 Starter").GetInt32(), Is.EqualTo(2),
                "manually chosen duplicate palette colors are allowed and saved");
        }

        var sessionFile = new Mock<IBrowserFile>();
        sessionFile.Setup(file => file.Name).Returns("session.json");
        sessionFile.Setup(file => file.Size).Returns(System.Text.Encoding.UTF8.GetByteCount(savedJson));
        sessionFile.Setup(file => file.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(savedJson)));
        var sessionFileInput = cut.FindComponents<InputFile>()[1];
        await cut.InvokeAsync(() => sessionFileInput.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([sessionFile.Object])));
        cut.WaitForAssertion(() => {
            var loadedCategories = cut.FindComponent<CategoryListEditor>();
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "VS Starter renamed"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "K9 Starter"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(cut.FindComponent<CardEditor>(), ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(cut.FindComponents<ComboEditor>()[0], ".accordion-body .category-tag", "K9 Starter"), Is.EqualTo("category-color-2"));
        });
    }

    [Test]
    public async Task InvalidAndMissingCategoryColorsAreRepairedWithoutChangingValidDuplicates() {
        var aCategory = new CategoryBase("A");
        var bCategory = new CategoryBase("B");
        var cCategory = new CategoryBase("C");
        var cut = Render(new SessionState {
            Categories = [aCategory, bCategory, cCategory],
            CategoryColorIndices = new(StringComparer.Ordinal) {
                ["A"] = 7,
                ["B"] = 7,
                ["C"] = CategoryColorPalette.PaletteSize,
                ["Removed"] = 4
            }
        });
        var categories = cut.FindComponent<CategoryListEditor>();

        Assert.That(CategoryColorClass(categories, ".category-chip", "A"), Is.EqualTo("category-color-7"));
        Assert.That(CategoryColorClass(categories, ".category-chip", "B"), Is.EqualTo("category-color-7"));
        Assert.That(CategoryColorClass(categories, ".category-chip", "C"), Is.EqualTo("category-color-0"));
        await categories.Find("[placeholder='Category name']").InputAsync(new() { Value = "D" });
        await categories.Find("button.btn.btn-primary").ClickAsync(new());
        categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(CategoryColorClass(categories, ".category-chip", "D"), Is.EqualTo("category-color-1"),
            "automatic assignment uses the first unused palette slot after existing duplicate choices");

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using var document = System.Text.Json.JsonDocument.Parse(savedJson);
        var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
        Assert.That(savedColors.GetProperty("A").GetInt32(), Is.EqualTo(7));
        Assert.That(savedColors.GetProperty("B").GetInt32(), Is.EqualTo(7));
        Assert.That(savedColors.GetProperty("C").GetInt32(), Is.EqualTo(0));
        Assert.That(savedColors.GetProperty("D").GetInt32(), Is.EqualTo(1));
        Assert.That(savedColors.TryGetProperty("Removed", out _), Is.False);
    }

    [Test]
    public void MetadataPropertyColorsAreConsistentAcrossCardAndComboChipContexts() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var detectedCard = new Card([fire], name: "Detected FIRE");
        var manualCard = new Card([fire], name: "Manual FIRE", manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var combo = new Combo([new(fire, 1, 5)], "FIRE route");
        var cut = Render(new SessionState { Cards = [detectedCard, manualCard], Combos = [combo], HandSize = 5 });
        var detected = cut.FindComponents<CardEditor>()[0];
        var manual = cut.FindComponents<CardEditor>()[1];
        var comboEditor = cut.FindComponent<ComboEditor>();
        var expectedClass = "card-property-color-attribute-fire";

        Assert.That(detected.Find(".detected-card-property").ClassList,
            Does.Contain("card-property-tag").And.Contain(expectedClass));
        Assert.That(manual.Find(".manual-card-property").ClassList,
            Does.Contain("card-property-tag").And.Contain(expectedClass));
        Assert.That(manual.Find(".manual-property-header-badge").ClassList, Does.Contain(expectedClass));
        Assert.That(comboEditor.Find(".combo-header-content .category-tag").ClassList, Does.Contain(expectedClass));
        Assert.That(comboEditor.Find(".accordion-body .category-tag").ClassList, Does.Contain(expectedClass));
        Assert.That(cut.FindAll(".card-property-tag.text-bg-secondary"), Is.Empty);
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
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[placeholder='Category name']").Input("A");
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.FindAll("select option[value='user:A']"), Has.Count.EqualTo(2));
        cut.Find("[placeholder='Category name']").Input("a");
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        var card = cut.FindComponent<CardEditor>();
        card.Find("select").Change("user:A");
        Button(card, "Add").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        card.Find(".accordion-body .badge button").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.FindAll("select option[value='user:A']"), Is.Empty);
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
        Assert.That(cut.FindAll("select option[value='user:A']"), Is.Empty);
    }

}
