using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorExcavationTest : CalculatorEditorTestBase {
    [Test]
    public async Task ExcavationEditorPreservesOperationAndCanDisableReplaceAndRestoreIt() {
        Card card = new Card([], id: "prosperity", externalCardId: 84211599)
            .WithEffect(new PotOfProsperityEffect().Compile(), CardEffectRegistry.Default.Find(84211599)!.Profile);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find(".simulation-details-toggle").ClickAsync(new());
        Assert.That(editor.Find(".simulation-excavation-note").TextContent, Is.Not.Empty);
        OpenSimulation(editor);
        Assert.That(editor.Find("#cardDraw0").GetAttribute("value"), Is.EqualTo("excavation"));
        Assert.That(editor.FindAll("#cardCost0, #cardDiscard0, #cardDrawOnce0"), Is.Empty);
        await SaveSimulation(editor);
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(card.EffectPlan));

        await editor.Find(".simulation-toggle").ClickAsync(new());
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.False);
        Assert.That(editor.Instance.Card.Excavation?.Count, Is.EqualTo(6));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        await SaveSimulation(editor);
        Assert.That(editor.Instance.Card.Excavation, Is.Null);
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(1));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(card.Id));

        await editor.Find(".simulation-restore").ClickAsync(new());
        Assert.That(editor.Instance.Card.Excavation, Is.EqualTo(card.Excavation));
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.False);
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await SaveSimulation(editor);
        Assert.That(editor.Instance.Card.HasEffect, Is.False);
    }

    [Test]
    public async Task FixedExcavationPriorityIsNotAnEditableDrawOrSearchRank() {
        Card excavation = new([], name: "Excavation", id: "excavation", excavation: new(3));
        Card draw = new([], name: "Draw", id: "draw", drawCount: 1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Cards = [excavation, draw], DrawActivationPriority = new(DrawActivationMode.CustomPriority, [excavation.Id, draw.Id])
        });
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.FindAll("[data-priority-id='excavation']"), Is.Empty);
        Assert.That(cut.FindAll("[data-priority-id='draw']"), Has.Count.EqualTo(1));
    }
}
