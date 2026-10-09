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
public sealed class CalculatorEditorTestSession : CalculatorEditorTestBase {

    [Test]
    public async Task SaveSessionButtonInvokesSessionServiceWithSuggestedJsonName() {
        SessionService codec = new(context.JSInterop.JSRuntime, new JsonSerializer());
        Mock<ISessionService> sessionService = new();
        sessionService.Setup(service => service.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        sessionService.Setup(service => service.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        sessionService.Setup(service => service.SaveSessionAsync(It.IsAny<SessionState>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        context.Services.AddSingleton<ISessionService>(sessionService.Object);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Button(cut, "Save Session").ClickAsync(new());

        sessionService.Verify(
            service => service.SaveSessionAsync(
                It.IsAny<SessionState>(),
                It.Is<string>(name =>
                    name.StartsWith("calculator_session_", StringComparison.Ordinal)
                    && name.EndsWith(".json", StringComparison.Ordinal))
            ),
            Times.Once
        );
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task SaveSessionButtonShowsTheExistingErrorWhenFileWriteFails() {
        SessionService codec = new(context.JSInterop.JSRuntime, new JsonSerializer());
        Mock<ISessionService> sessionService = new();
        sessionService.Setup(service => service.SerializeSession(It.IsAny<SessionState>())).Returns<SessionState>(codec.SerializeSession);
        sessionService.Setup(service => service.LoadSessionAsync(It.IsAny<string>())).Returns<string>(codec.LoadSessionAsync);
        sessionService.Setup(service => service.SaveSessionAsync(It.IsAny<SessionState>(), It.IsAny<string>()))
            .ThrowsAsync(new JSException("Session file write failed."));
        context.Services.AddSingleton<ISessionService>(sessionService.Object);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Button(cut, "Save Session").ClickAsync(new());

        Assert.That(cut.Find("[role='alert']").TextContent,
            Does.Contain("Failed to save session: Session file write failed."));
    }

    [Test]
    public async Task DuplicationRenameAndSessionSaveLoadPreserveBothMaximumModes() {
        SessionState session = Session();
        Combo source = new([new(a, 1, 5, RequirementMaximumMode.HandSize), new(b, 0, 0)], "Modes",
            cards: [new(session.Cards[0].Id, 1, 5, RequirementMaximumMode.HandSize), new(session.Cards[1].Id, 1, 5)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = [source],
            HandSize = 5
        });
        await DuplicateComboAsync(cut, 0);
        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        Assert.That(editors[1].Instance.Combo.Categories.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
        Assert.That(editors[1].Instance.Combo.Cards.Select(c => c.MaximumMode), Is.EqualTo(new[] { RequirementMaximumMode.HandSize, RequirementMaximumMode.Fixed }));
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        foreach (IRenderedComponent<ComboEditor> editor in editors) {
            Assert.That(editor.Instance.Combo.Categories[0].BaseCategory.Name, Is.EqualTo("Renamed"));
            Assert.That(editor.Instance.Combo.Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        }

        await Button(cut, "Save Session").ClickAsync(new());
        string json = SavedSessionJson();
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "modes.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2)));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        foreach (IRenderedComponent<ComboEditor> editor in cut.FindComponents<ComboEditor>()) {
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

    [TestCase("\"Categories\":null")]
    [TestCase("\"Categories\":[null]")]
    [TestCase("\"Combos\":null")]
    [TestCase("\"ComboGroups\":[null]")]
    [TestCase("\"Combos\":[{\"Categories\":[null]}]")]
    [TestCase("\"Combos\":[{\"Categories\":[],\"Cards\":[null]}]")]
    [TestCase("\"Categories\":[{\"Name\":\"Role\"},{\"Name\":\"Role\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":\"One\"},{\"Id\":\"g\",\"Name\":\"Two\"}]")]
    [TestCase("\"ComboGroups\":[{\"Name\":\"Group\"}]")]
    public async Task InvalidFileModelDataCannotPartiallyReplaceWorkingSession(string invalidField) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        await cut.Find("[aria-label='Category name']").InputAsync(new() { Value = "Preserved draft" });
        string before = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        string result = cut.Find(".probability-results").TextContent;
        PinnedResultSnapshot pinned = cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
        int writes = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(
            $"{{\"SchemaVersion\":3,{invalidField}}}", "invalid.json"));
        cut.WaitForState(() => cut.Markup.Contains("Failed to load session", StringComparison.Ordinal));

        Assert.That(cut.FindComponent<SessionShareButton>().Instance.Snapshot, Is.EqualTo(before));
        Assert.That(cut.Find(".probability-results").TextContent, Is.EqualTo(result));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pinned));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Preserved draft"));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
    }

    [Test]
    public async Task MixedComboSessionRoundTripKeepsCardIdentityAndGroup() {
        Card card = new([a], 2, "Piece");
        SessionState session = new() {
            Categories = [a], Cards = [card],
            Combos = [new([new(a, 1, 2)], "Mixed", groupId: "g", cards: [new(card.Id, 1, 2)])],
            ComboGroups = [new("g", "Group")], HandSize = 1
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        JSRuntimeInvocation invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain(card.Id).And.Contain("\"Cards\""));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "mixed.json"));
        Card loadedCard = cut.FindComponent<CardEditor>().Instance.Card;
        Combo loadedCombo = cut.FindComponent<ComboEditor>().Instance.Combo;
        Assert.That(loadedCard.Id, Is.EqualTo(card.Id));
        Assert.That(loadedCombo.Cards.Single().CardId, Is.EqualTo(card.Id));
        Assert.That(loadedCombo.GroupId, Is.EqualTo("g"));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "2" });
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent,
            Does.Contain(1.0.ToString("P2")));
    }

    [Test]
    public async Task SessionSaveLoadRoundTripPreservesZeroAndResetsOldEditorDrafts() {
        SessionState session = Session();
        session.Combos.Clear();
        session.Combos.Add(new Combo([new(a, 0, 0)], "No A"));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Save Session").ClickAsync(new());
        JSRuntimeInvocation invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"MaxCount\": 0"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        IRenderedComponent<ComboEditor> combo = cut.FindComponent<ComboEditor>();
        await combo.Find("select").ChangeAsync(new() { Value = "user:B" });
        await combo.Find("#minCount0").InputAsync(new() { Value = "2" });
        await combo.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[placeholder='Category name']").InputAsync(new() { Value = "Old draft" });
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "session.json"));
        IRenderedComponent<ComboEditor> loaded = cut.FindComponent<ComboEditor>();
        Assert.That(loaded.Find("select").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(loaded.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(loaded.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(cut.Find("[placeholder='Category name']").GetAttribute("value"), Is.Null.Or.Empty);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".alert-primary").TextContent, Does.Contain((1.0 / 6).ToString("P2")));
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(
            cut.FindComponent<CategoryListEditor>().Find("[role=alert]").TextContent, Does.Contain("still used"));
    }

}
