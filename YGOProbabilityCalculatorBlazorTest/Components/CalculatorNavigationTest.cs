using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Layout;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorNavigationTest : CalculatorEditorTestBase {
    private Mock<IFileService> _files = null!;
    private NavigationManager Navigation => context.Services.GetRequiredService<NavigationManager>();
    private ISessionService Sessions => context.Services.GetRequiredService<ISessionService>();
    private IPendingSessionService Pending => context.Services.GetRequiredService<IPendingSessionService>();

    [SetUp]
    public void SetUpRoutes() {
        _files = new();
        context.Services.AddSingleton(_files.Object);
        context.Services.AddSingleton<LocalePreference>();
    }

    private IRenderedComponent<App> RenderApp(SessionState? session = null) {
        Pending.PendingSession = session;
        return context.RenderComponent<App>();
    }

    private async Task Navigate(IRenderedComponent<App> app, string path) {
        await app.InvokeAsync(() => Navigation.NavigateTo(path));
        app.WaitForState(() => app.FindComponents<ProbabilityCalculatorComponent>().All(calculator =>
            calculator.Instance.Active == (new Uri(Navigation.Uri).AbsolutePath == "/")));
    }

    private static string Snapshot(IRenderedFragment app) => app.FindComponent<SessionShareButton>().Instance.Snapshot;

    [Test]
    public async Task HelpVisitsKeepTheLiveWorkspaceEditorsAndDrafts() {
        SessionState session = new() {
            Categories = [b, a],
            Cards = [new([a], 3, "Custom A", id: "a", drawCount: 1), new([b], 2, "Custom B", active: false, id: "b")],
            Combos = [new([new(a, 1, 2)], "First", groupId: "g"), new([new(b, 0, 0)], "Second", active: false)],
            ComboGroups = [new("g", "Custom group")],
            HandSize = 2,
            FastExecutionStrategy = FastExecutionStrategy.FastOnly,
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, ["b", "a"]),
            CategoryColorIndices = new() { ["A"] = 3, ["B"] = 2 }
        };
        IRenderedComponent<App> app = RenderApp(session);
        IRenderedComponent<ProbabilityCalculatorComponent> calculator = app.FindComponent<ProbabilityCalculatorComponent>();
        IRenderedComponent<CardEditor> card = app.FindComponents<CardEditor>()[0];
        await card.Find(".accordion-button").ClickAsync(new());
        OpenSimulation(card);
        await card.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        IRenderedComponent<ComboEditor> combo = app.FindComponents<ComboEditor>()[0];
        await combo.Find(".accordion-button").ClickAsync(new());
        await combo.Find("[data-requirement='category:0']").ClickAsync(new());
        await combo.Find("#minCount0").InputAsync(new() { Value = "0" });
        string before = Snapshot(app);
        int inspections = context.JSInterop.Invocations["sessionRecovery.initialize"].Count;

        for (int i = 0; i < 3; i++) {
            await Navigate(app, "/help#sessions");
            Assert.That(app.Find(".calculator-workspace").ParentElement!.HasAttribute("hidden"), Is.True);
            _files.Verify(service => service.ReadAllTextAsync(It.IsAny<string>()), Times.Never);
            await Navigate(app, "/");
            Assert.That(app.FindComponent<ProbabilityCalculatorComponent>().Instance, Is.SameAs(calculator.Instance));
            Assert.That(app.FindComponents<CardEditor>()[0].Instance, Is.SameAs(card.Instance));
            Assert.That(app.FindComponents<ComboEditor>()[0].Instance, Is.SameAs(combo.Instance));
            Assert.That(card.Find("#cardDraw0").GetAttribute("value"), Is.EqualTo("2"));
            Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
            Assert.That(Snapshot(app), Is.EqualTo(before));
        }

        Assert.That(context.JSInterop.Invocations["sessionRecovery.initialize"].Count, Is.EqualTo(inspections));
    }

    [Test]
    public async Task FreshHelpDoesNotLoadAnOlderRecoveryDraftAutomatically() {
        context.JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true).SetResult(new() {
            Exists = true, Payload = Sessions.SerializeSession(Session())
        });
        Navigation.NavigateTo("/help");
        IRenderedComponent<App> app = RenderApp();
        Assert.That(app.FindComponents<ProbabilityCalculatorComponent>(), Is.Empty);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.initialize"], Is.Empty);

        await Navigate(app, "/");
        app.WaitForState(() => app.FindComponent<SessionRecovery>().Instance.HasRecovery);
        Assert.That(app.FindComponents<CardEditor>(), Is.Empty);
        await Button(app, "Add card").ClickAsync(new());
        string newer = Snapshot(app);
        await Navigate(app, "/help");
        await Navigate(app, "/");
        Assert.That(Snapshot(app), Is.EqualTo(newer));
        Assert.That(app.FindComponent<SessionRecovery>().Instance.HasRecovery, Is.True);
    }

    [Test]
    public async Task ExplicitExampleReplacesTheRetainedWorkspaceAndDoesNotReplayOnLaterVisits() {
        SessionState example = new() { HandSize = 1, Cards = [new([], 1, "Chosen example", id: "example")] };
        _files.Setup(service => service.ReadAllTextAsync("sample-data/example_session_state.json"))
            .ReturnsAsync(Sessions.SerializeSession(example));
        IRenderedComponent<App> app = RenderApp(Session());
        ProbabilityCalculatorComponent owner = app.FindComponent<ProbabilityCalculatorComponent>().Instance;
        await Navigate(app, "/help");
        await app.Find(".example-session-action button").ClickAsync(new());
        app.WaitForState(() => app.FindComponents<CardEditor>().Count == 1
            && app.FindComponent<CardEditor>().Instance.Card.Name == "Chosen example");
        Assert.That(app.FindComponent<ProbabilityCalculatorComponent>().Instance, Is.SameAs(owner));
        Assert.That(Pending.PendingSession, Is.Null);
        await Button(app, "Add card").ClickAsync(new());
        string edited = Snapshot(app);
        await Navigate(app, "/help");
        await Navigate(app, "/");
        Assert.That(Snapshot(app), Is.EqualTo(edited));
        _files.Verify(service => service.ReadAllTextAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task SharedReturnOffersReplacementWithoutReplacingTheRetainedWorkspace() {
        IRenderedComponent<App> app = RenderApp(Session());
        string before = Snapshot(app);
        await Navigate(app, "/help");
        Pending.PendingSession = new() { HandSize = 1, Cards = [new([], 1, "Pending", id: "pending")] };
        string shared = SessionShareCodec.CreateLink(Navigation.BaseUri, Sessions.SerializeSession(new() {
            HandSize = 1, Cards = [new([], 1, "Shared", id: "shared")]
        }));
        await Navigate(app, shared);
        Assert.That(Snapshot(app), Is.EqualTo(before));
        Assert.That(Pending.PendingSession, Is.Null);
        Assert.That(app.Find("section[aria-label='Shared session']"), Is.Not.Null);
        await Button(app, "Load shared session").ClickAsync(new());
        app.WaitForState(() => app.FindComponents<CardEditor>().Count == 1);
        Assert.That(app.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Shared"));
    }

    [Test]
    public async Task LanguageReloadOnHelpRestoresItsWorkspaceBeforeAcknowledgingTheHandoff() {
        IRenderedComponent<App> original = RenderApp(Session());
        await Navigate(original, "/help");
        await original.Find("#language-preference").ChangeAsync(new() { Value = "de" });
        string checkpoint = (string)context.JSInterop.Invocations["ygoLocale.change"].Single().Arguments[1]!;
        await original.InvokeAsync(() => original.FindComponent<ProbabilityCalculatorComponent>().Instance.DisposeAsync().AsTask());
        original.Dispose();
        context.JSInterop.Setup<MainLayout.LanguageHandoff?>("ygoLocale.getHandoff")
            .SetResult(new("help-handoff", checkpoint));
        IRenderedComponent<App> restored = RenderApp();
        restored.WaitForState(() => context.JSInterop.Invocations["ygoLocale.complete"].Any(invocation =>
            (string)invocation.Arguments[0]! == "help-handoff"));
        Assert.That(restored.Find(".calculator-workspace").ParentElement!.HasAttribute("hidden"), Is.True);
        Assert.That(restored.FindComponents<CardEditor>(), Has.Count.EqualTo(2));
        string before = Snapshot(restored);
        await Navigate(restored, "/");
        Assert.That(Snapshot(restored), Is.EqualTo(before));
    }
}
