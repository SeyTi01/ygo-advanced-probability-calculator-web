using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class MultiModeSimulationEditorTest : CalculatorEditorTestBase {
    private static Card Chant() => CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 67115133));

    [Test]
    public void VerifiedModeUsesTheSharedEditorAndNoOpSaveKeepsItsIdentity() {
        Card source = Chant();
        Card? saved = null;
        Mock<ICardInfoService> catalog = Mock.Get(context.Services.GetRequiredService<ICardInfoService>());
        catalog.Setup(service => service.GetCatalogCardsAsync()).ReturnsAsync(new[] { new CardInfo { Id = 5318639, Name = "Mystical Space Typhoon" } });
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        IRenderedComponent<PostDrawSelectorEditor> protectedMode = editor.FindComponents<PostDrawSelectorEditor>().Last();
        Assert.That(protectedMode.Find(".combo-card-tag").TextContent, Is.EqualTo("Card: Mystical Space Typhoon"));
        protectedMode.Find(".requirement-edit").Click();
        Assert.That(protectedMode.FindComponents<RequirementEditForm>(), Has.Count.EqualTo(1));
        Assert.That(protectedMode.FindComponent<RequirementPropertyPicker>().Instance.Value, Is.EqualTo("metadata:catalog-id:5318639"));
        protectedMode.Find("form").Submit();
        Assert.That(protectedMode.Instance.Selector, Is.EqualTo(CardSelector.CatalogIdentity(5318639)));
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null);
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(source.EffectPlan));
    }

    [Test]
    public void EditingVerifiedModeChangesOnlyThatModeAndCancelRestoresTheOriginal() {
        Card source = Chant();
        Card? saved = null;
        CategoryBase property = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, new[] { property })
            .Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        IRenderedComponent<PostDrawSelectorEditor> mode = editor.FindComponents<PostDrawSelectorEditor>().Last();
        mode.Find(".requirement-edit").Click();
        mode.Find(".requirement-property-picker select").Change(property.Identity);
        mode.Find(".requirement-cancel").Click();
        Assert.That(mode.Instance.Selector, Is.EqualTo(source.Search!.Modes![1].Selector));
        mode.Find(".requirement-edit").Click();
        Assert.That(mode.FindComponent<RequirementPropertyPicker>().Instance.Value, Is.EqualTo("metadata:catalog-id:5318639"));
        mode.Find(".requirement-property-picker select").Change(property.Identity);
        mode.Find("form").Submit();
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Modes![0], Is.EqualTo(source.Search.Modes[0]));
        Assert.That(saved.Search.Modes[1].Selector, Is.EqualTo(CardSelector.For(property)));
        Assert.That(saved.Search.Modes.Select(searchMode => searchMode.ActivationLimit), Is.EqualTo(new int?[] { 1, 1 }));
        Assert.That(saved.Search.ObjectiveMetadataOnly, Is.EqualTo(source.Search.ObjectiveMetadataOnly));
        Assert.That(saved.Search.Selector, Is.EqualTo(SearchEffectPlan.ModeTargetUnion(saved.Search.Modes)));
    }

    [TestCase("property", CardSelectorKind.AllOf)]
    [TestCase("alternative", CardSelectorKind.AnyOf)]
    public void AddingAndOrToVerifiedModeKeepsItsCatalogPredicate(string action, CardSelectorKind operation) {
        Card source = Chant();
        Card? saved = null;
        CategoryBase property = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, new[] { property })
            .Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        IRenderedComponent<PostDrawSelectorEditor> mode = editor.FindComponents<PostDrawSelectorEditor>().Last();
        mode.Find(".requirement-edit").Click();
        mode.Find($"button[data-action='{action}']").Click();
        mode.Find(".requirement-property-picker select").Change(property.Identity);
        mode.Find("form").Submit();
        Assert.That(mode.Instance.Selector.Kind, Is.EqualTo(operation));
        Assert.That(mode.Instance.Selector.AuthoredTerms, Does.Contain(CardSelector.CatalogIdentity(5318639)));
        Assert.That(mode.Instance.Selector.AuthoredTerms, Does.Contain(CardSelector.For(property)));
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Modes![0], Is.EqualTo(source.Search!.Modes![0]));
        Assert.That(saved.Search.Modes[1].Selector.AuthoredTerms, Does.Contain(CardSelector.CatalogIdentity(5318639)));
        Assert.That(saved.Search.Modes.Select(searchMode => searchMode.ActivationLimit), Is.EqualTo(new int?[] { 1, 1 }));
    }

    [Test]
    public async Task ChangingOneModePreservesItsSiblingAllowanceProvenanceAndSessionRoundTrip() {
        Card source = Chant();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".simulation-search-mode"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll(".simulation-search-mode-heading .badge").Select(badge => badge.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Mode: 1: 1 per entry", "Mode: 2: 1 per entry" }));
        editor.FindAll(".expression-member").Single(member => member.TextContent.Contains("Level")).Click();
        editor.Find("input[aria-label='Minimum Level']").Input("2");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Deck, new[] { source }));
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.EqualTo("2"));
        editor.Find(".selector-expression form").Submit();
        editor.Find(".simulation-save").Click();

        SearchEffectPlan search = saved!.Search!;
        Assert.That(search.Modes![1], Is.EqualTo(source.Search!.Modes![1]));
        Assert.That(search.Modes.Select(mode => mode.ActivationLimit), Is.EqualTo(new int?[] { 1, 1 }));
        Assert.That(search.ObjectiveMetadataOnly, Is.True);
        Assert.That(search.Modes[0].Selector.AuthoredTerms.Single(term => term.Kind == CardSelectorKind.NumericRange).NumericRange,
            Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, 2, 4)));
        Assert.That(search.Selector, Is.EqualTo(SearchEffectPlan.ModeTargetUnion(search.Modes)));
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState decoded = await codec.LoadSessionAsync(codec.SerializeSession(new() { Cards = [saved] }));
        Assert.That(decoded.Cards.Single().EffectPlan, Is.EqualTo(saved.EffectPlan));

        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-restore").Click();
        Assert.That(saved.EffectPlan, Is.EqualTo(source.EffectPlan));
    }

    [Test]
    public async Task ClearingTheVerifiedModeKeepsTheOtherModeAndSessionMeaning() {
        Card source = Chant();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        IRenderedComponent<PostDrawSelectorEditor> protectedMode = editor.FindComponents<PostDrawSelectorEditor>().Last();
        Assert.That(protectedMode.FindComponents<SelectorExpressionEditor>(), Has.Count.EqualTo(1));
        Button(protectedMode, "Clear filter").Click();
        Assert.That(protectedMode.Instance.Selector, Is.EqualTo(CardSelector.Any));
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Selector, Is.EqualTo(CardSelector.Any));
        Assert.That(saved.Search.Modes![0], Is.EqualTo(source.Search!.Modes![0]));
        Assert.That(saved.Search.Modes[1].ActivationLimit, Is.EqualTo(1));
        Assert.That(saved.Search.ObjectiveMetadataOnly, Is.True);
        Assert.DoesNotThrow(saved.Search.Validate);

        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState decoded = await codec.LoadSessionAsync(codec.SerializeSession(new() { Cards = [saved] }));
        Assert.That(decoded.Cards.Single().Search, Is.EqualTo(saved.Search));
    }

    [Test]
    public void ClearingAModeWithAnUnfinishedConditionReleasesSaveAndKeepsTheOtherMode() {
        Card source = Chant();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        editor.FindAll(".expression-member").Single(member => member.TextContent.Contains("Level")).Click();
        editor.Find("input[aria-label='Minimum Level']").Input("");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        IRenderedComponent<PostDrawSelectorEditor> firstMode = editor.FindComponents<PostDrawSelectorEditor>().First();
        Button(firstMode, "Clear filter").Click();
        Assert.That(editor.FindAll(".selector-expression form"), Is.Empty);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Modes![0].Selector, Is.EqualTo(CardSelector.Any));
        Assert.That(saved.Search.Modes[1], Is.EqualTo(source.Search!.Modes![1]));
    }

    [Test]
    public void EscapeAndCancelDiscardModeDraftsAndInvalidRangesBlockSave() {
        Card source = Chant();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        editor.FindAll(".expression-member").Single(member => member.TextContent.Contains("Level")).Click();
        editor.Find("input[aria-label='Minimum Level']").Input("5");
        editor.Find(".selector-expression form").Submit();
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find(".selector-expression form").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".selector-expression form"), Is.Empty);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        editor.FindComponents<PostDrawSelectorEditor>().Last().Find("button").Click();
        editor.Find(".simulation-cancel").Click();
        Assert.That(saved, Is.Null);
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindComponents<PostDrawSelectorEditor>().Last().Instance.Selector, Is.EqualTo(source.Search!.Modes![1].Selector));
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null);
    }
}
