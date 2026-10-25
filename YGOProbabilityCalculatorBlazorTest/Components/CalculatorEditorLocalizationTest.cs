using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazorTest.Localization;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorLocalizationTest : CalculatorEditorTestBase {
    [Test]
    public void PseudoLocaleRendersCoreLabelsValidationModelNotesAndAccessibleSettings() {
        MaintainedCardEffect effect = CardEffectRegistry.Default.Find(35261759)!;
        Card card = new Card([], 1, "Authored name", externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
        using PresentationLocale locale = new();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { HandSize = 5, Cards = [card] });
        Assert.Multiple(() => {
            Assert.That(cut.FindAll("h4").Any(heading => heading.TextContent.Contains("⟦Deck 1⟧")), Is.True);
            Assert.That(cut.Markup, Does.Contain("⟦Hand 5; only 1 copies⟧"));
            Assert.That(cut.Find(".calculation-settings-trigger").GetAttribute("aria-label"), Is.EqualTo("⟦Settings⟧"));
            Assert.That(cut.Find("label[for=handSize]").TextContent, Is.EqualTo("Hand Size:"));
            Assert.That(cut.Find("#handSize").GetAttribute("type"), Is.EqualTo("number"));
            Assert.That(cut.Markup, Does.Contain("Authored name"));
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(SimulationDetails(editor), Does.Contain("⟦Maintained model note⟧"));
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(effect.Profile));
    }
}
