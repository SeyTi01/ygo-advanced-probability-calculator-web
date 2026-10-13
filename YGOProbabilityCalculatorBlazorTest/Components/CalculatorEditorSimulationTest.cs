using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorSimulationTest : CalculatorEditorTestBase {
    private static Card BuiltIn(int id) => CardEffectRegistry.Default.RestoreBuiltIn(new([], 3, externalCardId: id));

    [TestCase(55144522)]
    [TestCase(35261759)]
    [TestCase(1475311)]
    [TestCase(93946239)]
    public void BuiltInsAreQuietReadOnlyAndExposeSavedDetailsOnRequest(int id) {
        Card card = BuiltIn(id);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Built-in simulation"));
        Assert.That(editor.Find(".simulation-summary").TextContent, Is.EqualTo(card.EffectProfile.Summary));
        Assert.That(editor.FindAll(".card-simulation select, .card-simulation input, .card-simulation p"), Is.Empty);
        Assert.That(editor.FindAll(".simulation-restore"), Is.Empty);
        Assert.That(editor.Find(".simulation-edit").TextContent, Is.EqualTo("Customize simulation"));
        Assert.That(editor.Find(".simulation-details-toggle").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        if (card.EffectProfile.Support == EffectSupport.Partial) {
            Assert.That(editor.Find(".simulation-heading").TextContent, Does.Contain("Partial model"));
        }
        string details = SimulationDetails(editor);
        Assert.That(details, Does.Contain(card.EffectProfile.Limitations ?? ""));
        editor.Find(".simulation-details-toggle").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".simulation-details"), Is.Empty);
        Assert.That(editor.Instance.Card, Is.SameAs(card));
    }

    [Test]
    public void UnconfiguredManualAndUnknownCatalogCardsOnlyOfferConfiguration() {
        Card manual = new([], name: "Manual");
        Card unknown = new Card([], name: "Unknown", externalCardId: 123).WithEffect(manual.EffectPlan, new(EffectSource.Custom, 123));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [manual, unknown] });
        foreach (IRenderedComponent<CardEditor> editor in cut.FindComponents<CardEditor>()) {
            Assert.That(editor.Find(".card-simulation").TextContent.Trim(), Is.EqualTo("Configure simulation"));
            Assert.That(editor.FindAll(".card-simulation select, .card-simulation input"), Is.Empty);
        }
    }

    [Test]
    public async Task DraftCancelAndNoOpSaveDoNotEditSessionInvalidateResultsOrChangeProvenance() {
        SessionState session = Session();
        session.Cards[0] = BuiltIn(55144522).WithCategories([a]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Card original = editor.Instance.Card;
        string snapshot = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        int recoveryCount = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        Assert.That(editor.Instance.Card, Is.SameAs(original));
        await editor.Find(".simulation-cancel").ClickAsync(new());
        OpenSimulation(editor);
        Assert.That(editor.Find("#cardDraw0").GetAttribute("value"), Is.EqualTo("2"));
        await editor.Find(".simulation-save").ClickAsync(new());
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        await editor.Find(".simulation-save").ClickAsync(new());
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        await editor.Find("#cardDraw0").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".simulation-editor"), Is.Empty);
        Assert.That(editor.Instance.Card, Is.SameAs(original));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.FindComponent<SessionShareButton>().Instance.Snapshot, Is.EqualTo(snapshot));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"].Count, Is.EqualTo(recoveryCount));
    }

    [Test]
    public async Task SaveOverrideMatchingValuesAndClearRequireExplicitRestoreAndPreserveUnrelatedState() {
        Card card = BuiltIn(35261759).WithName("My Desires").WithActive(false).WithCategories([a]);
        SessionState session = new() { Cards = [card], Categories = [a], Combos = [new([], cards: [new(card.Id, 1, 3)])] };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        Assert.That(editor.Instance.Card.PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
        Assert.That(editor.Find(".simulation-summary").TextContent, Does.Contain("Banish 10"));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        Assert.That((await codec.LoadSessionAsync(SavedSessionJson())).Cards[0].EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Find(".simulation-summary").TextContent, Is.EqualTo("No draw effect"));
        await editor.Find(".simulation-restore").ClickAsync(new());
        Card restored = editor.Instance.Card;
        Assert.That(restored.EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(restored.EffectProfile, Is.EqualTo(card.EffectProfile));
        Assert.That((restored.Id, restored.Name, restored.Copies, restored.Active, restored.ExternalCardId),
            Is.EqualTo((card.Id, card.Name, card.Copies, card.Active, card.ExternalCardId)));
        Assert.That(restored.Categories, Is.EqualTo(card.Categories));
        Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Cards[0].CardId, Is.EqualTo(card.Id));
    }

    [Test]
    public async Task SavedRevisionAndLegacySnapshotAreNotUpgradedUntilRestore() {
        Card current = BuiltIn(35261759);
        Card older = current.WithEffect(DrawEffectPlan.Draw(1, false), current.EffectProfile with { Revision = 7, Summary = "Saved revision summary", Limitations = "Saved limitation" });
        Card legacy = current.Duplicate().WithEffect(DrawEffectPlan.Draw(3, false), EffectProfile.Legacy);
        Card removed = new([], drawCount: 2, effectProfile: new(EffectSource.BuiltIn, 123, 8, "Removed definition snapshot", EffectSupport.ModeledWithinRules));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [older, legacy, removed] });
        IRenderedComponent<CardEditor>[] editors = cut.FindComponents<CardEditor>().ToArray();
        Assert.That(editors[0].Find(".simulation-summary").TextContent, Is.EqualTo("Saved revision summary"));
        Assert.That(SimulationDetails(editors[0]), Does.Contain("revision 7").And.Contain("Saved limitation"));
        Assert.That(editors[1].Find(".simulation-source").TextContent, Is.EqualTo("Saved simulation"));
        Assert.That(editors[1].Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.LegacySnapshot));
        Assert.That(editors[2].FindAll(".simulation-restore"), Is.Empty);
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards.Select(card => (card.EffectPlan, card.EffectProfile)),
            Is.EqualTo(new[] { older, legacy, removed }.Select(card => (card.EffectPlan, card.EffectProfile))));
        await editors[0].Find(".simulation-restore").ClickAsync(new());
        await editors[1].Find(".simulation-restore").ClickAsync(new());
        Assert.That(editors[0].Instance.Card.EffectProfile, Is.EqualTo(current.EffectProfile));
        Assert.That(editors[1].Instance.Card.EffectPlan, Is.EqualTo(current.EffectPlan));
    }

    [TestCase("Configured")]
    [TestCase("Simplified")]
    [TestCase("OpeningHand")]
    public async Task UnsupportedModelIsNeverOfferedAsAnEditableGenericDraw(string mode) {
        Card unsupported = BuiltIn(55144522);
        unsupported = unsupported.WithEffect(unsupported.EffectPlan, unsupported.EffectProfile with { ExecutionModel = EffectExecutionModel.Unsupported });
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [unsupported] });
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Find(".accordion-button").TextContent, Does.Contain("Unsupported effect"));
        Assert.That(editor.Find(".card-simulation").TextContent, Does.Contain("cannot run"));
        Assert.That(editor.FindAll(".simulation-edit"), Is.Empty);
        Assert.That(editor.Instance.Card.EffectProfile.ExecutionModel, Is.EqualTo(EffectExecutionModel.Unsupported));
        SimulationDetails(editor);
        await editor.Find(".simulation-restore").ClickAsync(new());
        Assert.That(editor.Instance.Card.EffectProfile.ExecutionModel, Is.EqualTo(EffectExecutionModel.DrawSequenceV1));
    }

    [Test]
    public async Task InvalidHiddenCostBlocksSaveUntilCorrectedEvenAfterOtherEdits() {
        Card card = BuiltIn(35261759);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        await editor.Find("#cardDeckCost0").ChangeAsync(new() { Value = "61" });
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = false });
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        Assert.That(editor.Find("[role='alert']").TextContent, Does.Contain("0 to 60"));
        Assert.That(editor.Instance.Card, Is.SameAs(card));
        await editor.Find("#cardDeckCost0").ChangeAsync(new() { Value = "9" });
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.PreDrawDeckRemovalCount, Is.EqualTo(9));
        Assert.That(editor.Instance.Card.DrawOncePerTurn, Is.False);
    }

    [Test]
    public async Task RemovedDraftCategoryCanBeCorrectedWithoutDiscardingOtherEdits() {
        Card card = new([], drawCount: 2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card], Categories = [a] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        await editor.Find("#cardCost0").ChangeAsync(new() { Value = "1" });
        await editor.Find("#cardCostEligibility0").ChangeAsync(new() { Value = a.Identity });
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Find("[role='alert']").TextContent, Does.Contain("category was removed"));
        Assert.That(editor.Instance.Card, Is.SameAs(card));
        await editor.Find("#cardCost0").ChangeAsync(new() { Value = "0" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(3));
        Assert.That(editor.Instance.Card.PreDrawSelector, Is.EqualTo(CardSelector.Any));
    }

    [Test]
    public async Task DraftSurvivesReorderRenameAndUnrelatedEditsWithoutRestoringOldCardValues() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPreDrawRemovalCount(1).WithPreDrawSelector(CardSelector.For(a));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
        await editor.Find("#cardCopies0").InputAsync(new() { Value = "4" });
        await RowAction(cut, "card First, row 1", "Move down").ClickAsync(new());
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.That(editor.Find("#cardDraw1").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(2));
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(3));
        Assert.That(editor.Instance.Card.Copies, Is.EqualTo(4));
        Assert.That(editor.Instance.Card.PreDrawSelector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed" }));
    }
}
