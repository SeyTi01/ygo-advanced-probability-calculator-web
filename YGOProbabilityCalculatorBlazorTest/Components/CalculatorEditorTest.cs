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

    // Synchronous bUnit events discard their dispatcher task; calculation tests await events
    // before asserting or editing again, and retain pending calculation tasks until release.
    private static IElement Button(IRenderedFragment fragment, string text) =>
        fragment.FindAll("button").Single(element => element.TextContent.Trim() == text);

    private static void AssertPreviousResult(IRenderedFragment fragment) {
        Assert.That(fragment.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(fragment.Find(".probability-result-status").TextContent.Trim(),
            Is.EqualTo("Previous result · inputs changed"));
    }

    private static async Task RenameGroupWithEnter(IRenderedFragment fragment, string oldName, string newName) {
        await fragment.Find($"[aria-label='Edit group {oldName}']").ClickAsync(new());
        var input = fragment.Find($"[aria-label='New name for group {oldName}']");
        await input.InputAsync(new() { Value = newName });
        await input.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }

    private SessionState Session() => new() {
        Categories = [a, b], Cards = [new([a], 2, "First"), new([b], 2, "Second")],
        Combos = [new([new(a, 1, 2)], "First combo"), new([new(b, 1, 2)], "Second combo")], HandSize = 2
    };

    [Test]
    public async Task CardPropertiesAreHiddenAndSameLabelRequirementsCanBeEditedIndependently() {
        var user = new CategoryBase("Spell");
        var spell = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var quick = new CategoryBase("Quick-Play Spell", CategorySource.Metadata, "spell-type:quick-play");
        var orphan = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var cut = Render(new SessionState {
            Categories = [user, b], Cards = [new([spell, quick], 2, "Imported", externalCardId: 123)],
            Combos = [new([new(orphan, 0, 0)]), new([])], HandSize = 1,
            CategoryColorIndices = new() { ["Spell"] = 5, ["B"] = 2 }
        });
        var categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(2));
        var card = cut.FindComponent<CardEditor>();
        Assert.That(card.FindAll(".category-tag"), Is.Empty);
        Assert.That(card.FindAll("select option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { "", "user:Spell", "user:B" }));
        var combo = cut.FindComponents<ComboEditor>()[1];
        Assert.That(combo.FindAll("optgroup").Select(g => g.GetAttribute("label")), Is.EqualTo(new[] { "User categories", "Card properties" }));
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.TextContent),
            Is.EqualTo(new[] { "Monster", "Quick-Play Spell", "Spell" }));
        Assert.That(combo.FindAll("option").Where(o => o.TextContent == "Spell").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { "user:Spell", "metadata:kind:spell" }));

        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await Button(combo, "Add").ClickAsync(new());
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = user.Identity });
        await Button(combo, "Add").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Source),
            Is.EqualTo(new[] { CategorySource.Metadata, CategorySource.User }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await combo.Find("#minCount1").InputAsync(new() { Value = "0" });
        await combo.Find("#maxCount1").InputAsync(new() { Value = "0" });
        await Button(combo, "Update").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories[0].MaxCount, Is.Zero);
        Assert.That(combo.Instance.Combo.Categories[1].MinCount, Is.EqualTo(1));

        // A metadata membership does not prevent adding/removing a same-label user membership.
        await card.Find("select").ChangeAsync(new() { Value = user.Identity });
        await Button(card, "Add").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Has.Count.EqualTo(3));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await categories.Find("[aria-label='Edit category Spell']").ClickAsync(new());
        await categories.Find("[aria-label='New name for category Spell']").InputAsync(new() { Value = "Role" });
        await categories.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(combo.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo(spell.Identity));
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Name), Is.EqualTo(new[] { "Spell", "Role" }));
        Assert.That(card.Instance.Card.Categories[0], Is.EqualTo(spell));
        Assert.That(card.Instance.Card.Categories[2].Name, Is.EqualTo("Role"));
        await card.Find("[aria-label='Remove category Role from card Imported']").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Is.EqualTo(new[] { spell, quick }));
        Assert.That(combo.FindAll(".accordion-body .text-bg-secondary"), Has.Count.EqualTo(1));
        Assert.That(combo.FindAll(".accordion-body [class*='category-color-']"), Has.Count.EqualTo(1));

        await combo.Find("[aria-label='Remove category Role from combo Combo 2']").ClickAsync(new());
        await categories.Find("[aria-label='Remove category Role']").ClickAsync(new());
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(combo.Instance.Combo.Categories.Single().BaseCategory, Is.EqualTo(spell));
        await cut.Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { orphan.Identity, spell.Identity }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        Assert.That(Button(combo, "Update"), Is.Not.Null);
    }

    [Test]
    public void CategoryEditorDoesNotExposeMetadataDefinitionsEvenIfSupplied() {
        var property = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var cut = context.RenderComponent<CategoryListEditor>(p => p.Add(x => x.CategoryBases, [a, property])
            .Add(x => x.Cards, []).Add(x => x.Combos, []));
        Assert.That(cut.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Edit category Spell']"), Is.Empty);
    }

    [Test]
    public async Task ReorderControlsMoveAllFourListsAndKeepSessionOrderAndReferences() {
        var c = new CategoryBase("C");
        var cards = new List<Card> { new([a], 1, "Same"), new([b], 1, "Same"), new([c], 1, "") };
        var groups = new List<ComboGroup> { new("g1", "One"), new("g2", "Two"), new("g3", "Three") };
        var combos = new List<Combo> {
            new([new(a, 1, 1)], "Same", groupId: "g1", cards: [new(cards[0].Id, 1, 1)]),
            new([new(b, 1, 1)], "Same", groupId: "g2"),
            new([new(c, 1, 1)], "", groupId: "g3")
        };
        var cut = Render(new SessionState { Categories = [a, b, c], Cards = cards,
            Combos = combos, ComboGroups = groups, HandSize = 1 });

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
        var totalBeforeMove = cut.Find(".probability-total-value").TextContent;
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

        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(x => x.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponents<CardEditor>().Select(x => x.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(x => x.Instance.Combo),
            Is.EqualTo(new[] { combos[2], combos[1], combos[0] }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(x => x.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.Cards.Single().CardId, Is.EqualTo(cards[0].Id));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.GroupId, Is.EqualTo("g1"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(totalBeforeMove));
        Assert.That(cut.FindAll(".probability-group .combo-probability-name").Select(x => x.TextContent.Trim().Split(' ')[0]),
            Is.EqualTo(new[] { "Three", "Two", "One" }));
        Assert.That(cut.FindAll(".combo-probability-item .combo-probability-name")[0].TextContent,
            Does.Contain("Unnamed combo 1"));

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(json)) {
            var root = document.RootElement;
            Assert.That(root.GetProperty("Categories").EnumerateArray().Select(x => x.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "C", "B", "A" }));
            Assert.That(root.GetProperty("Cards").EnumerateArray().Select(x => x.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
            Assert.That(root.GetProperty("Combos").EnumerateArray().Select(x => x.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "", "Same", "Same" }));
            Assert.That(root.GetProperty("ComboGroups").EnumerateArray().Select(x => x.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { "g3", "g2", "g1" }));
        }
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "ordered.json"));
        Assert.That(cut.FindComponents<CardEditor>().Select(x => x.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(x => x.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(x => x.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(x => x.Instance.Combo.GroupId),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
    }

    [Test]
    public void MovingExpandedEditorsKeepsDraftAndTargetsMovedItem() {
        var session = Session();
        session.Cards[1] = session.Cards[1].WithActive(false);
        session.Combos[1] = session.Combos[1].WithActive(false);
        var cut = Render(session);
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
        var session = new SessionState {
            Categories = [a, b], Cards = [new([a], 2, "First")],
            Combos = [new([new(a, 1, 2)], "First combo")],
            ComboGroups = [new("g1", "One"), new("g2", "Two")], HandSize = 2
        };
        var cut = Render(session);
        cut.Find("[aria-label='Edit category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed");
        var categoryChip = cut.FindAll(".category-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for category A']") is not null);
        Assert.That(categoryChip.QuerySelectorAll("button").Select(x => x.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save category name", "Move category A left", "Move category A right", "Exit category edit mode", "Remove category A" }));
        cut.Find("[aria-label='Move category A right']").Click();
        Assert.That(cut.Find("[aria-label='New name for category A']").GetAttribute("value"), Is.EqualTo("Renamed"));
        cut.Find("[aria-label='Save category name']").Click();
        cut.Find("[aria-label='Edit group One']").Click();
        cut.Find("[aria-label='New name for group One']").Input("Updated");
        var groupChip = cut.FindAll(".combo-group-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for group One']") is not null);
        Assert.That(groupChip.QuerySelectorAll("button").Select(x => x.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save group name", "Move group One left", "Move group One right", "Exit group edit mode", "Remove group One" }));
        cut.Find("[aria-label='Move group One right']").Click();
        Assert.That(cut.Find("[aria-label='New name for group One']").GetAttribute("value"), Is.EqualTo("Updated"));
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.FindAll(".category-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").Click();
        Assert.That(cut.Find("#cardCategory0").QuerySelectorAll("option").Skip(1).Select(x => x.TextContent.Trim()),
            Is.EqualTo(new[] { "B", "Renamed" }));
        Assert.That(cut.Find("#comboGroup0").QuerySelectorAll("option").Skip(1).Select(x => x.TextContent.Trim()),
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
    public void CardOnlyComboCanBeEditedAndMissingReferenceBlocksCalculationUntilRemoved() {
        var first = new Card([], 2, "Twin");
        var second = new Card([], 2, "Twin");
        var combo = new Combo([], "Direct", cards: [new(first.Id, 0, 0)]);
        var cut = Render(new SessionState { Cards = [first, second], Combos = [combo], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        editor.Find(".accordion-button").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(editor.Find(".combo-card-tag").TextContent, Does.Contain("Card: Twin (0–0)"));

        editor.Find("#constraintKind0").Change("Card");
        Assert.That(editor.FindAll("#comboCard0 option").Select(option => option.TextContent.Trim()),
            Does.Contain("Twin #2"));
        editor.Find("#comboCard0").Change(first.Id);
        Assert.That(Button(editor, "Update").TextContent.Trim(), Is.EqualTo("Update"));
        editor.Find("#minCount0").Input("1");
        editor.Find("#maxCount0").Input("1");
        Button(editor, "Update").Click();
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1–1)"));

        cut.FindComponent<CardEditor>().Find(".accordion-button").Click();
        cut.FindComponent<CardEditor>().Find("#cardName0").Input("Renamed");
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Renamed (1–1)"));
        cut.FindComponent<CardEditor>().Find("[title='Remove card']").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference"));
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-missing-card").TextContent,
            Does.Contain("Missing card"));
        cut.FindComponent<ComboEditor>().Find(".combo-missing-card button").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        editor = cut.FindComponent<ComboEditor>();
        editor.Find("#comboCard0").Change(second.Id);
        editor.Find("#maxCount0").Input("1");
        Button(editor, "Add").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1–1)"));
    }

    [Test]
    public async Task NewCardAndCategoryRequirementsCannotShareOneDrawnCard() {
        var role = new CategoryBase("Role");
        var card = new Card([role], 1, "Piece");
        var other = new Card([], 1, "Other");
        var cut = Render(new SessionState {
            Categories = [role], Cards = [card, other], Combos = [new([], "Mixed")], HandSize = 1
        });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = "user:Role" });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = card.Id });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent,
            Does.Contain(SmallDeckOracleTest.EnumerateProbability([card, other],
                [new([new(role, 1, 1)], cards: [new(card.Id, 1, 1)])], 1).ToString("P2")));
        Assert.That(editor.FindAll(".accordion-body .category-tag"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ImportDoesNotRebindSameNamedCardAndMissingZeroBoundBlocksCalculation() {
        var original = new Card([], 2, "Same");
        var replacement = new Card([], 2, "Same");
        var importer = new Mock<IDeckImportService>();
        importer.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);
        var cut = Render(new SessionState {
            Cards = [original], Combos = [new([], cards: [new(original.Id, 0, 0)])], HandSize = 1
        });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123", "deck.ydk"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Id, Is.EqualTo(replacement.Id));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
            Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-missing-card").TextContent,
                Does.Contain("Missing card"));
        });
        cut.Find("#comboActive0").Change(false);
        Assert.That(cut.Markup, Does.Not.Contain("An active combo has a missing card reference"));
    }

    [Test]
    public async Task MixedComboSessionRoundTripKeepsCardIdentityAndGroup() {
        var card = new Card([a], 2, "Piece");
        var session = new SessionState {
            Categories = [a], Cards = [card],
            Combos = [new([new(a, 1, 2)], "Mixed", groupId: "g", cards: [new(card.Id, 1, 2)])],
            ComboGroups = [new("g", "Group")], HandSize = 1
        };
        var cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain(card.Id).And.Contain("\"Cards\""));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "mixed.json"));
        {
            var loadedCard = cut.FindComponent<CardEditor>().Instance.Card;
            var loadedCombo = cut.FindComponent<ComboEditor>().Instance.Combo;
            Assert.That(loadedCard.Id, Is.EqualTo(card.Id));
            Assert.That(loadedCombo.Cards.Single().CardId, Is.EqualTo(card.Id));
            Assert.That(loadedCombo.GroupId, Is.EqualTo("g"));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent,
            Does.Contain(1.0.ToString("P2")));
    }

    [Test]
    public async Task TooManyActiveCombosExplainTheLimitAndInactiveCombosDoNotCount() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.AddRange(Enumerable.Range(0, 31)
            .Select(_ => new Combo(new[] { new ComboCategory(a, 1, 2) })));
        var cut = Render(session);
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("at most 30 active combos"));
        await cut.Find("#comboActive30").ChangeAsync(new() { Value = false });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        for (var index = 1; index < 30; index++) await cut.Find($"#comboActive{index}").ChangeAsync(new() { Value = false });
        await Button(cut, "Calculate").ClickAsync(new());
        var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, [session.Combos[0]], session.HandSize);
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
    }

    [Test]
    public void ComboHeaderKeepsVisibleBadgesAndAccessibleControlsDistinct() {
        var categories = Enumerable.Range(1, 7)
            .Select(index => new CategoryBase($"Long Category {index}"))
            .ToList();
        const string comboName = "A Long Combo Name For Mobile";
        var combo = new Combo(categories.Select(category => new ComboCategory(category, 1, 5)).ToList(), comboName);
        var cut = Render(new SessionState {
            Categories = categories,
            Cards = [],
            Combos = [combo],
            HandSize = 5
        });
        var editor = cut.FindComponent<ComboEditor>();
        var header = editor.Find(".combo-editor-header");
        var accordionButton = header.QuerySelector("button.accordion-button");

        Assert.That(accordionButton, Is.Not.Null);
        Assert.That(accordionButton!.GetAttribute("aria-controls"), Is.EqualTo("combo0"));
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(accordionButton.TextContent, Does.Contain(comboName));
        Assert.That(accordionButton.QuerySelectorAll(".combo-header-content .category-tag").Length, Is.EqualTo(6));
        Assert.That(accordionButton.QuerySelector(".combo-header-content .badge.bg-secondary")?.TextContent, Is.EqualTo("+1 more"));
        Assert.That(accordionButton.TextContent, Does.Contain("Long Category 1 (1–5)"));

        var actionButtons = header.QuerySelectorAll("button");
        var activeToggle = header.QuerySelector("input.entry-editor-active-checkbox");
        var removeButton = header.QuerySelector("button[aria-label='Remove combo']");
        Assert.That(actionButtons.Length, Is.EqualTo(4));
        Assert.That(header.QuerySelectorAll(".reorder-controls button").Length, Is.EqualTo(2));
        Assert.That(activeToggle?.GetAttribute("aria-label"), Is.EqualTo($"Active combo {comboName}"));
        Assert.That(activeToggle?.GetAttribute("title"), Is.EqualTo($"Toggle {comboName} active state"));
        Assert.That(removeButton?.GetAttribute("title"), Is.EqualTo("Remove combo"));

        accordionButton.Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        editor.Find(".combo-editor-header .accordion-button").Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
    }

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
        card.Find("select").Change("user:A");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("already added"));
        card.Find("select").Change("missing");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("not found"));
        card.Find("select").Change("user:B");
        card.Find("input[type=number]").Input("6");
        card.Find("#cardName0").Input("Renamed");
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("user:B"));
        Button(card, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Renamed").And.Contain("(6)"));
        Assert.That(card.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ZeroMaximumSurvivesRenameParentRerenderAndHandSizeChangeAndCanBeUpdated() {
        var cut = Render(Session());
        var combo = cut.FindComponents<ComboEditor>()[0];
        combo.Find("select").Change("user:A");
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
        combo.Find("select").Change("user:A");
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
        combo.Find("select").Change("user:B");
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
        combo.Find("select").Change("user:B");
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
        card.Find("select").Change("user:A");
        combo.Find("select").Change("user:A");
        combo.Find("#minCount1").Input("0");
        combo.Find("#maxCount1").Input("0");
        cut.FindComponents<CardEditor>()[0].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[0].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(1));
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("user:A"));
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
        cards.FindComponents<CardEditor>()[0].Find("select").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("select").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");
        combos.FindComponents<ComboEditor>()[0].Find("#maxCount0").Input("0");
        session.Cards.Reverse();
        session.Combos.Reverse();
        cards.SetParametersAndRender(p => p.Add(x => x.Cards, session.Cards));
        combos.SetParametersAndRender(p => p.Add(x => x.Combos, session.Combos));
        Assert.That(cards.FindComponents<CardEditor>()[1].Find("select").GetAttribute("value"), Is.EqualTo("user:B"));
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
    public async Task SessionSaveLoadRoundTripPreservesZeroAndResetsOldEditorDrafts() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 0, 0)], "No A"));
        var cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"MaxCount\": 0"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var combo = cut.FindComponent<ComboEditor>();
        await combo.Find("select").ChangeAsync(new() { Value = "user:B" });
        await combo.Find("#minCount0").InputAsync(new() { Value = "2" });
        await combo.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[placeholder='Category name']").InputAsync(new() { Value = "Old draft" });
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        {
            var loaded = cut.FindComponent<ComboEditor>();
            Assert.That(loaded.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(loaded.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(loaded.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
            Assert.That(cut.Find("[placeholder='Category name']").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".alert-primary").TextContent, Does.Contain((1.0 / 6).ToString("P2")));
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(
            cut.FindComponent<CategoryListEditor>().Find("[role=alert]").TextContent, Does.Contain("still used"));
    }

    [Test]
    public async Task DeckImportReplacesRowsWithoutReusingDraftsAndUpdatesCalculationEligibility() {
        // Exercise the real YDK parser; only the external card-name lookup is stubbed.
        context.Services.AddSingleton<IProbabilityCalculatorService>(
            new SequencedProbabilityCalculator(
                new ProbabilityCalculationResult(
                    0.75,
                    [new ComboProbabilityResult(0, "Unused result", 0.6)])));
        context.Services.AddSingleton<IFileService, FileService>();
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(x => x.GetCardInfoAsync(123)).ReturnsAsync(new CardInfo { Id = 123, Name = "Imported" });
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<IDeckImportService, DeckImportService>();
        var cut = Render(Session());
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        await cut.FindComponents<CardEditor>()[0].Find("select").ChangeAsync(new() { Value = "user:B" });
        await cut.FindComponents<CardEditor>()[0].Find(".accordion-button").ClickAsync(new());
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123\n123\n#extra\n456", "deck.ydk"));
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        var card = cut.FindComponent<CardEditor>();
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo(groupId));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Imported").And.Contain("(2)"));
        Assert.That(card.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SessionLoadClearsPreviousResultsAndDiscardsInFlightCompletion(bool failCalculation) {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(0.9, [new ComboProbabilityResult(0, "Old session", 0.8)]),
            failSecond: failCalculation);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Null);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            const string nextSession = """
                {
                  "Categories": [{"Name":"Loaded"}],
                  "Cards": [{"Categories":[{"Name":"Loaded"}],"Copies":3,"Name":"Loaded card"}],
                  "Combos": [{"Categories":[{"BaseCategory":{"Name":"Loaded"},"MinCount":0,"MaxCount":0}],"Name":"Loaded combo"}],
                  "HandSize": 1
                }
                """;
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(nextSession, "next.json"));
            {
                Assert.That(cut.FindAll(".probability-results"), Is.Empty);
                Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("1"));
                Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Loaded card"));
                Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Name, Is.EqualTo("Loaded combo"));
            }
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
    }

    [Test]
    public async Task ResultsShowStandaloneProbabilitiesInOrderForDuplicateAndUnnamedCombos() {
        var session = new SessionState {
            Categories = [a, b],
            Cards = [new([a], 2, "A copies"), new([b], 2, "B copies")],
            Combos = [
                new([new(a, 1, 1)], "Duplicate"),
                new([new(a, 1, 1)], "Duplicate"),
                new([new(b, 0, 0)])
            ],
            HandSize = 2
        };
        var cut = Render(session);
        var expectedTotal = SmallDeckOracleTest.EnumerateProbability(session.Cards, session.Combos, session.HandSize);
        var expectedStandalone = session.Combos
            .Select(combo => SmallDeckOracleTest.EnumerateProbability(session.Cards, [combo], session.HandSize))
            .ToArray();

        await Button(cut, "Calculate").ClickAsync(new());
        {
            var result = cut.Find(".probability-results");
            Assert.That(result.GetAttribute("aria-live"), Is.EqualTo("polite"));
            var totalRow = result.QuerySelector(".probability-total")!;
            Assert.That(totalRow.ClassList.Contains("combo-probability-row"), Is.True);
            Assert.That(totalRow.ParentElement!.ClassList.Contains("probability-results"), Is.True,
                "the summary row must sit outside the numbered combo list");
            Assert.That(totalRow.QuerySelector(".combo-probability-name")!.TextContent.Trim(),
                Is.EqualTo("Any active combo"));
            Assert.That(totalRow.QuerySelector(".combo-probability-value")!.TextContent,
                Is.EqualTo(expectedTotal.ToString("P2")));

            var rows = result.QuerySelectorAll(".combo-probability-item");
            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows.All(row => row.QuerySelector(".combo-probability-row") is not null), Is.True);
            Assert.That(rows.Select(row => row.QuerySelector(".combo-probability-name")!.TextContent),
                Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
            for (var index = 0; index < rows.Length; index++) {
                Assert.That(rows[index].QuerySelector(".combo-probability-value")!.TextContent,
                    Is.EqualTo(expectedStandalone[index].ToString("P2")));
            }
        }
    }

    [Test]
    public void ComboGroupsUseCategoryStyleInlineEditingAndKeepAssignmentsAndComboDrafts() {
        var cut = Render(Session());
        var first = cut.FindComponents<ComboEditor>()[0];
        first.Find("#comboCategory0").Change("user:B");
        first.Find("#minCount0").Input("0");
        first.Find("#maxCount0").Input("0");

        cut.Find("[aria-label='Add combo group']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        cut.Find("[aria-label='New combo group name']").Input("tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        var groupId = first.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        first.Find("#comboGroup0").Change(groupId);
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='New combo group name']").Input("Tier 2");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        var secondId = cut.FindAll("#comboGroup0 option:not([value=''])")[1].GetAttribute("value")!;

        cut.Find("[aria-label='Edit group Tier 1']").Click();
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 1);
        cut.Find("[aria-label='New name for group Tier 1']").Input("");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("tier 2");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("Tier One");
        cut.Find("[aria-label='New name for group Tier 1']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 2);
        Assert.That(cut.Find("[aria-label='Edit group Tier One']").TextContent, Is.EqualTo("Tier One"));
        Assert.That(first.Instance.Combo.GroupId,
            Is.EqualTo(groupId));
        Assert.That(cut.Find("#comboGroup0").TextContent, Does.Contain("Tier One"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='Edit group Tier 2']").Click();
        var secondRename = cut.Find("[aria-label='New name for group Tier 2']");
        secondRename.Input("Discarded");
        secondRename.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 4);
        Assert.That(cut.Find("[aria-label='Edit group Tier 2']").TextContent, Is.EqualTo("Tier 2"));

        cut.Find("#comboGroup0").Change(secondId);
        Assert.That(first.Instance.Combo.GroupId, Is.EqualTo(secondId));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='Remove group Tier One']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        Assert.That(first.Instance.Combo.GroupId, Is.EqualTo(secondId));
        cut.Find("[aria-label='Remove group Tier 2']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupResultsUseActiveMembersAndAllGroupChangesInvalidateResults() {
        var session = Session();
        var cut = Render(session);
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await cut.Find("#comboGroup1").ChangeAsync(new() { Value = groupId });
        var groupChip = cut.Find(".combo-group-chip");
        Assert.That(groupChip.ClassList.Contains("me-2"), Is.True);
        Assert.That(groupChip.QuerySelector(".category-name-trigger")?.TextContent.Trim(), Is.EqualTo("Tier 1"));

        await RenameGroupWithEnter(cut, "Tier 1", "Tier One");
        await Button(cut, "Calculate").ClickAsync(new());
        {
            var result = cut.Find(".probability-results");
            var active = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo).ToList();
            var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, active, 2);
            Assert.That(result.QuerySelector(".probability-group .combo-probability-value")!.TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(result.QuerySelectorAll(".combo-probability-item").Length, Is.EqualTo(2));
            Assert.That(result.QuerySelector(".probability-group")!.TextContent, Does.Contain("Tier One"));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }

        var secondComboEditor = cut.FindComponents<ComboEditor>()[1];
        await secondComboEditor.Find("#comboActive1").ChangeAsync(new() { Value = false });
        Assert.That(secondComboEditor.Find("#comboActive1").HasAttribute("checked"), Is.False);
        Assert.That(secondComboEditor.Instance.Combo.Active, Is.False);
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        {
            var first = cut.FindComponents<ComboEditor>()[0].Instance.Combo;
            var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, [first], 2);
            Assert.That(cut.Find(".probability-group .combo-probability-value").TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = "" });
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-group .combo-probability-value").TextContent, Is.EqualTo(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void GroupMembershipAndInactiveStateRoundTripWhileLegacySessionsRemainUngrouped() {
        var cut = Render(Session());
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='Add combo group']").Click();
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        cut.Find("#comboGroup0").Change(groupId);
        cut.Find("#comboActive0").Change(false);
        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"ComboGroups\"").And.Contain("\"GroupId\""));

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "groups.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo(groupId));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Active, Is.False);
        });

        const string legacy = """
            {"Categories":[{"Name":"A"}],"Cards":[],"Combos":[{"Categories":[],"Name":"Legacy"}],"HandSize":5}
            """;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(legacy, "legacy.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.GroupId, Is.Null);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Active, Is.True);
        });
    }

    [Test]
    public async Task CategoryRenameInvalidatesAndRecalculatesTheWholeResultSet() {
        var cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));

        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed A" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        AssertPreviousResult(cut);

        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card)
            .Where(card => card.Active).ToList();
        var combos = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo)
            .Where(combo => combo.Active).ToList();
        var expected = SmallDeckOracleTest.EnumerateProbability(cards, combos, 2);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-total").TextContent,
            Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task ResultsRemainVisibleAndAreReplacedOnlyWhenRecalculationSucceeds() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Updated combo", 0.6)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));

        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(calculator.CallCount, Is.EqualTo(1), "input edits must not calculate automatically");

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(cut.Find(".results-section button").HasAttribute("disabled"), Is.True);
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.75.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Updated combo"));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task InvalidatedInFlightCalculationCannotReplaceThePreviousResult() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Stale completion", 0.8)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        Assert.That(cut.Markup, Does.Not.Contain("Stale completion"));
    }

    [Test]
    public async Task CalculationErrorKeepsOldNumbersMarkedAsPreviousInputs() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Unused result", 0.8)]),
            failSecond: true);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("Calculation failed: expected test failure"));
    }

    [Test]
    public async Task InputChangeDuringCalculationCannotRestoreStaleTotalOrComboRows() {
        var delayedCalculator = new DelayedProbabilityCalculator();
        context.Services.AddSingleton<IProbabilityCalculatorService>(delayedCalculator);
        var cut = Render(Session());

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await delayedCalculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }
        finally {
            delayedCalculator.Continue.Set();
            await calculation;
        }

        Assert.That(calculation.IsCompletedSuccessfully, Is.True, "the stale event handler must finish before checking its result");
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResourceLimitIsExplainedUnlessInputsHaveChanged(bool changeInputs) {
        var calculator = new DelayedProbabilityCalculator { ExceedLimit = true };
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeInputs) await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        finally {
            calculator.Continue.Set();
            await calculation;
        }
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.Markup.Contains("Calculation stopped"), Is.EqualTo(!changeInputs));
    }

    private sealed class DelayedProbabilityCalculator : IProbabilityCalculatorService {
        public bool ExceedLimit { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Continue { get; } = new(false);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
            Started.SetResult();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the delayed calculation.");
            if (ExceedLimit) throw new ProbabilityCalculationLimitException();
            return new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Stale combo", 0.5)]);
        }
    }

    private sealed class SequencedProbabilityCalculator(
        ProbabilityCalculationResult secondResult,
        bool failSecond = false) : IProbabilityCalculatorService {
        private int callCount;

        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ContinueSecond { get; } = new(false);
        public int CallCount => Volatile.Read(ref callCount);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1) {
                return new ProbabilityCalculationResult(
                    0.25,
                    [new ComboProbabilityResult(0, "Original combo", 0.2)]);
            }

            SecondStarted.SetResult();
            if (!ContinueSecond.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the second calculation.");
            if (failSecond)
                throw new InvalidOperationException("expected test failure");

            return secondResult;
        }
    }
}
