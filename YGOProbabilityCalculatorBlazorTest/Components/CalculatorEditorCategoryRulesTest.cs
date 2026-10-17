using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorCategoryRulesTest : CalculatorEditorTestBase {
    private static readonly CategoryBase Monster = new("Monster", CategorySource.Metadata, "kind:monster");
    private static readonly CategoryBase Vs = new("Archetype: Vanquish Soul", CategorySource.Metadata, "archetype:vanquish%20soul");
    private static readonly CategoryBase Spell = new("Spell", CategorySource.Metadata, "kind:spell");
    private static readonly CategoryBase Trap = new("Trap", CategorySource.Metadata, "kind:trap");
    private static readonly CategoryBase Role = new("VS Monster");

    private static SessionState RuleSession() => new() {
        Categories = [Role], Cards = [new([Vs, Monster], 3, "Razen"), new([Role, Spell], 1, "ROTA"), new([Vs, Spell], 1, "VS Spell")],
        Combos = [new([new(Role, 1, 5)], "VS")], HandSize = 1
    };

    private static void OpenRule(IRenderedComponent<ProbabilityCalculatorComponent> cut, string name = "VS Monster") {
        cut.Find($"[aria-label='Edit category {name}']").Click();
        IRenderedComponent<CategoryListEditor> categories = cut.FindComponent<CategoryListEditor>();
        categories.FindAll("button").Single(button => button.TextContent.Trim() is "Define by properties" or "Edit rule").Click();
    }

    private static void DefineVs(IRenderedComponent<ProbabilityCalculatorComponent> cut) {
        OpenRule(cut);
        cut.Find("[aria-label='Add rule alternative']").Change(Vs.Identity);
        cut.Find("[aria-label='Add AND property to alternative 1']").Change(Monster.Identity);
    }

    [Test]
    public async Task AuthorRulePreviewsAndPreservesManualExceptionAcrossRemovalAndCalculation() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        DefineVs(cut);
        Assert.That(cut.FindComponent<CategoryRuleEditor>().Markup, Does.Contain("1 matching entry (3 copies)"));
        Assert.That(cut.FindComponent<CategoryRuleEditor>().FindAll("p"), Is.Empty);
        Button(cut, "Apply rule").Click();
        Assert.That(cut.FindComponents<CategoryRuleEditor>(), Is.Empty);
        IRenderedComponent<CardEditor>[] cards = cut.FindComponents<CardEditor>().ToArray();
        Assert.That(cards[0].Markup, Does.Contain("Automatic"));
        Assert.That(cards[0].Find(".accordion-button").TextContent, Does.Not.Contain("VS Monster"));
        Assert.That(cards[1].Find(".accordion-button").TextContent, Does.Contain("VS Monster"));
        Assert.That(cards[0].FindAll("[aria-label='Remove category VS Monster from card Razen']"), Is.Empty);
        Assert.That(cards[1].Markup, Does.Contain("Manual"));
        Assert.That(cards[0].Instance.Card.Categories, Does.Not.Contain(Role));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Does.Contain("80"));
        OpenRule(cut);
        Button(cut, "Remove rule").Click();
        Assert.That(cards[0].Instance.Card.Categories, Does.Not.Contain(Role));
        Assert.That(cards[1].Instance.Card.Categories, Does.Contain(Role));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Does.Contain("20"));
    }

    [Test]
    public void CancelEscapeAndRuleEditDoNotMutateExplicitMemberships() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        DefineVs(cut);
        cut.Find(".category-rule-editor").KeyDown("Escape");
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases[0].Rule, Is.Null);
        DefineVs(cut);
        Button(cut, "Apply rule").Click();
        OpenRule(cut);
        cut.Find("[aria-label='Add rule alternative']").Change(Spell.Identity);
        Assert.That(cut.FindComponent<CategoryRuleEditor>().Markup, Does.Contain("3 matching entries (5 copies)"));
        Button(cut, "Cancel").Click();
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases[0].Rule!.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        OpenRule(cut);
        cut.Find("[aria-label='Add rule alternative']").Change(Spell.Identity);
        Button(cut, "Apply rule").Click();
        Assert.That(cut.FindComponents<CardEditor>().Last().Markup, Does.Contain("Automatic"));
        Assert.That(cut.FindComponents<CardEditor>().Last().Instance.Card.Categories, Does.Not.Contain(Role));
    }

    [Test]
    public void RuleFlowPreservesUnsavedRenameDraft() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        cut.Find("[aria-label='Edit category VS Monster']").Click();
        cut.Find("[aria-label='New name for category VS Monster']").Input("Draft role");
        Button(cut, "Define by properties").Click();
        cut.Find("[aria-label='Add rule alternative']").Change(Monster.Identity);
        Button(cut, "Apply rule").Click();
        Assert.That(cut.Find("[aria-label='New name for category VS Monster']").GetAttribute("value"), Is.EqualTo("Draft role"));
        cut.Find("[aria-label='Save category name']").Click();
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Single().Name, Is.EqualTo("Draft role"));
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Single().Rule, Is.Not.Null);
    }

    [Test]
    public void ManualAssignmentAndRemovalLeaveAutomaticSectionIntact() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        DefineVs(cut);
        Button(cut, "Apply rule").Click();
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>().First();
        Assert.That(card.Markup, Does.Not.Contain("Keep manually"));
        card.Find("#cardCategory0").Change(Role.Identity);
        Button(card, "Add").Click();
        Assert.That(card.Find(".detected-card-properties").TextContent, Does.Contain("VS Monster"));
        Assert.That(card.Find(".manual-card-properties").TextContent, Does.Contain("VS Monster"));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("VS Monster"));
        cut.Find("[aria-label='Remove category VS Monster from card Razen']").Click();
        Assert.That(card.Instance.Card.Categories, Does.Not.Contain(Role));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Not.Contain("VS Monster"));
        Assert.That(card.Markup, Does.Contain("Automatic"));
        Assert.That(card.FindAll("[aria-label='Remove category VS Monster from card Razen']"), Is.Empty);
    }

    [Test]
    public void RenamePreservesRuleAndReferencesAndAutomaticOnlyCategoryCanBeDeleted() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        DefineVs(cut);
        Button(cut, "Apply rule").Click();
        cut.Find("[aria-label='Edit category VS Monster']").Click();
        cut.Find("[aria-label='New name for category VS Monster']").Input("VS role");
        cut.Find("[aria-label='Save category name']").Click();
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases[0].Rule, Is.Not.Null);
        Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Categories[0].BaseCategory.Name, Is.EqualTo("VS role"));
        Assert.That(cut.FindComponents<CardEditor>().First().Markup, Does.Contain("VS role"));
        cut.Find("[aria-label='Remove category VS role']").Click();
        Assert.That(cut.Markup, Does.Contain("This category is still used by a card or combo."));

        SessionState unused = new() { Categories = [new("Unused", rule: CardSelector.For(Monster))], Cards = [new([Monster])], HandSize = 1 };
        cut.Dispose();
        cut = Render(unused);
        Assert.That(cut.FindComponent<CardEditor>().Markup, Does.Contain("Unused"));
        cut.Find("[aria-label='Remove category Unused']").Click();
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases, Is.Empty);
        Assert.That(cut.FindComponent<CardEditor>().Markup, Does.Not.Contain("Unused"));
    }

    [Test]
    public async Task MetadataReplacementAndManualOverridesRefreshAutomaticMembership() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(RuleSession());
        DefineVs(cut);
        Button(cut, "Apply rule").Click();
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>().First();
        await cut.InvokeAsync(() => card.Instance.OnReplaceCard.InvokeAsync((0, card.Instance.Card.WithObjectiveMetadata([Vs], 1))));
        Assert.That(card.FindAll(".accordion-button .category-tag"), Is.Empty);
        await cut.InvokeAsync(() => card.Instance.OnReplaceCard.InvokeAsync((0, card.Instance.Card.WithManualMetadataCategory(Monster))));
        Assert.That(card.Markup, Does.Contain("Automatic"));
        await cut.InvokeAsync(() => card.Instance.OnReplaceCard.InvokeAsync((0, card.Instance.Card.WithoutManualMetadataCategory(Monster.MetadataKey!))));
        Assert.That(card.FindAll(".accordion-button .category-tag"), Is.Empty);
    }

    [Test]
    public void ExpandingCategoryShowsSavedAndOrRuleEvenWhenNothingMatches() {
        CardSelector rule = CardSelector.AnyOfSelectors([
            CardSelector.AllOf([Vs.Identity, Trap.Identity]),
            CardSelector.AllOf([Spell.Identity, Trap.Identity])
        ]);
        CategoryBase role = new("Property role", rule: rule);
        SessionState session = new() {
            Categories = [role],
            Cards = [new([Vs, Monster], 3, "Razen"), new([Spell], 1, "ROTA"), new([Vs, Spell], 1, "VS Spell")],
            HandSize = 1
        };

        Assert.That(session.Cards.All(card => !rule.Matches(card)), Is.True);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CategoryListEditor> categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(categories.FindAll(".category-rule-summary"), Is.Empty);

        cut.Find("[aria-label='Edit category Property role']").Click();

        IRenderedComponent<CategoryRuleSummary> summary = categories.FindComponent<CategoryRuleSummary>();
        Assert.That(summary.Markup, Does.Contain("Archetype: Vanquish Soul"));
        Assert.That(summary.Markup, Does.Contain("AND"));
        Assert.That(summary.Markup, Does.Contain("OR"));
        Assert.That(summary.Markup, Does.Contain("("));
        Assert.That(summary.Markup, Does.Contain("Trap"));
        Assert.That(categories.FindAll(".category-rule-editor"), Is.Empty);
        Assert.That(categories.Instance.CategoryBases.Single().Rule, Is.EqualTo(rule));
        Assert.That(cut.FindComponents<CardEditor>().SelectMany(card => card.Instance.Card.Categories), Does.Not.Contain(role));
    }

    [Test]
    public void SavedRuleSummaryStaysSeparateFromDraftAndRuleActionsPreserveRenameDraft() {
        CardSelector savedRule = CardSelector.AnyOfSelectors([
            CardSelector.AllOf([Vs.Identity, Monster.Identity]),
            CardSelector.For(Spell)
        ]);
        CategoryBase role = new("Property role", rule: savedRule);
        SessionState session = new() {
            Categories = [role],
            Cards = [new([Vs, Monster], 3, "Razen"), new([Spell], 1, "ROTA"), new([Trap], 1, "Trap")],
            HandSize = 1
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CategoryListEditor> categories = cut.FindComponent<CategoryListEditor>();
        cut.Find("[aria-label='Edit category Property role']").Click();
        cut.Find("[aria-label='New name for category Property role']").Input("Draft role");
        cut.Find("[aria-label='Choose color for category Property role']").Click();
        cut.Find("[aria-label='Set Property role color to Green']").Click();
        string colorClass = categories.Find(".category-chip").ClassName;

        Button(cut, "Edit rule").Click();
        IRenderedComponent<CategoryRuleSummary> summary = categories.FindComponent<CategoryRuleSummary>();
        string savedExpression = summary.Markup;
        IRenderedComponent<CategoryRuleEditor> editor = categories.FindComponent<CategoryRuleEditor>();
        editor.Find("[aria-label='Add rule alternative']").Change(Trap.Identity);

        Assert.That(editor.FindAll(".category-rule-term").Select(term => term.TextContent), Has.Some.Contains("Trap"));
        Assert.That(summary.Markup, Is.EqualTo(savedExpression));
        Assert.That(summary.Markup, Does.Not.Contain("Trap"));
        Assert.That(categories.Instance.CategoryBases.Single().Rule, Is.EqualTo(savedRule));

        Button(cut, "Cancel").Click();
        Assert.That(categories.Instance.CategoryBases.Single().Rule, Is.EqualTo(savedRule));
        Assert.That(cut.Find("[aria-label='New name for category Property role']").GetAttribute("value"), Is.EqualTo("Draft role"));
        Assert.That(categories.Find(".category-chip").ClassName, Is.EqualTo(colorClass));

        Button(cut, "Edit rule").Click();
        categories.FindComponent<CategoryRuleEditor>().Find("[aria-label='Add rule alternative']").Change(Trap.Identity);
        Button(cut, "Apply rule").Click();

        Assert.That(categories.Instance.CategoryBases.Single().Rule!.CategoryIdentities, Does.Contain(Trap.Identity));
        Assert.That(cut.Find("[aria-label='New name for category Property role']").GetAttribute("value"), Is.EqualTo("Draft role"));
        Assert.That(categories.FindComponent<CategoryRuleSummary>().Markup, Does.Contain("Trap"));
        Assert.That(categories.Find(".category-chip").ClassName, Is.EqualTo(colorClass));

        Button(cut, "Edit rule").Click();
        Button(cut, "Remove rule").Click();

        Assert.That(categories.Instance.CategoryBases.Single().Rule, Is.Null);
        Assert.That(categories.FindAll(".category-rule-summary"), Is.Empty);
        Assert.That(cut.Find("[aria-label='New name for category Property role']").GetAttribute("value"), Is.EqualTo("Draft role"));
        Assert.That(categories.FindAll("button").Select(button => button.TextContent.Trim()), Does.Contain("Define by properties"));
    }
}
