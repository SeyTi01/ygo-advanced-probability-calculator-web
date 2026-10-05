using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestCombos : CalculatorEditorTestBase
{
    [Test]
    public void ComboHeaderKeepsVisibleBadgesAndAccessibleControlsDistinct()
    {
        List<CategoryBase> categories =
            [.. Enumerable.Range(1, 7).Select(index => new CategoryBase($"Long Category {index}"))];
        const string comboName = "A Long Combo Name For Mobile";
        Combo combo = new(categories.Select(category => new ComboCategory(category, 1, 5)).ToList(), comboName);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState
        {
            Categories = categories,
            Cards = [],
            Combos = [combo],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        IElement header = editor.Find(".combo-editor-header");
        IElement? accordionButton = header.QuerySelector("button.accordion-button");

        Assert.That(accordionButton, Is.Not.Null);
        Assert.That(accordionButton!.GetAttribute("aria-controls"), Is.EqualTo("combo0"));
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(accordionButton.TextContent, Does.Contain(comboName));
        Assert.That(accordionButton.QuerySelectorAll(".combo-header-content .category-tag").Length, Is.EqualTo(6));
        Assert.That(accordionButton.QuerySelector(".combo-header-content .badge.bg-secondary")?.TextContent,
            Is.EqualTo("+1 more"));
        Assert.That(accordionButton.TextContent, Does.Contain("Long Category 1 (1–5)"));

        IHtmlCollection<IElement> actionButtons = header.QuerySelectorAll("button");
        IElement? activeToggle = header.QuerySelector("input.entry-editor-active-checkbox");
        IElement? removeButton = header.QuerySelector("button[aria-label='Remove combo']");
        Assert.That(header.QuerySelector($"button[aria-label='Duplicate combo {comboName}']"), Is.Null);
        Assert.That(actionButtons.Length, Is.EqualTo(4));
        Assert.That(header.QuerySelectorAll(".reorder-controls button").Length, Is.EqualTo(2));
        Assert.That(activeToggle?.GetAttribute("aria-label"), Is.EqualTo($"Active combo {comboName}"));
        Assert.That(activeToggle?.GetAttribute("title"), Is.EqualTo($"Toggle {comboName} active state"));
        Assert.That(removeButton?.GetAttribute("title"), Is.EqualTo("Remove combo"));
        accordionButton.Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"),
            Is.EqualTo("true"));
        IElement duplicateButton =
            editor.Find(".accordion-body button[aria-label='Duplicate combo A Long Combo Name For Mobile']");
        Assert.Multiple(() =>
        {
            Assert.That(duplicateButton.TextContent.Trim(), Is.EqualTo("Duplicate combo"));
            Assert.That(duplicateButton.GetAttribute("title"), Is.EqualTo($"Duplicate combo {comboName}"));
            Assert.That(duplicateButton.GetAttribute("type"), Is.EqualTo("button"));
            Assert.That(duplicateButton.ClassList, Does.Contain("btn-sm"));
            Assert.That(duplicateButton.ClassList, Does.Contain("btn-outline-secondary"));
        });
        editor.Find(".combo-editor-header .accordion-button").Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"),
            Is.EqualTo("false"));
        Assert.That(editor.FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);
    }

    [Test]
    public async Task DuplicateActionAppearsOnlyOnTheExpandedComboAndCardHeaderControlsStayUnchanged()
    {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState
        {
            Categories = [a],
            Cards = [new([a], 3, "Card")],
            Combos = [new([], "First combo"), new([], "Second combo")],
            HandSize = 3
        });
        IReadOnlyList<IRenderedComponent<ComboEditor>> combos = cut.FindComponents<ComboEditor>();
        IElement cardHeader = cut.FindComponent<CardEditor>().Find(".entry-editor-header");

        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(cardHeader.QuerySelectorAll("button").Length, Is.EqualTo(4));
            Assert.That(cardHeader.QuerySelectorAll(".reorder-controls button").Length, Is.EqualTo(2));
            Assert.That(cardHeader.QuerySelector("input.entry-editor-active-checkbox"), Is.Not.Null);
            Assert.That(cardHeader.QuerySelector("button[aria-label='Remove card']"), Is.Not.Null);
            Assert.That(combos[0].Find(".combo-editor-header").QuerySelectorAll("button").Length, Is.EqualTo(4));
            Assert.That(combos[0].Find(".combo-editor-header").QuerySelector("button[aria-label='Remove combo']"),
                Is.Not.Null);
        });

        await combos[0].Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        Assert.That(combos[0].Find(".accordion-body button[aria-label^='Duplicate combo ']").TextContent.Trim(),
            Is.EqualTo("Duplicate combo"));
        Assert.That(combos[1].FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);

        await combos[1].Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        Assert.That(combos[0].FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);
        Assert.That(combos[1].Find(".accordion-body button[aria-label^='Duplicate combo ']").GetAttribute("aria-label"),
            Is.EqualTo("Duplicate combo Second combo"));
    }

    [Test]
    public async Task DuplicateComboCopiesCommittedStateAfterTheSourceAndCanBeEditedIndependently()
    {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        List<Card> cards =
        [
            new([a, monster], 2, "First card", id: "first-card-id"),
            new([b], 2, "Second card", id: "second-card-id")
        ];
        Combo source = new(
            [new(a, 1, 2), new(monster, 0, 1)],
            "Starter",
            active: false,
            groupId: "group-one",
            cards: [new(cards[0].Id, 1, 2), new("missing-card-id", 0, 1)]);
        SessionState session = new()
        {
            Categories = [a, b],
            Cards = cards,
            Combos = [new([], "Before"), source, new([], "After")],
            ComboGroups = [new("group-one", "Group One"), new("group-two", "Group Two")],
            HandSize = 2
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> sourceEditor = cut.FindComponents<ComboEditor>()[1];

        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Is.Empty);
        await sourceEditor.Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        IElement duplicateAction = sourceEditor.Find(".accordion-body button[aria-label='Duplicate combo Starter']");
        Assert.Multiple(() =>
        {
            Assert.That(duplicateAction.GetAttribute("title"), Is.EqualTo("Duplicate combo Starter"));
            Assert.That(duplicateAction.GetAttribute("type"), Is.EqualTo("button"));
            Assert.That(duplicateAction.ClassList, Does.Not.Contain("btn-danger"));
        });

        await duplicateAction.ClickAsync(new());

        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors.Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new[] { "Before", "Starter", "Starter copy", "After" }));
        Assert.That(editors[1].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.That(editors[1].Instance.Combo, Is.SameAs(source));
        Assert.That(editors[2].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(editors[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));

        IRenderedComponent<ComboEditor> duplicateEditor = editors[2];
        Combo duplicate = duplicateEditor.Instance.Combo;
        Assert.Multiple(() =>
        {
            Assert.That(duplicate, Is.Not.SameAs(source));
            Assert.That(duplicate.Name, Is.EqualTo("Starter copy"));
            Assert.That(duplicate.Active, Is.False);
            Assert.That(duplicate.GroupId, Is.EqualTo("group-one"));
            Assert.That(duplicate.Categories, Is.Not.SameAs(source.Categories));
            Assert.That(duplicate.Cards, Is.Not.SameAs(source.Cards));
            Assert.That(duplicate.Categories.Select(category =>
                    (category.BaseCategory.Identity, category.MinCount, category.MaxCount)),
                Is.EqualTo(new[] { (a.Identity, 1, 2), (monster.Identity, 0, 1) }));
            Assert.That(duplicate.Categories[0], Is.Not.SameAs(source.Categories[0]));
            Assert.That(duplicate.Categories[1].BaseCategory, Is.SameAs(monster));
            Assert.That(duplicate.Cards.Select(card => (card.CardId, card.MinCount, card.MaxCount)),
                Is.EqualTo(new[] { (cards[0].Id, 1, 2), ("missing-card-id", 0, 1) }));
            Assert.That(duplicate.Cards[0], Is.Not.SameAs(source.Cards[0]));
        });

        await duplicateEditor.Find("#comboName2").InputAsync(new() { Value = "Adjusted route" });
        await duplicateEditor.Find("#comboCategory2").ChangeAsync(new() { Value = a.Identity });
        await duplicateEditor.Find("#minCount2").InputAsync(new() { Value = "2" });
        await duplicateEditor.Find("#maxCount2").InputAsync(new() { Value = "2" });
        await Button(duplicateEditor, "Update").ClickAsync(new());
        await duplicateEditor.Find(".accordion-body .combo-card-tag button").ClickAsync(new());
        await duplicateEditor.Find("#comboGroup2").ChangeAsync(new() { Value = "group-two" });
        await duplicateEditor.Find("#comboActive2").ChangeAsync(new() { Value = true });

        Assert.Multiple(() =>
        {
            Assert.That(source.Name, Is.EqualTo("Starter"));
            Assert.That(source.Active, Is.False);
            Assert.That(source.GroupId, Is.EqualTo("group-one"));
            Assert.That(source.Categories.Select(category => (category.MinCount, category.MaxCount)),
                Is.EqualTo(new[] { (1, 2), (0, 1) }));
            Assert.That(source.Cards.Select(card => card.CardId),
                Is.EqualTo(new[] { cards[0].Id, "missing-card-id" }));
            Assert.That(duplicateEditor.Instance.Combo.Name, Is.EqualTo("Adjusted route"));
            Assert.That(duplicateEditor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(2));
            Assert.That(duplicateEditor.Instance.Combo.Categories[0].MaxCount, Is.EqualTo(2));
            Assert.That(duplicateEditor.Instance.Combo.Cards.Select(card => card.CardId),
                Is.EqualTo(new[] { "missing-card-id" }));
            Assert.That(duplicateEditor.Instance.Combo.GroupId, Is.EqualTo("group-two"));
            Assert.That(duplicateEditor.Instance.Combo.Active, Is.True);
        });
    }

    [Test]
    public async Task DuplicateComboNamesUseCopySuffixesAndKeepUnnamedCombosUnnamed()
    {
        SessionState session = new()
        {
            Combos = [new([], "Starter"), new([], null)],
            HandSize = 5
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new string?[] { "Starter", "Starter copy", null }));

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new string?[] { "Starter", "Starter copy 2", "Starter copy", null }));

        await DuplicateComboAsync(cut, 3);
        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[3].Instance.Combo.Name, Is.Null);
        Assert.That(editors[4].Instance.Combo.Name, Is.Null);
        Assert.That(editors[4].Find(".combo-header-name").TextContent, Is.EqualTo("Combo 5"));
    }

    [Test]
    public async Task DuplicateComboNameCollisionsAreCaseInsensitiveAndDeterministic()
    {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState
        {
            Combos = [new([], "Starter"), new([], "starter COPY"), new([], "STARTER copy 2")],
            HandSize = 5
        });

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Starter copy 3"));

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Starter copy 4"));
    }

    [Test]
    public async Task DuplicateComboUsesCommittedModelAndPreservesExistingCategoryDraftsAndOrder()
    {
        SessionState session = Session();
        Combo before = new([], "Before");
        Combo middle = new([new(a, 1, 1)], "Middle");
        Combo after = new([new(b, 1, 1)], "After");
        session.Combos.Clear();
        session.Combos.AddRange(new[] { before, middle, after });
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> sourceEditor = cut.FindComponents<ComboEditor>()[1];
        IRenderedComponent<ComboEditor> otherEditor = cut.FindComponents<ComboEditor>()[2];

        await sourceEditor.Find("#comboCategory1").ChangeAsync(new() { Value = b.Identity });
        await sourceEditor.Find("#minCount1").InputAsync(new() { Value = "0" });
        await sourceEditor.Find("#maxCount1").InputAsync(new() { Value = "0" });
        await otherEditor.Find("#comboCategory2").ChangeAsync(new() { Value = a.Identity });
        await otherEditor.Find("#minCount2").InputAsync(new() { Value = "0" });
        await otherEditor.Find("#maxCount2").InputAsync(new() { Value = "2" });

        await DuplicateComboAsync(cut, 1);

        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[1].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.That(editors[3].Instance, Is.SameAs(otherEditor.Instance));
        Assert.Multiple(() =>
        {
            Assert.That(sourceEditor.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo(b.Identity));
            Assert.That(sourceEditor.Find("#minCount1").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(sourceEditor.Find("#maxCount1").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(editors[2].Instance.Combo.Categories.Select(category => category.BaseCategory.Identity),
                Is.EqualTo(new[] { a.Identity }));
            Assert.That(editors[2].Find("#comboCategory2").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(editors[2].Find("#minCount2").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(editors[2].Find("#maxCount2").GetAttribute("value"), Is.EqualTo(string.Empty));
            Assert.That(editors[2].Find("#maxCount2").GetAttribute("placeholder"), Is.EqualTo("Any"));
            Assert.That(editors[2].FindAll("#maxAny2"), Is.Empty);
            Assert.That(otherEditor.Find("#comboCategory3").GetAttribute("value"), Is.EqualTo(a.Identity));
            Assert.That(otherEditor.Find("#minCount3").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(otherEditor.Find("#maxCount3").GetAttribute("value"), Is.EqualTo("2"));
        });

        await cut.Find("[aria-label='Move combo Middle copy, row 3 down']").ClickAsync(new());
        editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors.Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new[] { "Before", "Middle", "After", "Middle copy" }));
        Assert.That(editors[3].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(otherEditor.Find("#comboCategory2").GetAttribute("value"), Is.EqualTo(a.Identity));
    }

    [Test]
    public async Task DuplicateComboDoesNotCopyUncommittedDirectCardDraft()
    {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([new(a, 1, 2)], "Card draft source"));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> sourceEditor = cut.FindComponent<ComboEditor>();
        string cardId = session.Cards[1].Id;

        await sourceEditor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await sourceEditor.Find("#comboCard0").ChangeAsync(new() { Value = cardId });
        await sourceEditor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await sourceEditor.Find("#maxCount0").InputAsync(new() { Value = "2" });
        await DuplicateComboAsync(cut, 0);

        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[0].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.Multiple(() =>
        {
            Assert.That(sourceEditor.Instance.Combo.Cards, Is.Empty);
            Assert.That(sourceEditor.Find("#constraintKind0").GetAttribute("value"), Is.EqualTo("Card"));
            Assert.That(sourceEditor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo(cardId));
            Assert.That(sourceEditor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(sourceEditor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(editors[1].Instance.Combo.Cards, Is.Empty);
            Assert.That(editors[1].Find("#constraintKind1").GetAttribute("value"), Is.EqualTo("Category"));
            Assert.That(editors[1].Find("#comboCategory1").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(editors[1].Find("#minCount1").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(editors[1].Find("#maxCount1").GetAttribute("value"), Is.EqualTo(string.Empty));
            Assert.That(editors[1].Find("#maxCount1").GetAttribute("placeholder"), Is.EqualTo("Any"));
            Assert.That(editors[1].FindAll("#maxAny1"), Is.Empty);
        });
    }

    [Test]
    public async Task DuplicateComboSurvivesSessionSaveLoadAndRemainsIndependentlyEditable()
    {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        List<Card> cards = [new([a, monster], 2, "First card", id: "persisted-card-id")];
        Combo source = new([new(a, 1, 2), new(monster, 0, 1)],
            "Starter",
            active: false,
            groupId: "persisted-group",
            cards: [new(cards[0].Id, 1, 2)]);
        SessionState session = new()
        {
            Categories = [a, b],
            Cards = cards,
            Combos = [new([new(b, 0, 1)], "Before"), source],
            ComboGroups = [new("persisted-group", "Tier 1")],
            HandSize = 2
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);

        await DuplicateComboAsync(cut, 1);
        await Button(cut, "Save Session").ClickAsync(new());
        JSRuntimeInvocation invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement[] combos = [.. document.RootElement.GetProperty("Combos").EnumerateArray()];
            Assert.That(combos.Select(combo => combo.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "Before", "Starter", "Starter copy" }));
            Assert.That(combos[2].GetProperty("Active").GetBoolean(), Is.False);
            Assert.That(combos[2].GetProperty("GroupId").GetString(), Is.EqualTo("persisted-group"));
            Assert.That(combos[2].GetProperty("Categories").EnumerateArray().Select(category =>
                    (category.GetProperty("BaseCategory").GetProperty("Name").GetString(),
                        category.GetProperty("MinCount").GetInt32(), category.GetProperty("MaxCount").GetInt32())),
                Is.EqualTo(new[] { ("A", 1, 2), ("Monster", 0, 1) }));
            Assert.That(combos[2].GetProperty("Cards").EnumerateArray().Select(card =>
                    (card.GetProperty("CardId").GetString(), card.GetProperty("MinCount").GetInt32(),
                        card.GetProperty("MaxCount").GetInt32())),
                Is.EqualTo(new[] { ("persisted-card-id", 1, 2) }));
        }

        cut.FindComponents<InputFile>()[1]
            .UploadFiles(InputFileContent.CreateFromText(json, "duplicated-session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(3)));
        IReadOnlyList<IRenderedComponent<ComboEditor>> loaded = cut.FindComponents<ComboEditor>();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Select(editor => editor.Instance.Combo.Name),
                Is.EqualTo(new[] { "Before", "Starter", "Starter copy" }));
            Assert.That(loaded[2].Instance.Combo.Categories.Select(category => category.BaseCategory.Identity),
                Is.EqualTo(new[] { a.Identity, monster.Identity }));
            Assert.That(loaded[2].Instance.Combo.Cards.Single().CardId, Is.EqualTo(cards[0].Id));
            Assert.That(loaded[2].Instance.Combo.GroupId, Is.EqualTo("persisted-group"));
            Assert.That(loaded[2].Instance.Combo.Active, Is.False);
        });

        await loaded[2].Find("#comboName2").InputAsync(new() { Value = "Revised after load" });
        Assert.That(loaded[1].Instance.Combo.Name, Is.EqualTo("Starter"));
        Assert.That(loaded[2].Instance.Combo.Name, Is.EqualTo("Revised after load"));
    }

    [Test]
    public async Task DuplicateComboInvalidatesResultsWithoutRecalculatingAndDiscardsInFlightCompletion()
    {
        SequencedProbabilityCalculator calculator = new(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Stale completion", 0.8)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(calculator.CallCount, Is.EqualTo(1));

        Task calculation = Button(cut, "Calculate").ClickAsync(new());

        try
        {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DuplicateComboAsync(cut, 0);
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
            Assert.That(calculator.CallCount, Is.EqualTo(2), "duplication must not trigger another calculation");
        }
        finally
        {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        Assert.That(cut.Markup, Does.Not.Contain("Stale completion"));
        Assert.That(calculator.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task CalculationAfterDuplicationIncludesBothActiveCombos()
    {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await DuplicateComboAsync(cut, 0);

        List<Card> cards =
            [.. cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).Where(card => card.Active)];
        List<Combo> combos =
            [.. cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo).Where(combo => combo.Active)];
        double expected = SmallDeckOracle.EnumerateProbability(cards, combos, 2);

        await Button(cut, "Calculate").ClickAsync(new());

        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
        Assert.That(cut.FindAll(".combo-probability-item .combo-probability-name")
                .Select(name => name.TextContent.Trim()),
            Is.EqualTo(new[] { "First combo", "First combo copy", "Second combo" }));
    }
}
