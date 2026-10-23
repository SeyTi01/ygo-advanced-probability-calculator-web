using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorSimulationTest : CalculatorEditorTestBase {
    private static Card BuiltIn(int id) => CardEffectRegistry.Default.RestoreBuiltIn(new([], 3, externalCardId: id));

    [TestCase(38120068, "cardCost")]
    [TestCase(1475311, "cardDiscard")]
    [TestCase(35261759, "cardDeckCost")]
    public async Task SelectiveZeroOperationKeepsOtherEntriesSnapshotsAndRestoresBuiltIn(int identity, string control) {
        Card original = BuiltIn(identity).WithName("Same name");
        Card other = original.Duplicate().WithCopies(1);
        SessionState session = new() {
            Cards = [original, other, new([], 4, id: "target")], HandSize = 2,
            Combos = [new([], cards: [new("target", 1, 2)])]
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        PinnedResultSnapshot pinned = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        OpenSimulation(editor);
        await editor.Find($"#{control}0").ChangeAsync(new() { Value = "0" });
        await editor.Find(".simulation-cancel").ClickAsync(new());
        Assert.That(editor.Instance.Card, Is.SameAs(original));
        OpenSimulation(editor);
        await editor.Find($"#{control}0").ChangeAsync(new() { Value = "0" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Card custom = editor.Instance.Card;
        DrawEffectPlan expected = control switch {
            "cardCost" => original.EffectPlan with { HandCost = 0, HandCostSelector = CardSelector.Any },
            "cardDiscard" => original.EffectPlan with { RemovalCount = 0, RemovalSelector = CardSelector.Any },
            _ => original.EffectPlan with { HiddenDeckCost = 0 }
        };
        Assert.That(custom.EffectPlan, Is.EqualTo(expected));
        Assert.That(custom.Id, Is.EqualTo(original.Id));
        Assert.That(custom.EffectProfile, Is.EqualTo(original.EffectProfile.AsCustom()));
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card, Is.SameAs(other));
        AssertPreviousResult(cut);
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pinned));

        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string share = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        string recovery = context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!.ToString()!;
        await Button(cut, "Save Session").ClickAsync(new());
        foreach (string json in new[] { share, recovery, SavedSessionJson() }) {
            SessionState loaded = await codec.LoadSessionAsync(json);
            Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
            Assert.That(loaded.Cards[0].EffectPlan, Is.EqualTo(expected));
            Assert.That(loaded.Cards[0].EffectProfile, Is.EqualTo(custom.EffectProfile));
            Assert.That(loaded.Cards[1].EffectPlan, Is.EqualTo(other.EffectPlan));
        }
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".result-accuracy").TextContent, Does.StartWith("Mode: Progressive"));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pinned));
        await editor.Find(".simulation-restore").ClickAsync(new());
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(original.EffectPlan));
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(original.EffectProfile));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent.Trim(), Is.EqualTo(pinned.Total.ToString("P2")));
    }

    [TestCase("cardCost"), TestCase("cardDiscard"), TestCase("cardDeckCost")]
    public async Task ZeroingOneOperationPreservesTimingMillAndAllOtherOperations(string control) {
        Card card = BuiltIn(49238328);
        DrawEffectPlan plan = card.EffectPlan with { HandCost = 1, RemovalCount = 1, HiddenDeckCost = 1, HiddenPostDrawMill = 2 };
        card = card.WithEffect(plan, card.EffectProfile.AsCustom());
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        OpenSimulation(editor);
        await editor.Find($"#{control}0").ChangeAsync(new() { Value = "0" });
        await editor.Find(".simulation-save").ClickAsync(new());
        DrawEffectPlan expected = control switch {
            "cardCost" => plan with { HandCost = 0 },
            "cardDiscard" => plan with { RemovalCount = 0 },
            _ => plan with { HiddenDeckCost = 0 }
        };
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(expected));
    }

    [TestCase(55144522)]
    [TestCase(35261759)]
    [TestCase(1475311)]
    [TestCase(93946239)]
    [TestCase(7925734)]
    [TestCase(49238328)]
    [TestCase(24940422)]
    [TestCase(691925)]
    public void BuiltInsAreQuietReadOnlyAndExposeSavedDetailsOnRequest(int id) {
        Card card = BuiltIn(id);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Built-in simulation"));
        Assert.That(editor.FindAll(".simulation-summary"), Is.Empty);
        if (card.EffectProfile.Support == EffectSupport.ModeledWithinRules) {
            Assert.That(editor.FindAll(".simulation-status"), Is.Empty);
            Assert.That(editor.Find(".simulation-source svg").GetAttribute("aria-hidden"), Is.EqualTo("true"));
        }
        else {
            Assert.That(editor.Find(".simulation-heading .simulation-status").TextContent, Is.EqualTo("Partial"));
            Assert.That(editor.FindAll(".simulation-source svg"), Is.Empty);
        }
        Assert.That(editor.FindAll(".card-simulation select, .card-simulation input, .card-simulation p"), Is.Empty);
        Assert.That(editor.FindAll(".simulation-restore"), Is.Empty);
        Assert.That(editor.Find(".simulation-edit").TextContent, Is.EqualTo("Customize simulation"));
        bool performance = DrawEffectPerformance.RequiresDetailedHandState(card.EffectPlan, card.EffectProfile.ExecutionModel);
        if (string.IsNullOrWhiteSpace(card.EffectProfile.Limitations) && !performance) {
            Assert.That(editor.FindAll(".simulation-details-toggle"), Is.Empty);
        }
        else {
            Assert.That(editor.Find(".simulation-details-toggle").GetAttribute("aria-expanded"), Is.EqualTo("false"));
            SimulationDetails(editor);
            Assert.That(editor.FindAll(".simulation-performance"), Has.Count.EqualTo(performance ? 1 : 0));
            if (!string.IsNullOrWhiteSpace(card.EffectProfile.Limitations)) {
                Assert.That(editor.Find(".simulation-limitations").TextContent, Does.Contain(card.EffectProfile.Limitations));
            }
            editor.Find(".simulation-details-toggle").KeyDown(new KeyboardEventArgs { Key = "Escape" });
            Assert.That(editor.FindAll(".simulation-details"), Is.Empty);
        }
        Assert.That(editor.Instance.Card, Is.SameAs(card));
    }

    [Test]
    public void UnconfiguredManualAndUnknownCatalogCardsIdentifyAbsenceAndOfferConfiguration() {
        Card manual = new([], name: "Manual");
        Card unknown = new Card([], name: "Unknown", externalCardId: 123).WithEffect(manual.EffectPlan, new(EffectSource.Custom, 123));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [manual, unknown] });
        foreach (IRenderedComponent<CardEditor> editor in cut.FindComponents<CardEditor>()) {
            Assert.That(editor.Find(".simulation-source").TextContent.Trim(), Is.EqualTo("No built-in simulation"));
            Assert.That(editor.Find(".simulation-source svg").GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(editor.Find(".simulation-edit").TextContent, Is.EqualTo("Configure simulation"));
            Assert.That(editor.FindAll(".simulation-details-toggle, .simulation-status, .simulation-summary"), Is.Empty);
            Assert.That(editor.FindAll(".card-simulation select, .card-simulation input"), Is.Empty);
        }
    }

    [TestCase(null)]
    [TestCase(123)]
    public async Task CustomOnlyEffectKeepsItsSourceThroughModeChangesAndDraftCancel(int? identity) {
        Card card = new Card([], externalCardId: identity).WithDrawCount(2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        foreach (string mode in new[] { "OpeningHand", "Configured" }) {
            await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode });
            Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
            Assert.That(editor.FindAll(".simulation-status, .simulation-details-toggle, .simulation-restore"), Is.Empty);
            OpenSimulation(editor);
            await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
            await editor.Find(".simulation-cancel").ClickAsync(new());
            Assert.That(editor.Instance.Card, Is.SameAs(card));
        }
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
        Assert.That(editor.Find(".simulation-summary").TextContent, Is.EqualTo("No draw effect"));
    }

    [TestCase(35261759)]
    [TestCase(93946239)]
    public async Task ModeChangesPreserveBuiltInSupportAndExactSavedNote(int id) {
        Card card = BuiltIn(id);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        string heading = editor.Find(".simulation-heading").TextContent;
        foreach (string mode in new[] { "OpeningHand", "Configured" }) {
            await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode });
            Assert.That(editor.Find(".simulation-heading").TextContent, Is.EqualTo(heading));
            Assert.That(editor.FindAll(".simulation-details-toggle"), Has.Count.EqualTo(1));
            string details = SimulationDetails(editor).Trim();
            if (!string.IsNullOrWhiteSpace(card.EffectProfile.Limitations)) {
                Assert.That(details, Does.Contain($"Model limits: {card.EffectProfile.Limitations}"));
            }
            Assert.That(editor.FindAll(".simulation-performance"), Has.Count.EqualTo(
                DrawEffectPerformance.RequiresDetailedHandState(card.EffectPlan, card.EffectProfile.ExecutionModel) ? 1 : 0));
            await editor.Find(".simulation-details-toggle").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
            Assert.That(editor.Instance.Card, Is.SameAs(card));
        }
    }

    [Test]
    public async Task SolarCustomDraftSaveRecoveryAndAtomicRestorePreserveMill() {
        Card original = BuiltIn(691925).WithName("Solar Recharge");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [original] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.FindAll(".simulation-mill-note"), Is.Empty);
        OpenSimulation(editor);
        Assert.That(editor.FindAll(".simulation-mill-note"), Has.Count.EqualTo(1));
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        await editor.Find(".simulation-cancel").ClickAsync(new());
        Assert.That(editor.Instance.Card, Is.SameAs(original));
        OpenSimulation(editor);
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = true });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.PostDrawDeckMillCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        await editor.Find("#cardCopies0").InputAsync(new() { Value = "2" });
        Assert.That(editor.Instance.Card.PostDrawDeckMillCount, Is.EqualTo(2));
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        Assert.That((await codec.LoadSessionAsync(SavedSessionJson())).Cards[0].PostDrawDeckMillCount, Is.EqualTo(2));
        string recovery = context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!.ToString()!;
        Assert.That(recovery, Does.Contain("PostDrawDeckMillCount"));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.Instance.Card.PostDrawDeckMillCount, Is.Zero);
        await editor.Find(".simulation-restore").ClickAsync(new());
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(original.EffectPlan));
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(original.EffectProfile));
        Assert.That(editor.Instance.Card.Copies, Is.EqualTo(2));
    }

    [Test]
    public void LegacySnapshotWithoutMaintainedDefinitionKeepsSavedSourceAndDetails() {
        Card card = new([], drawCount: 1, externalCardId: 123);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Saved simulation"));
        Assert.That(editor.FindAll(".simulation-status, .simulation-restore"), Is.Empty);
        SimulationDetails(editor);
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.LegacySnapshot));
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(card.EffectPlan));
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
        Assert.That(editor.FindAll(".simulation-status, .simulation-details-toggle, .simulation-details"), Is.Empty);
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
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
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
        Assert.That(editors[0].Find(".card-simulation").TextContent, Does.Not.Contain("Saved revision summary"));
        Assert.That(SimulationDetails(editors[0]), Does.Contain(older.EffectProfile.Limitations));
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
        Assert.That(editor.FindAll(".simulation-status, .simulation-source svg"), Is.Empty);
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
