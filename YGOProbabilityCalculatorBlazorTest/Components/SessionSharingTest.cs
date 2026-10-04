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
    private Bunit.TestContext context = null!;
    private ISessionService sessions = null!;
    private NavigationManager navigation = null!;
    private Mock<ILegacyCardMetadataEnricher> enricher = null!;
    private Mock<IBackgroundCalculator> background = null!;
    [SetUp] public void Setup() {
        context = new(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        background = new();
        background.Setup(x => x.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot snapshot, CancellationToken cancellation) => new BackgroundCalculatorTestAdapter(new ProbabilityCalculatorService()).CalculateAsync(snapshot, cancellation));
        context.Services.AddSingleton(background.Object);
        context.Services.AddSingleton(Mock.Of<IDeckImportService>());
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        enricher = new(); enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(Task.CompletedTask);
        context.Services.AddSingleton(enricher.Object);
        sessions = context.Services.GetRequiredService<ISessionService>();
        navigation = context.Services.GetRequiredService<NavigationManager>();
    }
    [TearDown] public void Cleanup() => context.Dispose();
    private static SessionState Session(string name = "Shared") => new() {
        HandSize = 3, Categories = [new("Role")], Cards = [new([], 3, name, false, "stable-id")],
        Combos = [new([], "Incomplete", false)], ComboGroups = [new("g", "Group")], CategoryColorIndices = new() { ["Role"] = 3 }
    };
    private string Link(SessionState session) => SessionShareCodec.CreateLink(navigation.BaseUri, sessions.SerializeSession(session));
    private void Recovery(string name = "Recovered") => context.JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true)
        .SetResult(new() { Exists = true, Payload = sessions.SerializeSession(Session(name)) });
    private static AngleSharp.Dom.IElement Button(IRenderedFragment cut, string text) => cut.FindAll("button").Single(x => x.TextContent.Trim().EndsWith(text, StringComparison.Ordinal));
    private static string Snapshot(IRenderedComponent<ProbabilityCalculatorComponent> cut) => cut.FindComponent<SessionShareButton>().Instance.Snapshot;
    private int Writes => context.JSInterop.Invocations["sessionRecovery.update"].Count;
    private Task LoadFile(IRenderedComponent<ProbabilityCalculatorComponent> cut, string json) {
        var file = new Mock<IBrowserFile>();
        file.Setup(x => x.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        return cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
    }

    [Test] public async Task InitialOfferKeepsRecoveryAndPendingExampleUntilExplicitAcceptance() {
        Recovery();
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Session("Example");
        navigation.NavigateTo(Link(Session()));
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Load shared session").And.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
        var queued = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.JSInterop.SetupVoid("sessionRecovery.update", invocation => {
            queued.TrySetResult((string)invocation.Arguments[2]!); return true;
        }).SetVoidResult();
        await Button(cut, "Load shared session").ClickAsync(new());
        var bytes = await queued.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(bytes, Is.EqualTo(sessions.SerializeSession(Session())));
        Assert.That(Snapshot(cut), Is.EqualTo(bytes));
        Assert.That(navigation.Uri, Does.Not.Contain("#"));
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
    }

    [Test] public async Task InspectionPendingDisablesLoadingAndNeverQueuesOrDiscards() {
        var inspection = context.JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true);
        navigation.NavigateTo(Link(Session()));
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.True);
        Assert.That(Writes, Is.Zero);
        inspection.SetResult(new() { Exists = true, Payload = sessions.SerializeSession(Session("Recovered")) });
        cut.WaitForState(() => !Button(cut, "Load shared session").HasAttribute("disabled"));
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
    }

    [Test] public async Task LaterNavigationPreservesDraftAndDismissedLinkCanBeOfferedAgain() {
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        await Button(cut, "Add New Card").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Unsaved draft");
        var before = Snapshot(cut); var writes = Writes;
        var link = Link(Session());
        await cut.InvokeAsync(() => navigation.NavigateTo(link));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Find("[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Unsaved draft"));
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(Writes, Is.EqualTo(writes));
        await cut.InvokeAsync(() => navigation.NavigateTo(link));
        Assert.That(cut.Markup, Does.Contain("Load shared session"));
        await cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + "#ordinary-anchor"));
        Assert.That(cut.Markup, Does.Not.Contain("Load shared session"));
        Assert.That(navigation.Uri, Does.EndWith("#ordinary-anchor"));
    }

    [TestCase("edit")]
    [TestCase("reverted buttons")]
    [TestCase("link")]
    [TestCase("file")]
    [TestCase("recovery")]
    [TestCase("dismiss")]
    [TestCase("dispose")]
    public async Task NewerActionsFenceDelayedSharedEnrichment(string action) {
        Recovery(); navigation.NavigateTo(Link(Session("Old link")));
        var host = context.RenderComponent<SharingHost>(p => p.Add(x => x.Visible, true));
        var cut = host.FindComponent<ProbabilityCalculatorComponent>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.Is<SessionState>(s => s.Cards[0].Name == "Old link")))
            .Returns(() => { started.SetResult(); return completion.Task; });
        var loading = Button(cut, "Load shared session").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        switch (action) {
            case "edit": cut.Find("#handSize").Input("6"); cut.Find("#handSize").Change("6"); break;
            case "reverted buttons":
                await Button(cut, "Add New Card").ClickAsync(new());
                await cut.Find("[aria-label='Remove card']").ClickAsync(new());
                break;
            case "link": await cut.InvokeAsync(() => navigation.NavigateTo(Link(Session("New link")))); break;
            case "file": cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(sessions.SerializeSession(Session("File")), "session.json")); break;
            case "recovery": await Button(cut, "Restore previous session").ClickAsync(new()); break;
            case "dismiss": await Button(cut, "Dismiss shared link").ClickAsync(new()); break;
            case "dispose": host.SetParametersAndRender(p => p.Add(x => x.Visible, false)); break;
        }
        completion.SetResult(); await loading;
        if (action == "dispose") { Assert.That(Writes, Is.Zero); return; }
        var snapshot = Snapshot(cut);
        Assert.That(snapshot, Does.Not.Contain("Old link"));
        if (action == "file") Assert.That(snapshot, Does.Contain("File"));
        if (action == "recovery") Assert.That(snapshot, Does.Contain("Recovered"));
        if (action == "link") {
            await Button(cut, "Load shared session").ClickAsync(new());
            Assert.That(Snapshot(cut), Does.Contain("New link"));
        }
    }

    [Test] public async Task LinkArrivalFencesAnOlderFileAndUnmountUnsubscribes() {
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(() => { started.TrySetResult(); return completion.Task; });
        var file = LoadFile(cut, sessions.SerializeSession(Session("Old file")));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => navigation.NavigateTo(Link(Session())));
        completion.SetResult(); await file;
        Assert.That(Snapshot(cut), Does.Not.Contain("Old file"));
        cut.Dispose();
        Assert.DoesNotThrow(() => navigation.NavigateTo(Link(Session("Later"))));
    }

    [TestCase("#ygo-session=v999.abc")]
    [TestCase("#ygo-session=v1.%ZZ")]
    public async Task InvalidLinksPreserveAcceptedStateAndRecovery(string fragment) {
        Recovery(); var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        var before = Snapshot(cut);
        await cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + fragment));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Shared session"));
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [Test] public async Task ClipboardDenialExposesExactCapturedLinkAndDraftIsExcluded() {
        context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(false);
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        await Button(cut, "Add New Card").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Not saved");
        var before = Snapshot(cut);
        await Button(cut, "Copy share link").ClickAsync(new());
        var link = cut.Find("textarea[readonly]").TextContent;
        Assert.That(link, Is.EqualTo(Link(await sessions.LoadSessionAsync(before))));
        Assert.That(link, Does.Not.Contain("?"));
        Assert.That(cut.Markup, Does.Contain("Select and copy this link").And.Not.Contain("Share link copied."));
        Assert.That(context.JSInterop.Invocations["saveSessionFile"], Is.Empty);
    }

    [Test] public async Task OfferAndInvalidLinkKeepCalculationRunningAndAcceptanceCancelsAtCommit() {
        var role = new CategoryBase("Role");
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = new() {
            HandSize = 1, Categories = [role], Cards = [new([role], 3, "Working")], Combos = [new([new(role, 1, 1)], "Working route")]
        };
        var result = new TaskCompletionSource<ProbabilityCalculationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        background.Setup(x => x.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken cancellation) => { token = cancellation; return result.Task; });
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        await cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + "#ygo-session=v1.%ZZ"));
        Assert.That(token.IsCancellationRequested, Is.False);
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        await cut.InvokeAsync(() => navigation.NavigateTo(Link(Session())));
        Assert.That(token.IsCancellationRequested, Is.False);
        Assert.That(cut.Find("[aria-label='Cancel calculation']").HasAttribute("disabled"), Is.False);
        await Button(cut, "Load shared session").ClickAsync(new());
        Assert.That(token.IsCancellationRequested, Is.True);
        result.SetResult(new(.5, [new(0, "Obsolete", .5)])); await calculation;
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(Snapshot(cut), Is.EqualTo(sessions.SerializeSession(Session())));
    }

    [Test] public async Task ALinkFencesAnOlderStartupExampleWithoutLosingRecovery() {
        Recovery();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(completion.Task);
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Session("Old example");
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        await cut.InvokeAsync(() => navigation.NavigateTo(Link(Session())));
        completion.SetResult();
        cut.WaitForState(() => cut.Markup.Contains("Restore previous session", StringComparison.Ordinal));
        Assert.That(Snapshot(cut), Does.Not.Contain("Old example"));
        Assert.That(Writes, Is.Zero);
        Assert.That(cut.Markup, Does.Contain("Load shared session"));
    }

    [Test] public async Task DelayedClipboardCompletionCannotAnnounceAnObsoleteSnapshot() {
        var clipboard = context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true);
        var snapshot = sessions.SerializeSession(Session());
        var cut = context.RenderComponent<SessionShareButton>(p => p.Add(x => x.Snapshot, snapshot).Add(x => x.Capture, () => snapshot));
        var copy = Button(cut, "Copy share link").ClickAsync(new());
        snapshot = sessions.SerializeSession(Session("Newer accepted state"));
        cut.SetParametersAndRender(p => p.Add(x => x.Snapshot, snapshot));
        clipboard.SetResult(true); await copy;
        Assert.That(cut.Markup, Does.Not.Contain("Share link copied."));
        Assert.That(cut.FindAll("textarea"), Is.Empty);
    }

    [Test] public async Task OversizedSessionOffersFileSharingAndNeverTouchesClipboard() {
        var snapshot = new string('a', SessionShareCodec.MaxJsonBytes + 1);
        var cut = context.RenderComponent<SessionShareButton>(p => p.Add(x => x.Snapshot, snapshot).Add(x => x.Capture, () => snapshot));
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("Share a normal session file instead"));
        Assert.That(context.JSInterop.Invocations["sessionSharing.copy"], Is.Empty);
        Assert.That(cut.FindAll("textarea"), Is.Empty);
    }

    [TestCase("success")]
    [TestCase("denied")]
    [TestCase("unavailable")]
    public async Task OutboundCopyPreservesWorkspaceDraftResultsPinNavigationAndRecovery(string outcome) {
        var clipboard = context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true);
        if (outcome == "unavailable") clipboard.SetException(new JSException("Clipboard unavailable"));
        else clipboard.SetResult(outcome == "success");
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.JSInterop.SetupVoid("sessionRecovery.update", _ => { saved.TrySetResult(); return true; }).SetVoidResult();
        var role = new CategoryBase("Role");
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = new() {
            HandSize = 1, Categories = [role], Cards = [new([role], 3, "Working")],
            Combos = [new([new(role, 1, 1)], "Working route")]
        };
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        cut.Find("[aria-label='Category name']").Input("Unsaved draft");
        var before = Snapshot(cut);
        var results = cut.FindComponent<PinnedResultPanel>();
        var current = results.Instance.Current!;
        var currentTotal = current.Total;
        var currentContext = current.Context;
        var currentComboDefinitions = current.Context.Combos.Select(row => (row.Lineage, row.Signature)).ToArray();
        var currentRows = CurrentRows(cut);
        var pin = results.Instance.Snapshot;
        var pinnedValues = PinnedValues(pin);
        var uri = navigation.Uri;
        var history = ((FakeNavigationManager)navigation).History.Count;
        var writes = Writes;
        var suspensions = context.JSInterop.Invocations["sessionRecovery.suspend"].Count;
        var clock = new FeedbackClock();
        cut.FindComponent<SessionShareButton>().SetParametersAndRender(p => p.Add(x => x.FeedbackTimeProvider, clock));

        for (var i = 0; i < 2; i++) {
            await Button(cut, "Copy share link").ClickAsync(new());
            var copied = (string)context.JSInterop.Invocations["sessionSharing.copy"].Last().Arguments[0]!;
            Assert.That(copied, Is.EqualTo(Link(await sessions.LoadSessionAsync(before))));
            if (outcome == "success") {
                Assert.That(cut.Markup, Does.Contain("Share link copied."));
                await cut.InvokeAsync(clock.Expire);
                Assert.That(cut.Markup, Does.Not.Contain("Share link copied."));
            }
            else {
                Assert.That(cut.Find("textarea[readonly]").TextContent, Is.EqualTo(copied));
                await cut.Find("textarea[readonly]").ClickAsync(new());
                Assert.That(context.JSInterop.Invocations["sessionSharing.select"], Has.Count.EqualTo(i + 1));
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
            Assert.That(navigation.Uri, Is.EqualTo(uri));
            Assert.That(((FakeNavigationManager)navigation).History, Has.Count.EqualTo(history));
            Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
            Assert.That(Writes, Is.EqualTo(writes));
            Assert.That(context.JSInterop.Invocations["sessionRecovery.suspend"], Has.Count.EqualTo(suspensions));
            Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        }
        // Positive control: this same locally generated session is still a valid inbound link.
        var generatedLink = Link(await sessions.LoadSessionAsync(before));
        await cut.InvokeAsync(() => navigation.NavigateTo(generatedLink));
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
        context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(success);
        navigation.NavigateTo(Link(Session("Incoming")));
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        var before = Snapshot(cut);
        var uri = navigation.Uri;
        var history = ((FakeNavigationManager)navigation).History.Count;
        var incoming = cut.Find("[aria-label='Shared session']");
        Assert.That(incoming.QuerySelector("[role='status']")!.TextContent, Is.EqualTo("A shared session is available."));
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Has.Count.EqualTo(1));
        Assert.That(cut.Find("[aria-label='Shared session'] [role='status']").TextContent, Is.EqualTo("A shared session is available."));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
        Assert.That(navigation.Uri, Is.EqualTo(uri));
        Assert.That(((FakeNavigationManager)navigation).History, Has.Count.EqualTo(history));
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        await Button(cut, "Dismiss shared link").ClickAsync(new());
        await Button(cut, "Copy share link").ClickAsync(new());
        cut.Render();
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        await cut.InvokeAsync(() => navigation.NavigateTo(uri));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
    }

    [Test] public async Task CopyLeavesIncomingOfferActionableForItsOriginalSession() {
        Recovery();
        var incoming = Session("Incoming");
        var incomingJson = sessions.SerializeSession(incoming);
        navigation.NavigateTo(Link(incoming));
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        cut.WaitForState(() => cut.FindComponent<SessionRecovery>().Instance.InspectionComplete);
        context.JSInterop.Setup<bool>("sessionSharing.copy", _ => true).SetResult(true);
        await Button(cut, "Copy share link").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='Shared session'] [role='status']").TextContent, Is.EqualTo("A shared session is available."));
        Assert.That(Button(cut, "Load shared session").HasAttribute("disabled"), Is.False);
        Assert.That(Snapshot(cut), Is.Not.EqualTo(incomingJson));

        var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.JSInterop.SetupVoid("sessionRecovery.update", invocation => {
            saved.TrySetResult((string)invocation.Arguments[2]!);
            return true;
        }).SetVoidResult();
        await Button(cut, "Load shared session").ClickAsync(new());
        var applied = await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(applied, Is.EqualTo(incomingJson));
        Assert.That(Snapshot(cut), Is.EqualTo(incomingJson));
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
        Assert.That(navigation.Uri, Does.Not.Contain("#"));
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [TestCase(true, "navigate")]
    [TestCase(false, "navigate")]
    [TestCase(true, "dismiss")]
    [TestCase(false, "dismiss")]
    [TestCase(true, "dispose")]
    [TestCase(false, "dispose")]
    public async Task DelayedCopyCompletionDoesNotOwnIncomingNavigationOrResurrectConsumedOffers(bool success, string action) {
        Recovery();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = context.JSInterop.Setup<bool>("sessionSharing.copy", _ => { started.TrySetResult(); return true; });
        var host = context.RenderComponent<SharingHost>(p => p.Add(x => x.Visible, true));
        var cut = host.FindComponent<ProbabilityCalculatorComponent>();
        var before = Snapshot(cut);
        var copy = Button(cut, "Copy share link").ClickAsync(new());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var link = Link(Session("Incoming during copy"));
        await cut.InvokeAsync(() => navigation.NavigateTo(link));
        if (action == "dismiss") await Button(cut, "Dismiss shared link").ClickAsync(new());
        if (action == "dispose") host.SetParametersAndRender(p => p.Add(x => x.Visible, false));
        clipboard.SetResult(success);
        await copy;
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        if (action == "dispose") {
            Assert.That(host.FindComponents<SessionShareButton>(), Is.Empty);
            Assert.That(navigation.Uri, Is.EqualTo(link));
            return;
        }
        Assert.That(Snapshot(cut), Is.EqualTo(before));
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(cut.FindAll("[aria-label='Shared session']"), Has.Count.EqualTo(action == "navigate" ? 1 : 0));
        Assert.That(navigation.Uri, Is.EqualTo(action == "navigate" ? link : navigation.BaseUri));
        if (action == "dismiss") {
            await cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + "#ordinary-anchor"));
            cut.Render();
            Assert.That(cut.FindAll("[aria-label='Shared session']"), Is.Empty);
            Assert.That(navigation.Uri, Does.EndWith("#ordinary-anchor"));
            await cut.InvokeAsync(() => navigation.NavigateTo(link));
            Assert.That(cut.Markup, Does.Contain("Load shared session"));
        }
    }

    private sealed class FeedbackClock : TimeProvider {
        private ManualTimer? timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => timer = new(callback, state);
        public void Expire() => timer?.Fire();
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer {
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            public void Fire() { if (!disposed) { disposed = true; callback(state); } }
        }
    }

    private sealed record ResultObservation(string Name, string Value, string? Comparison);
    private sealed record PinnedObservation(double Total, string Description,
        (int Index, string Label, double Probability, string? GroupId, Guid? Lineage, string? Signature)[] Combos,
        (string Id, string Label, double Probability, int ActiveCount)[] Groups);
    private static ResultObservation[] CurrentRows(IRenderedFragment cut) => cut.FindAll(".combo-probability-item")
        .Select(row => new ResultObservation(row.QuerySelector(".combo-probability-name")!.TextContent.Trim(),
            row.QuerySelector(".combo-probability-value")!.TextContent.Trim(),
            row.QuerySelector(".result-row-comparison")?.TextContent.Trim())).ToArray();
    private static PinnedObservation PinnedValues(PinnedResultSnapshot pin) => new(pin.Total, pin.Context.Description,
        pin.Combos.Select(row => (row.Index, row.Label, row.Probability, row.GroupId, row.Definition?.Lineage, row.Definition?.Signature)).ToArray(),
        pin.Groups.Select(row => (row.Id, row.Label, row.Probability, row.ActiveCount)).ToArray());
    private static void AssertPinnedValues(PinnedResultSnapshot actual, PinnedObservation expected) {
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(actual.Context.Description, Is.EqualTo(expected.Description));
        Assert.That(actual.Combos.Select(row => (row.Index, row.Label, row.Probability, row.GroupId, row.Definition?.Lineage, row.Definition?.Signature)).ToArray(), Is.EqualTo(expected.Combos));
        Assert.That(actual.Groups.Select(row => (row.Id, row.Label, row.Probability, row.ActiveCount)).ToArray(), Is.EqualTo(expected.Groups));
    }

    public sealed class SharingHost : ComponentBase {
        [Parameter] public bool Visible { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            if (!Visible) return;
            builder.OpenComponent<ProbabilityCalculatorComponent>(0); builder.CloseComponent();
        }
    }
}
