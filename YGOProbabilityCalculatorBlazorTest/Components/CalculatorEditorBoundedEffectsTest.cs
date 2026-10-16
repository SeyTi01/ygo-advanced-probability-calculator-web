using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorBoundedEffectsTest : CalculatorEditorTestBase {
    [TestCase(93946239), TestCase(78058681), TestCase(67115133), TestCase(36637374), TestCase(98567237), TestCase(34041788)]
    public void DraftCancelUntouchedSaveAndUnrelatedCostEditPreserveBoundedFields(int identity) {
        Card original = CardEffectRegistry.Default.RestoreBuiltIn(new([], 3, externalCardId: identity));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [original] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        editor.Find("#cardDraw0").Change("");
        editor.Find(".simulation-cancel").Click();
        Assert.That(editor.Instance.Card, Is.SameAs(original));
        OpenSimulation(editor);
        if (original.Search is not null) {
            editor.Find("#cardDraw0").Change("search");
        }
        editor.Find(".simulation-save").Click();
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(original.EffectPlan));
        OpenSimulation(editor);
        int cost = original.PreDrawRemovalCount == 1 ? 0 : 1;
        editor.Find("#cardCost0").Change(cost.ToString());
        editor.Find(".simulation-save").Click();
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(original.EffectPlan with { HandCost = cost }));
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        editor.Find(".simulation-restore").Click();
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(original.EffectPlan));
    }

    [Test]
    public async Task ModeSelectorsSurviveUserCategoryRenameWhileDraftIsOpen() {
        CategoryBase first = new("First"), second = new("Second");
        CardSelector selector = CardSelector.AnyOfSelectors([CardSelector.For(first), CardSelector.For(second)]);
        Card card = new([], search: new(selector, false, [new(CardSelector.For(first), 1), new(CardSelector.For(second), 1)]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card], Categories = [first, second] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        editor.Find("#cardCost0").Change("1");
        await cut.Find("[aria-label='Edit category First']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category First']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category First']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        editor.Find(".simulation-save").Click();
        SearchEffectPlan saved = editor.Instance.Card.Search!;
        Assert.DoesNotThrow(saved.Validate);
        Assert.That(saved.Modes![0].Selector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed" }));
        Assert.That(saved.Modes[1].Selector, Is.EqualTo(CardSelector.For(second)));
        Assert.That(editor.Instance.Card.PreDrawRemovalCount, Is.EqualTo(1));
    }
}
