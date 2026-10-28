using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Components.Shared;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Pages;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Localization;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class SessionLoadingTest {
    private Bunit.TestContext _context = null!;
    private ISessionService _sessions = null!;
    private Mock<ICardInfoService> _catalog = null!;
    private Mock<IFileService> _files = null!;
    private Mock<IDeckImportService> _imports = null!;
    private NavigationManager Navigation => _context.Services.GetRequiredService<NavigationManager>();
    private IPendingSessionService Pending => _context.Services.GetRequiredService<IPendingSessionService>();

    [SetUp]
    public void Setup() {
        _context = new();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<ISerializer, JsonSerializer>();
        _context.Services.AddSingleton<ISessionService, SessionService>();
        _context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        _context.Services.AddSingleton(Mock.Of<IBackgroundCalculator>());
        _context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        _context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        _catalog = new();
        _catalog.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<CardInfo>());
        _catalog.Setup(service => service.GetCatalogCardsAsync()).ReturnsAsync(Array.Empty<CardInfo>());
        _files = new();
        _imports = new();
        _context.Services.AddSingleton(_catalog.Object);
        _context.Services.AddSingleton(_files.Object);
        _context.Services.AddSingleton(_imports.Object);
        _sessions = _context.Services.GetRequiredService<ISessionService>();
    }

    [TearDown]
    public void Cleanup() => _context.Dispose();

    private static SessionState Session(string name) => new() {
        Cards = [new([], 3, name, id: name, externalCardId: 1234)]
    };

    private IRenderedComponent<ProbabilityCalculatorComponent> Mount() {
        Pending.PendingSession = Session("Initial");
        return _context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    private Task Upload(IRenderedComponent<ProbabilityCalculatorComponent> cut, string json, Stream? stream = null) {
        Mock<IBrowserFile> file = new();
        file.Setup(browserFile => browserFile.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => stream ?? new MemoryStream(Encoding.UTF8.GetBytes(json)));
        return cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
    }

    private Task Upload(IRenderedComponent<ProbabilityCalculatorComponent> cut, SessionState session) => Upload(cut, _sessions.SerializeSession(session));

    private TaskCompletionSource<IReadOnlyList<CardInfo>> HoldCatalog() {
        TaskCompletionSource<IReadOnlyList<CardInfo>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _catalog.Setup(service => service.GetCatalogCardsAsync()).Returns(gate.Task);
        return gate;
    }

    private static void Loading(IRenderedFragment cut, bool active) {
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".session-loading-content").Count, Is.EqualTo(active ? 1 : 0));
            if (active) {
                Assert.That(cut.Find(".session-loading-status[role='status'][aria-live='polite']").TextContent.Trim(), Is.EqualTo("Loading session…"));
                Assert.That(cut.Find(".session-loading-content .spinner-border").GetAttribute("aria-hidden"), Is.EqualTo("true"));
            }
        });
    }

    [Test]
    public async Task FileStatusBeginsBeforePendingReadAndEndsOnSuccess() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Mount();
        GatedStream stream = new(_sessions.SerializeSession(Session("Loaded")));
        Task load = Upload(cut, "", stream);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Loading(cut, true);
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Initial"));
        Assert.That(cut.Find("#sessionFileInput").HasAttribute("disabled"), Is.False);
        stream.Gate.SetResult();
        await load;
        Loading(cut, false);
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Loaded"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedFileClearsStatusAndKeepsAcceptedWorkspace(bool readFailure) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Mount();
        GatedStream stream = new("malformed JSON");
        Task load = Upload(cut, "", stream);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Loading(cut, true);
        if (readFailure) {
            stream.Gate.SetException(new IOException("Fixture read failure"));
        }
        else {
            stream.Gate.SetResult();
        }
        await load;
        Loading(cut, false);
        Assert.That(cut.Markup, Does.Contain("Failed to load session"));
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Initial"));
    }

    [Test]
    public async Task PendingExampleRendersBeforePaintBoundaryAndCatalogCompletion() {
        TaskCompletionSource<IReadOnlyList<CardInfo>> gate = HoldCatalog();
        Bunit.JSRuntimeInvocationHandler paint = _context.JSInterop.SetupVoid("sessionLoading.beforeWork");
        Pending.PendingSession = Session("Example");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        Loading(cut, true);
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        _catalog.Verify(service => service.GetCatalogCardsAsync(), Times.Never);
        paint.SetVoidResult();
        cut.WaitForAssertion(() => _catalog.Verify(service => service.GetCatalogCardsAsync(), Times.Once));
        Loading(cut, true);
        gate.SetResult([]);
        Loading(cut, false);
        cut.WaitForAssertion(() => Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Example")));
    }

    [TestCase("edit")]
    [TestCase("draft")]
    [TestCase("name")]
    [TestCase("clear")]
    [TestCase("navigation")]
    [TestCase("dispose")]
    public async Task InvalidatedPendingFileNeverRecreatesStatusOrOverwritesWorkspace(string action) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Mount();
        if (action == "name") {
            // A visible error makes name edits render their owner while deferring row updates.
            await Upload(cut, "malformed JSON");
        }
        TaskCompletionSource<IReadOnlyList<CardInfo>> gate = HoldCatalog();
        Task load = Upload(cut, Session("Obsolete"));
        Loading(cut, true);
        if (action == "edit") {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (action == "draft") {
            await cut.Find("#quickCardName").InputAsync(new() { Value = "Unsubmitted draft" });
            // bUnit does not bubble DOM events to the workspace's draft observer.
            await cut.Find(".calculator-workspace").InputAsync(new() { Value = "Unsubmitted draft" });
        }
        else if (action == "name") {
            IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
            await editor.Find(".accordion-button").ClickAsync(new());
            await editor.Find("input[id^='cardName']").InputAsync(new() { Value = "Renamed while loading" });
        }
        else if (action == "clear") {
            await cut.Find("button[aria-label='More workspace actions']").ClickAsync(new());
            await cut.FindAll("button").Single(button => button.TextContent.Trim() == "Clear workspace").ClickAsync(new());
            await cut.Find(".clear-workspace-confirmation .btn-danger").ClickAsync(new());
        }
        else if (action == "navigation") {
            await cut.InvokeAsync(() => Navigation.NavigateTo(SessionShareCodec.CreateLink(Navigation.BaseUri, _sessions.SerializeSession(Session("Offer")))));
        }
        else {
            await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
            // A disposed component cannot rerender; observe the owned state directly.
            await cut.InvokeAsync(() => cut.Render());
        }

        Loading(cut, false);
        gate.SetResult([]);
        await load;
        Loading(cut, false);
        Assert.That(cut.FindComponents<CardEditor>().Any(editor => editor.Instance.Card.Name == "Obsolete"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OlderCompletionCannotHideOrOverwriteNewerFile(bool failOld) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Mount();
        GatedStream first = new(_sessions.SerializeSession(Session("A")));
        GatedStream second = new(_sessions.SerializeSession(Session("B")));
        Task a = Upload(cut, "", first);
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        long? aVersion = cut.FindComponent<SessionLoadingStatus>().Instance.RequestVersion;
        Task b = Upload(cut, "", second);
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        long? bVersion = cut.FindComponent<SessionLoadingStatus>().Instance.RequestVersion;
        Assert.That(bVersion, Is.GreaterThan(aVersion));
        if (failOld) {
            first.Gate.SetException(new IOException("Obsolete error"));
        }
        else {
            first.Gate.SetResult();
        }
        await a;
        Loading(cut, true);
        Assert.That(cut.FindComponent<SessionLoadingStatus>().Instance.RequestVersion, Is.EqualTo(bVersion));
        Assert.That(cut.Markup, Does.Not.Contain("Obsolete error"));
        second.Gate.SetResult();
        await b;
        Loading(cut, false);
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("B"));
        for (int i = 0; i < 3; i++) {
            await Upload(cut, Session($"Rapid {i}"));
            Loading(cut, false);
        }
    }

    [TestCase("success")]
    [TestCase("dismiss")]
    [TestCase("navigation")]
    public async Task SharedOfferHasNoLoaderUntilAcceptedAndRevokesItOnDismissalOrNavigation(string action) {
        Navigation.NavigateTo(SessionShareCodec.CreateLink(Navigation.BaseUri, _sessions.SerializeSession(Session("Shared"))));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        Loading(cut, false);
        TaskCompletionSource<IReadOnlyList<CardInfo>> gate = HoldCatalog();
        Task load = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Load shared session").ClickAsync(new());
        Loading(cut, true);
        if (action == "dismiss") {
            await cut.FindAll("button").Single(button => button.TextContent.Trim() == "Dismiss shared link").ClickAsync(new());
            Loading(cut, false);
        }
        else if (action == "navigation") {
            await cut.InvokeAsync(() => Navigation.NavigateTo("/help"));
            Loading(cut, false);
        }
        gate.SetResult([]);
        await load;
        Loading(cut, false);
        Assert.That(cut.FindComponents<CardEditor>().Count, Is.EqualTo(action == "success" ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RecoveryUsesOneCommonStatusAndEditRejectsLateRestoration(bool edit) {
        _context.JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true)
            .SetResult(new() { Exists = true, Payload = _sessions.SerializeSession(Session("Recovered")) });
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        TaskCompletionSource<IReadOnlyList<CardInfo>> gate = HoldCatalog();
        Task load = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Restore previous session").ClickAsync(new());
        Loading(cut, true);
        if (edit) {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            Loading(cut, false);
        }
        gate.SetResult([]);
        await load;
        Loading(cut, false);
        Assert.That(cut.FindComponents<CardEditor>().Count, Is.EqualTo(edit ? 0 : 1));
        if (edit) {
            Assert.That(cut.Markup, Does.Contain("Current work changed while restoring"));
        }
    }

    [Test]
    public async Task DeckImportSupersedesSessionWithoutShowingSessionStatus() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Mount();
        GatedStream stream = new(_sessions.SerializeSession(Session("Old session")));
        Task old = Upload(cut, "", stream);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Loading(cut, true);
        TaskCompletionSource<List<Card>> deck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _imports.Setup(service => service.ImportDeckFromYdkeAsync(It.IsAny<string>())).Returns(deck.Task);
        await cut.Find("button[aria-label='Import deck']").ClickAsync(new());
        await cut.FindAll("button").Single(button => button.TextContent.Trim() == "Import YDKe").ClickAsync(new());
        Task import = cut.Find(".ydke-import-form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Loading(cut, false);
        stream.Gate.SetResult();
        await old;
        deck.SetResult(Session("Deck").Cards);
        await import;
        Loading(cut, false);
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Deck"));
    }

    [TestCase("success")]
    [TestCase("error")]
    [TestCase("navigate")]
    public async Task HelpShowsPendingFileStatusAndCannotNavigateAfterDisposal(string action) {
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _files.Setup(service => service.ReadAllTextAsync(It.IsAny<string>())).Returns(gate.Task);
        IRenderedComponent<Help> cut = _context.RenderComponent<Help>();
        Task load = cut.Find("button").ClickAsync(new());
        Loading(cut, true);
        Assert.That(cut.Find("button").HasAttribute("disabled"), Is.True);
        await cut.Find("button").ClickAsync(new());
        _files.Verify(service => service.ReadAllTextAsync("sample-data/example_session_state.json"), Times.Once);
        if (action == "navigate") {
            await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());
        }
        if (action == "error") {
            gate.SetException(new IOException("Fixture file failure"));
        }
        else {
            gate.SetResult(_sessions.SerializeSession(Session("Example")));
        }
        await load;
        if (action != "navigate") {
            Loading(cut, false);
        }
        Assert.That(Pending.PendingSession?.Cards[0].Name, Is.EqualTo(action == "success" ? "Example" : null));
        if (action == "error") {
            Assert.That(cut.Markup, Does.Contain("Fixture file failure"));
        }
        if (action == "success") {
            Assert.That(Navigation.Uri, Is.EqualTo(Navigation.BaseUri));
        }
    }

    [TestCase("en")]
    [TestCase("de")]
    public void HelpKeepsOneExampleActionBeforeCollapsedSemanticTopics(string language) {
        using PresentationLocale locale = new(language, production: true);
        IRenderedComponent<Help> cut = _context.RenderComponent<Help>();
        Assert.That(cut.FindAll(".example-session-action button"), Has.Count.EqualTo(1));
        Assert.That(cut.Find(".help-content > section .example-session-action"), Is.Not.Null);
        Assert.That(cut.Find("button").GetAttribute("aria-describedby"), Is.EqualTo("example-session-warning"));
        Assert.That(cut.FindAll("details.help-topic"), Has.Count.EqualTo(9));
        Assert.That(cut.FindAll("details[open]"), Is.Empty);
        Assert.That(cut.FindAll("details > summary > h2"), Has.Count.EqualTo(9));
        Assert.That(cut.Find("footer a").GetAttribute("href"), Does.EndWith("/docs/calculation-model.md"));
        string[] topics = ["GettingStarted", "Properties", "Requirements", "Simulations", "EffectResolution", "Modes", "Results", "Sessions", "Example"];
        Assert.That(cut.FindAll("details > summary > h2").Select(heading => heading.TextContent),
            Is.EqualTo(topics.Select(topic => UiText.Get($"Help.{topic}.Question"))));
        Assert.That(cut.Find(".example-session-action button").TextContent, Is.EqualTo(UiText.Get("Help.TryExample")));
        Assert.That(cut.Find("#effect-resolution .help-topic-body p:last-child a").TextContent,
            Is.EqualTo(UiText.Get("Help.EffectResolution.ModelScope.Part0")));
        Assert.That(cut.Find(".help-content").TextContent, Does.Not.Contain("{0}").And.Not.Contain("Help."));
        Assert.That(cut.Find("#example .help-topic-body").TextContent, Does.Contain("565375/658008"));
    }

    [Test]
    public async Task HelpRevealsInitialAndSubsequentFragmentsAndUnsubscribesOnDisposal() {
        BunitJSModuleInterop module = _context.JSInterop.SetupModule("./Pages/Help.razor.js");
        module.SetupVoid("revealTopic", _ => true).SetVoidResult();
        Navigation.NavigateTo("/help#requirements");
        IRenderedComponent<Help> cut = _context.RenderComponent<Help>();
        Assert.That(module.Invocations["revealTopic"].Last().Arguments[0], Is.EqualTo("#requirements"));
        await cut.InvokeAsync(() => Navigation.NavigateTo("/help#sessions"));
        Assert.That(module.Invocations["revealTopic"].Last().Arguments[0], Is.EqualTo("#sessions"));
        await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());
        int calls = module.Invocations["revealTopic"].Count;
        await cut.InvokeAsync(() => Navigation.NavigateTo("/help#results"));
        Assert.That(module.Invocations["revealTopic"], Has.Count.EqualTo(calls));
    }

    private sealed class GatedStream(string json) : MemoryStream(Encoding.UTF8.GetBytes(json)) {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            Started.TrySetResult();
            await Gate.Task;

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
