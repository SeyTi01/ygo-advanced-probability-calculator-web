using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using Bunit;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorDrawEffectsTest : CalculatorEditorTestBase {
    [Test]
    public async Task ExistingControlsMarkBuiltInEffectsCustomAndPersistTheChoice() {
        Card card = YGOProbabilityCalculatorBlazor.Services.CardEffects.CardEffectRegistry.Default.CreateImportedCard(
            new() { Id = 35261759, Name = "Pot of Desires" }, 3, 35261759);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [card] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn));
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        Assert.That(saved.Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(saved.Cards[0].Id, Is.EqualTo(card.Id));
    }

    [Test]
    public async Task CardPresentationTracksEffectiveModeAndKeepsAdvancedSettingsEditable() {
        CategoryBase dark = new("Dark");
        Card advanced = new(
            [dark],
            name: "Allure of Darkness",
            id: "allure",
            drawCount: 2,
            postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.For(dark));
        Card fixedDraw = new([], name: "Graceful Charity", id: "graceful", drawCount: 3);
        Card ordinary = new([], name: "Ordinary card", id: "ordinary");
        SessionState session = new() { Categories = [dark], Cards = [advanced, fixedDraw, ordinary] };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Simplified" });
        IRenderedComponent<CardEditor> allureEditor = cut.FindComponents<CardEditor>()[0];
        IRenderedComponent<CardEditor> gracefulEditor = cut.FindComponents<CardEditor>()[1];
        IRenderedComponent<CardEditor> ordinaryEditor = cut.FindComponents<CardEditor>()[2];

        Assert.That(allureEditor.Find(".accordion-button").TextContent, Does.Contain("Draw 2").And.Not.Contain("Remove 1"));
        Assert.That(allureEditor.Find("#cardDrawModeHelp0").TextContent,
            Does.Contain("Simplified draws ignore both pre-draw costs and post-draw removals"));
        Assert.That(allureEditor.Find("#cardDiscard0").HasAttribute("disabled"), Is.False);
        Assert.That(allureEditor.Find("#cardEligibility0"), Is.Not.Null);
        Assert.That(allureEditor.Find("#cardDraw0").GetAttribute("aria-describedby"),
            Is.EqualTo("cardDrawHelp0 cardDrawModeHelp0"));
        Assert.That(gracefulEditor.Find(".accordion-button").TextContent, Does.Contain("Draw 3").And.Not.Contain("Remove"));
        Assert.That(gracefulEditor.Find("#cardDrawHelp1").TextContent,
            Does.Contain("full draw amount").And.Contain("stays in hand"));
        Assert.That(gracefulEditor.FindAll("#cardDrawModeHelp1"), Is.Empty);
        Assert.That(ordinaryEditor.FindAll(".accordion-button .badge").ToArray(), Is.Empty);

        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Configured" });

        Assert.That(allureEditor.Find(".accordion-button").TextContent, Does.Contain("Draw 2 · Remove 1"));
        Assert.That(allureEditor.FindAll("#cardDrawModeHelp0"), Is.Empty);
        Assert.That(gracefulEditor.Find(".accordion-button").TextContent, Does.Contain("Draw 3").And.Not.Contain("Remove"));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });

        string inactiveHeader = allureEditor.Find(".accordion-button").TextContent;
        Assert.That(inactiveHeader, Does.Contain("Draw configured · Off").And.Not.Contain("Draw 2").And.Not.Contain("Remove 1"));
        Assert.That(allureEditor.Find("#cardDrawModeHelp0").TextContent,
            Does.Contain("Draw simulation is off"));
        Assert.That(allureEditor.Find("#cardDrawHelp0").TextContent,
            Does.Contain("Stored draw settings are retained").And.Not.Contain("resolves automatically").And.Not.Contain("outcome fails"));
        Assert.That(allureEditor.Find("#cardDiscard0").HasAttribute("disabled"), Is.False);
        Assert.That(allureEditor.Find("#cardEligibility0"), Is.Not.Null);
        Assert.That((allureEditor.Instance.Card.DrawCount,
            allureEditor.Instance.Card.DrawOncePerTurn,
            allureEditor.Instance.Card.PostDrawDiscardCount,
            allureEditor.Instance.Card.PostDrawSelector),
            Is.EqualTo(((int?)2, true, 1, CardSelector.For(dark))));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Configured" });
        Assert.That(allureEditor.Find(".accordion-button").TextContent, Does.Contain("Draw 2 · Remove 1"));
        Assert.That(allureEditor.FindAll("#cardDrawModeHelp0"), Is.Empty);
    }

    [Test]
    public async Task DrawSettingPreservesDraftsIdentityAndSessionAndInvalidatesResults() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        string id = editor.Instance.Card.Id;
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        AssertPreviousResult(cut);
        Assert.That(editor.Find("#cardDraw0").GetAttribute("aria-describedby"), Is.EqualTo("cardDrawHelp0"));
        Assert.That(editor.Find("#cardDrawHelp0").TextContent, Does.Contain("Separate entries resolve independently"));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].DrawCount, Is.EqualTo(2));
        await RowAction(cut, "card First, row 1", "Move down").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardDraw1").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#cardCategory1").GetAttribute("value"), Is.EqualTo("user:B"));
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        Assert.That(editor.Instance.Card.DrawCount, Is.Null);
        Assert.That(editor.Instance.Card.Active, Is.True);
        Assert.That(editor.Instance.Card.Copies, Is.EqualTo(2));
    }

    [Test]
    public async Task OncePerTurnTogglePreservesDraftAndIdentityAndInvalidatesResults() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        string id = editor.Instance.Card.Id;
        Assert.That(editor.Find("#cardDrawOnce0").HasAttribute("checked"), Is.True);
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = false });
        Assert.That(editor.Instance.Card.DrawOncePerTurn, Is.False);
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(2));
        Assert.That(editor.Find("#cardDrawHelp0").TextContent, Does.Contain("Every copy drawn"));
        AssertPreviousResult(cut);
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].DrawOncePerTurn, Is.False);
        await RowAction(cut, "card First, row 1", "Move down").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardCategory1").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(editor.Find("#cardDrawOnce1").HasAttribute("checked"), Is.False);
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        Assert.That(editor.FindAll("[role='switch']"), Is.Empty);
        Assert.That(editor.Instance.Card.Active, Is.True);
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Find("#cardDrawOnce1").HasAttribute("checked"), Is.False);
        await editor.Find("#cardDrawOnce1").ChangeAsync(new() { Value = true });
        Assert.That(editor.Instance.Card.DrawOncePerTurn, Is.True);
        Assert.That(editor.Find("#cardDrawHelp1").TextContent, Does.Contain("later copies stay in hand"));
    }

    [Test]
    public async Task InvalidSelectionDoesNotReplaceSavedSetting() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "4" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(1));
        Assert.That(editor.Markup, Does.Contain("Choose None, Draw 1, Draw 2, or Draw 3"));
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(3));
        Assert.That(editor.FindAll(".alert"), Is.Empty);
    }

    [Test]
    public async Task GlobalDrawEffectsToggleUsesOrdinaryCalculationAndRestoresConfiguredBehavior() {

        CategoryBase target = new("Target");
        Card draw = new([], id: "draw", drawCount: 1, drawOncePerTurn: false);
        SessionState session = new() {
            Cards = [draw, new([target], name: "Target", id: "target"), new([], 2, "Blank", id: "blank")],
            Combos = [new([], "Open target", cards: [new("target", 1, 1)])],
            HandSize = 1
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        DrawSequenceOracle.Counts drawExpected = DrawSequenceOracle.Enumerate(session.Cards, 1, session.Combos, []);
        double configuredExpected = (double)drawExpected.Wins[0] / (double)drawExpected.Total;
        List<Card> ordinaryCards = session.Cards.Select(card => card.WithDrawCount(null)).ToList();
        double ordinaryExpected = SmallDeckOracle.EnumerateProbability(ordinaryCards, session.Combos, 1);

        Assert.That(cut.FindAll(".calculation-settings-trigger").ToArray(), Has.Length.EqualTo(1));
        Assert.That(cut.FindAll(".draw-effects-control, .draw-effects-help").ToArray(), Is.Empty);
        IElement trigger = cut.Find(".calculation-settings-trigger");
        Assert.That(trigger.GetAttribute("aria-label"), Is.EqualTo("Calculation settings"));
        Assert.That(trigger.GetAttribute("title"), Is.EqualTo("Calculation settings"));
        Assert.That(trigger.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);

        await trigger.ClickAsync(new());
        IElement toggle = cut.Find("#calculationModel");
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.False);
        Assert.That(toggle.TagName, Is.EqualTo("SELECT"));
        Assert.That(toggle.GetAttribute("value"), Is.EqualTo("Configured"));
        Assert.That(cut.Find("label[for='calculationModel']").TextContent, Is.EqualTo("Calculation model"));
        AssertDrawEffectsHelpIsStable(false);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(configuredExpected.ToString("P2")));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        AssertPreviousResult(cut);
        AssertDrawEffectsHelpIsStable(true);
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Assert.That((editor.Instance.Card.DrawCount, editor.Instance.Card.DrawOncePerTurn), Is.EqualTo(((int?)1, false)));
        Assert.That(editor.Find("#cardDraw0").HasAttribute("disabled"), Is.False);
        Assert.That(editor.Find("#cardDrawOnce0").HasAttribute("disabled"), Is.False);

        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#calculationSettingsPanel").HasAttribute("hidden"), Is.True);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(ordinaryExpected.ToString("P2")));
        Assert.That((editor.Instance.Card.DrawCount, editor.Instance.Card.DrawOncePerTurn), Is.EqualTo(((int?)1, false)));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Configured" });
        AssertPreviousResult(cut);
        AssertDrawEffectsHelpIsStable(false);
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured"));
        Assert.That((cut.FindComponent<CardEditor>().Instance.Card.DrawCount, cut.FindComponent<CardEditor>().Instance.Card.DrawOncePerTurn),
            Is.EqualTo(((int?)1, false)));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(configuredExpected.ToString("P2")));

        void AssertDrawEffectsHelpIsStable(bool ignored) {
            Assert.That(cut.Find("#calculationModel").GetAttribute("aria-describedby"), Is.EqualTo("drawEffectsHelp"));
            Assert.That(cut.Find("#drawEffectsHelp").TextContent, Does.Contain(ignored ? "Draws are ignored" : "Applies supported draws"));
        }
    }

    [Test]
    public async Task SessionLoadRestoresToggleWhileImportsKeepTheCurrentMode() {
        Card configured = new([], 2, "Pot of Greed", id: "pot", externalCardId: 55144522, drawCount: 2, drawOncePerTurn: false);
        SessionState session = new() {
            Cards = [configured, new([], 3, "Other", id: "other")],
            Combos = [new([], "Route", cards: [new("pot", 1, 3)])],
            HandSize = 1,
            DrawEffectsEnabled = false
        };
        Mock<IDeckImportService> importer = new();
        Card importedPot = new([], 3, "Pot of Greed", id: "imported-pot", externalCardId: 55144522, drawCount: 2, drawOncePerTurn: false);
        importer.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync([importedPot]);
        context.Services.AddSingleton<IDeckImportService>(importer.Object);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
        string shared = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        SessionState sharedSession = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(shared);
        Assert.That(sharedSession.DrawEffectsEnabled, Is.False);
        Assert.That((sharedSession.Cards[0].DrawCount, sharedSession.Cards[0].DrawOncePerTurn), Is.EqualTo(((int?)2, false)));

        await Button(cut, "Save Session").ClickAsync(new());
        string saved = SavedSessionJson();
        SessionState savedSession = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(saved);
        Assert.That(savedSession.DrawEffectsEnabled, Is.False);
        Assert.That((savedSession.Cards[0].DrawCount, savedSession.Cards[0].DrawOncePerTurn), Is.EqualTo(((int?)2, false)));

        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n55144522", "deck.ydk"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.DrawCount, Is.EqualTo(2));
            Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
        });

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(saved, "disabled.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("OpeningHand"));
            Assert.That(cut.FindComponent<CardEditor>().Instance.Card.DrawCount, Is.EqualTo(2));
        });

        System.Text.Json.Nodes.JsonObject missingToggle = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        missingToggle.Remove(nameof(SessionState.DrawEffectsEnabled));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(missingToggle.ToJsonString(), "legacy.json"));
        cut.WaitForAssertion(() => Assert.That(cut.Find("#calculationModel").GetAttribute("value"), Is.EqualTo("Configured")));
    }

    [Test]
    public async Task PostDrawEditsPersistPreserveDraftsAndClearWithDrawNone() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(3);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        string id = editor.Instance.Card.Id;
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardDiscard0").ChangeAsync(new() { Value = "2" });
        Assert.That(editor.Instance.Card.PostDrawDiscardCount, Is.EqualTo(2));
        Assert.That(editor.Find(".accordion-button").TextContent, Does.Contain("Draw 3").And.Contain("Remove 2"));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        AssertPreviousResult(cut);
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].PostDrawDiscardCount, Is.EqualTo(2));
        await RowAction(cut, "card First, row 1", "Move down").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardDiscard1").GetAttribute("value"), Is.EqualTo("2"));
        await editor.Find("#cardDiscard1").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Instance.Card.PostDrawDiscardCount, Is.EqualTo(2));
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        Assert.That(editor.Instance.Card.PostDrawDiscardCount, Is.Zero);
        Assert.That(editor.FindAll("#cardDiscard1"), Is.Empty);
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Find("#cardDiscard1").GetAttribute("value"), Is.EqualTo("0"));
    }
}
