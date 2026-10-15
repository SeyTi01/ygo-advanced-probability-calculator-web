using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorGroupedSelectorTest : CalculatorEditorTestBase {
    [Test]
    public void CompactExpressionEditsThresholdAndGroupsWithPreviewSaveCancelAndRestore() {
        Card source = SearchEffectsTest.Preset(92221402);
        Card target = SelectorExpressionTest.Target();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.Deck, new[] { source, target })
            .Add(component => component.OnChanged, card => saved = card));
        Assert.That(editor.Markup, Does.Not.Contain("Partial"));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".selector-group").Count, Is.EqualTo(3));
        Assert.That(editor.FindAll(".draw-selector-chip").Count, Is.EqualTo(6));
        Assert.That(editor.FindAll("input[aria-label^='Minimum Level']").Count, Is.EqualTo(1));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain(target.Name));
        editor.Find("input[aria-label^='Minimum Level']").Change("6");
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        editor.Find(".simulation-cancel").Click();
        Assert.That(saved, Is.Null);
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.Find("input[aria-label^='Minimum Level']").GetAttribute("value"), Is.EqualTo("5"));
        editor.Find("input[aria-label^='Minimum Level']").Change("6");
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        Assert.That(saved.Search!.Matches(target), Is.False);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-edit").Click();
        editor.Find("[aria-label='Group operator root.0']").Change("AllOf");
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Selector.Signature, Does.Contain("AllOf"));
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-restore").Click();
        Assert.That(saved!.EffectPlan, Is.EqualTo(source.EffectPlan));
        Assert.That(saved.EffectProfile.Revision, Is.EqualTo(2));
    }

    [Test]
    public void InvalidThresholdBlocksSaveAcrossOtherDraftEditsUntilCorrectedOrRemoved() {
        Card source = SearchEffectsTest.Preset(92221402);
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source));
        editor.Find(".simulation-edit").Click();
        editor.Find("input[aria-label^='Minimum Level']").Change("-1");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("#cardSearchOnce0").Change(false);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("[aria-label='Add clause to group root']").Change("metadata:level:4");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("input[aria-label^='Minimum Level']").Change("7");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        editor.Find("input[aria-label^='Minimum Level']").Change("");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("[aria-label='Remove eligibility Level threshold']").Click();
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void ExistingFlatSelectorCanBecomeGroupedWithoutLosingDraftOnParentUpdate() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        Card source = SearchEffectsTest.Rota().WithSearch(new(CardSelector.For(a), false));
        Card target = new([a, b], name: "Target");
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, new[] { a, b })
            .Add(component => component.Deck, new[] { source, target }).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        editor.FindAll("button").Single(button => button.TextContent == "Edit groups").Click();
        editor.Find("[aria-label='New group operator root']").Change("AnyOf");
        editor.Find("[aria-label='Add grouped clause root']").Change(b.Identity);
        editor.Find("[aria-label='New group operator root']").Change("AllOf");
        editor.Find("[aria-label='Add grouped clause root']").Change("metadata:kind:monster");
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.DrawEffectsEnabled, false));
        Assert.That(editor.FindAll(".selector-group").Count, Is.EqualTo(2));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Selector.Matches(new Card([a, new("Monster", CategorySource.Metadata, "kind:monster")])), Is.True);
        Assert.That(saved.Search.Selector.Matches(target), Is.False);
        Assert.That(saved.Search.ObjectiveMetadataOnly, Is.False);
    }
}
