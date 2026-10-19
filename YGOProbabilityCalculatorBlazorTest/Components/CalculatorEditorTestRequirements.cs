using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
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
public sealed class CalculatorEditorTestRequirements : CalculatorEditorTestBase {

    [TestCase(false)]
    [TestCase(true)]
    public async Task NewRequirementsDefaultToAnyAndRemainDynamicAfterHandSizeChange(bool directCard) {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() {
            Value = directCard ? CardTarget(session.Cards[0].Id) : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        AssertAnyMaximumDraft(editor);
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Find(".accordion-button").TextContent, Does.Contain("(1 Min)"));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain("(1 Min)"));
        Combo combo = editor.Instance.Combo;
        Assert.That(directCard ? combo.Cards[0].MaximumMode : combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(directCard ? combo.Cards[0].MaxCount : combo.Categories[0].MaxCount, Is.EqualTo(6));
        Assert.That(directCard ? combo.Cards[0].GetEffectiveMaximum(6) : combo.Categories[0].GetEffectiveMaximum(6), Is.EqualTo(6));
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() {
            Value = directCard ? CardTarget(session.Cards[0].Id) : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Update requirement").ClickAsync(new());
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain("(2 Min)"));
    }

    [TestCase(false, 0, 5, RequirementMaximumMode.HandSize, "Any")]
    [TestCase(false, 1, 5, RequirementMaximumMode.HandSize, "1 Min")]
    [TestCase(false, 2, 5, RequirementMaximumMode.HandSize, "2 Min")]
    [TestCase(false, 0, 0, RequirementMaximumMode.Fixed, "None")]
    [TestCase(false, 1, 1, RequirementMaximumMode.Fixed, "1")]
    [TestCase(false, 0, 1, RequirementMaximumMode.Fixed, "1 Max")]
    [TestCase(false, 1, 3, RequirementMaximumMode.Fixed, "1–3")]
    [TestCase(true, 0, 5, RequirementMaximumMode.HandSize, "Any")]
    [TestCase(true, 1, 5, RequirementMaximumMode.HandSize, "1 Min")]
    [TestCase(true, 2, 5, RequirementMaximumMode.HandSize, "2 Min")]
    [TestCase(true, 0, 0, RequirementMaximumMode.Fixed, "None")]
    [TestCase(true, 1, 1, RequirementMaximumMode.Fixed, "1")]
    [TestCase(true, 0, 1, RequirementMaximumMode.Fixed, "1 Max")]
    [TestCase(true, 1, 3, RequirementMaximumMode.Fixed, "1–3")]
    public async Task RequirementBadgesSummarizeStoredRangeSemantics(
        bool directCard,
        int minimum,
        int maximum,
        RequirementMaximumMode mode,
        string expectedLabel
    ) {
        Card card = new([a], 2, "Twin");
        Combo combo = directCard
            ? new Combo([], "Display", cards: [new(card.Id, minimum, maximum, mode)])
            : new Combo([new(a, minimum, maximum, mode)], "Display");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Categories = [a], Cards = [card], Combos = [combo], HandSize = 5 });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string expectedBadgeText = directCard ? $"Card: Twin ({expectedLabel})" : $"A ({expectedLabel})";
        IElement accordionButton = editor.Find(".accordion-button");

        if (accordionButton.GetAttribute("aria-expanded") != "true") {
            await accordionButton.ClickAsync(new());
        }
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));

        await accordionButton.ClickAsync(new());
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(editor.Find(".combo-header-content .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));

        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(editor.Find(".combo-header-content .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));
        await accordionButton.ClickAsync(new());
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));
    }

    [TestCase(false, 1, 5, "1–5")]
    [TestCase(true, 1, 5, "1–5")]
    [TestCase(false, 0, 0, "None")]
    [TestCase(true, 0, 0, "None")]
    public async Task FixedRequirementsAndUncommittedDraftsSurviveHandSizeChange(
        bool directCard,
        int minimum,
        int maximum,
        string expectedLabel
    ) {
        SessionState session = Session();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = [new([])],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string selector = "#comboTarget0";
        string value = directCard ? CardTarget(session.Cards[0].Id) : a.Identity;
        await RequirementField(editor, selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = minimum.ToString() });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = maximum.ToString() });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        await Button(editor, "Add").ClickAsync(new());
        await cut.Find("#handSize").ChangeAsync(new() { Value = "5" });
        await RequirementField(editor, selector).ChangeAsync(new() { Value = value });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain($"({expectedLabel})"));
        Combo combo = editor.Instance.Combo;
        Assert.That(directCard ? combo.Cards[0].MaximumMode : combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(directCard ? combo.Cards[0].GetEffectiveMaximum(6) : combo.Categories[0].GetEffectiveMaximum(6), Is.EqualTo(maximum));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MaximumDraftAloneSwitchesBetweenFixedAndDynamicModes(bool directCard) {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string selector = "#comboTarget0";
        string value = directCard ? CardTarget(session.Cards[0].Id) : a.Identity;
        await RequirementField(editor, selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);

        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaxCount : editor.Instance.Combo.Categories[0].MaxCount,
            Is.EqualTo(2));

        await RequirementField(editor, selector).ChangeAsync(new() { Value = value });
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("placeholder"), Is.EqualTo("Any"));
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = string.Empty });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "7" });
        await Button(editor, "Update requirement").ClickAsync(new());

        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MinCount : editor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(7));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].GetEffectiveMaximum(6) : editor.Instance.Combo.Categories[0].GetEffectiveMaximum(6),
            Is.EqualTo(6));
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent, Does.Contain("(7 Min)"));
        await RequirementField(editor, selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        Assert.That(editor.FindAll("[role='alert']"), Is.Empty);
    }

    [TestCase("2", "", RequirementMaximumMode.HandSize)]
    [TestCase("2", "3", RequirementMaximumMode.Fixed)]
    [TestCase("0", "0", RequirementMaximumMode.Fixed)]
    public async Task CategorySelectionPreservesBoundsEnteredBeforeTheSelection(
        string minimum,
        string maximum,
        RequirementMaximumMode expectedMode
    ) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([])],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = minimum });

        if (maximum.Length > 0) {
            await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = maximum });
        }

        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo(minimum));
        Assert.That(Button(editor, "Add").TextContent.Trim(), Is.EqualTo("Add"));

        if (expectedMode == RequirementMaximumMode.HandSize) {
            AssertAnyMaximumDraft(editor);
        }
        else {
            Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo(maximum));
        }

        await Button(editor, "Add").ClickAsync(new());
        ComboCategory saved = editor.Instance.Combo.Categories.Single();
        Assert.That(saved.MinCount, Is.EqualTo(int.Parse(minimum)));
        Assert.That(saved.MaximumMode, Is.EqualTo(expectedMode));

        if (expectedMode == RequirementMaximumMode.Fixed) {
            Assert.That(saved.MaxCount, Is.EqualTo(int.Parse(maximum)));
        }
    }

    [Test]
    public async Task UnsavedCategoryChangesAndClearingKeepTheVisibleDraftBounds() {
        CategoryBase c = new("C");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, c],
            Combos = [new([])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "4" });

        foreach (string categoryIdentity in new[] { a.Identity, b.Identity, c.Identity, string.Empty }) {
            await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = categoryIdentity });
            Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
            Assert.That(Button(editor, "Add").TextContent.Trim(), Is.EqualTo("Add"));
        }
    }

    [Test]
    public async Task ExistingCategoryLoadsSavedBoundsAndNewCategoryKeepsOnlyTheVisibleValues() {
        CategoryBase c = new("C");
        ComboCategory savedAny = new(a, 3, 0, RequirementMaximumMode.HandSize);
        ComboCategory savedZero = new(b, 0, 0);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, c],
            Combos = [new([savedAny, savedZero])],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "7" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "8" });

        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        AssertAnyMaximumDraft(editor);
        Assert.That(Button(editor, "Update requirement").TextContent.Trim(), Is.EqualTo("Update requirement"));

        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        AssertAnyMaximumDraft(editor);
        Assert.That(Button(editor, "Add").TextContent.Trim(), Is.EqualTo("Add"));

        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(Button(editor, "Update requirement").TextContent.Trim(), Is.EqualTo("Update requirement"));

        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(Button(editor, "Add").TextContent.Trim(), Is.EqualTo("Add"));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(savedAny));
        Assert.That(editor.Instance.Combo.Categories[1], Is.SameAs(savedZero));
    }

    [Test]
    public async Task DirectCardAndRequirementKindChangesKeepUnsavedDraftBounds() {
        Card first = new([a], 2, "First", id: "first");
        Card second = new([b], 2, "Second", id: "second");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [first, second],
            Combos = [new([])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "4" });

        foreach (string cardId in new[] { first.Id, second.Id, string.Empty }) {
            await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = CardTarget(cardId) });
            Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
        }
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidNonEmptyMaximumDoesNotBecomeAny(bool directCard) {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() {
            Value = directCard ? CardTarget(session.Cards[0].Id) : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "invalid" });
        await Button(editor, "Add").ClickAsync(new());

        Assert.That(editor.Find("[role='alert']").TextContent, Does.Contain("Maximum count"));
        Assert.That(editor.Instance.Combo.Categories, Is.Empty);
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
    }

    [Test]
    public void CardOnlyComboCanBeEditedAndMissingReferenceBlocksCalculationUntilRemoved() {
        Card first = new([], 2, "Twin");
        Card second = new([], 2, "Twin");
        Combo combo = new([], "Direct", cards: [new(first.Id, 0, 0)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [first, second], Combos = [combo], HandSize = 2 });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        editor.Find(".accordion-button").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(editor.Find(".combo-card-tag").TextContent, Does.Contain("Card: Twin (None)"));
        OpenNewRequirement(editor);
        Assert.That(editor.FindAll("#comboTarget0 option").Select(option => option.TextContent.Trim()),
            Does.Contain("Twin #2"));
        RequirementField(editor, "#comboTarget0").Change(CardTarget(first.Id));
        Assert.That(Button(editor, "Update requirement").TextContent.Trim(), Is.EqualTo("Update requirement"));
        RequirementField(editor, "#minCount0").Input("1");
        RequirementField(editor, "#maxCount0").Input("1");
        Button(editor, "Update requirement").Click();
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1)"));

        cut.FindComponent<CardEditor>().Find(".accordion-button").Click();
        cut.FindComponent<CardEditor>().Find("#cardName0").Input("Renamed");
        cut.FindComponent<CardEditor>().Find("#cardName0").Blur();
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Renamed (1)"));
        FirstRowAction(cut.FindComponent<CardEditor>(), "card", "Remove").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference"));
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-missing-card").TextContent,
            Does.Contain("Missing card"));
        editor.Find("[data-requirement='card:0']").Click();
        Button(editor, "Remove requirement").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        editor = cut.FindComponent<ComboEditor>();
        RequirementField(editor, "#comboTarget0").Change(CardTarget(second.Id));
        RequirementField(editor, "#maxCount0").Input("1");
        Button(editor, "Add").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1)"));
    }

    [Test]
    public async Task NewCardAndCategoryRequirementsCannotShareOneDrawnCard() {
        CategoryBase role = new("Role");
        Card card = new([role], 1, "Piece");
        Card other = new([], 1, "Other");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [role], Cards = [card, other], Combos = [new([], "Mixed")], HandSize = 1
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = "user:Role" });
        await Button(editor, "Add").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = CardTarget(card.Id) });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Combo combo = new([new(role, 1, 1)], cards: [new(card.Id, 1, 1)]);
        double expected = SmallDeckOracle.EnumerateProbability([card, other], [combo], 1);
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(expected.ToString("P2")));
        Assert.That(editor.FindAll(".accordion-body .category-tag"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TooManyActiveCombosExplainTheLimitAndInactiveCombosDoNotCount() {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.AddRange(Enumerable.Range(0, 31)
            .Select(_ => new Combo(new[] { new ComboCategory(a, 1, 2) })));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("at most 30 active combos"));
        await SetActiveAsync(cut, "#comboActive30", false);
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        for (int index = 1; index < 30; index++) {
            await SetActiveAsync(cut, $"#comboActive{index}", false);
        }

        await Button(cut, "Calculate").ClickAsync(new());
        double expected = SmallDeckOracle.EnumerateProbability(session.Cards, [session.Combos[0]], session.HandSize);
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
    }

    [Test]
    public void CardValidationRetainsPositiveCopyCountsAndReportsSelectionErrors() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>()[0];
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        foreach (string value in new[] { "0", "-1", "", "nonsense" }) {
            card.Find("input[type=number]").Input(value);
            Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("at least 1"));
            Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("(2)"));
        }
        card.Find("select[id^='cardCategory']").Change("user:A");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("already added"));
        card.Find("select[id^='cardCategory']").Change("missing");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("not found"));
        card.Find("select[id^='cardCategory']").Change("user:B");
        card.Find("input[type=number]").Input("6");
        card.Find("#cardName0").Input("Renamed");
        Assert.That(card.Find("select[id^='cardCategory']").GetAttribute("value"), Is.EqualTo("user:B"));
        Button(card, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Renamed").And.Contain("(6)"));
        Assert.That(card.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ZeroMaximumSurvivesRenameParentRerenderAndHandSizeChangeAndCanBeUpdated() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>()[0];
        RequirementField(combo, "select[id^='comboTarget']").Change("user:A");
        Assert.That(RequirementField(combo, "#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        RequirementField(combo, "#minCount0").Input("0");
        RequirementField(combo, "#maxCount0").Input("0");
        combo.Find("#comboName0").Input("Zero A");
        cut.Find("#handSize").Change("3");
        combo.Find(".accordion-button").Click();
        Assert.That(RequirementField(combo, "#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Button(combo, "Update requirement").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (None)"));
        RequirementField(combo, "select[id^='comboTarget']").Change("user:A");
        Assert.That(RequirementField(combo, "#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        RequirementField(combo, "#maxCount0").Input("2");
        Button(combo, "Update requirement").Click();
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (2 Max)"));
    }

    [Test]
    public void ComboRejectsInvalidFixedRangesWithoutClampingAndDefaultsNewDraftsToAny() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>()[0];
        OpenNewRequirement(combo);
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        RequirementField(combo, "select[id^='comboTarget']").Change("user:B");
        RequirementField(combo, "#minCount0").Input("-1");
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Minimum"));
        RequirementField(combo, "#minCount0").Input("2");
        RequirementField(combo, "#maxCount0").Input("1");
        cut.Find("#handSize").Change("3");
        Assert.That(RequirementField(combo, "#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(combo.FindAll("#maxAny0"), Is.Empty);
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Maximum"));
        RequirementField(combo, "#maxCount0").Input("");
        Button(combo, "Add").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("B (2 Min)"));
        RequirementField(combo, "select[id^='comboTarget']").Change("");
        cut.Find("#handSize").Change("4");
        AssertAnyMaximumDraft(combo);
        RequirementField(combo, "select[id^='comboTarget']").Change("user:B");
        Button(combo, "Update requirement").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void RemovingConstraintPreservesOtherConstraintsFromLoadedSession() {
        // Session JSON can contain repeated category constraints. Removing B must
        // not silently discard either A constraint (together they require exactly one A).
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 1, 2), new(a, 0, 1), new(b, 0, 2)]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> combo = cut.FindComponent<ComboEditor>();
        combo.Find("[data-requirement='category:2']").Click();
        Button(combo, "Remove requirement").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (1–2)").And.Contain("A (1 Max)"));
        combo.Find("[data-requirement='category:0']").Click();
        Button(combo, "Remove requirement").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (1 Max)"));
    }

}
