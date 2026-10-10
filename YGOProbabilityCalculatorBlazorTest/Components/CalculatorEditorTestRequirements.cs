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
        if (directCard) {
            await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        }
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
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
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Update").ClickAsync(new());
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
        if (directCard) {
            await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        }
        string selector = directCard ? "#comboCard0" : "#comboCategory0";
        string value = directCard ? session.Cards[0].Id : a.Identity;
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#minCount0").InputAsync(new() { Value = minimum.ToString() });
        await editor.Find("#maxCount0").InputAsync(new() { Value = maximum.ToString() });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        await Button(editor, "Add").ClickAsync(new());
        await cut.Find("#handSize").ChangeAsync(new() { Value = "5" });
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
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
        if (directCard) {
            await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        }
        string selector = directCard ? "#comboCard0" : "#comboCategory0";
        string value = directCard ? session.Cards[0].Id : a.Identity;
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);

        await editor.Find("#maxCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaxCount : editor.Instance.Combo.Categories[0].MaxCount,
            Is.EqualTo(2));

        await editor.Find(selector).ChangeAsync(new() { Value = value });
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("placeholder"), Is.EqualTo("Any"));
        await editor.Find("#maxCount0").InputAsync(new() { Value = string.Empty });
        await editor.Find("#minCount0").InputAsync(new() { Value = "7" });
        await Button(editor, "Update").ClickAsync(new());

        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MinCount : editor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(7));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].GetEffectiveMaximum(6) : editor.Instance.Combo.Categories[0].GetEffectiveMaximum(6),
            Is.EqualTo(6));
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent, Does.Contain("(7 Min)"));
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        Assert.That(editor.FindAll("[role='alert']"), Is.Empty);
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
        if (directCard) {
            await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        }
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#maxCount0").InputAsync(new() { Value = "invalid" });
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

        editor.Find("#constraintKind0").Change("Card");
        Assert.That(editor.FindAll("#comboCard0 option").Select(option => option.TextContent.Trim()),
            Does.Contain("Twin #2"));
        editor.Find("#comboCard0").Change(first.Id);
        Assert.That(Button(editor, "Update").TextContent.Trim(), Is.EqualTo("Update"));
        editor.Find("#minCount0").Input("1");
        editor.Find("#maxCount0").Input("1");
        Button(editor, "Update").Click();
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1)"));

        cut.FindComponent<CardEditor>().Find(".accordion-button").Click();
        cut.FindComponent<CardEditor>().Find("#cardName0").Input("Renamed");
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Renamed (1)"));
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
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = "user:Role" });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = card.Id });
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
        await cut.Find("#comboActive30").ChangeAsync(new() { Value = false });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        for (int index = 1; index < 30; index++) {
            await cut.Find($"#comboActive{index}").ChangeAsync(new() { Value = false });
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
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (None)"));
        combo.Find("select").Change("user:A");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        combo.Find("#maxCount0").Input("2");
        Button(combo, "Update").Click();
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (2 Max)"));
    }

    [Test]
    public void ComboRejectsInvalidFixedRangesWithoutClampingAndDefaultsNewDraftsToAny() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>()[0];
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
        Assert.That(combo.FindAll("#maxAny0"), Is.Empty);
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Maximum"));
        combo.Find("#maxCount0").Input("");
        Button(combo, "Add").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("B (2 Min)"));
        combo.Find("select").Change("");
        cut.Find("#handSize").Change("4");
        AssertAnyMaximumDraft(combo);
        combo.Find("select").Change("user:B");
        Button(combo, "Update").Click();
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
        combo.FindAll(".accordion-body .badge button").ToArray()[2].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (1–2)").And.Contain("A (1 Max)"));
        combo.FindAll(".accordion-body .badge button").ToArray()[0].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (1 Max)"));
    }

}
