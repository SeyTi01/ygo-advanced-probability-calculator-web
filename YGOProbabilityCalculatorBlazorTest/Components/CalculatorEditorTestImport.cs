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
public sealed class CalculatorEditorTestImport : CalculatorEditorTestBase {

    [Test]
    public void ImportDoesNotRebindSameNamedCardAndMissingZeroBoundBlocksCalculation() {
        Card original = new([], 2, "Same");
        Card replacement = new([], 2, "Same");
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
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
    public async Task DeckImportReplacesRowsWithoutReusingDraftsAndUpdatesCalculationEligibility() {
        // Exercise the real YDK parser; only the external card-name lookup is stubbed.
        context.Services.AddSingleton<IProbabilityCalculatorService>(
            new SequencedProbabilityCalculator(
                new ProbabilityCalculationResult(
                    0.75,
                    [new ComboProbabilityResult(0, "Unused result", 0.6)])));
        context.Services.AddSingleton<IFileService, FileService>();
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.GetCardInfoAsync(123)).ReturnsAsync(new CardInfo { Id = 123, Name = "Imported" });
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo>());
        context.Services.AddSingleton(cardInfo.Object);
        context.Services.AddSingleton<IDeckImportService, DeckImportService>();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        string groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        await cut.FindComponents<CardEditor>()[0].Find("select").ChangeAsync(new() { Value = "user:B" });
        await cut.FindComponents<CardEditor>()[0].Find(".accordion-button").ClickAsync(new());
        cut.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("#main\n123\n123\n#extra\n456", "deck.ydk"));
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        IRenderedComponent<CardEditor> card = cut.FindComponent<CardEditor>();
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
        SessionState session = Session();
        string[] initialCardIds = session.Cards.Select(card => card.Id).ToArray();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);

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
        Card replacement = new([], copies: 3, name: "Imported YDKe card", externalCardId: 89631139);
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(code)).ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);
        context.Services.AddSingleton<IProbabilityCalculatorService>(
            new SequencedProbabilityCalculator(
                new ProbabilityCalculationResult(0.75, [new ComboProbabilityResult(0, "Unused result", 0.6)])));

        SessionState session = Session();
        session.ComboGroups.Add(new("preserved-group", "Tier 1"));
        session.Combos[0].GroupId = "preserved-group";
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));

        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("input#ydkeCodeInput").InputAsync(new() { Value = code });
        await Button(cut, "Import").ClickAsync(new());

        importer.Verify(service => service.ImportDeckFromYdkeAsync(code), Times.Once);
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Card imported = cut.FindComponent<CardEditor>().Instance.Card;
        Assert.That(imported.Name, Is.EqualTo("Imported YDKe card"));
        Assert.That(imported.ExternalCardId, Is.EqualTo(89631139));
        Assert.That(imported.Copies, Is.EqualTo(3));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo("preserved-group"));
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task CancelledYdkeImportCannotReplaceDeckOrReportLateFailure(bool fail, bool reopen) {
        TaskCompletionSource<List<Card>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(It.IsAny<string>()))
            .Returns(() => {
                started.SetResult();
                return completion.Task;
            });
        context.Services.AddSingleton(importer.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        string[] originalIds = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id).ToArray();
        string originalResults = cut.Find(".probability-results").TextContent;
        int writes = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "ydke://pending!!!" });
        Task import = Button(cut, "Import").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Button(cut, "Cancel").ClickAsync(new());
        // A late completion must also leave a newly opened form alone.
        if (reopen) {
            await Button(cut, "Import YDKe").ClickAsync(new());
            await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "New draft" });
        }

        if (fail) {
            completion.SetException(new InvalidOperationException("Obsolete failure"));
        } else {
            completion.SetResult([new([], name: "Obsolete deck")]);
        }

        await import;

        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id).ToArray(), Is.EqualTo(originalIds));
        Assert.That(cut.Find(".probability-results").TextContent, Is.EqualTo(originalResults));
        if (reopen) {
            Assert.That(cut.Find("#ydkeCodeInput").GetAttribute("value"), Is.EqualTo("New draft"));
        } else {
            Assert.That(cut.FindAll("#ydkeCodeInput"), Is.Empty);
        }

        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
    }

    [Test]
    public async Task YdkeImportFailureKeepsDeckAndCorrectedRetrySucceeds() {
        const string invalidCode = "not a ydke code";
        const string validCode = "ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!";
        Card replacement = new([], copies: 2, name: "Recovered import", externalCardId: 36996508);
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(invalidCode))
            .ThrowsAsync(new FormatException("YDKe code must start with 'ydke://'."));
        importer.Setup(service => service.ImportDeckFromYdkeAsync(validCode)).ReturnsAsync([replacement]);
        context.Services.AddSingleton(importer.Object);

        SessionState session = Session();
        string[] initialCardIds = session.Cards.Select(card => card.Id).ToArray();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
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
    public async Task CancellingOldYdkeFormDoesNotRevokeNewerFileLoad() {
        TaskCompletionSource<List<Card>> importCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IDeckImportService> importer = new();
        importer.Setup(service => service.ImportDeckFromYdkeAsync(It.IsAny<string>())).Returns(importCompletion.Task);
        context.Services.AddSingleton(importer.Object);
        TaskCompletionSource enrichmentStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource enrichmentCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ILegacyCardMetadataEnricher> enricher = new();
        enricher.Setup(service => service.EnrichAsync(It.IsAny<SessionState>())).Returns(Task.CompletedTask);
        context.Services.AddSingleton(enricher.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Import YDKe").ClickAsync(new());
        await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "ydke://pending!!!" });
        Task import = Button(cut, "Import").ClickAsync(new());
        enricher.Setup(service => service.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(() => {
                enrichmentStarted.SetResult();
                return enrichmentCompletion.Task;
            });
        SessionState replacement = new() { Cards = [new([], name: "Newer file")], HandSize = 7 };
        ISessionService sessions = context.Services.GetRequiredService<ISessionService>();
        TaskCompletionSource<string> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.JSInterop.SetupVoid("sessionRecovery.update", call => {
            saved.TrySetResult((string)call.Arguments[2]!);
            return true;
        }).SetVoidResult();
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(sessions.SerializeSession(replacement), "fixture.json"));
        await enrichmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Button(cut, "Cancel").ClickAsync(new());
        importCompletion.SetResult([new([], name: "Obsolete import")]);
        enrichmentCompletion.SetResult();
        await import;
        string payload = await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(payload, Is.EqualTo(sessions.SerializeSession(replacement)));
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Newer file"));
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("7"));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task YdkeImportControlsAreLabeledAndUseKeyboardAccessibleFormSemantics() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();

        await Button(cut, "Import YDKe").ClickAsync(new());
        IElement form = cut.Find("form.ydke-import-form");
        IElement input = cut.Find("form.ydke-import-form input#ydkeCodeInput");

        Assert.Multiple(() => {
            Assert.That(input.GetAttribute("aria-label"), Is.EqualTo("YDKe deck code"));
            Assert.That(cut.Find("label[for='ydkeCodeInput']").TextContent, Is.EqualTo("YDKe deck code"));
            Assert.That(cut.Find("form.ydke-import-form button[type='submit']").TextContent.Trim(), Is.EqualTo("Import"));
            Assert.That(cut.Find("form.ydke-import-form button[type='button']").TextContent.Trim(), Is.EqualTo("Cancel"));
            Assert.That(cut.Find("input#fileInput").GetAttribute("accept"), Is.EqualTo(".ydk"));
        });
    }

}
