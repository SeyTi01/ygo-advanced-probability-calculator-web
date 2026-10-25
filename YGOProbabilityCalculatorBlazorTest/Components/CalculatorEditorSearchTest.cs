using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorSearchTest : CalculatorEditorTestBase {
    [Test]
    public async Task SearchTargetsCustomizeCancelDisableRestoreAndSavedChannels() {
        Card rota = SearchEffectsTest.Rota();
        Card target = SearchEffectsTest.Warrior();
        SessionState session = new() { Cards = [rota, target, new([], 3)], HandSize = 2,
            Combos = [new([], cards: [new(target.Id, 1, 2)])] };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        editor.Find(".accordion-button").Click();
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("target"));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Not.Contain("29302858"));
        await cut.InvokeAsync(() => cut.FindComponent<CardListEditor>().Instance.ReplaceCard((1, target.WithActive(false))));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        await cut.InvokeAsync(() => cut.FindComponent<CardListEditor>().Instance.ReplaceCard((1, target)));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("target"));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Not.Contain("29302858"));
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardDraw0").Change("");
        editor.Find(".simulation-cancel").Click();
        Assert.That(editor.Instance.Card.Search, Is.EqualTo(rota.Search));
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardDraw0").Change("");
        editor.Find(".simulation-save").Click();
        Assert.That(editor.Instance.Card.Search, Is.Null);
        AssertPreviousResult(cut);
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        await Button(cut, "Save Session").ClickAsync(new());
        foreach (string saved in new[] { SavedSessionJson(), cut.FindComponent<SessionShareButton>().Instance.Snapshot,
            context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!.ToString()! }) {
            Assert.That((await codec.LoadSessionAsync(saved)).Cards[0].Search, Is.Null);
        }
        editor.Find(".simulation-restore").Click();
        Assert.That(editor.Instance.Card.Search, Is.EqualTo(rota.Search));
        Assert.That(editor.Find(".simulation-source").TextContent, Does.Contain("Built-in simulation"));
    }

    [Test]
    public async Task CustomSelectorAndDraftSurviveCategoryRename() {
        CategoryBase role = new("Role");
        Card source = SearchEffectsTest.Rota().WithSearch(new(CardSelector.For(role), false));
        SessionState state = new() { Cards = [source, new([role])], Categories = [role], HandSize = 1 };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(state);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        editor.Find(".accordion-button").Click();
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardSearchOnce0").Change(true);
        await cut.Find("[aria-label='Edit category Role']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category Role']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category Role']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(editor.Instance.Card.Search!.Selector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed" }));
        Assert.That(editor.Find("#cardSearchOnce0").HasAttribute("checked"), Is.True);
        editor.Find(".simulation-save").Click();
        Assert.That(editor.Instance.Card.OncePerTurn, Is.True);
        Assert.That(editor.Instance.Card.Search.ObjectiveMetadataOnly, Is.False);
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Not.Contain("None"));
    }

    [Test]
    public void TargetPreviewUpdatesOnDeckEditsWithoutOverwritingSearchDraft() {
        Card rota = SearchEffectsTest.Rota();
        Card target = SearchEffectsTest.Warrior();
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, rota).Add(component => component.Deck, new[] { rota, target }));
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardDraw0").Change("search");
        editor.Find("#cardSearchOnce0").Change(true);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Deck, new[] { rota, target.WithActive(false) }));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        Assert.That(editor.Find("#cardSearchOnce0").HasAttribute("checked"), Is.True);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.DrawEffectsEnabled, false));
        Assert.That(editor.Find(".simulation-mode").TextContent, Does.Contain("Opening hand only"));
    }

    [Test]
    public void SearchSummaryKeepsCustomPlansOnTheGenericFallback() {
        Card builtIn = SearchEffectsTest.Rota();
        Card custom = builtIn.WithEffect(builtIn.EffectPlan, builtIn.EffectProfile.AsCustom());
        Card changed = builtIn.WithSearch(new(CardSelector.Any, false));

        IRenderedComponent<CardSimulationEditor> builtInEditor = RenderSearchEditor(builtIn);
        IRenderedComponent<CardSimulationEditor> customEditor = RenderSearchEditor(custom);
        IRenderedComponent<CardSimulationEditor> changedEditor = RenderSearchEditor(changed);

        Assert.That(builtInEditor.Find(".simulation-summary").TextContent, Is.EqualTo("Search 1 Level 1–4 Warrior monster"));
        Assert.That(customEditor.Find(".simulation-source").TextContent, Does.Contain("Custom simulation"));
        Assert.That(customEditor.Find(".simulation-summary").TextContent, Is.EqualTo("Search 1 card from Deck"));
        Assert.That(changedEditor.Find(".simulation-summary").TextContent, Is.EqualTo("Search 1 card from Deck"));
    }

    [Test]
    public void SearchTargetNamesOmitUniqueIdsAndDisambiguateOnlyNameCollisions() {
        Card source = SearchEffectsTest.Rota();
        Card catalogTarget = SearchEffectsTest.Warrior();
        Card sameCatalogEntry = new(catalogTarget.Categories, name: "target", id: "duplicate-entry", externalCardId: 29302858);
        Card otherCatalogTarget = new(catalogTarget.Categories, name: "target", id: "other-catalog", externalCardId: 12345);
        Card customTarget = new(catalogTarget.Categories, name: "target", id: "8b106ff0-4a99-467d-b2f9-533181f24a56");
        source = source.WithSearch(source.Search! with { ObjectiveMetadataOnly = false });
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source)
            .Add(component => component.Deck, new[] { source, catalogTarget, sameCatalogEntry, otherCatalogTarget, customTarget }));

        string targets = editor.Find(".simulation-targets").TextContent;

        Assert.That(targets, Does.Contain("target (29302858)"));
        Assert.That(targets, Does.Contain("target (12345)"));
        Assert.That(targets, Does.Contain("target (custom 1)"));
        Assert.That(targets, Does.Not.Contain("8b106ff0"));
        Assert.That(targets.Split("target (29302858)", StringSplitOptions.None), Has.Length.EqualTo(2),
            "Repeated entries for the same catalog card are presented once.");
    }

    private IRenderedComponent<CardSimulationEditor> RenderSearchEditor(Card card) => context.RenderComponent<CardSimulationEditor>(parameters => parameters
        .Add(component => component.Card, card)
        .Add(component => component.Deck, new[] { card, SearchEffectsTest.Warrior() }));

    [TestCase(false), TestCase(true)]
    public void BuiltInToCustomRoleWorksWithoutProvenanceOption(bool automatic) {
        CategoryBase fire = new("FIRE", CategorySource.Metadata, "attribute:fire");
        CategoryBase role = new("My targets", rule: automatic ? CardSelector.For(fire) : null);
        Card rota = SearchEffectsTest.Rota();
        Card target = new(automatic ? [fire] : [role], name: "Custom target", id: "custom-target");
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, rota).Add(component => component.Deck, new[] { rota, target })
            .Add(component => component.CategoryBases, new[] { role })
            .Add(component => component.OnChanged, card => saved = card));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll("[id^=cardSearchObjective]"), Is.Empty);
        Button(editor, "Any card (clear filter)").Click();
        editor.Find("#cardSearchEligibility0").Change(role.Identity);
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("Custom target"));
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.ObjectiveMetadataOnly, Is.False);
        Assert.That(saved.Search.Selector, Is.EqualTo(CardSelector.For(role)));
        Assert.That(saved.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([saved, target, new([], 2)],
            [new([], cards: [new(target.Id, 1, 1)])], 1, [], categoryDefinitions: [role]);
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(0.5));
    }

    [TestCase(false), TestCase(true)]
    public void OpeningAndSavingOldCustomPlanKeepsItsMatchingPolicy(bool objective) {
        Card source = SearchEffectsTest.Rota().WithSearch(new(CardSelector.Any, objective));
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source)
            .Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null, "An untouched save does not reinterpret persisted settings.");
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardSearchOnce0").Change(true);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.ObjectiveMetadataOnly, Is.EqualTo(objective));
        Assert.That(saved.OncePerTurn, Is.True);
    }

    [TestCase(false), TestCase(true)]
    public void DrawAndSearchEditorsUseTheSameOncePerTurnFlag(bool draw) {
        Card source = SearchEffectsTest.Preset(80181649);
        if (draw) {
            source = source.WithDrawCount(1);
        }
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        string input = draw ? "#cardDrawOnce0" : "#cardSearchOnce0";
        Assert.That(editor.Find(input).HasAttribute("checked"), Is.True);
        editor.Find(input).Change(false);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.EffectPlan.OncePerTurn, Is.False);
    }

}
