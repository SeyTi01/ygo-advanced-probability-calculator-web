using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CanonicalSelectorEditorTest : CalculatorEditorTestBase {
    [TestCase(32807846, 4, 2)]
    [TestCase(85106525, 4, 2)]
    [TestCase(57103969, 4, 2)]
    [TestCase(72323266, 3, 3)]
    [TestCase(96729612, 7, 2)]
    public void FreshAndRestoredRangesHaveOneFieldAndAnEditableRealNumericOperand(int id, int maximum, int operands) {
        Card source = CardEffectRegistry.Default.CreateImportedCard(new() { Id = id, Name = "Source" }, 1, id);
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindComponents<ExpressionRow>(), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(1));
        Assert.That(editor.FindComponents<ExpressionGroup>(), Is.Empty);
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.FindAll(".requirement-operand"), Has.Count.EqualTo(operands));
        Assert.That(editor.Find("select[aria-label='Comparison']").GetAttribute("value"), Is.EqualTo("range"));
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(editor.Find("input[aria-label='Maximum Level']").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        editor.Find("input[aria-label='Minimum Level']").Input("2");
        editor.Find(".selector-expression form").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(saved, Is.Null);
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.EqualTo("1"));
        editor.Find("input[aria-label='Minimum Level']").Input("2");
        editor.Find(".selector-expression form").Submit();
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".selector-expression form"), Is.Empty);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        CardSelector changed = saved.Search!.Selector.AuthoredTerms.Single(term => term.Kind == CardSelectorKind.NumericRange);
        Assert.That(changed.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, 2, maximum)));
        CardSelector decoded = JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(saved.Search.Selector))!;
        Assert.That(decoded, Is.EqualTo(saved.Search.Selector));
        Assert.That(decoded.AuthoredTerms.Select(term => term.Signature), Is.EqualTo(saved.Search.Selector.AuthoredTerms.Select(term => term.Signature)));
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-restore").Click();
        Assert.That(saved!.EffectPlan, Is.EqualTo(source.EffectPlan));
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnchangedLegacyLiteralEditorNeverConvertsItsNumericMeaning(bool custom) {
        const int id = 38120068;
        MaintainedCardEffect current = CardEffectRegistry.Default.Find(id)!;
        EffectProfile profile = custom ? current.Profile.AsCustom() : current.Profile with { Revision = 1 };
        Card source = new Card([], externalCardId: id).WithEffect(LegacySelectorPlans.Plan(id), profile);
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.Find("select[aria-label='Comparison']").GetAttribute("value"), Is.EqualTo("exact"));
        Assert.That(editor.Find("input[aria-label='Level']").GetAttribute("value"), Is.EqualTo("8"));
        editor.Find(".selector-expression form").Submit();
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null, "Saving an untouched literal must not change strictness or provenance.");
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(LegacySelectorPlans.Plan(id)));
        Assert.That(editor.Instance.Card.PreDrawSelector.HasNumericPredicate, Is.False);
    }

    [Test]
    public void LegacyRotaKeepsSeparateLiteralOrTermUntilExplicitRestore() {
        const int id = 32807846;
        MaintainedCardEffect current = CardEffectRegistry.Default.Find(id)!;
        Card source = new Card([], externalCardId: id).WithEffect(LegacySelectorPlans.Plan(id), current.Profile with { Revision = 3 });
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(2));
        foreach (IRenderedComponent<ExpressionRow> row in editor.FindComponents<ExpressionRow>().ToArray()) {
            row.Find(".requirement-edit").Click();
            editor.Find(".selector-expression form").Submit();
        }
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null);
        editor.Find(".simulation-details-toggle").Click();
        editor.Find(".simulation-restore").Click();
        Assert.That(saved!.EffectPlan, Is.EqualTo(current.Plan));
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(1));
    }

    [Test]
    public void ExactCanonicalPaymentUsesOneNumericFieldAndRetainsBoundsWhenEdited() {
        Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 38120068));
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(1));
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.FindAll(".requirement-operand"), Has.Count.EqualTo(1));
        Assert.That(editor.Find("select[aria-label='Comparison']").GetAttribute("value"), Is.EqualTo("exact"));
        editor.Find("input[aria-label='Level']").Input("7");
        editor.Find(".selector-expression form").Submit();
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.PreDrawSelector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, 7, 7)));
    }

    [Test]
    public void FactoredSelectorsKeepMeaningfulNestedOrRowsAndVerifiedFiltersStayProtected() {
        foreach (int id in new[] { 26845680, 60764609 }) {
            Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: id));
            IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters.Add(component => component.Card, source));
            editor.Find(".simulation-edit").Click();
            Assert.That(editor.FindComponents<ExpressionRow>(), Has.Count.EqualTo(2));
            Assert.That(editor.FindComponents<ExpressionGroup>(), Has.Count.EqualTo(1));
            Assert.That(editor.FindAll(".selector-expression .expression-separator > select").Select(select => select.GetAttribute("value")), Does.Contain("AND"));
        }
        foreach (int id in new[] { 67115133, 48805472, 78058681 }) {
            Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: id));
            IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters.Add(component => component.Card, source));
            editor.Find(".simulation-edit").Click();
            Assert.That(editor.FindAll(".requirement-edit"), Is.Empty, $"{id}: protected Objective/Not/catalog filters cannot be weakened by display editing.");
            if (id == 67115133) {
                Assert.That(editor.Markup, Does.Contain("each mode once"));
                Assert.That(editor.Instance.Card.Search!.Modes, Has.Count.EqualTo(2));
            }
            else {
                Assert.That(editor.FindAll("button").Any(button => button.TextContent.Contains("Replace verified filter")), Is.True);
            }
        }
    }
}
