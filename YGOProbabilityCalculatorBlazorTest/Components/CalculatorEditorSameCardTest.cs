using Bunit;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorSameCardTest : CalculatorEditorTestBase {
    [Test]
    public async Task AndRowInheritsBoundsExcludesExistingConjunctAndRestoresDraftOnCancelOrCommit() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([new(a, 1, 2)])],
            HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "4" });
        Combo before = editor.Instance.Combo;

        await ExtendRequirement(editor, "category:0", sameCard: true);
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.Empty);
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.FindAll("#comboTarget0 option").ToArray().Any(option => option.GetAttribute("value") == a.Identity), Is.False);
        Assert.That(editor.FindAll("#comboTarget0 option").ToArray().Any(option => option.GetAttribute("value") == b.Identity), Is.True);
        Assert.That(editor.Instance.Combo, Is.SameAs(before));

        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("4"));

        await ExtendRequirement(editor, "category:0", sameCard: true);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "" });
        await Button(editor, "Add property").ClickAsync(new());

        ComboCategory requirement = editor.Instance.Combo.Categories.Single();
        Assert.That(requirement.Selector.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        Assert.That(requirement.Selector.CategoryIdentities, Is.EquivalentTo(new[] { a.Identity, b.Identity }));
        Assert.That(requirement.MinCount, Is.EqualTo(2));
        Assert.That(requirement.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
    }

    [Test]
    public async Task RemovingConjunctChipsCollapsesAtTwoAndDeletesTheLastRequirement() {
        SessionState session = Session();
        ComboCategory top = ComboCategory.SameCard([a, b], 1, 2);
        ComboCategory alternative = ComboCategory.SameCard([a, b], 1, 2);
        session = new() {
            Categories = session.Categories,
            Cards = session.Cards,
            HandSize = session.HandSize,
            Combos = [new([top], alternativeGroups: [new([
                ComboAlternative.For(alternative),
                ComboAlternative.For(new ComboCard(session.Cards[0].Id, 1, 2))
            ])])]
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll(".expanded-expression .same-card-and"), Has.Count.EqualTo(2));

        await RemoveProperty(editor, "category:0", 2);
        Assert.That(editor.Instance.Combo.Categories.Single().Selector.Kind, Is.EqualTo(CardSelectorKind.Category));
        Assert.That(editor.Instance.Combo.Categories.Single().MaxCount, Is.EqualTo(2));

        await RemoveProperty(editor, "alternative:0:0", 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category!.Selector.Kind, Is.EqualTo(CardSelectorKind.Category));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category!.MinCount, Is.EqualTo(1));
        await DeleteRequirement(editor, "category:0");
        Assert.That(editor.Instance.Combo.Categories, Is.Empty);

        await DeleteRequirement(editor, "alternative:0:0");
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(editor.Instance.Combo.Cards.Single().CardId, Is.EqualTo(session.Cards[0].Id));
        Assert.That(editor.FindAll(".same-card-edit, [aria-label^='Edit same-card']"), Is.Empty);
    }

    [Test]
    public async Task OrAlternativesKeepIndependentAndConditionsAndSharedRanges() {
        CategoryBase c = new("C");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, c],
            Combos = [new([new(a, 1, 3)])],
            HandSize = 3
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ExtendRequirement(editor, "category:0", sameCard: true);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add property").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories[0].Selector.CategoryIdentities, Is.EquivalentTo(new[] { a.Identity, b.Identity }));

        await ExtendRequirement(editor, "category:0");
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "3" });
        await Button(editor, "Add alternative").ClickAsync(new());

        await ExtendRequirement(editor, "alternative:0:1", sameCard: true);
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("3"));
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "" });
        await Button(editor, "Add property").ClickAsync(new());

        ComboAlternativeGroup group = editor.Instance.Combo.AlternativeGroups.Single();
        ComboCategory first = group.Alternatives[0].Category!;
        ComboCategory second = group.Alternatives[1].Category!;
        Assert.That(first.Selector.CategoryIdentities, Is.EquivalentTo(new[] { a.Identity, b.Identity }));
        Assert.That(first.MinCount, Is.EqualTo(1));
        Assert.That(first.MaxCount, Is.EqualTo(3));
        Assert.That(second.Selector.CategoryIdentities, Is.EquivalentTo(new[] { b.Identity, c.Identity }));
        Assert.That(second.MinCount, Is.EqualTo(2));
        Assert.That(second.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));

        await RemoveProperty(editor, "alternative:0:1", 1);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[1].Category!.Selector.Kind, Is.EqualTo(CardSelectorKind.Category));
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Category, Is.SameAs(first));
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[1].Category!.MinCount, Is.EqualTo(2));
    }

    [Test]
    public async Task StandaloneSameCardAndRemainsCompactWithoutOuterGrouping() {
        ComboCategory requirement = ComboCategory.SameCard([a, b], 1, 3);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([requirement])],
            HandSize = 3
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());

        IElement expression = editor.Find(".expanded-expression > .requirement-row");
        Assert.That(expression.QuerySelector(".composite-alternative"), Is.Null);
        Assert.That(expression.QuerySelectorAll(".same-card-category .same-card-conjunct"), Has.Length.EqualTo(2));
        Assert.That(expression.QuerySelectorAll(".same-card-and"), Has.Length.EqualTo(1));
        Assert.That(editor.Find(".combo-header-content .same-card-category").QuerySelectorAll(".same-card-conjunct"), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task AndTargetSurvivesCategoryRenameAndPreservesItsDraft() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([new(a, 1, 2)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ExtendRequirement(editor, "category:0", sameCard: true);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find(".alternative-scope").TextContent, Does.Contain("requirement 1"));
        await Button(editor, "Add property").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Single().Selector.CategoryIdentities, Does.Contain("user:Renamed"));
    }

    [Test]
    public async Task AndTargetInsideOrGroupSurvivesCategoryRename() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([], alternativeGroups: [new([
                ComboAlternative.For(new ComboCategory(a, 1, 2)),
                ComboAlternative.For(new ComboCategory(b, 1, 2))
            ])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ExtendRequirement(editor, "alternative:0:0", sameCard: true);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.That(editor.Find(".alternative-scope").TextContent, Does.Contain("OR group 1, alternative 1"));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        await Button(editor, "Add property").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category!.Selector.CategoryIdentities,
            Is.EquivalentTo(new[] { "user:Renamed", b.Identity }));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[1].Category!.BaseCategory, Is.SameAs(b));
    }

    [Test]
    public async Task SameCardDrawAuthoringAndRenameCoverEveryReference() {
        SessionState session = Session();
        ComboCategory requirement = ComboCategory.SameCard([a, b], 1, 2);
        session.Combos[0] = new([requirement], alternativeGroups: [new([ComboAlternative.For(requirement), ComboAlternative.For(new ComboCard(session.Cards[0].Id, 1, 1))])]);
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPostDrawDiscardCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> card = cut.FindComponents<CardEditor>().ToArray()[0];
        await card.Find("#cardEligibility0").ChangeAsync(new() { Value = a.Identity });
        await card.Find("[aria-label='Add AND eligibility to alternative 1']").ChangeAsync(new() { Value = b.Identity });
        await card.Find("#cardEligibility0").ChangeAsync(new() { Value = "metadata:attribute:dark" });
        await card.Find("[aria-label='Add AND eligibility to alternative 2']").ChangeAsync(new() { Value = a.Identity });
        Assert.That(card.Instance.Card.PostDrawSelector.Children, Has.Count.EqualTo(2));
        Assert.That(card.Instance.Card.PostDrawSelector.Children.All(child => child.Kind == CardSelectorKind.AllOf), Is.True);
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>().ToArray()[0];
        Assert.That(combo.Instance.Combo.AllCategories.All(category => category.Selector.CategoryIdentities.Contains("user:Renamed")), Is.True);
        Assert.That(card.Instance.Card.PostDrawSelector.CategoryIdentities, Does.Contain("user:Renamed").And.Not.Contain(a.Identity));
        Assert.That(card.Instance.Card.PostDrawSelector.Children.All(child => child.Kind == CardSelectorKind.AllOf), Is.True);
        await card.FindAll("[aria-label='Remove eligibility B']").ToArray()[0].ClickAsync(new());
        Assert.That(card.Instance.Card.PostDrawSelector.Alternatives.Any(child => child.Kind == CardSelectorKind.Category), Is.True);
    }
}
