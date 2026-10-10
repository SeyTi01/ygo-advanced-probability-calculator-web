using Bunit;
using Bunit.TestDoubles;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Shared;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class SessionSharingTest {
    private Bunit.TestContext _context = null!;
    private ISessionService _sessions = null!;
    private NavigationManager _navigation = null!;
    private Mock<ILegacyCardMetadataEnricher> _enricher = null!;
    private Mock<IBackgroundCalculator> _background = null!;

    [SetUp]
    public void Setup() {
        _context = new();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<ISerializer, JsonSerializer>();
        _context.Services.AddSingleton<ISessionService, SessionService>();
        _context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        _context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        _background = new();
        _background.Setup(calculator => calculator.CalculateAsync(
                It.IsAny<CalculationSnapshot>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns((CalculationSnapshot snapshot, CancellationToken cancellation) =>
                new BackgroundCalculatorTestAdapter(new ProbabilityCalculatorService())
                    .CalculateAsync(snapshot, cancellation));
        _context.Services.AddSingleton(_background.Object);
        _context.Services.AddSingleton(Mock.Of<IDeckImportService>());
        _context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<CardInfo>());
        _context.Services.AddSingleton(cardInfo.Object);
        _enricher = new();
        _enricher.Setup(enricher => enricher.EnrichAsync(It.IsAny<SessionState>())).Returns(Task.CompletedTask);
        _context.Services.AddSingleton(_enricher.Object);
        _sessions = _context.Services.GetRequiredService<ISessionService>();
        _navigation = _context.Services.GetRequiredService<NavigationManager>();
    }

    [TearDown]
    public void Cleanup() => _context.Dispose();

    private static SessionState Session(string name = "Shared") => new() {
        HandSize = 3,
        Categories = [new("Role")],
        Cards = [new([], 3, name, false, "stable-id")],
        Combos = [new([], "Incomplete", false)],
        ComboGroups = [new("g", "Group")],
        CategoryColorIndices = new() { ["Role"] = 3 }
    };

    private string Link(SessionState session) => SessionShareCodec.CreateLink(
        _navigation.BaseUri,
        _sessions.SerializeSession(session)
    );

    private void Recovery(string name = "Recovered") {
        Bunit.JSRuntimeInvocationHandler<SessionRecovery.Inspection?> inspection = _context.JSInterop
            .Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true);
        inspection.SetResult(new() {
            Exists = true,
            Payload = _sessions.SerializeSession(Session(name))
        });
    }

    private static AngleSharp.Dom.IElement Button(IRenderedFragment cut, string text) => cut.FindAll("button")
        .Single(button => button.TextContent.Trim().EndsWith(text, StringComparison.Ordinal));

    private static string Snapshot(IRenderedComponent<ProbabilityCalculatorComponent> cut) => cut.FindComponent<SessionShareButton>().Instance.Snapshot;
    private int Writes => _context.JSInterop.Invocations["sessionRecovery.update"].Count;
    private Task LoadFile(IRenderedComponent<ProbabilityCalculatorComponent> cut, string json) {
        Mock<IBrowserFile> file = new();
        file.Setup(browserFile => browserFile.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        return cut.InvokeAsync(() => {
            IRenderedComponent<InputFile> sessionInput = cut.FindComponents<InputFile>()[1];
            InputFileChangeEventArgs change = new([file.Object]);
            return sessionInput.Instance.OnChange.InvokeAsync(change);
        });
    }

    [Test]
    public async Task InitialOfferKeepsRecoveryAndPendingExampleUntilExplicitAcceptance() {
        Recovery();
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Session("Example");
        _navigation.NavigateTo(Link(Session()));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Load shared session").And.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
        TaskCompletionSource<string> queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.JSInterop.SetupVoid("sessionRecovery.update", invocation => {
            queued.TrySetResult((string)invocation.Arguments[2]!);
            return true;
        }).SetVoidResult();
        await Button(cut, "Load shared session").ClickAsync(new());
        string bytes = await queued.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(bytes, Is.EqualTo(_sessions.SerializeSession(Session())));
        Assert.That(Snapshot(cut), Is.EqualTo(bytes));
        Assert.That(_navigation.Uri, Does.Not.Contain("#"));
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
    }

    [Test]
    public async Task InspectionPendingDisablesLoadingAndNeverQueuesOrDiscards() {
        Bunit.JSRuntimeInvocationHandler<SessionRecovery.Inspection?> inspection = _context.JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true);
        _navigation.NavigateTo(Link(Session()));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.True);
        Assert.That(Writes, Is.Zero);
        inspection.SetResult(new() { Exists = true, Payload = _sessions.SerializeSession(Session("Recovered")) });
        cut.WaitForState(() => !Button(cut, "Load shared session").HasAttribute("disabled"));
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        Assert.That(Writes, Is.Zero);
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
    }

    [Test]
    public async Task LaterNavigationPreservesDraftAndDismissedLinkCanBeOfferedAgain() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        await Button(cut, "Add New Card").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Unsaved draft");
        string before = Snapshot(cut);
        int writes = Writes;
        string link = Link(Session());
        await cut.InvokeAsync(() => _navigation.NavigateTo(link));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Unsaved draft"));
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(Writes, Is.EqualTo(writes));
        await cut.InvokeAsync(() => _navigation.NavigateTo(link));
        Assert.That(cut.Markup, Does.Contain("Load shared session"));
        await cut.InvokeAsync(() => _navigation.NavigateTo(_navigation.BaseUri + "#ordinary-anchor"));
        Assert.That(cut.Markup, Does.Not.Contain("Load shared session"));
        Assert.That(_navigation.Uri, Does.EndWith("#ordinary-anchor"));
    }

    [TestCase("edit")]
    [TestCase("reverted buttons")]
    [TestCase("link")]
    [TestCase("file")]
    [TestCase("recovery")]
    [TestCase("dismiss")]
    [TestCase("dispose")]
    public async Task NewerActionsFenceDelayedSharedEnrichment(string action) {
        Recovery();
        _navigation.NavigateTo(Link(Session("Old link")));
        IRenderedComponent<SharingHost> host = _context.RenderComponent<SharingHost>(parameters =>
            parameters.Add(component => component.Visible, true));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = host.FindComponent<ProbabilityCalculatorComponent>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _enricher.Setup(enricher => enricher.EnrichAsync(It.Is<SessionState>(session => session.Cards[0].Name == "Old link")))
            .Returns(() => {
                started.SetResult();
                return completion.Task;
            });
        Task loading = Button(cut, "Load shared session").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        switch (action) {
            case "edit":
                cut.Find("#handSize").Input("6");
                cut.Find("#handSize").Change("6");
                break;
            case "reverted buttons":
                await Button(cut, "Add New Card").ClickAsync(new());
                await cut.Find("[aria-label='Remove card']").ClickAsync(new());
                break;
            case "link":
                await cut.InvokeAsync(() => _navigation.NavigateTo(Link(Session("New link"))));
                break;
            case "file":
                cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(_sessions.SerializeSession(Session("File")), "session.json"));
                break;
            case "recovery":
                await Button(cut, "Restore previous session").ClickAsync(new());
                break;
            case "dismiss":
                await Button(cut, "Dismiss shared link").ClickAsync(new());
                break;
            case "dispose":
                host.SetParametersAndRender(parameters => parameters.Add(component => component.Visible, false));
                break;
        }
        completion.SetResult();
        await loading;
        if (action == "dispose") {
            Assert.That(Writes, Is.Zero);
            return;
        }

        string snapshot = Snapshot(cut);
        Assert.That(snapshot, Does.Not.Contain("Old link"));
        if (action == "file") {
            Assert.That(snapshot, Does.Contain("File"));
        }

        if (action == "recovery") {
            Assert.That(snapshot, Does.Contain("Recovered"));
        }

        if (action == "link") {
            await Button(cut, "Load shared session").ClickAsync(new());
            Assert.That(Snapshot(cut), Does.Contain("New link"));
        }
    }

    [Test]
    public async Task LinkArrivalFencesAnOlderFileAndUnmountUnsubscribes() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _enricher.Setup(enricher => enricher.EnrichAsync(It.IsAny<SessionState>())).Returns(() => {
            started.TrySetResult();
            return completion.Task;
        });
        Task file = LoadFile(cut, _sessions.SerializeSession(Session("Old file")));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => _navigation.NavigateTo(Link(Session())));
        completion.SetResult();
        await file;
        Assert.That(Snapshot(cut), Does.Not.Contain("Old file"));
        cut.Dispose();
        Assert.DoesNotThrow(() => _navigation.NavigateTo(Link(Session("Later"))));
    }

    [TestCase("#ygo-session=v999.abc")]
    [TestCase("#ygo-session=v1.%ZZ")]
    public async Task InvalidLinksPreserveAcceptedStateAndRecovery(string fragment) {
        Recovery();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        string before = Snapshot(cut);
        await cut.InvokeAsync(() => _navigation.NavigateTo(_navigation.BaseUri + fragment));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Shared session"));
        Assert.That(Writes, Is.Zero);
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [Test]
    public async Task ClipboardDenialExposesExactCapturedLinkAndDraftIsExcluded() {
        _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        await Button(cut, "Add New Card").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Not saved");
        string before = Snapshot(cut);
        await Button(cut, "Copy share link").ClickAsync(new());
        string link = cut.Find("textarea[readonly]").TextContent;
        Assert.That(link, Is.EqualTo(Link(await _sessions.LoadSessionAsync(before))));
        Assert.That(link, Does.Not.Contain("?"));
        Assert.That(cut.Markup, Does.Contain("Select and copy this link").And.Not.Contain("Share link copied."));
        Assert.That(_context.JSInterop.Invocations["saveSessionFile"], Is.Empty);
    }

    [TestCase("\"Categories\":[{\"Name\":\"Role\"},{\"Name\":\"Role\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":\"One\"},{\"Id\":\"g\",\"Name\":\"Two\"}]")]
    public async Task InvalidEditorIdentitiesCannotBecomeAnAcceptableShareOffer(string invalidField) {
        Recovery();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        string before = Snapshot(cut);
        string link = SessionShareCodec.CreateLink(_navigation.BaseUri, $"{{\"SchemaVersion\":3,{invalidField}}}");
        await cut.InvokeAsync(() => _navigation.NavigateTo(link));
        Assert.That(cut.Markup, Does.Contain("The shared session is invalid"));
        Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Load shared session"), Is.False);
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(Writes, Is.Zero);
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [Test]
    public async Task OfferAndInvalidLinkKeepCalculationRunningAndAcceptanceCancelsAtCommit() {
        CategoryBase role = new("Role");
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = new() {
            HandSize = 1, Categories = [role], Cards = [new([role], 3, "Working")], Combos = [new([new(role, 1, 1)], "Working route")]
        };
        TaskCompletionSource<ProbabilityCalculationResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        _background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken cancellation) => {
                token = cancellation;
                return result.Task;
            });
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        await cut.InvokeAsync(() => _navigation.NavigateTo(_navigation.BaseUri + "#ygo-session=v1.%ZZ"));
        Assert.That(token.IsCancellationRequested, Is.False);
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        await cut.InvokeAsync(() => _navigation.NavigateTo(Link(Session())));
        Assert.That(token.IsCancellationRequested, Is.False);
        Assert.That(cut.Find("[aria-label='Cancel calculation']").HasAttribute("disabled"), Is.False);
        await Button(cut, "Load shared session").ClickAsync(new());
        Assert.That(token.IsCancellationRequested, Is.True);
        result.SetResult(new(.5, [new(0, "Obsolete", .5)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(Snapshot(cut), Is.EqualTo(_sessions.SerializeSession(Session())));
    }

    [Test]
    public async Task ALinkFencesAnOlderStartupExampleWithoutLosingRecovery() {
        Recovery();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _enricher.Setup(enricher => enricher.EnrichAsync(It.IsAny<SessionState>())).Returns(completion.Task);
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Session("Old example");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        await cut.InvokeAsync(() => _navigation.NavigateTo(Link(Session())));
        completion.SetResult();
        cut.WaitForState(() => cut.Markup.Contains("Restore previous session", StringComparison.Ordinal));
        Assert.That(Snapshot(cut), Does.Not.Contain("Old example"));
        Assert.That(Writes, Is.Zero);
        Assert.That(cut.Markup, Does.Contain("Load shared session"));
    }

    [Test]
    public async Task DelayedClipboardCompletionCannotAnnounceAnObsoleteSnapshot() {
        Bunit.JSRuntimeInvocationHandler<bool> clipboard = _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true);
        string snapshot = _sessions.SerializeSession(Session());
        IRenderedComponent<SessionShareButton> cut = _context.RenderComponent<SessionShareButton>(parameters => {
            parameters
                .Add(component => component.Snapshot, snapshot)
                .Add(component => component.Capture, () => snapshot);
        });
        Task copy = Button(cut, "Copy share link").ClickAsync(new());
        snapshot = _sessions.SerializeSession(Session("Newer accepted state"));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.Snapshot, snapshot));
        clipboard.SetResult(true);
        await copy;
        Assert.That(cut.Markup, Does.Not.Contain("Share link copied."));
        Assert.That(cut.FindAll("textarea"), Is.Empty);
    }

    [Test]
    public async Task OversizedSessionOffersFileSharingAndNeverTouchesClipboard() {
        string snapshot = new('a', SessionShareCodec.MaxJsonBytes + 1);
        IRenderedComponent<SessionShareButton> cut = _context.RenderComponent<SessionShareButton>(parameters => {
            parameters
                .Add(component => component.Snapshot, snapshot)
                .Add(component => component.Capture, () => snapshot);
        });
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("Share a normal session file instead"));
        Assert.That(_context.JSInterop.Invocations["sessionSharing.copy"], Is.Empty);
        Assert.That(cut.FindAll("textarea"), Is.Empty);
    }

    [TestCase("success")]
    [TestCase("denied")]
    [TestCase("unavailable")]
    public async Task OutboundCopyPreservesWorkspaceDraftResultsPinNavigationAndRecovery(string outcome) {
        Bunit.JSRuntimeInvocationHandler<bool> clipboard = _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true);
        if (outcome == "unavailable") {
            clipboard.SetException(new JSException("Clipboard unavailable"));
        }
        else {
            clipboard.SetResult(outcome == "success");
        }
        TaskCompletionSource saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.JSInterop.SetupVoid("sessionRecovery.update", _ => {
            saved.TrySetResult();
            return true;
        }).SetVoidResult();
        CategoryBase role = new("Role");
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = new() {
            HandSize = 1, Categories = [role], Cards = [new([role], 3, "Working")],
            Combos = [new([new(role, 1, 1)], "Working route")]
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Unsaved draft");
        string before = Snapshot(cut);
        IRenderedComponent<PinnedResultPanel> results = cut.FindComponent<PinnedResultPanel>();
        PinnedResultSnapshot current = results.Instance.Current!;
        double currentTotal = current.Total;
        PinnedCalculationContext currentContext = current.Context;
        (Guid Lineage, string Signature)[] currentComboDefinitions = current.Context.Combos.Select(row => (row.Lineage, row.Signature)).ToArray();
        ResultObservation[] currentRows = CurrentRows(cut);
        PinnedResultSnapshot pin = results.Instance.Snapshot;
        PinnedObservation pinnedValues = PinnedValues(pin);
        string uri = _navigation.Uri;
        int history = ((FakeNavigationManager)_navigation).History.Count;
        int writes = Writes;
        int suspensions = _context.JSInterop.Invocations["sessionRecovery.suspend"].Count;
        FeedbackClock clock = new();
        cut.FindComponent<SessionShareButton>().SetParametersAndRender(parameters => parameters
            .Add(component => component.FeedbackTimeProvider, clock)
        );

        for (int i = 0; i < 2; i++) {
            await Button(cut, "Copy share link").ClickAsync(new());
            string copied = (string)_context.JSInterop.Invocations["sessionSharing.copy"].Last().Arguments[0]!;
            Assert.That(copied, Is.EqualTo(Link(await _sessions.LoadSessionAsync(before))));
            if (outcome == "success") {
                Assert.That(cut.Markup, Does.Contain("Share link copied."));
                await cut.InvokeAsync(clock.Expire);
                Assert.That(cut.Markup, Does.Not.Contain("Share link copied."));
            }
            else {
                Assert.That(cut.Find("textarea[readonly]").TextContent, Is.EqualTo(copied));
                await cut.Find("textarea[readonly]").ClickAsync(new());
                Assert.That(_context.JSInterop.Invocations["sessionSharing.select"], Has.Count.EqualTo(i + 1));
            }
            Assert.That(Snapshot(cut), Is.EqualTo(before));
            Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Unsaved draft"));
            Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("#probabilityResultsHeading").TextContent, Is.EqualTo("Current result"));
            Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Current, Is.Not.Null);
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Current!.Total, Is.EqualTo(currentTotal));
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Current!.Context, Is.EqualTo(currentContext));
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Current!.Context.Combos
                .Select(row => (row.Lineage, row.Signature)).ToArray(), Is.EqualTo(currentComboDefinitions));
            Assert.That(CurrentRows(cut), Is.EqualTo(currentRows));
            Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pin));
            AssertPinnedValues(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, pinnedValues);
            Assert.That(_navigation.Uri, Is.EqualTo(uri));
            Assert.That(((FakeNavigationManager)_navigation).History, Has.Count.EqualTo(history));
            Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
            Assert.That(Writes, Is.EqualTo(writes));
            Assert.That(_context.JSInterop.Invocations["sessionRecovery.suspend"], Has.Count.EqualTo(suspensions));
            Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        }
        // Positive control: this same locally generated session is still a valid inbound link.
        string generatedLink = Link(await _sessions.LoadSessionAsync(before));
        await cut.InvokeAsync(() => _navigation.NavigateTo(generatedLink));
        Assert.That(cut.Markup, Does.Contain("Load shared session"));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        await Button(cut, "Calculate").ClickAsync(new());
        cut.Render();
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot, Is.SameAs(pin));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CopyKeepsAnExistingIncomingOfferAndSavedRecoveryUntilExplicitDismissal(bool success) {
        Recovery();
        _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(success);
        _navigation.NavigateTo(Link(Session("Incoming")));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        string before = Snapshot(cut);
        string uri = _navigation.Uri;
        int history = ((FakeNavigationManager)_navigation).History.Count;
        AngleSharp.Dom.IElement incoming = cut.Find("[aria-label='Shared session']");
        Assert.That(incoming.QuerySelector("[role='status']")!.TextContent, Is.EqualTo("A shared session is available."));
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Has.Count.EqualTo(1));
        Assert.That(cut.Find("[aria-label='Shared session'] [role='status']").TextContent, Is.EqualTo("A shared session is available."));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
        Assert.That(_navigation.Uri, Is.EqualTo(uri));
        Assert.That(((FakeNavigationManager)_navigation).History, Has.Count.EqualTo(history));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        await Button(cut, "Copy share link").ClickAsync(new());
        cut.Render();
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        await cut.InvokeAsync(() => _navigation.NavigateTo(uri));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public async Task CopyLeavesIncomingOfferActionableForItsOriginalSession() {
        Recovery();
        SessionState incoming = Session("Incoming");
        string incomingJson = _sessions.SerializeSession(incoming);
        _navigation.NavigateTo(Link(incoming));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = _context.RenderComponent<ProbabilityCalculatorComponent>();
        cut.WaitForState(() => cut.FindComponent<SessionRecovery>().Instance.InspectionComplete);
        _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(true);
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Shared session'] [role='status']").TextContent, Is.EqualTo("A shared session is available."));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
        Assert.That(Snapshot(cut), Is.Not.EqualTo(incomingJson));

        TaskCompletionSource<string> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.JSInterop.SetupVoid("sessionRecovery.update", invocation => {
            saved.TrySetResult((string)invocation.Arguments[2]!);
            return true;
        }).SetVoidResult();
        await Button(cut, "Load shared session").ClickAsync(new());
        string applied = await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(applied, Is.EqualTo(incomingJson));
        Assert.That(Snapshot(cut), Is.EqualTo(incomingJson));
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
        Assert.That(_navigation.Uri, Does.Not.Contain("#"));
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [TestCase(true, "navigate")]
    [TestCase(false, "navigate")]
    [TestCase(true, "dismiss")]
    [TestCase(false, "dismiss")]
    [TestCase(true, "dispose")]
    [TestCase(false, "dispose")]
    public async Task DelayedCopyCompletionDoesNotOwnIncomingNavigationOrResurrectConsumedOffers(bool success, string action) {
        Recovery();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Bunit.JSRuntimeInvocationHandler<bool> clipboard = _context.JSInterop.Setup<bool>("sessionSharing.copy", _ => {
            started.TrySetResult();
            return true;
        });
        IRenderedComponent<SharingHost> host = _context.RenderComponent<SharingHost>(parameters =>
            parameters.Add(component => component.Visible, true));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = host.FindComponent<ProbabilityCalculatorComponent>();
        string before = Snapshot(cut);
        Task copy = Button(cut, "Copy share link").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        string link = Link(Session("Incoming during copy"));
        await cut.InvokeAsync(() => _navigation.NavigateTo(link));
        if (action == "dismiss") {
            await Button(cut, "Dismiss shared link").ClickAsync(new());
        }

        if (action == "dispose") {
            host.SetParametersAndRender(parameters => parameters.Add(component => component.Visible, false));
        }
        clipboard.SetResult(success);
        await copy;
        Assert.That(Writes, Is.Zero);
        Assert.That(_context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        if (action == "dispose") {
            Assert.That(host.FindComponents<SessionShareButton>(), Is.Empty);
            Assert.That(_navigation.Uri, Is.EqualTo(link));
            return;
        }

        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Has.Count.EqualTo(action == "navigate" ? 1 : 0));
        Assert.That(_navigation.Uri, Is.EqualTo(action == "navigate" ? link : _navigation.BaseUri));
        if (action == "dismiss") {
            await cut.InvokeAsync(() => _navigation.NavigateTo(_navigation.BaseUri + "#ordinary-anchor"));
            cut.Render();
            Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
            Assert.That(_navigation.Uri, Does.EndWith("#ordinary-anchor"));
            await cut.InvokeAsync(() => _navigation.NavigateTo(link));
            Assert.That(cut.Markup, Does.Contain("Load shared session"));
        }
    }

    private sealed class FeedbackClock : TimeProvider {
        private ManualTimer? _timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new(callback, state);

        public void Expire() => _timer?.Fire();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync() {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire() {
                if (!_disposed) {
                    _disposed = true;
                    callback(state);
                }
            }
        }
    }

    private sealed record ResultObservation(string Name, string Value, string? Comparison);
    private sealed record PinnedObservation(
        double Total,
        string Description,
        (int Index, string Label, double Probability, string? GroupId, Guid? Lineage, string? Signature)[] Combos,
        (string Id, string Label, double Probability, int ActiveCount)[] Groups
    );

    private static ResultObservation[] CurrentRows(IRenderedFragment cut) => cut.FindAll(".combo-probability-item")
        .Select(row => new ResultObservation(
            row.QuerySelector(".combo-probability-name")!.TextContent.Trim(),
            row.QuerySelector(".combo-probability-value")!.TextContent.Trim(),
            row.QuerySelector(".result-row-comparison")?.TextContent.Trim()
        ))
        .ToArray();

    private static PinnedObservation PinnedValues(PinnedResultSnapshot pin) => new(
        pin.Total,
        pin.Context.Description,
        pin.Combos.Select(row => (
            row.Index,
            row.Label,
            row.Probability,
            row.GroupId,
            row.Definition?.Lineage,
            row.Definition?.Signature
        ))
            .ToArray(),
        pin.Groups.Select(row => (row.Id, row.Label, row.Probability, row.ActiveCount)).ToArray()
    );

    private static void AssertPinnedValues(PinnedResultSnapshot actual, PinnedObservation expected) {
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(actual.Context.Description, Is.EqualTo(expected.Description));
        (int Index, string Label, double Probability, string? GroupId, Guid? Lineage, string? Signature)[] comboValues =
            actual.Combos.Select(row => (
                row.Index,
                row.Label,
                row.Probability,
                row.GroupId,
                row.Definition?.Lineage,
                row.Definition?.Signature
            ))
                .ToArray();
        Assert.That(comboValues, Is.EqualTo(expected.Combos));

        (string Id, string Label, double Probability, int ActiveCount)[] groupValues =
            actual.Groups.Select(row => (row.Id, row.Label, row.Probability, row.ActiveCount)).ToArray();
        Assert.That(groupValues, Is.EqualTo(expected.Groups));
    }

    public sealed class SharingHost : ComponentBase {
        [Parameter] public bool Visible { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            if (!Visible) {
                return;
            }

            builder.OpenComponent<ProbabilityCalculatorComponent>(0);
            builder.CloseComponent();
        }
    }
}
