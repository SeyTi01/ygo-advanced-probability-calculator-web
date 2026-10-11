using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorSameCardTest : CalculatorEditorTestBase {
    [Test]
    public async Task SameCardRenamePreservesOpenDraftAndCompositeReferencesPreventDeletion() {
        SessionState session = Session();
        session.Cards.Clear();
        session.Combos.RemoveAt(1);
        session.Combos[0] = new([ComboCategory.SameCard([a, b], 1, 2)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<ComboEditor> combo = cut.FindComponents<ComboEditor>().ToArray()[0];
        await cut.Find("[aria-label='Remove category B']").ClickAsync(new());
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        await combo.Find("[aria-label='Edit same-card requirement (A AND B)']").ClickAsync(new());
        await combo.Find("[aria-label='Same-card minimum']").ChangeAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category B']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category B']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category B']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.That(combo.Find("[aria-label='Same-card minimum']").GetAttribute("value"), Is.EqualTo("2"));
        await Button(combo, "Apply AND").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories[0].MinCount, Is.EqualTo(2));
        Assert.That(combo.Instance.Combo.Categories[0].Selector.CategoryIdentities, Does.Contain("user:Renamed"));
        await combo.Find("[aria-label='Edit same-card requirement (A AND Renamed)']").ClickAsync(new());
        await combo.Find("[aria-label='Remove conjunct 2']").ClickAsync(new());
        await Button(combo, "Apply AND").ClickAsync(new());
        await cut.Find("[aria-label='Remove category Renamed']").ClickAsync(new());
        Assert.That(cut.FindAll("select option[value='user:Renamed']"), Is.Empty);
    }

    [Test]
    public async Task SameCardAuthoringEditsSharedBoundsConjunctsAndOrAlternativeWithoutLosingDraft() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<ComboEditor> editor = cut.FindComponents<ComboEditor>().ToArray()[0];
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await editor.Find("[aria-label='Edit same-card requirement A']").ClickAsync(new());
        await editor.Find("[aria-label='Add AND category']").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("[aria-label='Same-card minimum']").ChangeAsync(new() { Value = "2" });
        await editor.Find("[aria-label='Same-card maximum']").ChangeAsync(new() { Value = "" });
        await Button(editor, "Apply AND").ClickAsync(new());
        ComboCategory requirement = editor.Instance.Combo.Categories[0];
        Assert.That(requirement.Selector.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        Assert.That(requirement.MinCount, Is.EqualTo(2));
        Assert.That(requirement.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        await editor.Find("[aria-label='Edit same-card requirement (A AND B)']").ClickAsync(new());
        await editor.Find("[aria-label='Conjunct 2']").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Apply AND").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories[0].Selector.Kind, Is.EqualTo(CardSelectorKind.Category));
        await editor.Find("[aria-label='Edit same-card requirement A']").ClickAsync(new());
        await editor.Find("[aria-label='Add AND category']").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("[aria-label='Remove conjunct 2']").ClickAsync(new());
        await editor.Find("[aria-label='Add AND category']").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Apply AND").ClickAsync(new());
        await Button(editor, "Add OR").ClickAsync(new());
        await editor.Find("[data-alternative-target='category:0']").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        await editor.Find("[aria-label='Edit same-card alternative 2 in group 1']").ClickAsync(new());
        await editor.Find("[aria-label='Add AND category']").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Apply AND").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives.All(leaf => leaf.Category!.Selector.Kind == CardSelectorKind.AllOf), Is.True);
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Combos[0].AlternativeGroups[0].Alternatives[0].Category!.Selector.Kind, Is.EqualTo(CardSelectorKind.AllOf));
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
        // Normalization sorts AllOf before Category.
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
