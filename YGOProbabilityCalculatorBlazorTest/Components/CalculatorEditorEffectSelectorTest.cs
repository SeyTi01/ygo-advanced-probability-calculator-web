using Bunit;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorEffectSelectorTest : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task CategoryRenameReachesModeHiddenByAnyUnionAndKeepsProvenanceAndDraft(bool editOpen) {
        CategoryBase role = new("Role");
        EffectProfile profile = new(EffectSource.BuiltIn, 78058681, 1, "Saved search", EffectSupport.Partial);
        SearchEffectMode[] modes = [new(CardSelector.Any, 1), new(CardSelector.For(role), 2)];
        Card source = new([], search: new(CardSelector.Any, false, modes), effectProfile: profile, simulationEnabled: false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [source], Categories = [role] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        if (editOpen) {
            OpenSimulation(editor);
            await editor.Find("#cardCost0").ChangeAsync(new() { Value = "1" });
        }

        await cut.Find("[aria-label='Edit category Role']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category Role']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category Role']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Card renamed = editor.Instance.Card;
        Assert.That(renamed.Id, Is.EqualTo(source.Id));
        Assert.That(renamed.EffectProfile, Is.EqualTo(profile));
        Assert.That(renamed.SimulationEnabled, Is.False);
        Assert.That(renamed.Search!.Selector, Is.EqualTo(CardSelector.Any));
        Assert.That(renamed.Search.Modes![0], Is.EqualTo(modes[0]));
        Assert.That(renamed.Search.Modes[1], Is.EqualTo(new SearchEffectMode(CardSelector.For(new("Renamed")), 2)));
        Assert.DoesNotThrow(renamed.Search.Validate);
        if (editOpen) {
            Assert.That(editor.Find("#cardCost0").GetAttribute("value"), Is.EqualTo("1"));
            await editor.Find(".simulation-save").ClickAsync(new());
            Assert.That(editor.Instance.Card.PreDrawRemovalCount, Is.EqualTo(1));
            Assert.That(editor.Instance.Card.Search, Is.EqualTo(renamed.Search));
        }
    }

    [Test]
    public async Task SaveRejectsRemovedCategoryReferencedOnlyByModeHiddenByAnyUnion() {
        CategoryBase role = new("Role");
        Card source = new([], search: new(CardSelector.Any, false, [new(CardSelector.Any, 1), new(CardSelector.For(role), 2)]));
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source)
            .Add(component => component.CategoryBases, new[] { role })
            .Add(component => component.OnChanged, card => saved = card));
        OpenSimulation(editor);
        await editor.Find("#cardCost0").ChangeAsync(new() { Value = "1" });
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.CategoryBases, Array.Empty<CategoryBase>()));

        await editor.Find(".simulation-save").ClickAsync(new());

        Assert.That(saved, Is.Null);
        Assert.That(editor.FindAll(".simulation-editor"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll("[role=alert]"), Is.Not.Empty);
    }
}
