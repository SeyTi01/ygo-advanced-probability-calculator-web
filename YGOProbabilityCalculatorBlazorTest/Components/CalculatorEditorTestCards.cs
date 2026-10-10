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
public sealed class CalculatorEditorTestCards : CalculatorEditorTestBase {

    [TestCase(false)]
    [TestCase(true)]
    public async Task LargeCopyTotalsRemainEditableWithoutOverflowingTheWorkspace(bool editAfterLoad) {
        SessionState session = Session();
        if (!editAfterLoad) {
            session.Cards[0] = session.Cards[0].WithCopies(int.MaxValue);
        }

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        if (editAfterLoad) {
            await cut.FindComponent<CardEditor>().Find("#cardCopies0").InputAsync(new() { Value = int.MaxValue.ToString() });
        }

        Assert.That(cut.FindComponent<CardListEditor>().Find("h4").TextContent,
            Is.EqualTo("Deck (2147483649)"));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("The active deck is too large to calculate"));

        await cut.FindComponent<CardEditor>().Find("#cardCopies0").InputAsync(new() { Value = "2" });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReorderControlsMoveAllFourListsAndKeepSessionOrderAndReferences() {
        CategoryBase categoryC = new("C");
        List<Card> cards = [new([a], 1, "Same"), new([b], 1, "Same"), new([categoryC], 1, "")];
        List<ComboGroup> groups = [new("g1", "One"), new("g2", "Two"), new("g3", "Three")];
        List<Combo> combos = [
            new([new(a, 1, 1)], "Same", groupId: "g1", cards: [new(cards[0].Id, 1, 1)]),
            new([new(b, 1, 1)], "Same", groupId: "g2"),
            new([new(categoryC, 1, 1)], "", groupId: "g3")
        ];
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, categoryC],
            Cards = cards,
            Combos = combos,
            ComboGroups = groups,
            HandSize = 1
        });

        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.FindComponent<ComboListEditor>().FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.Find("[aria-label='Move card Same, row 1 up']").TextContent, Is.EqualTo("▲"));
        Assert.That(cut.Find("[aria-label='Move combo Same, row 1 up']").TextContent, Is.EqualTo("▲"));
        Assert.That(cut.Find("[aria-label='Move card Same, row 1 up']").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[aria-label='Move combo Combo 3, row 3 down']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category A left']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit group One']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move group One left']").HasAttribute("disabled"), Is.True);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        string totalBeforeMove = cut.Find(".probability-total-value").TextContent;
        Assert.That(cut.Find("[aria-label='Move category A left']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Move category A right']").ClickAsync(new());
        AssertPreviousResult(cut);
        await cut.Find("[aria-label='Move category A right']").ClickAsync(new());
        await cut.Find("[aria-label='Edit category C']").ClickAsync(new());
        await cut.Find("[aria-label='Move category C left']").ClickAsync(new());
        await cut.Find("[aria-label='Move group One right']").ClickAsync(new());
        await cut.Find("[aria-label='Move group One right']").ClickAsync(new());
        await cut.Find("[aria-label='Edit group Three']").ClickAsync(new());
        await cut.Find("[aria-label='Move group Three left']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category C right']").TextContent, Is.EqualTo("▶"));
        Assert.That(cut.Find("[aria-label='Move group Three left']").TextContent, Is.EqualTo("◀"));
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category A right']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit group One']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move group One right']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Move card Same, row 1 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move card Same, row 2 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move card Card 2, row 2 up']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Same, row 1 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Same, row 2 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Combo 2, row 2 up']").ClickAsync(new());

        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(category => category.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo),
            Is.EqualTo(new[] { combos[2], combos[1], combos[0] }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(group => group.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.Cards.Single().CardId, Is.EqualTo(cards[0].Id));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.GroupId, Is.EqualTo("g1"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(totalBeforeMove));
        Assert.That(cut.FindAll(".probability-group .combo-probability-name").Select(comboName => comboName.TextContent.Trim().Split(' ')[0]),
            Is.EqualTo(new[] { "Three", "Two", "One" }));
        Assert.That(cut.FindAll(".combo-probability-item .combo-probability-name").ToArray()[0].TextContent,
            Does.Contain("Unnamed combo 1"));

        await Button(cut, "Save Session").ClickAsync(new());
        JSRuntimeInvocation invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json)) {
            System.Text.Json.JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("Categories").EnumerateArray().Select(category => category.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "C", "B", "A" }));
            Assert.That(root.GetProperty("Cards").EnumerateArray().Select(card => card.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
            Assert.That(root.GetProperty("Combos").EnumerateArray().Select(combo => combo.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "", "Same", "Same" }));
            Assert.That(root.GetProperty("ComboGroups").EnumerateArray().Select(group => group.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { "g3", "g2", "g1" }));
        }
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "ordered.json"));
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(category => category.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(group => group.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.GroupId),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
    }

    [Test]
    public void MovingExpandedEditorsKeepsDraftAndTargetsMovedItem() {
        SessionState session = Session();
        session.Cards[1] = session.Cards[1].WithActive(false);
        session.Combos[1] = session.Combos[1].WithActive(false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<CardEditor>()[0].Find("#cardCategory0").Change("user:B");
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<ComboEditor>()[0].Find("#comboCategory0").Change("user:B");
        cut.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");

        cut.Find("[aria-label='Move card First, row 1 down']").Click();
        cut.Find("[aria-label='Move combo First combo, row 1 down']").Click();
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.Name, Is.EqualTo("First"));
        Assert.That(cut.FindComponents<CardEditor>()[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(cut.FindComponents<CardEditor>()[1].Find("#cardCategory1").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("First combo"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Find("#minCount1").GetAttribute("value"), Is.EqualTo("0"));

        cut.FindComponents<CardEditor>()[1].Find("#cardName1").Input("Moved card");
        cut.FindComponents<ComboEditor>()[1].Find("#comboName1").Input("Moved combo");
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.Name, Is.EqualTo("Moved card"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Moved combo"));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Active, Is.False);
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Active, Is.False);
        cut.FindComponents<CardEditor>()[1].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[1].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.Name, Is.EqualTo("Second"));
        Assert.That(cut.FindComponents<ComboEditor>().Single().Instance.Combo.Name, Is.EqualTo("Second combo"));
    }

    [Test]
    public void ChipMovesKeepRenameDraftsAndDropdownOrder() {
        SessionState session = new() {
            Categories = [a, b], Cards = [new([a], 2, "First")],
            Combos = [new([new(a, 1, 2)], "First combo")],
            ComboGroups = [new("g1", "One"), new("g2", "Two")], HandSize = 2
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        cut.Find("[aria-label='Edit category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed");
        IElement categoryChip = cut.FindAll(".category-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for category A']") is not null);
        Assert.That(categoryChip.QuerySelectorAll("button").Select(button => button.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save category name", "Move category A left", "Move category A right", "Exit category edit mode", "Choose color for category A", "Remove category A" }));
        cut.Find("[aria-label='Move category A right']").Click();
        Assert.That(cut.Find("[aria-label='New name for category A']").GetAttribute("value"), Is.EqualTo("Renamed"));
        cut.Find("[aria-label='Save category name']").Click();
        cut.Find("[aria-label='Edit group One']").Click();
        cut.Find("[aria-label='New name for group One']").Input("Updated");
        IElement groupChip = cut.FindAll(".combo-group-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for group One']") is not null);
        Assert.That(groupChip.QuerySelectorAll("button").Select(button => button.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save group name", "Move group One left", "Move group One right", "Exit group edit mode", "Remove group One" }));
        cut.Find("[aria-label='Move group One right']").Click();
        Assert.That(cut.Find("[aria-label='New name for group One']").GetAttribute("value"), Is.EqualTo("Updated"));
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.FindAll(".category-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").Click();
        Assert.That(cut.Find("#cardCategory0").QuerySelectorAll("option").Skip(1).Select(option => option.TextContent.Trim()),
            Is.EqualTo(new[] { "B", "Renamed" }));
        Assert.That(cut.Find("#comboGroup0").QuerySelectorAll("option").Skip(1).Select(option => option.TextContent.Trim()),
            Is.EqualTo(new[] { "Two", "Updated" }));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Categories.Single().Name, Is.EqualTo("Renamed"));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Categories.Single().BaseCategory.Name, Is.EqualTo("Renamed"));

        cut.Find("[aria-label='Edit category B']").Click();
        cut.Find("[aria-label='New name for category B']").Input("Discarded");
        cut.Find("[aria-label='Move category B right']").Click();
        cut.Find("[aria-label='New name for category B']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip").Select(chip =>
            chip.QuerySelector(".category-name-trigger")?.TextContent.Trim()), Is.EqualTo(new[] { "Renamed", "B" }));
        Assert.That(cut.FindAll(".category-chip .reorder-controls"), Is.Empty);

        cut.Find("[aria-label='Edit group Two']").Click();
        cut.Find("[aria-label='New name for group Two']").Input("Discarded");
        cut.Find("[aria-label='Move group Two right']").Click();
        cut.Find("[aria-label='Exit group edit mode']").Click();
        Assert.That(cut.FindAll(".combo-group-chip").Select(chip =>
            chip.QuerySelector(".category-name-trigger")?.TextContent.Trim()), Is.EqualTo(new[] { "Updated", "Two" }));
        Assert.That(cut.FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
    }

    [Test]
    public async Task DirectCardSelectorIsAlphabeticalWithoutChangingDeckOrCategoryOrder() {
        CategoryBase zetaCategory = new("Zeta");
        CategoryBase alphaCategory = new("Alpha");
        List<Card> cards = [
            new([], name: "zebra", id: "zebra"),
            new([], name: "alpha", id: "alpha-lower"),
            new([], name: "Alpha", id: "alpha-upper"),
            new([], name: "beta", id: "beta-first"),
            new([], name: "Beta", id: "beta-case"),
            new([], name: "beta", id: "beta-second"),
            new([], name: null, id: "unnamed-first"),
            new([], name: "  ", id: "unnamed-second")
        ];
        string[] deckOrder = cards.Select(card => card.Id).ToArray();
        Combo combo = new([], "Direct", cards: [new("missing-card", 1, 1)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [zetaCategory, alphaCategory], Cards = cards, Combos = [combo], HandSize = 1
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.Find("[role='status']").TextContent, Does.Contain("Missing card reference"));
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });

        IElement selector = editor.Find("#comboCard0");
        Assert.That(selector.QuerySelectorAll("option").Select(option => option.TextContent.Trim()), Is.EqualTo(new[] {
            "Select card…", "alpha #2", "Alpha #3", "beta #4", "Beta #5", "beta #6", "zebra #1",
            "Unnamed card #7", "Unnamed card #8"
        }));
        Assert.That(selector.QuerySelectorAll("option").Select(option => option.GetAttribute("value")), Is.EqualTo(new[] {
            "", "alpha-lower", "alpha-upper", "beta-first", "beta-case", "beta-second", "zebra",
            "unnamed-first", "unnamed-second"
        }));
        Assert.That(selector.QuerySelectorAll("option").Any(option => option.GetAttribute("value") == "missing-card"), Is.False);
        Assert.That(cut.FindComponents<CardEditor>().Select(cardEditor => cardEditor.Instance.Card.Id), Is.EqualTo(deckOrder));

        await selector.ChangeAsync(new() { Value = "beta-case" });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        Assert.That(editor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo("beta-case"));

        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Category" });
        Assert.That(editor.Find("#comboCategory0").QuerySelectorAll("option").Skip(1).Select(option => option.TextContent.Trim()),
            Is.EqualTo(new[] { "Zeta", "Alpha" }), "category selectors retain configured category order");
        Assert.That(cut.FindComponents<CardEditor>().Select(cardEditor => cardEditor.Instance.Card.Id), Is.EqualTo(deckOrder));
    }

    [Test]
    public async Task DeckSortIsOneShotStableAndPreservesCardAndComboDrafts() {
        CategoryBase draftCategory = new("Draft Category");
        CategoryBase otherCategory = new("Other Category");
        CategoryBase fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        Card zebra = new([draftCategory, fire], 3, "Zebra", false, "zebra", 123, [fire.MetadataKey!]);
        Card betaFirst = new([], 2, "beta", id: "beta-first");
        Card betaCase = new([], 1, "Beta", id: "beta-case");
        Card alphaLower = new([], 2, "alpha", id: "alpha-lower");
        Card betaSecond = new([], 1, "beta", id: "beta-second");
        Card alphaUpper = new([], 2, "Alpha", id: "alpha-upper");
        Card unnamedFirst = new([], 1, null, id: "unnamed-first");
        Card unnamedSecond = new([], 1, "  ", id: "unnamed-second");
        List<Card> cards = [zebra, betaFirst, betaCase, alphaLower, betaSecond, alphaUpper, unnamedFirst, unnamedSecond];
        Dictionary<string, (string? Name, int Copies, bool Active, int? ExternalCardId, string[] CategoryIdentities, string[] ManualKeys)> originalState = cards.ToDictionary(card => card.Id, card => (
            card.Name,
            card.Copies,
            card.Active,
            card.ExternalCardId,
            CategoryIdentities: card.Categories.Select(category => category.Identity).ToArray(),
            ManualKeys: card.ManualMetadataCategoryKeys.ToArray()));
        Combo combo = new([], "Direct", cards: [new(zebra.Id, 0, 0)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [draftCategory, otherCategory], Cards = cards, Combos = [combo], HandSize = 1
        });
        IRenderedComponent<CardListEditor> deckEditor = cut.FindComponent<CardListEditor>();
        Assert.That(deckEditor.FindAll("button").Count(button => button.TextContent.Trim() == "Sort A–Z"), Is.EqualTo(1));
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll("button").Any(button => button.TextContent.Trim() == "Sort A–Z"), Is.False);
        Assert.That(cut.FindComponent<ComboListEditor>().FindAll("button").Any(button => button.TextContent.Trim() == "Sort A–Z"), Is.False);
        IElement sortButton = deckEditor.Find("button[aria-label='Sort deck A–Z']");
        Assert.That(sortButton.GetAttribute("title"), Is.EqualTo("Sort deck A–Z"));

        IRenderedComponent<CardEditor> zebraEditor = cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == zebra.Id);
        CardEditor originalZebraEditor = zebraEditor.Instance;
        await zebraEditor.Find(".accordion-button").ClickAsync(new());
        await zebraEditor.Find("#cardCategory0").ChangeAsync(new() { Value = otherCategory.Identity });

        IRenderedComponent<ComboEditor> comboEditor = cut.FindComponent<ComboEditor>();
        ComboEditor originalComboEditor = comboEditor.Instance;
        await comboEditor.Find(".accordion-button").ClickAsync(new());
        await comboEditor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await comboEditor.Find("#comboCard0").ChangeAsync(new() { Value = alphaLower.Id });
        await comboEditor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await comboEditor.Find("#maxCount0").InputAsync(new() { Value = "3" });

        await sortButton.ClickAsync(new());
        IReadOnlyList<IRenderedComponent<CardEditor>> sortedEditors = cut.FindComponents<CardEditor>();
        Assert.That(sortedEditors.Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] {
            alphaLower.Id, alphaUpper.Id, betaFirst.Id, betaCase.Id, betaSecond.Id, zebra.Id,
            unnamedFirst.Id, unnamedSecond.Id
        }));
        foreach (Card card in cards) {
            Assert.That(sortedEditors.Single(editor => editor.Instance.Card.Id == card.Id).Instance.Card, Is.SameAs(card));
        }
        zebraEditor = sortedEditors.Single(editor => editor.Instance.Card.Id == zebra.Id);
        Assert.That(zebraEditor.Instance, Is.SameAs(originalZebraEditor));
        Assert.That(zebraEditor.Instance.ActiveCardIndex, Is.EqualTo(5));
        Assert.That(zebraEditor.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(zebraEditor.Find("#cardCategory5").GetAttribute("value"), Is.EqualTo(otherCategory.Identity));
        Assert.That(zebraEditor.Find("#cardName5").GetAttribute("value"), Is.EqualTo("Zebra"));
        Assert.That(zebraEditor.Find("#cardCopies5").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(zebraEditor.Instance.Card, Is.SameAs(zebra));
        Assert.That((zebra.Copies, zebra.Name, zebra.Active, zebra.ExternalCardId), Is.EqualTo((3, "Zebra", false, (int?)123)));
        Assert.That(zebra.Categories.Select(category => category.Identity), Is.EqualTo(originalState[zebra.Id].CategoryIdentities));
        Assert.That(zebra.ManualMetadataCategoryKeys, Is.EquivalentTo(originalState[zebra.Id].ManualKeys));
        foreach (IRenderedComponent<CardEditor> editor in sortedEditors) {
            Card card = editor.Instance.Card;
            (string? Name, int Copies, bool Active, int? ExternalCardId, string[] CategoryIdentities, string[] ManualKeys) before = originalState[card.Id];
            Assert.That((card.Name, card.Copies, card.Active, card.ExternalCardId),
                Is.EqualTo((before.Name, before.Copies, before.Active, before.ExternalCardId)));
            Assert.That(card.Categories.Select(category => category.Identity), Is.EqualTo(before.CategoryIdentities));
            Assert.That(card.ManualMetadataCategoryKeys, Is.EquivalentTo(before.ManualKeys));
        }

        Assert.That(comboEditor.Instance, Is.SameAs(originalComboEditor));
        Assert.That(comboEditor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo(alphaLower.Id));
        Assert.That(comboEditor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(comboEditor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(comboEditor.Instance.Combo.Cards.Single().CardId, Is.EqualTo(zebra.Id));

        await cut.Find("[aria-label='Move card alpha, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] {
            alphaUpper.Id, alphaLower.Id, betaFirst.Id, betaCase.Id, betaSecond.Id, zebra.Id,
            unnamedFirst.Id, unnamedSecond.Id
        }));
        Assert.That(zebraEditor.Find("#cardCategory5").GetAttribute("value"), Is.EqualTo(otherCategory.Identity));
    }

    [Test]
    public async Task DeckSortInvalidatesResultsAndPersistsThroughSaveLoadAndManualMove() {
        Card zebra = new([a], 2, "Zebra", id: "zebra");
        Card alpha = new([a], 2, "Alpha", id: "alpha");
        CountingProbabilityCalculator calculator = new();
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a], Cards = [zebra, alpha], Combos = [new([new(a, 1, 2)], "Any A")], HandSize = 2
        });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(calculator.CallCount, Is.EqualTo(1));

        await cut.Find("button[aria-label='Sort deck A–Z']").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(calculator.CallCount, Is.EqualTo(1), "sorting marks the result stale without calculating again");
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] { alpha.Id, zebra.Id }));
        await Button(cut, "Save Session").ClickAsync(new());
        string firstSave = SavedSessionJson();
        using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(firstSave)) {
            System.Text.Json.JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("Cards").EnumerateArray().Select(card => card.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { alpha.Id, zebra.Id }));
            Assert.That(root.TryGetProperty("SortMode", out _), Is.False);
        }
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(firstSave, "sorted.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id),
            Is.EqualTo(new[] { alpha.Id, zebra.Id })));

        await cut.Find("[aria-label='Move card Alpha, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] { zebra.Id, alpha.Id }));
        await Button(cut, "Save Session").ClickAsync(new());
        string secondSave = SavedSessionJson(1);
        using System.Text.Json.JsonDocument secondDocument = System.Text.Json.JsonDocument.Parse(secondSave);
        Assert.That(secondDocument.RootElement.GetProperty("Cards").EnumerateArray().Select(card => card.GetProperty("Id").GetString()),
            Is.EqualTo(new[] { zebra.Id, alpha.Id }));
    }

    [Test]
    public void DeletingEarlierRowsPreservesSurvivingDraftsAndActiveEditors() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>()[1];
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>()[1];
        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select[id^='cardCategory']").Change("user:A");
        combo.Find("select").Change("user:A");
        combo.Find("#minCount1").Input("0");
        combo.Find("#maxCount1").Input("0");
        cut.FindComponents<CardEditor>()[0].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[0].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(1));
        Assert.That(card.Find("select[id^='cardCategory']").GetAttribute("value"), Is.EqualTo("user:A"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Button(card, "Add").Click();
        Button(combo, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Second"));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (None)"));
        card.Find("[title='Remove card']").Click();
        combo.Find("[title='Remove combo']").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void ReorderingRowsKeepsDraftsWithTheirModels() {
        SessionState session = Session();
        IRenderedComponent<CardListEditor> cards = context.RenderComponent<CardListEditor>(parameters => {
            parameters
                .Add(component => component.Cards, session.Cards)
                .Add(component => component.CategoryBases, session.Categories);
        });
        IRenderedComponent<ComboListEditor> combos = context.RenderComponent<ComboListEditor>(parameters => {
            parameters
                .Add(component => component.Combos, session.Combos)
                .Add(component => component.CategoryBases, session.Categories);
        });
        cards.FindComponents<CardEditor>()[0].Find("select[id^='cardCategory']").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("select").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");
        combos.FindComponents<ComboEditor>()[0].Find("#maxCount0").Input("0");
        session.Cards.Reverse();
        session.Combos.Reverse();
        cards.SetParametersAndRender(parameters => parameters.Add(component => component.Cards, session.Cards));
        combos.SetParametersAndRender(parameters => parameters.Add(component => component.Combos, session.Combos));
        Assert.That(cards.FindComponents<CardEditor>()[1].Find("select[id^='cardCategory']").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(combos.FindComponents<ComboEditor>()[1].Find("#maxCount1").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cards.FindComponents<CardEditor>()[0].Find("select[id^='cardCategory']").GetAttribute("value"), Is.Null.Or.Empty);
    }

}
