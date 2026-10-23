using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorModelNotesTest : CalculatorEditorTestBase {
    [TestCase(35261759)]
    [TestCase(691925)]
    public async Task MatchingSavedBuiltInUsesCorrectNotesWithoutChangingSavedSession(int identity) {
        MaintainedCardEffect current = CardEffectRegistry.Default.Find(identity)!;
        EffectProfile profile = current.Profile with { Summary = "Old summary", Limitations = "Identities are never tracked." };
        Card saved = new Card([], name: current.Name, externalCardId: identity).WithEffect(current.Plan, profile);
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string before = CardJson(codec.SerializeSession(new() { Cards = [saved] }));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [saved] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        string details = SimulationDetails(editor);
        Assert.That(details, Does.Contain(current.Profile.Limitations).And.Not.Contain("never tracked").And.Not.Contain("Saved built-in revision"));
        Assert.That(editor.FindAll(".simulation-restore"), Is.Empty);
        Assert.That(editor.Instance.Card, Is.SameAs(saved));
        OpenSimulation(editor);
        if (identity == 35261759) {
            Assert.That(editor.Markup, Does.Not.Contain("not banished identities"));
        }
        await Button(editor, "Cancel").ClickAsync(new());
        await Button(cut, "Save Session").ClickAsync(new());
        Assert.That(CardJson(SavedSessionJson()), Is.EqualTo(before));
        SessionState loaded = await codec.LoadSessionAsync(SavedSessionJson());
        Assert.That(loaded.Cards.Single().EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(loaded.Cards.Single().EffectProfile, Is.EqualTo(profile));
    }

    [Test]
    public void CurrentPlanWithObsoleteNoteBecomesQuietWithoutReplacingItsProfile() {
        MaintainedCardEffect current = CardEffectRegistry.Default.Find(55144522)!;
        EffectProfile profile = current.Profile with { Limitations = "Obsolete caveat" };
        Card saved = new Card([]).WithEffect(current.Plan, profile);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [saved] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.FindAll(".simulation-details-toggle"), Is.Empty);
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(profile));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(saved.Id));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalRevisionOrDifferentPlanRetainsSavedScopeAndRestoreAction(bool changePlan) {
        MaintainedCardEffect current = CardEffectRegistry.Default.Find(changePlan ? 35261759 : 49238328)!;
        EffectProfile profile = current.Profile with { Revision = changePlan ? current.Profile.Revision : 1, Limitations = "Historical scope" };
        DrawEffectPlan plan = changePlan ? current.Plan with { HiddenDeckCost = 0 } : current.Plan;
        Card saved = new Card([], externalCardId: current.CanonicalCardId).WithEffect(plan, profile);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [saved] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(SimulationDetails(editor), Does.Contain("Historical scope").And.Contain("Saved built-in revision"));
        Assert.That(editor.FindAll(".simulation-restore"), Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(plan));
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(profile));
    }
    private static string CardJson(string json) {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("Cards")[0].GetRawText();
    }

}
