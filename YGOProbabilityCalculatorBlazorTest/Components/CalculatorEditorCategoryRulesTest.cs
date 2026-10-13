using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorCategoryRulesTest : CalculatorEditorTestBase {
    private static readonly CategoryBase Monster = new("Monster", CategorySource.Metadata, "kind:monster");
    private static readonly CategoryBase Vs = new("Archetype: Vanquish Soul", CategorySource.Metadata, "archetype:vanquish%20soul");
    private static readonly CategoryBase Spell = new("Spell", CategorySource.Metadata, "kind:spell");
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
}
