using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorTest {
    private TestContext context = null!;
    private readonly CategoryBase a = new("A");
    private readonly CategoryBase b = new("B");

    [SetUp]
    public void SetUp() {
        context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IBackgroundCalculator, BackgroundCalculatorTestAdapter>();
        context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo>(StringComparer.Ordinal));
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<ICardArtworkService, CardArtworkService>();
        context.Services.AddSingleton<ILegacyCardMetadataEnricher, LegacyCardMetadataEnricher>();
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    [Test]
    public async Task ArtworkRequiresExplicitIntentAndKeepsResultsDraftsActiveStateAndSessionUnchanged() {
        var artwork = new Mock<ICardArtworkService>();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        context.Services.AddSingleton(artwork.Object);
        var session = Session();
        var original = session.Cards[0];
        session.Cards[0] = new Card(original.Categories, original.Copies, original.Name, false, original.Id, 1234);
        var cut = Render(session);
        var editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(cut.FindAll("img"), Is.Empty);
        artwork.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        await editor.Find(".accordion-button").ClickAsync(new());
        artwork.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(cut, "Calculate").ClickAsync(new());
        var results = cut.Find(".probability-results").OuterHtml;
        await Button(cut, "Save Session").ClickAsync(new());
        var before = SavedSessionJson();
        await editor.Find("[aria-label='Show artwork for First']").ClickAsync(new());
        Assert.That(editor.Find("img").GetAttribute("src"), Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/1234.jpg"));
        Assert.That(editor.Find("img").GetAttribute("alt"), Does.Contain("First"));
        Assert.That(cut.Find(".probability-results").OuterHtml, Is.EqualTo(results));
        Assert.That(editor.Instance.Card.Active, Is.False);
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo(b.Identity));
        await Button(cut, "Save Session").ClickAsync(new());
        Assert.That(SavedSessionJson(1), Is.EqualTo(before));
        Assert.That(before, Does.Not.Contain("image").And.Not.Contain("Artwork").And.Not.Contain("blob:"));
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("img"), Is.Empty, "CSS-hidden bodies must not retain image elements");
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll("img"), Has.Count.EqualTo(1));
        artwork.Verify(x => x.GetArtworkUrlAsync(1234), Times.Once);
    }

    [Test]
    public async Task ArtworkFollowsStableEditorThroughReorderingAndDisappearsOnDeletion() {
        var artwork = new Mock<ICardArtworkService>();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        context.Services.AddSingleton(artwork.Object);
        var session = Session();
        var card = session.Cards[0];
        session.Cards[0] = new Card(card.Categories, card.Copies, card.Name, card.Active, card.Id, 1234);
        var cut = Render(session);
        var editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("[aria-label='Show artwork for First']").ClickAsync(new());
        await editor.Find("[aria-label='Move card First, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance, Is.SameAs(editor.Instance));
        Assert.That(editor.Find("#cardCategory1").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("img").GetAttribute("src"), Does.EndWith("/small/1234.jpg"));
        await editor.Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(cut.FindAll("img"), Is.Empty);
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.Name, Is.EqualTo("Second"));
        artwork.Verify(x => x.GetArtworkUrlAsync(1234), Times.Once);
    }

    [Test]
    public async Task ReplacedRowIgnoresLateArtworkResolution() {
        var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var artwork = new Mock<ICardArtworkService>();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).Returns(response.Task);
        artwork.Setup(x => x.GetArtworkUrlAsync(5678)).ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/5678.jpg");
        context.Services.AddSingleton(artwork.Object);
        var cut = context.RenderComponent<CardArtwork>(p => p.Add(x => x.CardId, "old")
            .Add(x => x.ExternalCardId, 1234).Add(x => x.Name, "Old").Add(x => x.Visible, true));
        var opening = cut.Find("button").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Loading artwork")));
        cut.SetParametersAndRender(p => p.Add(x => x.CardId, "new").Add(x => x.ExternalCardId, 5678).Add(x => x.Name, "New"));
        response.SetResult(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        await opening;
        Assert.That(cut.FindAll("img"), Is.Empty);
        await cut.Find("button").ClickAsync(new());
        Assert.That(cut.Find("img").GetAttribute("src"), Does.EndWith("/small/5678.jpg"));
        Assert.That(cut.Markup, Does.Not.Contain("1234.jpg"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovedOrSessionReplacedEditorIgnoresLateArtwork(bool replaceSession) {
        var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var artwork = new Mock<ICardArtworkService>();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).Returns(response.Task);
        artwork.Setup(x => x.GetArtworkUrlAsync(5678)).ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/5678.jpg");
        context.Services.AddSingleton(artwork.Object);
        var session = Session();
        var card = session.Cards[0];
        session.Cards[0] = new Card(card.Categories, card.Copies, card.Name, card.Active, card.Id, 1234);
        var cut = Render(session);
        var editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find(".accordion-button").ClickAsync(new());
        var opening = editor.Find("[aria-label='Show artwork for First']").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Loading artwork")));
        if (replaceSession) {
            const string replacement = """
                {"SchemaVersion":2,"Categories":[],"Cards":[{"Categories":[],"Copies":1,"Name":"Replacement",
                 "Id":"replacement","ExternalCardId":5678}],"Combos":[],"HandSize":5}
                """;
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(replacement, "replacement.json"));
            cut.WaitForAssertion(() => Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Replacement")));
        }
        else await editor.Find("[aria-label='Remove card']").ClickAsync(new());
        response.SetResult(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        await opening;
        Assert.That(cut.FindAll("img"), Is.Empty);
        if (replaceSession) {
            var replacementEditor = cut.FindComponent<CardEditor>();
            await replacementEditor.Find(".accordion-button").ClickAsync(new());
            await replacementEditor.Find("[aria-label='Show artwork for Replacement']").ClickAsync(new());
            Assert.That(replacementEditor.Find("img").GetAttribute("src"), Does.EndWith("/small/5678.jpg"));
        }
        Assert.That(cut.Markup, Does.Not.Contain("1234.jpg"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingAndFailedArtworkRemainSmallAndDoNotRetryOnRenders(bool decodingFailure) {
        var artwork = new Mock<ICardArtworkService>();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).ReturnsAsync(decodingFailure
            ? CardArtworkService.ArtworkOrigin + "/small/1234.jpg" : null);
        context.Services.AddSingleton(artwork.Object);
        var cut = context.RenderComponent<CardArtwork>(p => p.Add(x => x.CardId, "card")
            .Add(x => x.ExternalCardId, 1234).Add(x => x.Name, "Name").Add(x => x.Visible, true));
        await cut.Find("button").ClickAsync(new());
        if (decodingFailure) await cut.Find("img").TriggerEventAsync("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());
        Assert.That(cut.FindAll("img"), Is.Empty);
        Assert.That(cut.FindAll(".card-artwork-preview"), Is.Empty);
        Assert.That(cut.Find("[role='status']").TextContent, Is.EqualTo("Artwork unavailable"));
        await cut.Find("button").ClickAsync(new());
        await cut.Find("button").ClickAsync(new());
        cut.SetParametersAndRender(p => p.Add(x => x.Name, "Renamed"));
        artwork.Verify(x => x.GetArtworkUrlAsync(1234), Times.Once);
    }

    [Test]
    public void ManualCardsHaveNoArtworkLookupOrControl() {
        var artwork = new Mock<ICardArtworkService>();
        context.Services.AddSingleton(artwork.Object);
        var cut = Render(Session());
        Assert.That(cut.FindAll(".card-artwork-disclosure"), Is.Empty);
        artwork.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task SaveSessionButtonInvokesSessionServiceWithSuggestedJsonName() {
        var codec = new SessionService(context.JSInterop.JSRuntime, new JsonSerializer());
        var sessionService = new Mock<ISessionService>();
        sessionService.Setup(service => service.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        sessionService.Setup(service => service.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        sessionService.Setup(service => service.SaveSessionAsync(It.IsAny<SessionState>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        context.Services.AddSingleton<ISessionService>(sessionService.Object);

        var cut = Render();
        await Button(cut, "Save Session").ClickAsync(new());

        sessionService.Verify(service => service.SaveSessionAsync(
            It.IsAny<SessionState>(),
            It.Is<string>(name => name.StartsWith("calculator_session_", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))),
            Times.Once);
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task SaveSessionButtonShowsTheExistingErrorWhenFileWriteFails() {
        var codec = new SessionService(context.JSInterop.JSRuntime, new JsonSerializer());
        var sessionService = new Mock<ISessionService>();
        sessionService.Setup(service => service.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        sessionService.Setup(service => service.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        sessionService.Setup(service => service.SaveSessionAsync(It.IsAny<SessionState>(), It.IsAny<string>()))
            .ThrowsAsync(new JSException("Session file write failed."));
        context.Services.AddSingleton<ISessionService>(sessionService.Object);

        var cut = Render();
        await Button(cut, "Save Session").ClickAsync(new());

        Assert.That(cut.Find("[role='alert']").TextContent,
            Does.Contain("Failed to save session: Session file write failed."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NewRequirementsDefaultToAnyAndRemainDynamicAfterHandSizeChange(bool directCard) {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        var cut = Render(new SessionState { Categories = session.Categories, Cards = session.Cards, Combos = session.Combos, HandSize = 5 });
        var editor = cut.FindComponent<ComboEditor>();
        if (directCard) await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        AssertAnyMaximumDraft(editor);
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Find(".accordion-button").TextContent, Does.Contain("(1 Min)"));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain("(1 Min)"));
        var combo = editor.Instance.Combo;
        Assert.That(directCard ? combo.Cards[0].MaximumMode : combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(directCard ? combo.Cards[0].MaxCount : combo.Categories[0].MaxCount, Is.EqualTo(6));
        Assert.That(directCard ? combo.Cards[0].GetEffectiveMaximum(6) : combo.Categories[0].GetEffectiveMaximum(6), Is.EqualTo(6));
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Update").ClickAsync(new());
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain("(2 Min)"));
    }

    [TestCase(false, 0, 5, RequirementMaximumMode.HandSize, "Any")]
    [TestCase(false, 1, 5, RequirementMaximumMode.HandSize, "1 Min")]
    [TestCase(false, 2, 5, RequirementMaximumMode.HandSize, "2 Min")]
    [TestCase(false, 0, 0, RequirementMaximumMode.Fixed, "None")]
    [TestCase(false, 1, 1, RequirementMaximumMode.Fixed, "1")]
    [TestCase(false, 0, 1, RequirementMaximumMode.Fixed, "1 Max")]
    [TestCase(false, 1, 3, RequirementMaximumMode.Fixed, "1–3")]
    [TestCase(true, 0, 5, RequirementMaximumMode.HandSize, "Any")]
    [TestCase(true, 1, 5, RequirementMaximumMode.HandSize, "1 Min")]
    [TestCase(true, 2, 5, RequirementMaximumMode.HandSize, "2 Min")]
    [TestCase(true, 0, 0, RequirementMaximumMode.Fixed, "None")]
    [TestCase(true, 1, 1, RequirementMaximumMode.Fixed, "1")]
    [TestCase(true, 0, 1, RequirementMaximumMode.Fixed, "1 Max")]
    [TestCase(true, 1, 3, RequirementMaximumMode.Fixed, "1–3")]
    public async Task RequirementBadgesSummarizeStoredRangeSemantics(
        bool directCard, int minimum, int maximum, RequirementMaximumMode mode, string expectedLabel) {
        var card = new Card([a], 2, "Twin");
        var combo = directCard
            ? new Combo([], "Display", cards: [new(card.Id, minimum, maximum, mode)])
            : new Combo([new(a, minimum, maximum, mode)], "Display");
        var cut = Render(new SessionState { Categories = [a], Cards = [card], Combos = [combo], HandSize = 5 });
        var editor = cut.FindComponent<ComboEditor>();
        var expectedBadgeText = directCard ? $"Card: Twin ({expectedLabel})" : $"A ({expectedLabel})";
        var accordionButton = editor.Find(".accordion-button");

        if (accordionButton.GetAttribute("aria-expanded") != "true")
            await accordionButton.ClickAsync(new());
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));

        await accordionButton.ClickAsync(new());
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(editor.Find(".combo-header-content .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));

        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(editor.Find(".combo-header-content .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));
        await accordionButton.ClickAsync(new());
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent.Trim(), Is.EqualTo(expectedBadgeText));
    }

    [TestCase(false, 1, 5, "1–5")]
    [TestCase(true, 1, 5, "1–5")]
    [TestCase(false, 0, 0, "None")]
    [TestCase(true, 0, 0, "None")]
    public async Task FixedRequirementsAndUncommittedDraftsSurviveHandSizeChange(
        bool directCard, int minimum, int maximum, string expectedLabel) {
        var session = Session();
        var cut = Render(new SessionState { Categories = session.Categories, Cards = session.Cards, Combos = [new([])], HandSize = 5 });
        var editor = cut.FindComponent<ComboEditor>();
        if (directCard) await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        var selector = directCard ? "#comboCard0" : "#comboCategory0";
        var value = directCard ? session.Cards[0].Id : a.Identity;
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#minCount0").InputAsync(new() { Value = minimum.ToString() });
        await editor.Find("#maxCount0").InputAsync(new() { Value = maximum.ToString() });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        await Button(editor, "Add").ClickAsync(new());
        await cut.Find("#handSize").ChangeAsync(new() { Value = "5" });
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        Assert.That(editor.FindAll("#maxAny0"), Is.Empty);
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo(maximum.ToString()));
        Assert.That(editor.Find(".accordion-body .badge").TextContent, Does.Contain($"({expectedLabel})"));
        var combo = editor.Instance.Combo;
        Assert.That(directCard ? combo.Cards[0].MaximumMode : combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(directCard ? combo.Cards[0].GetEffectiveMaximum(6) : combo.Categories[0].GetEffectiveMaximum(6), Is.EqualTo(maximum));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MaximumDraftAloneSwitchesBetweenFixedAndDynamicModes(bool directCard) {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        var cut = Render(new SessionState { Categories = session.Categories, Cards = session.Cards, Combos = session.Combos, HandSize = 5 });
        var editor = cut.FindComponent<ComboEditor>();
        if (directCard) await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        var selector = directCard ? "#comboCard0" : "#comboCategory0";
        var value = directCard ? session.Cards[0].Id : a.Identity;
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);

        await editor.Find("#maxCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaxCount : editor.Instance.Combo.Categories[0].MaxCount,
            Is.EqualTo(2));

        await editor.Find(selector).ChangeAsync(new() { Value = value });
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("placeholder"), Is.EqualTo("Any"));
        await editor.Find("#maxCount0").InputAsync(new() { Value = string.Empty });
        await editor.Find("#minCount0").InputAsync(new() { Value = "7" });
        await Button(editor, "Update").ClickAsync(new());

        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MaximumMode : editor.Instance.Combo.Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].MinCount : editor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(7));
        Assert.That(directCard ? editor.Instance.Combo.Cards[0].GetEffectiveMaximum(6) : editor.Instance.Combo.Categories[0].GetEffectiveMaximum(6),
            Is.EqualTo(6));
        Assert.That(editor.Find(".accordion-body .category-tag").TextContent, Does.Contain("(7 Min)"));
        await editor.Find(selector).ChangeAsync(new() { Value = value });
        AssertAnyMaximumDraft(editor);
        Assert.That(editor.FindAll("[role='alert']"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidNonEmptyMaximumDoesNotBecomeAny(bool directCard) {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([]));
        var cut = Render(new SessionState { Categories = session.Categories, Cards = session.Cards, Combos = session.Combos, HandSize = 5 });
        var editor = cut.FindComponent<ComboEditor>();
        if (directCard) await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find(directCard ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() {
            Value = directCard ? session.Cards[0].Id : a.Identity
        });
        AssertAnyMaximumDraft(editor);
        await editor.Find("#maxCount0").InputAsync(new() { Value = "invalid" });
        await Button(editor, "Add").ClickAsync(new());

        Assert.That(editor.Find("[role='alert']").TextContent, Does.Contain("Maximum count"));
        Assert.That(editor.Instance.Combo.Categories, Is.Empty);
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
    }

    [Test]
    public async Task DuplicationRenameAndSessionSaveLoadPreserveBothMaximumModes() {
        var session = Session();
        var source = new Combo([new(a, 1, 5, RequirementMaximumMode.HandSize), new(b, 0, 0)], "Modes",
            cards: [new(session.Cards[0].Id, 1, 5, RequirementMaximumMode.HandSize), new(session.Cards[1].Id, 1, 5)]);
        var cut = Render(new SessionState { Categories = session.Categories, Cards = session.Cards, Combos = [source], HandSize = 5 });
        await DuplicateComboAsync(cut, 0);
        var editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[1].Instance.Combo.Categories.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
        Assert.That(editors[1].Instance.Combo.Cards.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        foreach (var editor in editors) {
            Assert.That(editor.Instance.Combo.Categories[0].BaseCategory.Name, Is.EqualTo("Renamed"));
            Assert.That(editor.Instance.Combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        }
        await Button(cut, "Save Session").ClickAsync(new());
        var json = SavedSessionJson();
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "modes.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2)));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        foreach (var editor in cut.FindComponents<ComboEditor>()) {
            Assert.That(editor.Instance.Combo.Categories.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
            Assert.That(editor.Instance.Combo.Cards.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
            Assert.That(editor.Instance.Combo.Categories.Select(c => c.GetEffectiveMaximum(6)), Is.EqualTo(new[] { 6, 0 }));
            Assert.That(editor.Instance.Combo.Cards.Select(c => c.GetEffectiveMaximum(6)), Is.EqualTo(new[] { 6, 5 }));
            await editor.Find("#constraintKind" + editor.Instance.Index).ChangeAsync(new() { Value = "Card" });
            await editor.Find("#comboCard" + editor.Instance.Index).ChangeAsync(new() { Value = session.Cards[1].Id });
            Assert.That(editor.FindAll("[id^='maxAny']"), Is.Empty);
            Assert.That(editor.Find("#maxCount" + editor.Instance.Index).GetAttribute("value"), Is.EqualTo("5"));
        }
    }

    private IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState? session = null) {
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    // Synchronous bUnit events discard their dispatcher task; calculation tests await events
    // before asserting or editing again, and retain pending calculation tasks until release.
    private static IElement Button(IRenderedFragment fragment, string text) =>
        fragment.FindAll("button").Single(element => element.TextContent.Trim() == text);

    private static void AssertAnyMaximumDraft(IRenderedFragment fragment, int index = 0) {
        var maximum = fragment.Find($"#maxCount{index}");
        Assert.That(maximum.GetAttribute("value"), Is.EqualTo(string.Empty));
        Assert.That(maximum.GetAttribute("placeholder"), Is.EqualTo("Any"));
        Assert.That(maximum.GetAttribute("title"), Is.EqualTo("Leave empty for Any"));
        Assert.That(maximum.HasAttribute("disabled"), Is.False);
        Assert.That(fragment.FindAll($"#maxAny{index}"), Is.Empty);
    }

    private static async Task DuplicateComboAsync(IRenderedFragment fragment, int index) {
        var editor = fragment.FindComponents<ComboEditor>()[index];
        if (editor.Find(".accordion-button").GetAttribute("aria-expanded") != "true")
            await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("button[aria-label^='Duplicate combo ']").ClickAsync(new());
    }

    private static void AssertPreviousResult(IRenderedFragment fragment) {
        Assert.That(fragment.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(fragment.Find(".probability-result-status .visually-hidden").TextContent.Trim(),
            Is.EqualTo("Previous result · inputs changed"));
    }

    private string SavedSessionJson(int saveNumber = 0) {
        var invocation = context.JSInterop.Invocations
            .Where(invocation => invocation.Arguments.Count == 2 &&
                invocation.Arguments[0] is string fileName && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                invocation.Arguments[1] is string)
            .ElementAt(saveNumber);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
    }

    private static async Task RenameGroupWithEnter(IRenderedFragment fragment, string oldName, string newName) {
        await fragment.Find($"[aria-label='Edit group {oldName}']").ClickAsync(new());
        var input = fragment.Find($"[aria-label='New name for group {oldName}']");
        await input.InputAsync(new() { Value = newName });
        await input.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }

    private SessionState Session() => new() {
        Categories = [a, b], Cards = [new([a], 2, "First"), new([b], 2, "Second")],
        Combos = [new([new(a, 1, 2)], "First combo"), new([new(b, 1, 2)], "Second combo")], HandSize = 2
    };

    [TestCase(false)]
    [TestCase(true)]
    public async Task ManualAndHelpPendingLegacySessionsAwaitEnrichmentAndSaveProperties(bool fromHelp) {
        const string legacy = """
            {"Categories":[{"Name":"Monster"}],
             "Cards":[{"Id":"ash","Name":"Ash Blossom & Joyous Spring","Copies":2,"Active":true,"Categories":[{"Name":"Monster"}]},
                      {"Id":"custom","Name":"My Custom Card","Copies":1,"Active":true,"Categories":[]}],
             "Combos":[{"Name":"Existing route","Categories":[{"BaseCategory":{"Name":"Monster"},"MinCount":1,"MaxCount":1}]}],"HandSize":1}
            """;
        var release = new TaskCompletionSource<IReadOnlyDictionary<string, CardInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).Returns(release.Task);
        context.Services.AddSingleton(cardInfo.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut;
        Task? upload = null;
        if (fromHelp) {
            var files = new Mock<IFileService>();
            files.Setup(service => service.ReadAllTextAsync("sample-data/example_session_state.json")).ReturnsAsync(legacy);
            context.Services.AddSingleton(files.Object);
            var help = context.RenderComponent<YGOProbabilityCalculatorBlazor.Pages.Help>();
            await Button(help, "Try It with Example Data").ClickAsync(new());
            Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Not.Null);
            cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        }
        else {
            cut = Render();
            var file = new Mock<IBrowserFile>();
            file.Setup(file => file.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy)));
            upload = cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange
                .InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        }
        cut.WaitForAssertion(() => cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()), Times.Once));
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty, "Restoration waits for the asynchronous best-effort step.");
        await cut.InvokeAsync(() => release.SetResult(new Dictionary<string, CardInfo>(StringComparer.Ordinal) {
            ["Ash Blossom & Joyous Spring"] = new() { Id = 14558127, Name = "Ash Blossom & Joyous Spring", Type = "Tuner Monster", Attribute = "FIRE", Race = "Zombie", Level = 3 }
        }));
        if (upload is not null) await upload;
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2)));
        cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { "Ash Blossom & Joyous Spring", "My Custom Card" }))), Times.Once);
        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToList();
        Assert.That(cards[0].ExternalCardId, Is.EqualTo(14558127));
        Assert.That(cards[1].ExternalCardId, Is.Null);
        Assert.That(cards[1].Categories, Is.Empty);
        Assert.That(cards.Select(card => card.Id), Is.EqualTo(new[] { "ash", "custom" }));
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip").Select(chip => chip.TextContent.Trim()), Is.EqualTo(new[] { "Monster" }));
        var combo = cut.FindComponent<ComboEditor>();
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(option => option.TextContent), Does.Contain("Tuner Monster"));
        Assert.That(combo.Instance.Combo.Categories.Single().BaseCategory.Source, Is.EqualTo(CategorySource.User));
        var expected = SmallDeckOracleTest.EnumerateProbability(cards, [combo.Instance.Combo], 1);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var saved = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using var document = System.Text.Json.JsonDocument.Parse(saved);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("Cards")[0].GetProperty("ExternalCardId").GetInt32(), Is.EqualTo(14558127));
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new HttpRequestException("Offline"));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(saved, "saved.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".probability-results"), Is.Empty));
        cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { "My Custom Card" }))), Times.Once);
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.ExternalCardId, Is.EqualTo(14558127));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
    }

    [Test]
    public async Task BundledHelpExampleTransfersSelfContainedPropertiesWithoutMetadataLookup() {
        var source = await File.ReadAllTextAsync(Path.Combine(NUnit.Framework.TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        var files = new Mock<IFileService>();
        files.Setup(service => service.ReadAllTextAsync("sample-data/example_session_state.json")).ReturnsAsync(source);
        var offline = new Mock<ICardInfoService>(MockBehavior.Strict);
        context.Services.AddSingleton(files.Object);
        context.Services.AddSingleton(offline.Object);
        var help = context.RenderComponent<YGOProbabilityCalculatorBlazor.Pages.Help>();
        await Button(help, "Try It with Example Data").ClickAsync(new());
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(25)));
        Assert.That(cut.FindComponent<ComboEditor>().FindAll("optgroup[label='Card properties'] option").Select(option => option.TextContent),
            Does.Contain("Monster").And.Contain("Quick-Play Spell"));
        Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Null);
        offline.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CardPropertiesAreHiddenAndSameLabelRequirementsCanBeEditedIndependently() {
        var user = new CategoryBase("Spell");
        var spell = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var quick = new CategoryBase("Quick-Play Spell", CategorySource.Metadata, "spell-type:quick-play");
        var orphan = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var cut = Render(new SessionState {
            Categories = [user, b], Cards = [new([spell, quick], 2, "Imported", externalCardId: 123)],
            Combos = [new([new(orphan, 0, 0)]), new([])], HandSize = 1,
            CategoryColorIndices = new() { ["Spell"] = 5, ["B"] = 2 }
        });
        var categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(2));
        var card = cut.FindComponent<CardEditor>();
        Assert.That(card.FindAll(".accordion-button .category-tag"), Is.Empty);
        var propertyInspector = card.Find("details.card-property-inspector");
        Assert.That(propertyInspector.HasAttribute("open"), Is.False);
        Assert.That(propertyInspector.QuerySelector("summary")!.TextContent.Trim(), Is.EqualTo("Card properties (2)"));
        Assert.That(propertyInspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EquivalentTo(new[] { spell.Name, quick.Name }));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Not.Contain(spell.Name).And.Not.Contain(quick.Name));
        Assert.That(card.FindAll("optgroup[label='User categories'] option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { "user:Spell", "user:B" }));
        Assert.That(card.FindAll("optgroup[label='Card properties'] option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { orphan.Identity }));
        var combo = cut.FindComponents<ComboEditor>()[1];
        Assert.That(combo.FindAll("optgroup").Select(g => g.GetAttribute("label")), Is.EqualTo(new[] { "User categories", "Card properties" }));
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.TextContent),
            Is.EqualTo(new[] { "Monster", "Quick-Play Spell", "Spell" }));
        Assert.That(combo.FindAll("option").Where(o => o.TextContent == "Spell").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { "user:Spell", "metadata:kind:spell" }));

        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await Button(combo, "Add").ClickAsync(new());
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = user.Identity });
        await Button(combo, "Add").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Source),
            Is.EqualTo(new[] { CategorySource.Metadata, CategorySource.User }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await combo.Find("#minCount1").InputAsync(new() { Value = "0" });
        await combo.Find("#maxCount1").InputAsync(new() { Value = "0" });
        await Button(combo, "Update").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories[0].MaxCount, Is.Zero);
        Assert.That(combo.Instance.Combo.Categories[1].MinCount, Is.EqualTo(1));

        // A metadata membership does not prevent adding/removing a same-label user membership.
        await card.Find("select").ChangeAsync(new() { Value = user.Identity });
        await Button(card, "Add").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Has.Count.EqualTo(3));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await categories.Find("[aria-label='Edit category Spell']").ClickAsync(new());
        await categories.Find("[aria-label='New name for category Spell']").InputAsync(new() { Value = "Role" });
        await categories.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(combo.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo(spell.Identity));
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Name), Is.EqualTo(new[] { "Spell", "Role" }));
        Assert.That(card.Instance.Card.Categories[0], Is.EqualTo(spell));
        Assert.That(card.Instance.Card.Categories[2].Name, Is.EqualTo("Role"));
        await card.Find("[aria-label='Remove category Role from card Imported']").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Is.EqualTo(new[] { spell, quick }));
        Assert.That(combo.FindAll(".accordion-body .card-property-tag"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .card-property-tag").ClassList, Does.Contain("card-property-color-spell"));
        Assert.That(combo.FindAll(".accordion-body [class*='category-color-']"), Has.Count.EqualTo(1));

        await combo.Find("[aria-label='Remove category Role from combo Combo 2']").ClickAsync(new());
        await categories.Find("[aria-label='Remove category Role']").ClickAsync(new());
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(combo.Instance.Combo.Categories.Single().BaseCategory, Is.EqualTo(spell));
        await cut.Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { orphan.Identity, spell.Identity }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        Assert.That(Button(combo, "Update"), Is.Not.Null);
    }

    [Test]
    public void CategoryEditorDoesNotExposeMetadataDefinitionsEvenIfSupplied() {
        var property = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var cut = context.RenderComponent<CategoryListEditor>(p => p.Add(x => x.CategoryBases, [a, property])
            .Add(x => x.Cards, []).Add(x => x.Combos, []));
        Assert.That(cut.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Edit category Spell']"), Is.Empty);
    }

    [Test]
    public void CollapsedCardHeaderShowsManualPropertiesButNotDetectedPropertiesAndDeduplicatesThem() {
        var user = new CategoryBase("VS Monster");
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var card = new Card([user, fire, fire, monster], name: "Reinforcement of the Army",
            manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var manualOnly = new Card([fire, fire], name: "Manual only",
            manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var cut = Render(new SessionState { Categories = [user], Cards = [card, manualOnly] });

        var header = cut.FindComponents<CardEditor>()[0].Find(".accordion-button");
        Assert.That(header.QuerySelectorAll(".category-tag").Select(badge => badge.TextContent.Trim()),
            Is.EqualTo(new[] { user.Name, fire.Name }));
        Assert.That(header.QuerySelectorAll(".manual-property-header-badge").Length, Is.EqualTo(1));
        Assert.That(header.QuerySelectorAll(".category-tag").Select(badge => badge.TextContent.Trim()), Does.Not.Contain(monster.Name));
        Assert.That(cut.FindComponents<CardEditor>()[0].Find("details.card-property-inspector summary").TextContent.Trim(),
            Is.EqualTo("Card properties (2)"));

        var manualHeader = cut.FindComponents<CardEditor>()[1].Find(".accordion-button");
        Assert.That(manualHeader.QuerySelectorAll(".category-tag").Length, Is.EqualTo(1));
        Assert.That(manualHeader.QuerySelector(".card-header-name")?.TextContent.Trim(), Is.EqualTo("Manual only (1)"));
        Assert.That(manualHeader.QuerySelector(".manual-property-header-badge")?.TextContent.Trim(), Is.EqualTo(fire.Name));
    }

    [Test]
    public void ResponsiveEntryHeadersKeepActionsOutsideTheHeadingAndDoNotToggleTheAccordion() {
        var cut = Render(new SessionState {
            Categories = [a],
            Cards = [new([a], 3, "Starter")],
            Combos = [new([new ComboCategory(a, 1, 5)], "Route")]
        });

        foreach (var kind in new[] { "card", "combo" }) {
            var header = cut.Find($".{kind}-editor .entry-editor-header");
            Assert.That(header.QuerySelectorAll("h2 button").Length, Is.EqualTo(1));
            Assert.That(header.QuerySelectorAll(".entry-actions button").Length, Is.EqualTo(3));
            Assert.That(header.QuerySelectorAll("input[type=checkbox]").Length, Is.EqualTo(1));
            var toggle = header.QuerySelector("input[type=checkbox]")!;
            Assert.That(header.QuerySelector("label")?.GetAttribute("for"), Is.EqualTo(toggle.Id));
            toggle.Change(false);

            header = cut.Find($".{kind}-editor .entry-editor-header");
            Assert.That(header.QuerySelector(".accordion-button")?.GetAttribute("aria-expanded"), Is.EqualTo("false"));
            Assert.That(header.QuerySelector(".accordion-button")?.TextContent, Does.Contain("Inactive"));
            header.QuerySelector(".accordion-button")!.Click();
            Assert.That(cut.Find($".{kind}-editor .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        }
    }

    [Test]
    public void SessionFileActionsExposeOneKeyboardAccessibleInputEach() {
        var cut = Render();
        foreach (var (id, name, extension) in new[] {
            ("fileInput", "Import Deck", ".ydk"),
            ("sessionFileInput", "Load Session", ".json")
        }) {
            var inputs = cut.FindAll($"input#{id}");
            Assert.That(inputs, Has.Count.EqualTo(1));
            var input = inputs[0];
            Assert.That(input.GetAttribute("aria-label"), Is.EqualTo(name));
            Assert.That(input.GetAttribute("accept"), Is.EqualTo(extension));
            Assert.That(input.GetAttribute("tabindex"), Is.Not.EqualTo("-1"));
            Assert.That(input.ClassList, Does.Not.Contain("d-none"));
            Assert.That(input.ParentElement?.TagName, Is.EqualTo("LABEL"));
            Assert.That(input.ParentElement?.GetAttribute("for"), Is.EqualTo(id));
        }
    }

    [Test]
    public async Task CardPropertyInspectorGroupsProvenanceAndRemovesOnlyManualProperties() {
        var user = new CategoryBase("VS Monster");
        var detected = new[] {
            new CategoryBase("Monster", CategorySource.Metadata, "kind:monster"),
            new CategoryBase("Effect Monster", CategorySource.Metadata, "kind:effect-monster"),
            new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire"),
            new CategoryBase("Monster Type: Warrior", CategorySource.Metadata, "race:warrior"),
            new CategoryBase("Level 4", CategorySource.Metadata, "level:4")
        };
        var manual = new CategoryBase("Archetype: Vanquish Soul", CategorySource.Metadata, "archetype:vanquish-soul");
        var card = new Card([user, .. detected, manual], name: "Vanquish Soul Razen",
            manualMetadataCategoryKeys: [manual.MetadataKey!]);
        var cut = Render(new SessionState { Categories = [user], Cards = [card] });
        var editor = cut.FindComponent<CardEditor>();
        var inspector = editor.Find("details.card-property-inspector");

        Assert.That(inspector.HasAttribute("open"), Is.False);
        Assert.That(inspector.QuerySelector("summary")!.TextContent.Trim(), Is.EqualTo("Card properties (6)"));
        Assert.That(inspector.QuerySelector(".detected-card-properties .form-text")!.TextContent.Trim(), Is.EqualTo("Detected from card data"));
        Assert.That(inspector.QuerySelector(".manual-card-properties .form-text")!.TextContent.Trim(), Is.EqualTo("Added manually"));
        Assert.That(inspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EquivalentTo(detected.Select(property => property.Name)));
        Assert.That(inspector.QuerySelectorAll(".detected-card-properties button.btn-close"), Is.Empty);
        Assert.That(inspector.QuerySelectorAll(".manual-card-property").Select(property => property.TextContent.Trim()),
            Is.EqualTo(new[] { manual.Name }));
        Assert.That(inspector.QuerySelector(".manual-card-property button.btn-close")!.GetAttribute("aria-label"),
            Is.EqualTo($"Remove manually added card property {manual.Name} from card {card.Name}"));

        await inspector.QuerySelector(".manual-card-property button.btn-close")!.ClickAsync(new());

        Assert.That(editor.Instance.Card.Categories, Does.Not.Contain(manual));
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (5)"));
        Assert.That(editor.FindAll(".manual-card-properties"), Is.Empty);
        Assert.That(editor.FindAll(".manual-property-header-badge"), Is.Empty);
        Assert.That(editor.FindAll(".detected-card-properties button.btn-close"), Is.Empty);
        Assert.That(editor.Find("[aria-label='Remove category VS Monster from card Vanquish Soul Razen']"), Is.Not.Null,
            "user-category editing remains available");
    }

    [Test]
    public void ObjectiveMetadataReconciliationMovesAnOverlappingManualPropertyToDetected() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var card = new Card([], name: "Reinforcement of the Army")
            .WithManualMetadataCategory(fire)
            .WithObjectiveMetadata([fire], 123);
        var cut = Render(new SessionState { Cards = [card] });
        var editor = cut.FindComponent<CardEditor>();
        var inspector = editor.Find("details.card-property-inspector");

        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(inspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EqualTo(new[] { fire.Name }));
        Assert.That(inspector.QuerySelectorAll(".manual-card-property"), Is.Empty);
        Assert.That(inspector.QuerySelectorAll("button.btn-close"), Is.Empty);
    }

    [Test]
    public async Task ManualPropertyUpdatesPreserveTheCardEditorAndItsSelectionDraftAcrossReplacement() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var level = new CategoryBase("Level 5", CategorySource.Metadata, "level:5");
        var originalCard = new Card([], name: "Custom");
        var cut = Render(new SessionState { Cards = [originalCard],
            Combos = [new([new(fire, 1, 1), new(level, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponent<CardEditor>();
        var originalEditor = editor.Instance;

        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());

        editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Instance, Is.SameAs(originalEditor));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(originalCard.Id));
        Assert.That(editor.Find(".accordion-button .manual-property-header-badge").TextContent.Trim(), Is.EqualTo(fire.Name));
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (1)"));

        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = level.Identity });
        await editor.Find("#cardName0").InputAsync(new() { Value = "Renamed" });

        editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Instance, Is.SameAs(originalEditor));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo(level.Identity));
        Assert.That(editor.Find(".accordion-button .manual-property-header-badge").TextContent.Trim(), Is.EqualTo(fire.Name));
        await Button(editor, "Add").ClickAsync(new());

        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys,
            Is.EquivalentTo(new[] { fire.MetadataKey, level.MetadataKey }));
        Assert.That(editor.FindAll(".accordion-button .manual-property-header-badge").Select(badge => badge.TextContent.Trim()),
            Is.EquivalentTo(new[] { fire.Name, level.Name }));
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (2)"));
    }

    [Test]
    public async Task ManualCardPropertyWorkflowPreservesUserIdentityResultsAndOfflineSession() {
        var offline = new Mock<ICardInfoService>();
        offline.Setup(s => s.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new HttpRequestException("Offline"));
        context.Services.AddSingleton(offline.Object);
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var user = new CategoryBase("Attribute: FIRE");
        var objective = new Card([fire], 2, "Objective", externalCardId: 123);
        var custom = new Card([], 2, "Custom");
        var cut = Render(new SessionState { Categories = [user], Cards = [objective, custom],
            Combos = [new([new(fire, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponents<CardEditor>()[1];
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll("optgroup").Select(g => g.GetAttribute("label")), Is.EqualTo(new[] { "User categories", "Card properties" }));
        Assert.That(editor.FindAll("option").Where(o => o.TextContent == fire.Name).Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { user.Identity, fire.Identity }));
        await Button(cut, "Calculate").ClickAsync(new());
        var before = cut.Find(".probability-total-value").TextContent;
        await editor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.Categories, Is.EqualTo(new[] { fire }));
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { fire.MetadataKey }));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        Assert.That(editor.FindAll("optgroup[label='Card properties'] option"), Is.Empty);
        Assert.That(editor.Find(".manual-card-properties").TextContent, Does.Contain("Added manually").And.Contain(fire.Name));
        Assert.That(editor.FindAll(".accordion-button .category-tag").Select(badge => badge.TextContent.Trim()),
            Is.EqualTo(new[] { fire.Name }));
        Assert.That(editor.Find(".manual-property-header-badge").ClassList,
            Does.Contain("card-property-tag").And.Contain("card-property-color-attribute-fire"));
        // A stale/forged selection cannot add another membership or change provenance.
        await editor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Card.Categories, Has.Count.EqualTo(1));
        await editor.Find("select").ChangeAsync(new() { Value = user.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Card.Categories, Is.EqualTo(new[] { fire, user }));
        Assert.That(editor.FindAll(".accordion-button .category-tag"), Has.Count.EqualTo(2));
        await editor.Find("[aria-label='Remove category Attribute: FIRE from card Custom']").ClickAsync(new());
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.Not.EqualTo(before));
        await editor.Find("#cardName1").InputAsync(new() { Value = "Renamed" });
        await editor.Find("#cardCopies1").InputAsync(new() { Value = "3" });
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll(".accordion-button .manual-property-header-badge"), Has.Count.EqualTo(1));
        await Button(cut, "Save Session").ClickAsync(new());
        var saved = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            (string)context.JSInterop.Invocations["saveSessionFile"].Single().Arguments[1]!));
        var file = new Mock<IBrowserFile>();
        file.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(saved)));
        await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { fire.MetadataKey }));
        Assert.That((editor.Instance.Card.Id, editor.Instance.Card.Copies, editor.Instance.Card.Name), Is.EqualTo((custom.Id, 3, "Renamed")));
        var objectiveEditor = cut.FindComponents<CardEditor>()[0];
        Assert.That(objectiveEditor.FindAll(".manual-card-properties, .accordion-body button.btn-close"), Is.Empty);
        Assert.That(objectiveEditor.FindAll(".detected-card-property"), Has.Count.EqualTo(1));
        Assert.That(objectiveEditor.FindAll("optgroup[label='Card properties'] option"), Is.Empty);
        await objectiveEditor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(objectiveEditor, "Add").ClickAsync(new());
        Assert.That(objectiveEditor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(objectiveEditor.Instance.Card.Categories, Is.EqualTo(new[] { fire }));
        await Button(cut, "Calculate").ClickAsync(new());
        await editor.Find("[aria-label='Remove manually added card property Attribute: FIRE from card Renamed']").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(editor.Instance.Card.Categories, Is.Empty);
        Assert.That(editor.FindAll("details.card-property-inspector"), Is.Empty);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(
            SmallDeckOracleTest.EnumerateProbability(cut.FindComponents<CardEditor>().Select(e => e.Instance.Card).ToList(),
                cut.FindComponents<ComboEditor>().Select(e => e.Instance.Combo).ToList(), 1).ToString("P2")));
    }

    [Test]
    public async Task ManualCardCanAddPropertyRetainedOnlyByComboAndKeepItThroughReordering() {
        var property = new CategoryBase("Level 5", CategorySource.Metadata, "level:5");
        var cut = Render(new SessionState { Cards = [new([], name: "Custom"), new([], name: "Other")],
            Combos = [new([new(property, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(editor.FindAll("optgroup[label='Card properties'] option").Select(o => o.TextContent), Is.EqualTo(new[] { property.Name }));
        await editor.Find("select").ChangeAsync(new() { Value = property.Identity });
        await editor.Find("[aria-label='Move card Custom, row 1 down']").ClickAsync(new());
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { property.MetadataKey }));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Categories, Is.Empty);
    }

    [Test]
    public async Task ReorderControlsMoveAllFourListsAndKeepSessionOrderAndReferences() {
        var c = new CategoryBase("C");
        var cards = new List<Card> { new([a], 1, "Same"), new([b], 1, "Same"), new([c], 1, "") };
        var groups = new List<ComboGroup> { new("g1", "One"), new("g2", "Two"), new("g3", "Three") };
        var combos = new List<Combo> {
            new([new(a, 1, 1)], "Same", groupId: "g1", cards: [new(cards[0].Id, 1, 1)]),
            new([new(b, 1, 1)], "Same", groupId: "g2"),
            new([new(c, 1, 1)], "", groupId: "g3")
        };
        var cut = Render(new SessionState { Categories = [a, b, c], Cards = cards,
            Combos = combos, ComboGroups = groups, HandSize = 1 });

        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.FindComponent<ComboListEditor>().FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.Find("[aria-label='Move card Same, row 1 up']").TextContent, Is.EqualTo("▲"));
        Assert.That(cut.Find("[aria-label='Move combo Same, row 1 up']").TextContent, Is.EqualTo("▲"));
        Assert.That(cut.Find("[aria-label='Move card Same, row 1 up']").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[aria-label='Move combo Combo 3, row 3 down']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category A left']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit group One']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move group One left']").HasAttribute("disabled"), Is.True);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var totalBeforeMove = cut.Find(".probability-total-value").TextContent;
        Assert.That(cut.Find("[aria-label='Move category A left']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Move category A right']").ClickAsync(new());
        AssertPreviousResult(cut);
        await cut.Find("[aria-label='Move category A right']").ClickAsync(new());
        await cut.Find("[aria-label='Edit category C']").ClickAsync(new());
        await cut.Find("[aria-label='Move category C left']").ClickAsync(new());
        await cut.Find("[aria-label='Move group One right']").ClickAsync(new());
        await cut.Find("[aria-label='Move group One right']").ClickAsync(new());
        await cut.Find("[aria-label='Edit group Three']").ClickAsync(new());
        await cut.Find("[aria-label='Move group Three left']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category C right']").TextContent, Is.EqualTo("▶"));
        Assert.That(cut.Find("[aria-label='Move group Three left']").TextContent, Is.EqualTo("◀"));
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move category A right']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Edit group One']").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Move group One right']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Move card Same, row 1 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move card Same, row 2 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move card Card 2, row 2 up']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Same, row 1 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Same, row 2 down']").ClickAsync(new());
        await cut.Find("[aria-label='Move combo Combo 2, row 2 up']").ClickAsync(new());

        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(x => x.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponents<CardEditor>().Select(x => x.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(x => x.Instance.Combo),
            Is.EqualTo(new[] { combos[2], combos[1], combos[0] }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(x => x.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.Cards.Single().CardId, Is.EqualTo(cards[0].Id));
        Assert.That(cut.FindComponents<ComboEditor>()[2].Instance.Combo.GroupId, Is.EqualTo("g1"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(totalBeforeMove));
        Assert.That(cut.FindAll(".probability-group .combo-probability-name").Select(x => x.TextContent.Trim().Split(' ')[0]),
            Is.EqualTo(new[] { "Three", "Two", "One" }));
        Assert.That(cut.FindAll(".combo-probability-item .combo-probability-name")[0].TextContent,
            Does.Contain("Unnamed combo 1"));

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(json)) {
            var root = document.RootElement;
            Assert.That(root.GetProperty("Categories").EnumerateArray().Select(x => x.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "C", "B", "A" }));
            Assert.That(root.GetProperty("Cards").EnumerateArray().Select(x => x.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
            Assert.That(root.GetProperty("Combos").EnumerateArray().Select(x => x.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "", "Same", "Same" }));
            Assert.That(root.GetProperty("ComboGroups").EnumerateArray().Select(x => x.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { "g3", "g2", "g1" }));
        }
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "ordered.json"));
        Assert.That(cut.FindComponents<CardEditor>().Select(x => x.Instance.Card.Id),
            Is.EqualTo(new[] { cards[2].Id, cards[1].Id, cards[0].Id }));
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(x => x.Name),
            Is.EqualTo(new[] { "C", "B", "A" }));
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(x => x.Id),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
        Assert.That(cut.FindComponents<ComboEditor>().Select(x => x.Instance.Combo.GroupId),
            Is.EqualTo(new[] { "g3", "g2", "g1" }));
    }

    [Test]
    public void MovingExpandedEditorsKeepsDraftAndTargetsMovedItem() {
        var session = Session();
        session.Cards[1] = session.Cards[1].WithActive(false);
        session.Combos[1] = session.Combos[1].WithActive(false);
        var cut = Render(session);
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<CardEditor>()[0].Find("#cardCategory0").Change("user:B");
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<ComboEditor>()[0].Find("#comboCategory0").Change("user:B");
        cut.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");

        cut.Find("[aria-label='Move card First, row 1 down']").Click();
        cut.Find("[aria-label='Move combo First combo, row 1 down']").Click();
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.Name, Is.EqualTo("First"));
        Assert.That(cut.FindComponents<CardEditor>()[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(cut.FindComponents<CardEditor>()[1].Find("#cardCategory1").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("First combo"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Find("#minCount1").GetAttribute("value"), Is.EqualTo("0"));

        cut.FindComponents<CardEditor>()[1].Find("#cardName1").Input("Moved card");
        cut.FindComponents<ComboEditor>()[1].Find("#comboName1").Input("Moved combo");
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.Name, Is.EqualTo("Moved card"));
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Moved combo"));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Active, Is.False);
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Active, Is.False);
        cut.FindComponents<CardEditor>()[1].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[1].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.Name, Is.EqualTo("Second"));
        Assert.That(cut.FindComponents<ComboEditor>().Single().Instance.Combo.Name, Is.EqualTo("Second combo"));
    }

    [Test]
    public void ChipMovesKeepRenameDraftsAndDropdownOrder() {
        var session = new SessionState {
            Categories = [a, b], Cards = [new([a], 2, "First")],
            Combos = [new([new(a, 1, 2)], "First combo")],
            ComboGroups = [new("g1", "One"), new("g2", "Two")], HandSize = 2
        };
        var cut = Render(session);
        cut.Find("[aria-label='Edit category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed");
        var categoryChip = cut.FindAll(".category-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for category A']") is not null);
        Assert.That(categoryChip.QuerySelectorAll("button").Select(x => x.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save category name", "Move category A left", "Move category A right", "Exit category edit mode", "Choose color for category A", "Remove category A" }));
        cut.Find("[aria-label='Move category A right']").Click();
        Assert.That(cut.Find("[aria-label='New name for category A']").GetAttribute("value"), Is.EqualTo("Renamed"));
        cut.Find("[aria-label='Save category name']").Click();
        cut.Find("[aria-label='Edit group One']").Click();
        cut.Find("[aria-label='New name for group One']").Input("Updated");
        var groupChip = cut.FindAll(".combo-group-chip").Single(chip =>
            chip.QuerySelector("[aria-label='New name for group One']") is not null);
        Assert.That(groupChip.QuerySelectorAll("button").Select(x => x.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Save group name", "Move group One left", "Move group One right", "Exit group edit mode", "Remove group One" }));
        cut.Find("[aria-label='Move group One right']").Click();
        Assert.That(cut.Find("[aria-label='New name for group One']").GetAttribute("value"), Is.EqualTo("Updated"));
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.FindAll(".category-chip .reorder-controls"), Is.Empty);
        Assert.That(cut.FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
        cut.FindComponents<CardEditor>()[0].Find(".accordion-button").Click();
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").Click();
        Assert.That(cut.Find("#cardCategory0").QuerySelectorAll("option").Skip(1).Select(x => x.TextContent.Trim()),
            Is.EqualTo(new[] { "B", "Renamed" }));
        Assert.That(cut.Find("#comboGroup0").QuerySelectorAll("option").Skip(1).Select(x => x.TextContent.Trim()),
            Is.EqualTo(new[] { "Two", "Updated" }));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Categories.Single().Name, Is.EqualTo("Renamed"));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Categories.Single().BaseCategory.Name, Is.EqualTo("Renamed"));

        cut.Find("[aria-label='Edit category B']").Click();
        cut.Find("[aria-label='New name for category B']").Input("Discarded");
        cut.Find("[aria-label='Move category B right']").Click();
        cut.Find("[aria-label='New name for category B']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip").Select(chip =>
            chip.QuerySelector(".category-name-trigger")?.TextContent.Trim()), Is.EqualTo(new[] { "Renamed", "B" }));
        Assert.That(cut.FindAll(".category-chip .reorder-controls"), Is.Empty);

        cut.Find("[aria-label='Edit group Two']").Click();
        cut.Find("[aria-label='New name for group Two']").Input("Discarded");
        cut.Find("[aria-label='Move group Two right']").Click();
        cut.Find("[aria-label='Exit group edit mode']").Click();
        Assert.That(cut.FindAll(".combo-group-chip").Select(chip =>
            chip.QuerySelector(".category-name-trigger")?.TextContent.Trim()), Is.EqualTo(new[] { "Updated", "Two" }));
        Assert.That(cut.FindAll(".combo-group-chip .reorder-controls"), Is.Empty);
    }

    [Test]
    public async Task DirectCardSelectorIsAlphabeticalWithoutChangingDeckOrCategoryOrder() {
        var zetaCategory = new CategoryBase("Zeta");
        var alphaCategory = new CategoryBase("Alpha");
        var cards = new List<Card> {
            new([], name: "zebra", id: "zebra"),
            new([], name: "alpha", id: "alpha-lower"),
            new([], name: "Alpha", id: "alpha-upper"),
            new([], name: "beta", id: "beta-first"),
            new([], name: "Beta", id: "beta-case"),
            new([], name: "beta", id: "beta-second"),
            new([], name: null, id: "unnamed-first"),
            new([], name: "  ", id: "unnamed-second")
        };
        var deckOrder = cards.Select(card => card.Id).ToArray();
        var combo = new Combo([], "Direct", cards: [new("missing-card", 1, 1)]);
        var cut = Render(new SessionState {
            Categories = [zetaCategory, alphaCategory], Cards = cards, Combos = [combo], HandSize = 1
        });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.Find("[role='status']").TextContent, Does.Contain("Missing card reference"));
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });

        var selector = editor.Find("#comboCard0");
        Assert.That(selector.QuerySelectorAll("option").Select(option => option.TextContent.Trim()), Is.EqualTo(new[] {
            "Select card…", "alpha #2", "Alpha #3", "beta #4", "Beta #5", "beta #6", "zebra #1",
            "Unnamed card #7", "Unnamed card #8"
        }));
        Assert.That(selector.QuerySelectorAll("option").Select(option => option.GetAttribute("value")), Is.EqualTo(new[] {
            "", "alpha-lower", "alpha-upper", "beta-first", "beta-case", "beta-second", "zebra",
            "unnamed-first", "unnamed-second"
        }));
        Assert.That(selector.QuerySelectorAll("option").Any(option => option.GetAttribute("value") == "missing-card"), Is.False);
        Assert.That(cut.FindComponents<CardEditor>().Select(cardEditor => cardEditor.Instance.Card.Id), Is.EqualTo(deckOrder));

        await selector.ChangeAsync(new() { Value = "beta-case" });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        Assert.That(editor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo("beta-case"));

        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Category" });
        Assert.That(editor.Find("#comboCategory0").QuerySelectorAll("option").Skip(1).Select(option => option.TextContent.Trim()),
            Is.EqualTo(new[] { "Zeta", "Alpha" }), "category selectors retain configured category order");
        Assert.That(cut.FindComponents<CardEditor>().Select(cardEditor => cardEditor.Instance.Card.Id), Is.EqualTo(deckOrder));
    }

    [Test]
    public async Task DeckSortIsOneShotStableAndPreservesCardAndComboDrafts() {
        var draftCategory = new CategoryBase("Draft Category");
        var otherCategory = new CategoryBase("Other Category");
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var zebra = new Card([draftCategory, fire], 3, "Zebra", false, "zebra", 123, [fire.MetadataKey!]);
        var betaFirst = new Card([], 2, "beta", id: "beta-first");
        var betaCase = new Card([], 1, "Beta", id: "beta-case");
        var alphaLower = new Card([], 2, "alpha", id: "alpha-lower");
        var betaSecond = new Card([], 1, "beta", id: "beta-second");
        var alphaUpper = new Card([], 2, "Alpha", id: "alpha-upper");
        var unnamedFirst = new Card([], 1, null, id: "unnamed-first");
        var unnamedSecond = new Card([], 1, "  ", id: "unnamed-second");
        var cards = new List<Card> { zebra, betaFirst, betaCase, alphaLower, betaSecond, alphaUpper, unnamedFirst, unnamedSecond };
        var originalState = cards.ToDictionary(card => card.Id, card => (
            card.Name,
            card.Copies,
            card.Active,
            card.ExternalCardId,
            CategoryIdentities: card.Categories.Select(category => category.Identity).ToArray(),
            ManualKeys: card.ManualMetadataCategoryKeys.ToArray()));
        var combo = new Combo([], "Direct", cards: [new(zebra.Id, 0, 0)]);
        var cut = Render(new SessionState {
            Categories = [draftCategory, otherCategory], Cards = cards, Combos = [combo], HandSize = 1
        });
        var deckEditor = cut.FindComponent<CardListEditor>();
        Assert.That(deckEditor.FindAll("button").Count(button => button.TextContent.Trim() == "Sort A–Z"), Is.EqualTo(1));
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll("button").Any(button => button.TextContent.Trim() == "Sort A–Z"), Is.False);
        Assert.That(cut.FindComponent<ComboListEditor>().FindAll("button").Any(button => button.TextContent.Trim() == "Sort A–Z"), Is.False);
        var sortButton = deckEditor.Find("button[aria-label='Sort deck A–Z']");
        Assert.That(sortButton.GetAttribute("title"), Is.EqualTo("Sort deck A–Z"));

        var zebraEditor = cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == zebra.Id);
        var originalZebraEditor = zebraEditor.Instance;
        await zebraEditor.Find(".accordion-button").ClickAsync(new());
        await zebraEditor.Find("#cardCategory0").ChangeAsync(new() { Value = otherCategory.Identity });

        var comboEditor = cut.FindComponent<ComboEditor>();
        var originalComboEditor = comboEditor.Instance;
        await comboEditor.Find(".accordion-button").ClickAsync(new());
        await comboEditor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await comboEditor.Find("#comboCard0").ChangeAsync(new() { Value = alphaLower.Id });
        await comboEditor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await comboEditor.Find("#maxCount0").InputAsync(new() { Value = "3" });

        await sortButton.ClickAsync(new());
        var sortedEditors = cut.FindComponents<CardEditor>();
        Assert.That(sortedEditors.Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] {
            alphaLower.Id, alphaUpper.Id, betaFirst.Id, betaCase.Id, betaSecond.Id, zebra.Id,
            unnamedFirst.Id, unnamedSecond.Id
        }));
        foreach (var card in cards)
            Assert.That(sortedEditors.Single(editor => editor.Instance.Card.Id == card.Id).Instance.Card, Is.SameAs(card));
        zebraEditor = sortedEditors.Single(editor => editor.Instance.Card.Id == zebra.Id);
        Assert.That(zebraEditor.Instance, Is.SameAs(originalZebraEditor));
        Assert.That(zebraEditor.Instance.ActiveCardIndex, Is.EqualTo(5));
        Assert.That(zebraEditor.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(zebraEditor.Find("#cardCategory5").GetAttribute("value"), Is.EqualTo(otherCategory.Identity));
        Assert.That(zebraEditor.Find("#cardName5").GetAttribute("value"), Is.EqualTo("Zebra"));
        Assert.That(zebraEditor.Find("#cardCopies5").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(zebraEditor.Instance.Card, Is.SameAs(zebra));
        Assert.That((zebra.Copies, zebra.Name, zebra.Active, zebra.ExternalCardId), Is.EqualTo((3, "Zebra", false, (int?)123)));
        Assert.That(zebra.Categories.Select(category => category.Identity), Is.EqualTo(originalState[zebra.Id].CategoryIdentities));
        Assert.That(zebra.ManualMetadataCategoryKeys, Is.EquivalentTo(originalState[zebra.Id].ManualKeys));
        foreach (var editor in sortedEditors) {
            var card = editor.Instance.Card;
            var before = originalState[card.Id];
            Assert.That((card.Name, card.Copies, card.Active, card.ExternalCardId),
                Is.EqualTo((before.Name, before.Copies, before.Active, before.ExternalCardId)));
            Assert.That(card.Categories.Select(category => category.Identity), Is.EqualTo(before.CategoryIdentities));
            Assert.That(card.ManualMetadataCategoryKeys, Is.EquivalentTo(before.ManualKeys));
        }

        Assert.That(comboEditor.Instance, Is.SameAs(originalComboEditor));
        Assert.That(comboEditor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo(alphaLower.Id));
        Assert.That(comboEditor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(comboEditor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(comboEditor.Instance.Combo.Cards.Single().CardId, Is.EqualTo(zebra.Id));

        await cut.Find("[aria-label='Move card alpha, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] {
            alphaUpper.Id, alphaLower.Id, betaFirst.Id, betaCase.Id, betaSecond.Id, zebra.Id,
            unnamedFirst.Id, unnamedSecond.Id
        }));
        Assert.That(zebraEditor.Find("#cardCategory5").GetAttribute("value"), Is.EqualTo(otherCategory.Identity));
    }

    [Test]
    public async Task DeckSortInvalidatesResultsAndPersistsThroughSaveLoadAndManualMove() {
        var zebra = new Card([a], 2, "Zebra", id: "zebra");
        var alpha = new Card([a], 2, "Alpha", id: "alpha");
        var calculator = new CountingProbabilityCalculator();
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(new SessionState {
            Categories = [a], Cards = [zebra, alpha], Combos = [new([new(a, 1, 2)], "Any A")], HandSize = 2
        });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(calculator.CallCount, Is.EqualTo(1));

        await cut.Find("button[aria-label='Sort deck A–Z']").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(calculator.CallCount, Is.EqualTo(1), "sorting marks the result stale without calculating again");
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] { alpha.Id, zebra.Id }));
        await Button(cut, "Save Session").ClickAsync(new());
        var firstSave = SavedSessionJson();
        using (var document = System.Text.Json.JsonDocument.Parse(firstSave)) {
            var root = document.RootElement;
            Assert.That(root.GetProperty("Cards").EnumerateArray().Select(card => card.GetProperty("Id").GetString()),
                Is.EqualTo(new[] { alpha.Id, zebra.Id }));
            Assert.That(root.TryGetProperty("SortMode", out _), Is.False);
        }
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(firstSave, "sorted.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id),
            Is.EqualTo(new[] { alpha.Id, zebra.Id })));

        await cut.Find("[aria-label='Move card Alpha, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id), Is.EqualTo(new[] { zebra.Id, alpha.Id }));
        await Button(cut, "Save Session").ClickAsync(new());
        var secondSave = SavedSessionJson(1);
        using var secondDocument = System.Text.Json.JsonDocument.Parse(secondSave);
        Assert.That(secondDocument.RootElement.GetProperty("Cards").EnumerateArray().Select(card => card.GetProperty("Id").GetString()),
            Is.EqualTo(new[] { zebra.Id, alpha.Id }));
    }

    [Test]
    public void CardOnlyComboCanBeEditedAndMissingReferenceBlocksCalculationUntilRemoved() {
        var first = new Card([], 2, "Twin");
        var second = new Card([], 2, "Twin");
        var combo = new Combo([], "Direct", cards: [new(first.Id, 0, 0)]);
        var cut = Render(new SessionState { Cards = [first, second], Combos = [combo], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        editor.Find(".accordion-button").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(editor.Find(".combo-card-tag").TextContent, Does.Contain("Card: Twin (None)"));

        editor.Find("#constraintKind0").Change("Card");
        Assert.That(editor.FindAll("#comboCard0 option").Select(option => option.TextContent.Trim()),
            Does.Contain("Twin #2"));
        editor.Find("#comboCard0").Change(first.Id);
        Assert.That(Button(editor, "Update").TextContent.Trim(), Is.EqualTo("Update"));
        editor.Find("#minCount0").Input("1");
        editor.Find("#maxCount0").Input("1");
        Button(editor, "Update").Click();
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1)"));

        cut.FindComponent<CardEditor>().Find(".accordion-button").Click();
        cut.FindComponent<CardEditor>().Find("#cardName0").Input("Renamed");
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Renamed (1)"));
        cut.FindComponent<CardEditor>().Find("[title='Remove card']").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference"));
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-missing-card").TextContent,
            Does.Contain("Missing card"));
        cut.FindComponent<ComboEditor>().Find(".combo-missing-card button").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        editor = cut.FindComponent<ComboEditor>();
        editor.Find("#comboCard0").Change(second.Id);
        editor.Find("#maxCount0").Input("1");
        Button(editor, "Add").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-card-tag").TextContent,
            Does.Contain("Card: Twin (1)"));
    }

    [Test]
    public async Task NewCardAndCategoryRequirementsCannotShareOneDrawnCard() {
        var role = new CategoryBase("Role");
        var card = new Card([role], 1, "Piece");
        var other = new Card([], 1, "Other");
        var cut = Render(new SessionState {
            Categories = [role], Cards = [card, other], Combos = [new([], "Mixed")], HandSize = 1
        });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = "user:Role" });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = card.Id });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent,
            Does.Contain(SmallDeckOracleTest.EnumerateProbability([card, other],
                [new([new(role, 1, 1)], cards: [new(card.Id, 1, 1)])], 1).ToString("P2")));
        Assert.That(editor.FindAll(".accordion-body .category-tag"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ImportDoesNotRebindSameNamedCardAndMissingZeroBoundBlocksCalculation() {
        var original = new Card([], 2, "Same");
        var replacement = new Card([], 2, "Same");
        var importer = new Mock<IDeckImportService>();
        importer.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);
        var cut = Render(new SessionState {
            Cards = [original], Combos = [new([], cards: [new(original.Id, 0, 0)])], HandSize = 1
        });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123", "deck.ydk"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Id, Is.EqualTo(replacement.Id));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
            Assert.That(cut.FindComponent<ComboEditor>().Find(".combo-missing-card").TextContent,
                Does.Contain("Missing card"));
        });
        cut.Find("#comboActive0").Change(false);
        Assert.That(cut.Markup, Does.Not.Contain("An active combo has a missing card reference"));
    }

    [Test]
    public async Task MixedComboSessionRoundTripKeepsCardIdentityAndGroup() {
        var card = new Card([a], 2, "Piece");
        var session = new SessionState {
            Categories = [a], Cards = [card],
            Combos = [new([new(a, 1, 2)], "Mixed", groupId: "g", cards: [new(card.Id, 1, 2)])],
            ComboGroups = [new("g", "Group")], HandSize = 1
        };
        var cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain(card.Id).And.Contain("\"Cards\""));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "mixed.json"));
        {
            var loadedCard = cut.FindComponent<CardEditor>().Instance.Card;
            var loadedCombo = cut.FindComponent<ComboEditor>().Instance.Combo;
            Assert.That(loadedCard.Id, Is.EqualTo(card.Id));
            Assert.That(loadedCombo.Cards.Single().CardId, Is.EqualTo(card.Id));
            Assert.That(loadedCombo.GroupId, Is.EqualTo("g"));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent,
            Does.Contain(1.0.ToString("P2")));
    }

    [Test]
    public async Task TooManyActiveCombosExplainTheLimitAndInactiveCombosDoNotCount() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.AddRange(Enumerable.Range(0, 31)
            .Select(_ => new Combo(new[] { new ComboCategory(a, 1, 2) })));
        var cut = Render(session);
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("at most 30 active combos"));
        await cut.Find("#comboActive30").ChangeAsync(new() { Value = false });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        for (var index = 1; index < 30; index++) await cut.Find($"#comboActive{index}").ChangeAsync(new() { Value = false });
        await Button(cut, "Calculate").ClickAsync(new());
        var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, [session.Combos[0]], session.HandSize);
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
    }

    [Test]
    public void ComboHeaderKeepsVisibleBadgesAndAccessibleControlsDistinct() {
        var categories = Enumerable.Range(1, 7)
            .Select(index => new CategoryBase($"Long Category {index}"))
            .ToList();
        const string comboName = "A Long Combo Name For Mobile";
        var combo = new Combo(categories.Select(category => new ComboCategory(category, 1, 5)).ToList(), comboName);
        var cut = Render(new SessionState {
            Categories = categories,
            Cards = [],
            Combos = [combo],
            HandSize = 5
        });
        var editor = cut.FindComponent<ComboEditor>();
        var header = editor.Find(".combo-editor-header");
        var accordionButton = header.QuerySelector("button.accordion-button");

        Assert.That(accordionButton, Is.Not.Null);
        Assert.That(accordionButton!.GetAttribute("aria-controls"), Is.EqualTo("combo0"));
        Assert.That(accordionButton.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(accordionButton.TextContent, Does.Contain(comboName));
        Assert.That(accordionButton.QuerySelectorAll(".combo-header-content .category-tag").Length, Is.EqualTo(6));
        Assert.That(accordionButton.QuerySelector(".combo-header-content .badge.bg-secondary")?.TextContent, Is.EqualTo("+1 more"));
        Assert.That(accordionButton.TextContent, Does.Contain("Long Category 1 (1–5)"));

        var actionButtons = header.QuerySelectorAll("button");
        var activeToggle = header.QuerySelector("input.entry-editor-active-checkbox");
        var removeButton = header.QuerySelector("button[aria-label='Remove combo']");
        Assert.That(header.QuerySelector($"button[aria-label='Duplicate combo {comboName}']"), Is.Null);
        Assert.That(actionButtons.Length, Is.EqualTo(4));
        Assert.That(header.QuerySelectorAll(".reorder-controls button").Length, Is.EqualTo(2));
        Assert.That(activeToggle?.GetAttribute("aria-label"), Is.EqualTo($"Active combo {comboName}"));
        Assert.That(activeToggle?.GetAttribute("title"), Is.EqualTo($"Toggle {comboName} active state"));
        Assert.That(removeButton?.GetAttribute("title"), Is.EqualTo("Remove combo"));
        accordionButton.Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        var duplicateButton = editor.Find(".accordion-body button[aria-label='Duplicate combo A Long Combo Name For Mobile']");
        Assert.Multiple(() => {
            Assert.That(duplicateButton.TextContent.Trim(), Is.EqualTo("Duplicate combo"));
            Assert.That(duplicateButton.GetAttribute("title"), Is.EqualTo($"Duplicate combo {comboName}"));
            Assert.That(duplicateButton.GetAttribute("type"), Is.EqualTo("button"));
            Assert.That(duplicateButton.ClassList, Does.Contain("btn-sm"));
            Assert.That(duplicateButton.ClassList, Does.Contain("btn-outline-secondary"));
        });
        editor.Find(".combo-editor-header .accordion-button").Click();
        Assert.That(editor.Find(".combo-editor-header .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(editor.FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);
    }

    [Test]
    public void ComboHeaderShowsOnlyValidGroupMembershipAsSeparateMetadata() {
        var card = new Card([a], 2, "Starter", id: "starter-card");
        const string groupId = "full-combo";
        const string staleGroupId = "removed-group";
        var cut = Render(new SessionState {
            Categories = [a],
            Cards = [card],
            Combos = [
                new([new ComboCategory(a, 1, 1)], "Grouped combo", active: false, groupId: groupId,
                    cards: [new ComboCard(card.Id, 1, 1)]),
                new([], "No group assigned"),
                new([], "Stale group reference", groupId: staleGroupId)
            ],
            ComboGroups = [new(groupId, "Full Combo"), new("optional-route", "Optional Route")],
            HandSize = 2
        });
        var editors = cut.FindComponents<ComboEditor>();
        var groupedHeader = editors[0].Find(".combo-header-content");
        var membership = groupedHeader.QuerySelector(".combo-group-membership");

        Assert.That(membership, Is.Not.Null);
        Assert.That(membership!.TextContent, Does.Contain("Group: Full Combo"));
        Assert.That(membership.ClassList, Does.Not.Contain("category-tag"));
        Assert.That(membership.ClassList, Does.Not.Contain("card-property-tag"));
        Assert.That(membership.ClassList, Does.Not.Contain("combo-card-tag"));
        Assert.That(groupedHeader.QuerySelector(".category-tag")?.TextContent, Does.Contain("A (1)"));
        Assert.That(groupedHeader.QuerySelector(".combo-card-tag")?.TextContent, Does.Contain("Card: Starter (1)"));
        Assert.That(groupedHeader.QuerySelector(".badge.text-bg-secondary")?.TextContent, Is.EqualTo("Inactive"));
        Assert.That(Array.IndexOf(groupedHeader.Children.ToArray(), membership),
            Is.LessThan(Array.IndexOf(groupedHeader.Children.ToArray(), groupedHeader.QuerySelector(".category-tag"))));

        foreach (var ungroupedEditor in editors.Skip(1)) {
            var headerContent = ungroupedEditor.Find(".combo-header-content");
            Assert.That(headerContent.QuerySelector(".combo-group-membership"), Is.Null);
            Assert.That(headerContent.TextContent, Does.Not.Contain("Ungrouped"));
            Assert.That(headerContent.TextContent, Does.Not.Contain(staleGroupId));
        }
    }

    [Test]
    public async Task DuplicateActionAppearsOnlyOnTheExpandedComboAndCardHeaderControlsStayUnchanged() {
        var cut = Render(new SessionState {
            Categories = [a],
            Cards = [new([a], 3, "Card")],
            Combos = [new([], "First combo"), new([], "Second combo")],
            HandSize = 3
        });
        var combos = cut.FindComponents<ComboEditor>();
        var cardHeader = cut.FindComponent<CardEditor>().Find(".entry-editor-header");

        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Is.Empty);
        Assert.Multiple(() => {
            Assert.That(cardHeader.QuerySelectorAll("button").Length, Is.EqualTo(4));
            Assert.That(cardHeader.QuerySelectorAll(".reorder-controls button").Length, Is.EqualTo(2));
            Assert.That(cardHeader.QuerySelector("input.entry-editor-active-checkbox"), Is.Not.Null);
            Assert.That(cardHeader.QuerySelector("button[aria-label='Remove card']"), Is.Not.Null);
            Assert.That(combos[0].Find(".combo-editor-header").QuerySelectorAll("button").Length, Is.EqualTo(4));
            Assert.That(combos[0].Find(".combo-editor-header").QuerySelector("button[aria-label='Remove combo']"), Is.Not.Null);
        });

        await combos[0].Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        Assert.That(combos[0].Find(".accordion-body button[aria-label^='Duplicate combo ']").TextContent.Trim(),
            Is.EqualTo("Duplicate combo"));
        Assert.That(combos[1].FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);

        await combos[1].Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        Assert.That(combos[0].FindAll(".accordion-body button[aria-label^='Duplicate combo ']"), Is.Empty);
        Assert.That(combos[1].Find(".accordion-body button[aria-label^='Duplicate combo ']").GetAttribute("aria-label"),
            Is.EqualTo("Duplicate combo Second combo"));
    }

    [Test]
    public void CategoryColorsStayConsistentAcrossViewsThroughRenameAndUnrelatedDeletion() {
        var categoryA = new CategoryBase("A");
        var categoryB = new CategoryBase("B");
        var categoryC = new CategoryBase("C");
        var session = new SessionState {
            Categories = [categoryA, categoryB, categoryC],
            Cards = [new([categoryA, categoryC], 3, "Card")],
            Combos = [new([new(categoryA, 1, 3), new(categoryC, 1, 3)], "Combo")],
            HandSize = 3
        };

        var cut = Render(session);
        var categoryList = cut.FindComponent<CategoryListEditor>();
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();
        var colorA = CategoryColorClass(categoryList, ".category-chip", "A");
        var colorC = CategoryColorClass(categoryList, ".category-chip", "C");

        Assert.That(colorA, Is.Not.EqualTo(colorC));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-button .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "A"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));

        cut.Find("[aria-label='Edit category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed");
        cut.Find("[aria-label='Save category name']").Click();

        Assert.That(CategoryColorClass(categoryList, ".category-chip", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(card, ".accordion-button .category-tag", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "C"), Is.EqualTo(colorC));

        cut.Find("[aria-label='Remove category B']").Click();
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "Renamed"), Is.EqualTo(colorA));
        Assert.That(CategoryColorClass(categoryList, ".category-chip", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "C"), Is.EqualTo(colorC));

        cut.Find("[placeholder='Category name']").Input("D");
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        var colorD = CategoryColorClass(categoryList, ".category-chip", "D");
        Assert.That(colorD, Is.Not.EqualTo(colorA).And.Not.EqualTo(colorC));
    }

    [Test]
    public void CategoryColorsRoundTripAndLegacySessionsReceiveDistinctAssignments() {
        var cut = Render(Session());
        var categories = cut.FindComponent<CategoryListEditor>();
        var firstColor = CategoryColorClass(categories, ".category-chip", "A");
        var secondColor = CategoryColorClass(categories, ".category-chip", "B");
        Assert.That(firstColor, Is.Not.EqualTo(secondColor));

        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(savedJson)) {
            var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
            Assert.That(savedColors.GetProperty("A").GetInt32(), Is.Not.EqualTo(savedColors.GetProperty("B").GetInt32()));
        }

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(savedJson, "session.json"));
        cut.WaitForAssertion(() => {
            var loadedCategories = cut.FindComponent<CategoryListEditor>();
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "A"), Is.EqualTo(firstColor));
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "B"), Is.EqualTo(secondColor));
            Assert.That(CategoryColorClass(cut.FindComponent<CardEditor>(), ".accordion-body .category-tag", "A"), Is.EqualTo(firstColor));
            Assert.That(CategoryColorClass(cut.FindComponents<ComboEditor>()[1], ".accordion-body .category-tag", "B"), Is.EqualTo(secondColor));
        });

        const string legacySession = """
            {
              "Categories": [{ "Name": "Legacy A" }, { "Name": "Legacy B" }],
              "Cards": [],
              "Combos": [],
              "HandSize": 5
            }
            """;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(legacySession, "legacy-session.json"));
        cut.WaitForAssertion(() => {
            var legacyCategories = cut.FindComponent<CategoryListEditor>();
            var legacyAColor = CategoryColorClass(legacyCategories, ".category-chip", "Legacy A");
            var legacyBColor = CategoryColorClass(legacyCategories, ".category-chip", "Legacy B");
            Assert.That(legacyAColor, Is.Not.EqualTo(legacyBColor));
        });
    }

    [Test]
    public async Task CategoryColorPickerUpdatesEveryViewPreservesRenameDraftAndRoundTripsDuplicates() {
        var vsStarter = new CategoryBase("VS Starter");
        var k9Starter = new CategoryBase("K9 Starter");
        var cut = Render(new SessionState {
            Categories = [vsStarter, k9Starter],
            Cards = [new([vsStarter, k9Starter], 2, "Starter card")],
            Combos = [new([new(vsStarter, 1, 2), new(k9Starter, 1, 2)], "Starter route")],
            HandSize = 2,
            CategoryColorIndices = new(StringComparer.Ordinal) { [vsStarter.Name] = 0, [k9Starter.Name] = 1 }
        });
        var categories = cut.FindComponent<CategoryListEditor>();
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();

        Assert.That(categories.FindAll(".category-color-swatch"), Is.Empty);
        Assert.That(CategoryColorClass(categories, ".category-chip", vsStarter.Name), Is.EqualTo("category-color-0"));
        await categories.Find("[aria-label='Edit category K9 Starter']").ClickAsync(new());
        await categories.Find("[aria-label='Choose color for category K9 Starter']").ClickAsync(new());
        var k9Swatches = categories.FindAll(".category-color-swatch");
        Assert.That(k9Swatches, Has.Count.EqualTo(CategoryColorPalette.PaletteSize));
        Assert.That(k9Swatches.Select(button => button.GetAttribute("aria-label")),
            Is.EqualTo(CategoryColorPalette.Options.Select(color => $"Set K9 Starter color to {color.Name}")));
        Assert.That(categories.Find("[aria-label='Set K9 Starter color to Orange']").GetAttribute("aria-pressed"), Is.EqualTo("true"));
        await categories.Find("[aria-label='Set K9 Starter color to Green']").ClickAsync(new());

        Assert.That(CategoryColorClass(categories, ".category-chip", vsStarter.Name), Is.EqualTo("category-color-0"),
            "changing one category leaves the other category unchanged");
        Assert.That(categories.Find("[aria-label='New name for category K9 Starter']").ParentElement!.ClassList,
            Does.Contain("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-button .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", k9Starter.Name), Is.EqualTo("category-color-2"));

        categories = cut.FindComponent<CategoryListEditor>();
        await categories.Find("[aria-label='Exit category edit mode']").ClickAsync(new());
        await categories.Find("[aria-label='Edit category VS Starter']").ClickAsync(new());
        await categories.Find("[aria-label='Choose color for category VS Starter']").ClickAsync(new());
        Assert.That(categories.Find("[aria-label='Set VS Starter color to Blue']").GetAttribute("aria-pressed"), Is.EqualTo("true"));
        await categories.Find("[aria-label='New name for category VS Starter']").InputAsync(new() { Value = "VS Starter renamed" });
        await categories.Find("[aria-label='Set VS Starter color to Green']").ClickAsync(new());

        categories = cut.FindComponent<CategoryListEditor>();
        var renameInput = categories.Find("[aria-label='New name for category VS Starter']");
        Assert.That(renameInput.GetAttribute("value"), Is.EqualTo("VS Starter renamed"));
        Assert.That(categories.FindAll("[aria-label='Edit category VS Starter renamed']"), Is.Empty,
            "choosing a color must not commit the pending rename");
        Assert.That(renameInput.ParentElement!.ClassList, Does.Contain("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", vsStarter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", vsStarter.Name), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(categories, ".category-chip", k9Starter.Name), Is.EqualTo("category-color-2"));

        await categories.Find("[aria-label='Save category name']").ClickAsync(new());
        categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(CategoryColorClass(categories, ".category-chip", "VS Starter renamed"), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(card, ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));
        Assert.That(CategoryColorClass(combo, ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(savedJson)) {
            var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
            Assert.That(savedColors.GetProperty("VS Starter renamed").GetInt32(), Is.EqualTo(2));
            Assert.That(savedColors.GetProperty("K9 Starter").GetInt32(), Is.EqualTo(2),
                "manually chosen duplicate palette colors are allowed and saved");
        }

        var sessionFile = new Mock<IBrowserFile>();
        sessionFile.Setup(file => file.Name).Returns("session.json");
        sessionFile.Setup(file => file.Size).Returns(System.Text.Encoding.UTF8.GetByteCount(savedJson));
        sessionFile.Setup(file => file.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(savedJson)));
        var sessionFileInput = cut.FindComponents<InputFile>()[1];
        await cut.InvokeAsync(() => sessionFileInput.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([sessionFile.Object])));
        cut.WaitForAssertion(() => {
            var loadedCategories = cut.FindComponent<CategoryListEditor>();
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "VS Starter renamed"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(loadedCategories, ".category-chip", "K9 Starter"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(cut.FindComponent<CardEditor>(), ".accordion-body .category-tag", "VS Starter renamed"), Is.EqualTo("category-color-2"));
            Assert.That(CategoryColorClass(cut.FindComponents<ComboEditor>()[0], ".accordion-body .category-tag", "K9 Starter"), Is.EqualTo("category-color-2"));
        });
    }

    [Test]
    public async Task InvalidAndMissingCategoryColorsAreRepairedWithoutChangingValidDuplicates() {
        var aCategory = new CategoryBase("A");
        var bCategory = new CategoryBase("B");
        var cCategory = new CategoryBase("C");
        var cut = Render(new SessionState {
            Categories = [aCategory, bCategory, cCategory],
            CategoryColorIndices = new(StringComparer.Ordinal) {
                ["A"] = 7,
                ["B"] = 7,
                ["C"] = CategoryColorPalette.PaletteSize,
                ["Removed"] = 4
            }
        });
        var categories = cut.FindComponent<CategoryListEditor>();

        Assert.That(CategoryColorClass(categories, ".category-chip", "A"), Is.EqualTo("category-color-7"));
        Assert.That(CategoryColorClass(categories, ".category-chip", "B"), Is.EqualTo("category-color-7"));
        Assert.That(CategoryColorClass(categories, ".category-chip", "C"), Is.EqualTo("category-color-0"));
        await categories.Find("[placeholder='Category name']").InputAsync(new() { Value = "D" });
        await categories.Find("button.btn.btn-primary").ClickAsync(new());
        categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(CategoryColorClass(categories, ".category-chip", "D"), Is.EqualTo("category-color-1"),
            "automatic assignment uses the first unused palette slot after existing duplicate choices");

        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using var document = System.Text.Json.JsonDocument.Parse(savedJson);
        var savedColors = document.RootElement.GetProperty("CategoryColorIndices");
        Assert.That(savedColors.GetProperty("A").GetInt32(), Is.EqualTo(7));
        Assert.That(savedColors.GetProperty("B").GetInt32(), Is.EqualTo(7));
        Assert.That(savedColors.GetProperty("C").GetInt32(), Is.EqualTo(0));
        Assert.That(savedColors.GetProperty("D").GetInt32(), Is.EqualTo(1));
        Assert.That(savedColors.TryGetProperty("Removed", out _), Is.False);
    }

    [Test]
    public void MetadataPropertyColorsAreConsistentAcrossCardAndComboChipContexts() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var detectedCard = new Card([fire], name: "Detected FIRE");
        var manualCard = new Card([fire], name: "Manual FIRE", manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var combo = new Combo([new(fire, 1, 5)], "FIRE route");
        var cut = Render(new SessionState { Cards = [detectedCard, manualCard], Combos = [combo], HandSize = 5 });
        var detected = cut.FindComponents<CardEditor>()[0];
        var manual = cut.FindComponents<CardEditor>()[1];
        var comboEditor = cut.FindComponent<ComboEditor>();
        var expectedClass = "card-property-color-attribute-fire";

        Assert.That(detected.Find(".detected-card-property").ClassList,
            Does.Contain("card-property-tag").And.Contain(expectedClass));
        Assert.That(manual.Find(".manual-card-property").ClassList,
            Does.Contain("card-property-tag").And.Contain(expectedClass));
        Assert.That(manual.Find(".manual-property-header-badge").ClassList, Does.Contain(expectedClass));
        Assert.That(comboEditor.Find(".combo-header-content .category-tag").ClassList, Does.Contain(expectedClass));
        Assert.That(comboEditor.Find(".accordion-body .category-tag").ClassList, Does.Contain(expectedClass));
        Assert.That(cut.FindAll(".card-property-tag.text-bg-secondary"), Is.Empty);
    }

    private static string CategoryColorClass(IRenderedFragment fragment, string selector, string categoryName) =>
        fragment.FindAll(selector)
            .Single(element => element.TextContent.Trim().StartsWith(categoryName, StringComparison.Ordinal))
            .ClassList.Single(className => className.StartsWith("category-color-", StringComparison.Ordinal));

    [Test]
    public void CategoriesRefreshSiblingSelectorsAndRejectEmptyDuplicateOrUsedNames() {
        var cut = Render();
        Button(cut, "Add New Card").Click();
        Button(cut, "Add New Combo").Click();
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[placeholder='Category name']").Input("A");
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.FindAll("select option[value='user:A']"), Has.Count.EqualTo(2));
        cut.Find("[placeholder='Category name']").Input("a");
        cut.FindComponent<CategoryListEditor>().Find("button.btn.btn-primary").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        var card = cut.FindComponent<CardEditor>();
        card.Find("select").Change("user:A");
        Button(card, "Add").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        card.Find(".accordion-body .badge button").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.FindAll("select option[value='user:A']"), Is.Empty);
    }

    [Test]
    public void LoadedComboAlonePreventsCategoryDeletionUntilConstraintIsRemoved() {
        var session = Session();
        session.Cards.Clear();
        var cut = Render(session);
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("still used"));
        cut.FindComponents<ComboEditor>()[0].Find(".accordion-body .badge button").Click();
        cut.Find("[aria-label='Remove category A']").Click();
        Assert.That(cut.FindAll("select option[value='user:A']"), Is.Empty);
    }

    [Test]
    public void CardValidationRetainsPositiveCopyCountsAndReportsSelectionErrors() {
        var cut = Render(Session());
        var card = cut.FindComponents<CardEditor>()[0];
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        foreach (var value in new[] { "0", "-1", "", "nonsense" }) {
            card.Find("input[type=number]").Input(value);
            Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("at least 1"));
            Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("(2)"));
        }
        card.Find("select").Change("user:A");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("already added"));
        card.Find("select").Change("missing");
        Button(card, "Add").Click();
        Assert.That(card.Find("[role=alert]").TextContent, Does.Contain("not found"));
        card.Find("select").Change("user:B");
        card.Find("input[type=number]").Input("6");
        card.Find("#cardName0").Input("Renamed");
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("user:B"));
        Button(card, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Renamed").And.Contain("(6)"));
        Assert.That(card.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public void ZeroMaximumSurvivesRenameParentRerenderAndHandSizeChangeAndCanBeUpdated() {
        var cut = Render(Session());
        var combo = cut.FindComponents<ComboEditor>()[0];
        combo.Find("select").Change("user:A");
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("0");
        combo.Find("#comboName0").Input("Zero A");
        cut.Find("#handSize").Change("3");
        combo.Find(".accordion-button").Click();
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Button(combo, "Update").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (None)"));
        combo.Find("select").Change("user:A");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        combo.Find("#maxCount0").Input("2");
        Button(combo, "Update").Click();
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (2 Max)"));
    }

    [Test]
    public void ComboRejectsInvalidFixedRangesWithoutClampingAndDefaultsNewDraftsToAny() {
        var cut = Render(Session());
        var combo = cut.FindComponents<ComboEditor>()[0];
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("select a category"));
        combo.Find("select").Change("user:B");
        combo.Find("#minCount0").Input("-1");
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Minimum"));
        combo.Find("#minCount0").Input("2");
        combo.Find("#maxCount0").Input("1");
        cut.Find("#handSize").Change("3");
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(combo.FindAll("#maxAny0"), Is.Empty);
        Button(combo, "Add").Click();
        Assert.That(combo.Find("[role=alert]").TextContent, Does.Contain("Maximum"));
        combo.Find("#maxCount0").Input("");
        Button(combo, "Add").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("B (2 Min)"));
        combo.Find("select").Change("");
        cut.Find("#handSize").Change("4");
        AssertAnyMaximumDraft(combo);
        combo.Find("select").Change("user:B");
        Button(combo, "Update").Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DuplicateComboCopiesCommittedStateAfterTheSourceAndCanBeEditedIndependently() {
        var monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var cards = new List<Card> {
            new([a, monster], 2, "First card", id: "first-card-id"),
            new([b], 2, "Second card", id: "second-card-id")
        };
        var source = new Combo(
            [new(a, 1, 2), new(monster, 0, 1)],
            "Starter",
            active: false,
            groupId: "group-one",
            cards: [new(cards[0].Id, 1, 2), new("missing-card-id", 0, 1)]);
        var session = new SessionState {
            Categories = [a, b],
            Cards = cards,
            Combos = [new([], "Before"), source, new([], "After")],
            ComboGroups = [new("group-one", "Group One"), new("group-two", "Group Two")],
            HandSize = 2
        };
        var cut = Render(session);
        var sourceEditor = cut.FindComponents<ComboEditor>()[1];

        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Is.Empty);
        await sourceEditor.Find(".accordion-button").ClickAsync(new());
        Assert.That(cut.FindAll("button[aria-label^='Duplicate combo ']"), Has.Count.EqualTo(1));
        var duplicateAction = sourceEditor.Find(".accordion-body button[aria-label='Duplicate combo Starter']");
        Assert.Multiple(() => {
            Assert.That(duplicateAction.GetAttribute("title"), Is.EqualTo("Duplicate combo Starter"));
            Assert.That(duplicateAction.GetAttribute("type"), Is.EqualTo("button"));
            Assert.That(duplicateAction.ClassList, Does.Not.Contain("btn-danger"));
        });

        await duplicateAction.ClickAsync(new());

        var editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors.Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new[] { "Before", "Starter", "Starter copy", "After" }));
        Assert.That(editors[1].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.That(editors[1].Instance.Combo, Is.SameAs(source));
        Assert.That(editors[2].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(editors[1].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));

        var duplicateEditor = editors[2];
        var duplicate = duplicateEditor.Instance.Combo;
        Assert.Multiple(() => {
            Assert.That(duplicate, Is.Not.SameAs(source));
            Assert.That(duplicate.Name, Is.EqualTo("Starter copy"));
            Assert.That(duplicate.Active, Is.False);
            Assert.That(duplicate.GroupId, Is.EqualTo("group-one"));
            Assert.That(duplicate.Categories, Is.Not.SameAs(source.Categories));
            Assert.That(duplicate.Cards, Is.Not.SameAs(source.Cards));
            Assert.That(duplicate.Categories.Select(category =>
                    (category.BaseCategory.Identity, category.MinCount, category.MaxCount)),
                Is.EqualTo(new[] { (a.Identity, 1, 2), (monster.Identity, 0, 1) }));
            Assert.That(duplicate.Categories[0], Is.Not.SameAs(source.Categories[0]));
            Assert.That(duplicate.Categories[1].BaseCategory, Is.SameAs(monster));
            Assert.That(duplicate.Cards.Select(card => (card.CardId, card.MinCount, card.MaxCount)),
                Is.EqualTo(new[] { (cards[0].Id, 1, 2), ("missing-card-id", 0, 1) }));
            Assert.That(duplicate.Cards[0], Is.Not.SameAs(source.Cards[0]));
        });

        await duplicateEditor.Find("#comboName2").InputAsync(new() { Value = "Adjusted route" });
        await duplicateEditor.Find("#comboCategory2").ChangeAsync(new() { Value = a.Identity });
        await duplicateEditor.Find("#minCount2").InputAsync(new() { Value = "2" });
        await duplicateEditor.Find("#maxCount2").InputAsync(new() { Value = "2" });
        await Button(duplicateEditor, "Update").ClickAsync(new());
        await duplicateEditor.Find(".accordion-body .combo-card-tag button").ClickAsync(new());
        await duplicateEditor.Find("#comboGroup2").ChangeAsync(new() { Value = "group-two" });
        await duplicateEditor.Find("#comboActive2").ChangeAsync(new() { Value = true });

        Assert.Multiple(() => {
            Assert.That(source.Name, Is.EqualTo("Starter"));
            Assert.That(source.Active, Is.False);
            Assert.That(source.GroupId, Is.EqualTo("group-one"));
            Assert.That(source.Categories.Select(category => (category.MinCount, category.MaxCount)),
                Is.EqualTo(new[] { (1, 2), (0, 1) }));
            Assert.That(source.Cards.Select(card => card.CardId),
                Is.EqualTo(new[] { cards[0].Id, "missing-card-id" }));
            Assert.That(duplicateEditor.Instance.Combo.Name, Is.EqualTo("Adjusted route"));
            Assert.That(duplicateEditor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(2));
            Assert.That(duplicateEditor.Instance.Combo.Categories[0].MaxCount, Is.EqualTo(2));
            Assert.That(duplicateEditor.Instance.Combo.Cards.Select(card => card.CardId),
                Is.EqualTo(new[] { "missing-card-id" }));
            Assert.That(duplicateEditor.Instance.Combo.GroupId, Is.EqualTo("group-two"));
            Assert.That(duplicateEditor.Instance.Combo.Active, Is.True);
        });
    }

    [Test]
    public async Task DuplicateComboNamesUseCopySuffixesAndKeepUnnamedCombosUnnamed() {
        var session = new SessionState {
            Combos = [new([], "Starter"), new([], null)],
            HandSize = 5
        };
        var cut = Render(session);

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new string?[] { "Starter", "Starter copy", null }));

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new string?[] { "Starter", "Starter copy 2", "Starter copy", null }));

        await DuplicateComboAsync(cut, 3);
        var editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[3].Instance.Combo.Name, Is.Null);
        Assert.That(editors[4].Instance.Combo.Name, Is.Null);
        Assert.That(editors[4].Find(".combo-header-name").TextContent, Is.EqualTo("Combo 5"));
    }

    [Test]
    public async Task DuplicateComboNameCollisionsAreCaseInsensitiveAndDeterministic() {
        var cut = Render(new SessionState {
            Combos = [new([], "Starter"), new([], "starter COPY"), new([], "STARTER copy 2")],
            HandSize = 5
        });

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Starter copy 3"));

        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>()[1].Instance.Combo.Name, Is.EqualTo("Starter copy 4"));
    }

    [Test]
    public async Task DuplicateComboUsesCommittedModelAndPreservesExistingCategoryDraftsAndOrder() {
        var session = Session();
        var before = new Combo([], "Before");
        var middle = new Combo([new(a, 1, 1)], "Middle");
        var after = new Combo([new(b, 1, 1)], "After");
        session.Combos.Clear();
        session.Combos.AddRange(new[] { before, middle, after });
        var cut = Render(session);
        var sourceEditor = cut.FindComponents<ComboEditor>()[1];
        var otherEditor = cut.FindComponents<ComboEditor>()[2];

        await sourceEditor.Find("#comboCategory1").ChangeAsync(new() { Value = b.Identity });
        await sourceEditor.Find("#minCount1").InputAsync(new() { Value = "0" });
        await sourceEditor.Find("#maxCount1").InputAsync(new() { Value = "0" });
        await otherEditor.Find("#comboCategory2").ChangeAsync(new() { Value = a.Identity });
        await otherEditor.Find("#minCount2").InputAsync(new() { Value = "0" });
        await otherEditor.Find("#maxCount2").InputAsync(new() { Value = "2" });

        await DuplicateComboAsync(cut, 1);

        var editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[1].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.That(editors[3].Instance, Is.SameAs(otherEditor.Instance));
        Assert.Multiple(() => {
            Assert.That(sourceEditor.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo(b.Identity));
            Assert.That(sourceEditor.Find("#minCount1").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(sourceEditor.Find("#maxCount1").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(editors[2].Instance.Combo.Categories.Select(category => category.BaseCategory.Identity),
                Is.EqualTo(new[] { a.Identity }));
            Assert.That(editors[2].Find("#comboCategory2").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(editors[2].Find("#minCount2").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(editors[2].Find("#maxCount2").GetAttribute("value"), Is.EqualTo(string.Empty));
            Assert.That(editors[2].Find("#maxCount2").GetAttribute("placeholder"), Is.EqualTo("Any"));
            Assert.That(editors[2].FindAll("#maxAny2"), Is.Empty);
            Assert.That(otherEditor.Find("#comboCategory3").GetAttribute("value"), Is.EqualTo(a.Identity));
            Assert.That(otherEditor.Find("#minCount3").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(otherEditor.Find("#maxCount3").GetAttribute("value"), Is.EqualTo("2"));
        });

        await cut.Find("[aria-label='Move combo Middle copy, row 3 down']").ClickAsync(new());
        editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors.Select(editor => editor.Instance.Combo.Name),
            Is.EqualTo(new[] { "Before", "Middle", "After", "Middle copy" }));
        Assert.That(editors[3].Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(otherEditor.Find("#comboCategory2").GetAttribute("value"), Is.EqualTo(a.Identity));
    }

    [Test]
    public async Task DuplicateComboDoesNotCopyUncommittedDirectCardDraft() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new([new(a, 1, 2)], "Card draft source"));
        var cut = Render(session);
        var sourceEditor = cut.FindComponent<ComboEditor>();
        var cardId = session.Cards[1].Id;

        await sourceEditor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await sourceEditor.Find("#comboCard0").ChangeAsync(new() { Value = cardId });
        await sourceEditor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await sourceEditor.Find("#maxCount0").InputAsync(new() { Value = "2" });
        await DuplicateComboAsync(cut, 0);

        var editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[0].Instance, Is.SameAs(sourceEditor.Instance));
        Assert.Multiple(() => {
            Assert.That(sourceEditor.Instance.Combo.Cards, Is.Empty);
            Assert.That(sourceEditor.Find("#constraintKind0").GetAttribute("value"), Is.EqualTo("Card"));
            Assert.That(sourceEditor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo(cardId));
            Assert.That(sourceEditor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(sourceEditor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(editors[1].Instance.Combo.Cards, Is.Empty);
            Assert.That(editors[1].Find("#constraintKind1").GetAttribute("value"), Is.EqualTo("Category"));
            Assert.That(editors[1].Find("#comboCategory1").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(editors[1].Find("#minCount1").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(editors[1].Find("#maxCount1").GetAttribute("value"), Is.EqualTo(string.Empty));
            Assert.That(editors[1].Find("#maxCount1").GetAttribute("placeholder"), Is.EqualTo("Any"));
            Assert.That(editors[1].FindAll("#maxAny1"), Is.Empty);
        });
    }

    [Test]
    public async Task DuplicateComboSurvivesSessionSaveLoadAndRemainsIndependentlyEditable() {
        var monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var cards = new List<Card> { new([a, monster], 2, "First card", id: "persisted-card-id") };
        var source = new Combo([new(a, 1, 2), new(monster, 0, 1)], "Starter",
            active: false, groupId: "persisted-group", cards: [new(cards[0].Id, 1, 2)]);
        var session = new SessionState {
            Categories = [a, b],
            Cards = cards,
            Combos = [new([new(b, 0, 1)], "Before"), source],
            ComboGroups = [new("persisted-group", "Tier 1")],
            HandSize = 2
        };
        var cut = Render(session);

        await DuplicateComboAsync(cut, 1);
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(json)) {
            var combos = document.RootElement.GetProperty("Combos").EnumerateArray().ToArray();
            Assert.That(combos.Select(combo => combo.GetProperty("Name").GetString()),
                Is.EqualTo(new[] { "Before", "Starter", "Starter copy" }));
            Assert.That(combos[2].GetProperty("Active").GetBoolean(), Is.False);
            Assert.That(combos[2].GetProperty("GroupId").GetString(), Is.EqualTo("persisted-group"));
            Assert.That(combos[2].GetProperty("Categories").EnumerateArray().Select(category =>
                    (category.GetProperty("BaseCategory").GetProperty("Name").GetString(),
                     category.GetProperty("MinCount").GetInt32(), category.GetProperty("MaxCount").GetInt32())),
                Is.EqualTo(new[] { ("A", 1, 2), ("Monster", 0, 1) }));
            Assert.That(combos[2].GetProperty("Cards").EnumerateArray().Select(card =>
                    (card.GetProperty("CardId").GetString(), card.GetProperty("MinCount").GetInt32(),
                     card.GetProperty("MaxCount").GetInt32())),
                Is.EqualTo(new[] { ("persisted-card-id", 1, 2) }));
        }

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "duplicated-session.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(3)));
        var loaded = cut.FindComponents<ComboEditor>();
        Assert.Multiple(() => {
            Assert.That(loaded.Select(editor => editor.Instance.Combo.Name),
                Is.EqualTo(new[] { "Before", "Starter", "Starter copy" }));
            Assert.That(loaded[2].Instance.Combo.Categories.Select(category => category.BaseCategory.Identity),
                Is.EqualTo(new[] { a.Identity, monster.Identity }));
            Assert.That(loaded[2].Instance.Combo.Cards.Single().CardId, Is.EqualTo(cards[0].Id));
            Assert.That(loaded[2].Instance.Combo.GroupId, Is.EqualTo("persisted-group"));
            Assert.That(loaded[2].Instance.Combo.Active, Is.False);
        });

        await loaded[2].Find("#comboName2").InputAsync(new() { Value = "Revised after load" });
        Assert.That(loaded[1].Instance.Combo.Name, Is.EqualTo("Starter"));
        Assert.That(loaded[2].Instance.Combo.Name, Is.EqualTo("Revised after load"));
    }

    [Test]
    public async Task DuplicateComboInvalidatesResultsWithoutRecalculatingAndDiscardsInFlightCompletion() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Stale completion", 0.8)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(calculator.CallCount, Is.EqualTo(1));

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DuplicateComboAsync(cut, 0);
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
            Assert.That(calculator.CallCount, Is.EqualTo(2), "duplication must not trigger another calculation");
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        Assert.That(cut.Markup, Does.Not.Contain("Stale completion"));
        Assert.That(calculator.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task CalculationAfterDuplicationIncludesBothActiveCombos() {
        var cut = Render(Session());
        await DuplicateComboAsync(cut, 0);

        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card)
            .Where(card => card.Active).ToList();
        var combos = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo)
            .Where(combo => combo.Active).ToList();
        var expected = SmallDeckOracleTest.EnumerateProbability(cards, combos, 2);

        await Button(cut, "Calculate").ClickAsync(new());

        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
        Assert.That(cut.FindAll(".combo-probability-item .combo-probability-name").Select(name => name.TextContent.Trim()),
            Is.EqualTo(new[] { "First combo", "First combo copy", "Second combo" }));
    }

    [Test]
    public void DeletingEarlierRowsPreservesSurvivingDraftsAndActiveEditors() {
        var cut = Render(Session());
        var card = cut.FindComponents<CardEditor>()[1];
        var combo = cut.FindComponents<ComboEditor>()[1];
        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select").Change("user:A");
        combo.Find("select").Change("user:A");
        combo.Find("#minCount1").Input("0");
        combo.Find("#maxCount1").Input("0");
        cut.FindComponents<CardEditor>()[0].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[0].Find("[title='Remove combo']").Click();
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(1));
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("user:A"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Button(card, "Add").Click();
        Button(combo, "Add").Click();
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Second"));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (None)"));
        card.Find("[title='Remove card']").Click();
        combo.Find("[title='Remove combo']").Click();
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void ReorderingRowsKeepsDraftsWithTheirModels() {
        var session = Session();
        var cards = context.RenderComponent<CardListEditor>(p => p.Add(x => x.Cards, session.Cards)
            .Add(x => x.CategoryBases, session.Categories));
        var combos = context.RenderComponent<ComboListEditor>(p => p.Add(x => x.Combos, session.Combos)
            .Add(x => x.CategoryBases, session.Categories));
        cards.FindComponents<CardEditor>()[0].Find("select").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("select").Change("user:B");
        combos.FindComponents<ComboEditor>()[0].Find("#minCount0").Input("0");
        combos.FindComponents<ComboEditor>()[0].Find("#maxCount0").Input("0");
        session.Cards.Reverse();
        session.Combos.Reverse();
        cards.SetParametersAndRender(p => p.Add(x => x.Cards, session.Cards));
        combos.SetParametersAndRender(p => p.Add(x => x.Combos, session.Combos));
        Assert.That(cards.FindComponents<CardEditor>()[1].Find("select").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(combos.FindComponents<ComboEditor>()[1].Find("#maxCount1").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cards.FindComponents<CardEditor>()[0].Find("select").GetAttribute("value"), Is.Null.Or.Empty);
    }

    [Test]
    public void RemovingConstraintPreservesOtherConstraintsFromLoadedSession() {
        // Session JSON can contain repeated category constraints. Removing B must
        // not silently discard either A constraint (together they require exactly one A).
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 1, 2), new(a, 0, 1), new(b, 0, 2)]));
        var cut = Render(session);
        var combo = cut.FindComponent<ComboEditor>();
        combo.FindAll(".accordion-body .badge button")[2].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(2));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (1–2)").And.Contain("A (1 Max)"));
        combo.FindAll(".accordion-body .badge button")[0].Click();
        Assert.That(combo.FindAll(".accordion-body .badge"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .badge").TextContent, Does.Contain("A (1 Max)"));
    }

    [Test]
    public async Task SessionSaveLoadRoundTripPreservesZeroAndResetsOldEditorDrafts() {
        var session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 0, 0)], "No A"));
        var cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"MaxCount\": 0"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var combo = cut.FindComponent<ComboEditor>();
        await combo.Find("select").ChangeAsync(new() { Value = "user:B" });
        await combo.Find("#minCount0").InputAsync(new() { Value = "2" });
        await combo.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[placeholder='Category name']").InputAsync(new() { Value = "Old draft" });
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        {
            var loaded = cut.FindComponent<ComboEditor>();
            Assert.That(loaded.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(loaded.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
            Assert.That(loaded.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
            Assert.That(cut.Find("[placeholder='Category name']").GetAttribute("value"), Is.Null.Or.Empty);
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".alert-primary").TextContent, Does.Contain((1.0 / 6).ToString("P2")));
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(
            cut.FindComponent<CategoryListEditor>().Find("[role=alert]").TextContent, Does.Contain("still used"));
    }

    [Test]
    public async Task DeckImportReplacesRowsWithoutReusingDraftsAndUpdatesCalculationEligibility() {
        // Exercise the real YDK parser; only the external card-name lookup is stubbed.
        context.Services.AddSingleton<IProbabilityCalculatorService>(
            new SequencedProbabilityCalculator(
                new ProbabilityCalculationResult(
                    0.75,
                    [new ComboProbabilityResult(0, "Unused result", 0.6)])));
        context.Services.AddSingleton<IFileService, FileService>();
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(x => x.GetCardInfoAsync(123)).ReturnsAsync(new CardInfo { Id = 123, Name = "Imported" });
        cardInfo.Setup(x => x.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo>());
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<IDeckImportService, DeckImportService>();
        var cut = Render(Session());
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        await cut.FindComponents<CardEditor>()[0].Find("select").ChangeAsync(new() { Value = "user:B" });
        await cut.FindComponents<CardEditor>()[0].Find(".accordion-button").ClickAsync(new());
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123\n123\n#extra\n456", "deck.ydk"));
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        var card = cut.FindComponent<CardEditor>();
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo(groupId));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Imported").And.Contain("(2)"));
        Assert.That(card.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
    }

    [Test]
    public async Task YdkeImportDisclosureCancelLeavesCurrentDeckUntouched() {
        var session = Session();
        var initialCardIds = session.Cards.Select(card => card.Id).ToArray();
        var cut = Render(session);

        await Button(cut, "Import YDKe").ClickAsync(new());
        Assert.That(Button(cut, "Import YDKe").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("input#ydkeCodeInput").GetAttribute("aria-label"), Is.EqualTo("YDKe deck code"));
        await cut.Find("input#ydkeCodeInput").InputAsync(new() { Value = "ydke://pending!!!" });
        await Button(cut, "Cancel").ClickAsync(new());

        Assert.That(cut.FindAll("input#ydkeCodeInput"), Is.Empty);
        Assert.That(Button(cut, "Import YDKe").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id).ToArray(), Is.EqualTo(initialCardIds));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task YdkeImportUsesImportServiceReplacesDeckAndClearsPreviousCalculation() {
        const string code = "ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!";
        var replacement = new Card([], copies: 3, name: "Imported YDKe card", externalCardId: 89631139);
        var importer = new Mock<IDeckImportService>();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(code)).ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);
        context.Services.AddSingleton<IProbabilityCalculatorService>(
            new SequencedProbabilityCalculator(
                new ProbabilityCalculationResult(0.75, [new ComboProbabilityResult(0, "Unused result", 0.6)])));

        var session = Session();
        session.ComboGroups.Add(new("preserved-group", "Tier 1"));
        session.Combos[0].GroupId = "preserved-group";
        var cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));

        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("input#ydkeCodeInput").InputAsync(new() { Value = code });
        await Button(cut, "Import").ClickAsync(new());

        importer.Verify(service => service.ImportDeckFromYdkeAsync(code), Times.Once);
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        var imported = cut.FindComponent<CardEditor>().Instance.Card;
        Assert.That(imported.Name, Is.EqualTo("Imported YDKe card"));
        Assert.That(imported.ExternalCardId, Is.EqualTo(89631139));
        Assert.That(imported.Copies, Is.EqualTo(3));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo("preserved-group"));
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task YdkeImportFailureKeepsDeckAndCorrectedRetrySucceeds() {
        const string invalidCode = "not a ydke code";
        const string validCode = "ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!";
        var replacement = new Card([], copies: 2, name: "Recovered import", externalCardId: 36996508);
        var importer = new Mock<IDeckImportService>();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(invalidCode))
            .ThrowsAsync(new FormatException("YDKe code must start with 'ydke://'."));
        importer.Setup(service => service.ImportDeckFromYdkeAsync(validCode)).ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);

        var session = Session();
        var initialCardIds = session.Cards.Select(card => card.Id).ToArray();
        var cut = Render(session);
        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("input#ydkeCodeInput").InputAsync(new() { Value = invalidCode });
        await Button(cut, "Import").ClickAsync(new());

        Assert.That(cut.Find("[role='alert']").TextContent, Does.Contain("Failed to import deck").And.Contain("ydke://"));
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id).ToArray(), Is.EqualTo(initialCardIds));
        Assert.That(cut.Find("input#ydkeCodeInput"), Is.Not.Null);

        await cut.Find("input#ydkeCodeInput").InputAsync(new() { Value = validCode });
        await Button(cut, "Import").ClickAsync(new());

        importer.Verify(service => service.ImportDeckFromYdkeAsync(invalidCode), Times.Once);
        importer.Verify(service => service.ImportDeckFromYdkeAsync(validCode), Times.Once);
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Recovered import"));
    }

    [Test]
    public async Task YdkeImportControlsAreLabeledAndUseKeyboardAccessibleFormSemantics() {
        var cut = Render();

        await Button(cut, "Import YDKe").ClickAsync(new());
        var form = cut.Find("form.ydke-import-form");
        var input = cut.Find("form.ydke-import-form input#ydkeCodeInput");

        Assert.Multiple(() => {
            Assert.That(input.GetAttribute("aria-label"), Is.EqualTo("YDKe deck code"));
            Assert.That(cut.Find("label[for='ydkeCodeInput']").TextContent, Is.EqualTo("YDKe deck code"));
            Assert.That(cut.Find("form.ydke-import-form button[type='submit']").TextContent.Trim(), Is.EqualTo("Import"));
            Assert.That(cut.Find("form.ydke-import-form button[type='button']").TextContent.Trim(), Is.EqualTo("Cancel"));
            Assert.That(cut.Find("input#fileInput").GetAttribute("accept"), Is.EqualTo(".ydk"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SessionLoadClearsPreviousResultsAndDiscardsInFlightCompletion(bool failCalculation) {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(0.9, [new ComboProbabilityResult(0, "Old session", 0.8)]),
            failSecond: failCalculation);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Null);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            const string nextSession = """
                {
                  "Categories": [{"Name":"Loaded"}],
                  "Cards": [{"Categories":[{"Name":"Loaded"}],"Copies":3,"Name":"Loaded card"}],
                  "Combos": [{"Categories":[{"BaseCategory":{"Name":"Loaded"},"MinCount":0,"MaxCount":0}],"Name":"Loaded combo"}],
                  "HandSize": 1
                }
                """;
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(nextSession, "next.json"));
            {
                Assert.That(cut.FindAll(".probability-results"), Is.Empty);
                Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("1"));
                Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Loaded card"));
                Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Name, Is.EqualTo("Loaded combo"));
            }
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
    }

    [Test]
    public async Task ResultsShowStandaloneProbabilitiesInOrderForDuplicateAndUnnamedCombos() {
        var session = new SessionState {
            Categories = [a, b],
            Cards = [new([a], 2, "A copies"), new([b], 2, "B copies")],
            Combos = [
                new([new(a, 1, 1)], "Duplicate"),
                new([new(a, 1, 1)], "Duplicate"),
                new([new(b, 0, 0)])
            ],
            HandSize = 2
        };
        var cut = Render(session);
        var expectedTotal = SmallDeckOracleTest.EnumerateProbability(session.Cards, session.Combos, session.HandSize);
        var expectedStandalone = session.Combos
            .Select(combo => SmallDeckOracleTest.EnumerateProbability(session.Cards, [combo], session.HandSize))
            .ToArray();

        await Button(cut, "Calculate").ClickAsync(new());
        {
            var result = cut.Find(".probability-results");
            Assert.That(result.GetAttribute("aria-live"), Is.EqualTo("polite"));
            var totalRow = result.QuerySelector(".probability-total")!;
            Assert.That(totalRow.ClassList.Contains("combo-probability-row"), Is.True);
            Assert.That(totalRow.ParentElement!.ClassList.Contains("probability-results"), Is.True,
                "the summary row must sit outside the numbered combo list");
            Assert.That(totalRow.QuerySelector(".combo-probability-name")!.TextContent.Trim(),
                Is.EqualTo("Any active combo"));
            Assert.That(totalRow.QuerySelector(".combo-probability-value")!.TextContent,
                Is.EqualTo(expectedTotal.ToString("P2")));

            var rows = result.QuerySelectorAll(".combo-probability-item");
            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows.All(row => row.QuerySelector(".combo-probability-row") is not null), Is.True);
            Assert.That(rows.Select(row => row.QuerySelector(".combo-probability-name")!.TextContent),
                Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
            for (var index = 0; index < rows.Length; index++) {
                Assert.That(rows[index].QuerySelector(".combo-probability-value")!.TextContent,
                    Is.EqualTo(expectedStandalone[index].ToString("P2")));
            }
        }
    }

    [Test]
    public void ComboGroupsUseCategoryStyleInlineEditingAndKeepAssignmentsAndComboDrafts() {
        var cut = Render(Session());
        var first = cut.FindComponents<ComboEditor>()[0];
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        first.Find("#comboCategory0").Change("user:B");
        first.Find("#minCount0").Input("0");
        first.Find("#maxCount0").Input("0");

        cut.Find("[aria-label='Add combo group']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        cut.Find("[aria-label='New combo group name']").Input("tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        var groupId = first.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        first.Find("#comboGroup0").Change(groupId);
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Group: Tier 1"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='New combo group name']").Input("Tier 2");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        var secondId = cut.FindAll("#comboGroup0 option:not([value=''])")[1].GetAttribute("value")!;

        cut.Find("[aria-label='Edit group Tier 1']").Click();
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 1);
        cut.Find("[aria-label='New name for group Tier 1']").Input("");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("tier 2");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("Tier One");
        cut.Find("[aria-label='New name for group Tier 1']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 2);
        Assert.That(cut.Find("[aria-label='Edit group Tier One']").TextContent, Is.EqualTo("Tier One"));
        Assert.That(first.Instance.Combo.GroupId,
            Is.EqualTo(groupId));
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Group: Tier One"));
        Assert.That(cut.Find("#comboGroup0").TextContent, Does.Contain("Tier One"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='Edit group Tier 2']").Click();
        var secondRename = cut.Find("[aria-label='New name for group Tier 2']");
        secondRename.Input("Discarded");
        secondRename.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 4);
        Assert.That(cut.Find("[aria-label='Edit group Tier 2']").TextContent, Is.EqualTo("Tier 2"));

        cut.Find("#comboGroup0").Change(secondId);
        Assert.That(first.Instance.Combo.GroupId, Is.EqualTo(secondId));
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Group: Tier 2"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("#comboGroup0").Change("");
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        cut.Find("#comboGroup0").Change(groupId);
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Group: Tier One"));

        cut.Find("[aria-label='Remove group Tier One']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        cut.Find("[aria-label='Remove group Tier 2']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupResultsUseActiveMembersAndAllGroupChangesInvalidateResults() {
        var session = Session();
        var cut = Render(session);
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await cut.Find("#comboGroup1").ChangeAsync(new() { Value = groupId });
        var groupChip = cut.Find(".combo-group-chip");
        Assert.That(groupChip.ClassList.Contains("me-2"), Is.True);
        Assert.That(groupChip.QuerySelector(".category-name-trigger")?.TextContent.Trim(), Is.EqualTo("Tier 1"));

        await RenameGroupWithEnter(cut, "Tier 1", "Tier One");
        await Button(cut, "Calculate").ClickAsync(new());
        {
            var result = cut.Find(".probability-results");
            var active = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo).ToList();
            var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, active, 2);
            Assert.That(result.QuerySelector(".probability-group .combo-probability-value")!.TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(result.QuerySelectorAll(".combo-probability-item").Length, Is.EqualTo(2));
            Assert.That(result.QuerySelector(".probability-group")!.TextContent, Does.Contain("Tier One"));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }

        var secondComboEditor = cut.FindComponents<ComboEditor>()[1];
        await secondComboEditor.Find("#comboActive1").ChangeAsync(new() { Value = false });
        Assert.That(secondComboEditor.Find("#comboActive1").HasAttribute("checked"), Is.False);
        Assert.That(secondComboEditor.Instance.Combo.Active, Is.False);
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        {
            var first = cut.FindComponents<ComboEditor>()[0].Instance.Combo;
            var expected = SmallDeckOracleTest.EnumerateProbability(session.Cards, [first], 2);
            Assert.That(cut.Find(".probability-group .combo-probability-value").TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = "" });
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-group .combo-probability-value").TextContent, Is.EqualTo(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void GroupMembershipAndInactiveStateRoundTripWhileLegacySessionsRemainUngrouped() {
        var cut = Render(Session());
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='Add combo group']").Click();
        var groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        cut.Find("#comboGroup0").Change(groupId);
        cut.Find("#comboActive0").Change(false);
        Button(cut, "Save Session").Click();
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"ComboGroups\"").And.Contain("\"GroupId\""));

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "groups.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo(groupId));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Active, Is.False);
        });

        const string legacy = """
            {"Categories":[{"Name":"A"}],"Cards":[],"Combos":[{"Categories":[],"Name":"Legacy"}],"HandSize":5}
            """;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(legacy, "legacy.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.GroupId, Is.Null);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Active, Is.True);
        });
    }

    [Test]
    public async Task CategoryRenameInvalidatesAndRecalculatesTheWholeResultSet() {
        var cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));

        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed A" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        AssertPreviousResult(cut);

        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card)
            .Where(card => card.Active).ToList();
        var combos = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo)
            .Where(combo => combo.Active).ToList();
        var expected = SmallDeckOracleTest.EnumerateProbability(cards, combos, 2);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-total").TextContent,
            Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task ResultsRemainVisibleAndAreReplacedOnlyWhenRecalculationSucceeds() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Updated combo", 0.6)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        var copyButton = cut.Find("button[title='Copy a summary of these results']");
        Assert.That(copyButton.HasAttribute("disabled"), Is.False);

        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        Assert.That(calculator.CallCount, Is.EqualTo(1), "input edits must not calculate automatically");

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var runningAction = cut.Find(".calculate-action > button");
            Assert.That(runningAction.GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
            Assert.That(runningAction.HasAttribute("disabled"), Is.False);
            Assert.That(cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.75.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Updated combo"));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task InvalidatedInFlightCalculationCannotReplaceThePreviousResult() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Stale completion", 0.8)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        Assert.That(cut.Markup, Does.Not.Contain("Stale completion"));
    }

    [Test]
    public async Task CalculationErrorKeepsOldNumbersMarkedAsPreviousInputs() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Unused result", 0.8)]),
            failSecond: true);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("Calculation failed: expected test failure"));
    }

    [Test]
    public async Task InputChangeDuringCalculationCannotRestoreStaleTotalOrComboRows() {
        var delayedCalculator = new DelayedProbabilityCalculator();
        context.Services.AddSingleton<IProbabilityCalculatorService>(delayedCalculator);
        var cut = Render(Session());

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await delayedCalculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }
        finally {
            delayedCalculator.Continue.Set();
            await calculation;
        }

        Assert.That(calculation.IsCompletedSuccessfully, Is.True, "the stale event handler must finish before checking its result");
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResourceLimitIsExplainedUnlessInputsHaveChanged(bool changeInputs) {
        var calculator = new DelayedProbabilityCalculator { ExceedLimit = true };
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeInputs) await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        finally {
            calculator.Continue.Set();
            await calculation;
        }
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.Markup.Contains("Calculation stopped"), Is.EqualTo(!changeInputs));
    }

    private sealed class CountingProbabilityCalculator : IProbabilityCalculatorService {
        public int CallCount { get; private set; }

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.25;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
            CallCount++;
            return new ProbabilityCalculationResult(
                0.25,
                [new ComboProbabilityResult(0, "Any A", 0.25)]);
        }
    }

    private sealed class DelayedProbabilityCalculator : IProbabilityCalculatorService {
        public bool ExceedLimit { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Continue { get; } = new(false);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
            Started.SetResult();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the delayed calculation.");
            if (ExceedLimit) throw new ProbabilityCalculationLimitException();
            return new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Stale combo", 0.5)]);
        }
    }

    private sealed class SequencedProbabilityCalculator(
        ProbabilityCalculationResult secondResult,
        bool failSecond = false) : IProbabilityCalculatorService {
        private int callCount;

        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ContinueSecond { get; } = new(false);
        public int CallCount => Volatile.Read(ref callCount);

        public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) => 0.75;

        public ProbabilityCalculationResult CalculateProbabilityResults(
            List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1) {
                return new ProbabilityCalculationResult(
                    0.25,
                    [new ComboProbabilityResult(0, "Original combo", 0.2)]);
            }

            SecondStarted.SetResult();
            if (!ContinueSecond.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the second calculation.");
            if (failSecond)
                throw new InvalidOperationException("expected test failure");

            return secondResult;
        }
    }
}
