using Bunit;
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

    public sealed class SharingHost : ComponentBase {
        [Parameter] public bool Visible { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            if (!Visible) return;
            builder.OpenComponent<ProbabilityCalculatorComponent>(0); builder.CloseComponent();
        }
    }
}
