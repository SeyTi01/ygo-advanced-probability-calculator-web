using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Localization;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorLocalizationTest : CalculatorEditorTestBase {
    [Test]
    public async Task RestoredAutomaticNamesAreAbbreviatedAcrossPropertiesSelectorsAndComboReferences() {
        CategoryBase[] metadata = [
            new("Card identity: Target", CategorySource.Metadata, "catalog-id:123"),
            new("Card name: Target", CategorySource.Metadata, "card-name:target"),
            new("Monster Type: Warrior", CategorySource.Metadata, "monster-race:warrior")
        ];
        CategoryBase custom = new("Card name: Authored role");
        Card target = new([.. metadata, custom], 3, "Target");
        SessionState original = new() {
            Cards = [target], Categories = [custom],
            Combos = [new([new(metadata[0], 1, 3)])], HandSize = 1
        };
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string saved = codec.SerializeSession(original);
        SessionState restored = await codec.LoadSessionAsync(saved);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(restored);
        IRenderedComponent<CardEditor> card = cut.FindComponent<CardEditor>();
        card.Find(".accordion-button").Click();
        Assert.That(card.Find(".detected-card-properties").TextContent,
            Does.Contain("Identity: Target").And.Contain("Name: Target").And.Contain("Type: Warrior"));
        IRenderedComponent<ComboEditor> combo = cut.FindComponent<ComboEditor>();
        Assert.That(combo.Find(".combo-header-requirements").TextContent, Does.Contain("Identity: Target"));
        combo.Find(".accordion-button").Click();
        OpenNewRequirement(combo);
        Assert.That(combo.Find(".requirement-property-picker").TextContent,
            Does.Contain("Identity: Target").And.Contain("Name: Target").And.Contain("Type: Warrior").And.Contain(custom.Name));
        Assert.That(restored.Cards[0].Categories.Select(category => category.Name),
            Is.EqualTo(original.Cards[0].Categories.Select(category => category.Name)));
        Assert.That(codec.SerializeSession(restored), Is.EqualTo(saved));
    }

    [Test]
    public void NestedExpressionStructureDoesNotUseTranslatedCssTokens() {
        using PresentationLocale locale = new();
        IRenderedComponent<ExpressionGroup> group = context.RenderComponent<ExpressionGroup>(parameters => parameters
            .Add(component => component.Nested, true)
            .Add(component => component.Label, "Nested alternatives")
            .AddChildContent("Requirements"));

        Assert.That(group.Find(".requirement-or-group.expression-group-nested[role=group]").TextContent, Is.EqualTo("Requirements"));
        Assert.That(group.Find("[role=group]").GetAttribute("aria-label"), Is.EqualTo("Nested alternatives"));
    }

    [TestCase("current")]
    [TestCase("historical")]
    [TestCase("custom")]
    [TestCase("legacy")]
    public async Task RenderedSearchSummaryUsesOnlyTheAcceptedDescription(string snapshot) {
        MaintainedCardEffect maintained = CardEffectRegistry.Default.Find(32807846)!;
        EffectProfile profile = maintained.Profile with { Summary = "Saved search summary", Limitations = "Saved search note" };
        profile = snapshot switch {
            "historical" => profile with { Revision = maintained.Profile.Revision + 1 },
            "custom" => profile.AsCustom(),
            "legacy" => EffectProfile.Legacy,
            _ => profile
        };
        Card card = new Card([], name: "Authored search", externalCardId: maintained.CanonicalCardId).WithEffect(maintained.Plan, profile);
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string before = codec.SerializeSession(new() { Cards = [card] });
        using PresentationLocale locale = new();
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters.Add(component => component.Card, card));
        string expected = snapshot switch {
            "current" => "⟦Maintained search⟧",
            "historical" => profile.Summary!,
            _ => "⟦Generic search⟧"
        };
        Assert.That(editor.Find(".simulation-summary").TextContent, Is.EqualTo(expected));
        if (snapshot is "current" or "historical") {
            editor.Find(".simulation-details-toggle").Click();
            Assert.That(editor.Find(".simulation-limitations").TextContent,
                Does.Contain(snapshot == "current" ? "⟦Maintained search note⟧" : profile.Limitations));
        }
        else {
            Assert.That(editor.FindAll(".simulation-limitations"), Is.Empty);
        }
        Assert.That(codec.SerializeSession(new() { Cards = [card] }), Is.EqualTo(before));
        Card retained = (await codec.LoadSessionAsync(before)).Cards.Single();
        Assert.That(retained.EffectProfile, Is.EqualTo(profile));
        Assert.That(retained.EffectPlan, Is.EqualTo(maintained.Plan));
    }

    [Test]
    public async Task PseudoLocaleCoversSearchEmptyRequirementsMaximumAndAdvancedCounts() {
        Card desires = CardEffectRegistry.Default.RestoreBuiltIn(new([], 1, "Desires", externalCardId: 35261759));
        using PresentationLocale locale = new();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [desires], Combos = [new([])] });
        Assert.That(cut.Find(".quick-add input").GetAttribute("placeholder"), Is.EqualTo("⟦Search cards⟧"));
        IRenderedComponent<ComboEditor> combo = cut.FindComponent<ComboEditor>();
        Assert.That(combo.Find(".combo-header-requirements").TextContent, Does.Contain("⟦No requirements⟧"));
        await combo.Find(".accordion-button").ClickAsync(new());
        OpenNewRequirement(combo);
        Assert.That(combo.Find(".requirement-bounds summary").TextContent, Is.EqualTo("⟦Unbounded maximum⟧"));
        await combo.Find("#maxCount0").InputAsync(new() { Value = "3" });
        Assert.That(combo.Find(".requirement-bounds summary").TextContent, Is.EqualTo("⟦3 is maximum⟧"));
        IRenderedComponent<CardSimulationEditor> effect = cut.FindComponent<CardSimulationEditor>();
        await effect.Find(".simulation-edit").ClickAsync(new());
        Assert.That(effect.Find(".simulation-advanced summary").TextContent, Is.EqualTo("⟦Banish 10; advanced⟧"));
        await effect.Find("#cardDeckCost0").ChangeAsync(new() { Value = "0" });
        Assert.That(effect.Find(".simulation-advanced summary").TextContent, Is.EqualTo("⟦Advanced⟧"));
        await effect.Find(".simulation-cancel").ClickAsync(new());
        Assert.That(effect.Instance.Card.EffectPlan, Is.EqualTo(desires.EffectPlan));
    }

    [Test]
    public async Task DetailedDrawWarningUsesTheResourceInItsExistingNote() {
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        session.Cards[session.Cards.IndexOf(graceful)] = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 35261759));
        session.Cards.Add(CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 1475311)));
        session.Cards.Add(graceful.WithCopies(1));
        using PresentationLocale locale = new();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.Find(".draw-complexity-notice[role=note]").TextContent, Does.Contain("⟦Detailed draw warning⟧"));
    }

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
