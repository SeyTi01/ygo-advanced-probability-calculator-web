using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class BackgroundCalculationIntegrationTest
{
    private TestContext context = null!;
    private ISessionService sessions = null!;
    private PendingSessionService pending = null!;
    private Mock<ILegacyCardMetadataEnricher> enricher = null!;
    private Mock<IDeckImportService> imports = null!;
    private ControlledCalculator calculator = null!;
    private IRenderedComponent<Host> host = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> cut = null!;
    private int Writes => context.JSInterop.Invocations["sessionRecovery.update"].Count;

    [SetUp]
    public void Setup()
    {
        context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        pending = new();
        calculator = new();
        imports = new();
        enricher = new();
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(Task.CompletedTask);
        context.Services.AddSingleton<IPendingSessionService>(pending);
        context.Services.AddSingleton<IBackgroundCalculator>(calculator);
        context.Services.AddSingleton(imports.Object);
        context.Services.AddSingleton(enricher.Object);
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        BunitJSModuleInterop clipboard = context.JSInterop.SetupModule("./js/probabilityResultExport.js");
        clipboard.Mode = JSRuntimeMode.Loose;
        clipboard.Setup<bool>("copyText", _ => true).SetResult(true);
        sessions = context.Services.GetRequiredService<ISessionService>();
    }

    [TearDown]
    public void Cleanup()
    {
        cut?.Dispose();
        host?.Dispose();
        context.Dispose();
    }

    private static SessionState Workspace(string name = "Initial") => new()
    {
        Cards = [new([], 4, name, id: "a"), new([], 4, "Other", id: "b")],
        Combos = [new([], "Route", cards: [new("a", 1, 5)])], HandSize = 2
    };

    private void Mount()
    {
        pending.PendingSession = Workspace();
        host = context.RenderComponent<Host>(p => p.Add(x => x.Visible, true));
        cut = host.FindComponent<ProbabilityCalculatorComponent>();
    }

    private Task Start() => cut.Find(".calculate-action > button").ClickAsync(new());

    private Task Cancel() => cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());

    private static ProbabilityCalculationResult Result(string name) => new(0.5, [new(0, name, 0.5)]);

    private async Task Complete(int job, Task action, string name)
    {
        calculator.Jobs[job].SetResult(Result(name));
        await action;
    }

    private Task File(SessionState session, int input = 1) => Upload(sessions.SerializeSession(session), input);

    private Task Upload(string json, int input = 1) => cut.InvokeAsync(() =>
        cut.FindComponents<InputFile>()[input].Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new SessionFile(json)])));

    private Task<string> NextUpdate()
    {
        TaskCompletionSource<string> queued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.JSInterop.SetupVoid("sessionRecovery.update",
            invocation =>
            {
                queued.TrySetResult((string)invocation.Arguments[2]!);

                return true;
            }).SetVoidResult();

        return queued.Task;
    }

    private static async Task<T> Milestone<T>(Task<T> task, string name)
    {
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            throw new AssertionException($"Timed out waiting for {name}.");
        }
    }

    private (TaskCompletionSource Gate, Task<bool> Started) DelayLoad()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns<SessionState>(session =>
        {
            if (session.Cards[0].Name != "Delayed load")
            {
                return Task.CompletedTask;
            }

            started.TrySetResult(true);

            return gate.Task;
        });

        return (gate, started.Task);
    }

    [TestCase("file", false)]
    [TestCase("file", true)]
    [TestCase("recovery", false)]
    [TestCase("recovery", true)]
    [TestCase("ydk", false)]
    [TestCase("ydk", true)]
    [TestCase("ydke", false)]
    [TestCase("ydke", true)]
    public async Task AcceptedReplacementCancelsOldWorkerAndItsLateCompletionCannotTouchNewJob(string kind, bool error)
    {
        Mount();
        await Complete(0, Start(), "Accepted before load");
        Task old = Start();
        Task<string> update = NextUpdate();
        SessionState replacement = Workspace("Replacement");

        if (kind == "file")
        {
            await File(replacement);
        }
        else if (kind == "recovery")
        {
            bool accepted = await cut.InvokeAsync(() =>
                cut.FindComponent<SessionRecovery>().Instance.ApplyRecovery(replacement));
            Assert.That(accepted, Is.True);
        }
        else if (kind == "ydk")
        {
            imports.Setup(x => x.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(replacement.Cards);
            await File(replacement, 0);
        }
        else
        {
            imports.Setup(x => x.ImportDeckFromYdkeAsync(It.IsAny<string>())).ReturnsAsync(replacement.Cards);
            await cut.FindAll("button").Single(x => x.TextContent.Trim() == "Import YDKe").ClickAsync(new());
            await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "fixture" });
            await cut.Find("form[aria-label='YDKe deck import']").TriggerEventAsync("onsubmit", EventArgs.Empty);
        }

        SessionState saved =
            await sessions.LoadSessionAsync(await Milestone(update, "accepted replacement recovery payload"));
        Assert.That(saved.Cards[0].Name, Is.EqualTo("Replacement"));
        Assert.That(calculator.Tokens[1].IsCancellationRequested, Is.True);
        Assert.That(cut.FindAll(".probability-results"),
            Is.Empty,
            "accepting a different workspace clears old results");
        Task newer = Start();

        if (error)
        {
            calculator.Jobs[1].SetException(new InvalidOperationException("Obsolete worker error"));
        }
        else
        {
            calculator.Jobs[1].SetResult(Result("Obsolete worker result"));
        }

        await old;
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(cut.Markup, Does.Not.Contain("Obsolete worker"));
        await Complete(2, newer, "Replacement result");
        Assert.That(cut.Markup, Does.Contain("Replacement result"));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PendingFileIgnoresCalculationOnlyTransitionsButRejectsAGenuineEdit(bool edit)
    {
        Mount();
        await Complete(0, Start(), "Accepted result");
        (TaskCompletionSource gate, Task<bool> started) = DelayLoad();
        Task load = File(Workspace("Delayed load"));
        await Milestone(started, "file enrichment start");
        Task worker = Start();
        await Cancel();
        Task<string> update = NextUpdate();

        if (edit)
        {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }

        gate.SetResult();
        await load;
        SessionState saved =
            await sessions.LoadSessionAsync(await Milestone(update, "accepted workspace autosave after pending file"));
        Assert.That(saved.HandSize, Is.EqualTo(edit ? 3 : 2));
        Assert.That(saved.Cards[0].Name, Is.EqualTo(edit ? "Initial" : "Delayed load"));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Name, Is.EqualTo(saved.Cards[0].Name));

        if (edit)
        {
            Assert.That(cut.Markup, Does.Contain("Current work changed while loading"));
            Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        }
        else
        {
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }

        calculator.Jobs[1].SetException(new InvalidOperationException("Obsolete worker error"));
        await worker;
        Assert.That(cut.Markup, Does.Not.Contain("Obsolete worker error"));
    }

    [Test]
    public async Task EditAutosavesWhileCancellationCopyAndCheckmarkExpiryDoNotChangeTheSnapshot()
    {
        Mount();
        await Complete(0, Start(), "Readable **route** \\ path");
        string initialBytes = (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;
        int writes = Writes;
        FeedbackClock clock = new();
        IRenderedComponent<ProbabilityResultExport> export = cut.FindComponent<ProbabilityResultExport>();
        export.SetParametersAndRender(p => p.Add(x => x.FeedbackTimeProvider, clock));
        await export.Find("button").ClickAsync(new());
        Assert.That(export.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));
        Assert.That(context.JSInterop.Invocations["copyText"].Single().Arguments[0],
            Does.Contain("Readable **route** \\ path"));
        await export.InvokeAsync(clock.Expire);
        export.WaitForState(() => export.FindAll(".probability-result-copy-success-icon").Count == 0);
        Task cancelled = Start();
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
        await Cancel();
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        calculator.Jobs[1].SetResult(Result("Late cancelled result"));
        await cancelled;
        Assert.That(Writes, Is.EqualTo(writes));
        Assert.That((string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!,
            Is.EqualTo(initialBytes));
        Task worker = Start();
        Task<string> update = NextUpdate();
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        SessionState saved =
            await sessions.LoadSessionAsync(await Milestone(update, "edit autosave during calculation"));
        Assert.That(saved.HandSize, Is.EqualTo(3));
        Assert.That(calculator.Tokens[2].IsCancellationRequested, Is.True);
        Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        Assert.That(context.JSInterop.Invocations["copyText"].Last().Arguments[0],
            Does.StartWith("Previous result — current inputs have changed.\nProbability results\nHand size: 2"));
        Task latest = Start();
        calculator.Jobs[2].SetException(new InvalidOperationException("Late edit error"));
        await worker;
        await Complete(3, latest, "Current result");
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        Assert.That(Writes, Is.EqualTo(writes + 1));
        Assert.That(cut.Markup, Does.Not.Contain("Late edit error").And.Not.Contain("Calculation was cancelled"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnmountCancelsWorkerRejectsPendingLoadFlushesRecoveryOwnerAndNewExampleCanCalculate(bool error)
    {
        Mount();
        ProbabilityCalculatorComponent oldInstance = cut.Instance;
        SessionState oldSession = cut.FindComponent<SessionRecovery>().Instance.Session;
        object? oldOwner = context.JSInterop.Invocations["sessionRecovery.initialize"].Single().Arguments[0];
        Task old = Start();
        (TaskCompletionSource gate, Task<bool> started) = DelayLoad();
        Task load = File(Workspace("Delayed load"));
        await Milestone(started, "pending load before unmount");
        host.SetParametersAndRender(p => p.Add(x => x.Visible, false));
        await host.InvokeAsync(oldInstance.Dispose); // Repeated disposal must be harmless.
        Assert.That(calculator.Tokens[0].IsCancellationRequested, Is.True);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.dispose"]
                .Count(x => Equals(x.Arguments[0], oldOwner)),
            Is.EqualTo(1));
        Task<string> update = NextUpdate();
        pending.PendingSession = Workspace("New example");
        host.SetParametersAndRender(p => p.Add(x => x.Visible, true));
        cut = host.FindComponent<ProbabilityCalculatorComponent>();
        SessionState saved =
            await sessions.LoadSessionAsync(await Milestone(update, "remounted example recovery payload"));
        Assert.That(saved.Cards[0].Name, Is.EqualTo("New example"));
        gate.SetResult();
        await load;
        Assert.That(oldSession.Cards[0].Name,
            Is.EqualTo("Initial"),
            "disposed load owner must reject enrichment completion");
        Task latest = Start();

        if (error)
        {
            calculator.Jobs[0].SetException(new InvalidOperationException("Disposed worker error"));
        }
        else
        {
            calculator.Jobs[0].SetResult(Result("Disposed worker result"));
        }

        await old;
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        await Complete(1, latest, "New example result");
        Assert.That(cut.Markup, Does.Contain("New example result").And.Not.Contain("Disposed worker"));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.initialize"].Select(x => x.Arguments[0]).Distinct()
                .Count(),
            Is.EqualTo(2));
    }

    [Test]
    public async Task MalformedFileKeepsCurrentWorkerAndReportsTheLoadFailure()
    {
        Mount();
        await Complete(0, Start(), "Accepted result");
        Task worker = Start();
        int writes = Writes;
        await Upload("{");
        Assert.That(calculator.Tokens[1].IsCancellationRequested, Is.False);
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(cut.Markup, Does.Contain("Accepted result").And.Contain("Failed to load session"));
        Assert.That(Writes, Is.EqualTo(writes));
        await Complete(1, worker, "Current worker result");
        Assert.That(cut.Markup, Does.Contain("Current worker result").And.Contain("Failed to load session"));
    }

    [Test]
    public async Task SupersededImportFailureCannotReplaceACurrentWorkerFailure()
    {
        Mount();
        TaskCompletionSource<List<Card>> response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        imports.Setup(x => x.ImportDeckFromYdkeAsync(It.IsAny<string>())).Returns(() =>
        {
            started.TrySetResult(true);

            return response.Task;
        });
        await cut.FindAll("button").Single(x => x.TextContent.Trim() == "Import YDKe").ClickAsync(new());
        await cut.Find("#ydkeCodeInput").InputAsync(new() { Value = "fixture" });
        Task import = cut.Find("form[aria-label='YDKe deck import']").TriggerEventAsync("onsubmit", EventArgs.Empty);
        await Milestone(started.Task, "pending import start");
        await File(Workspace("Accepted file"));
        Task worker = Start();
        calculator.Jobs[0].SetException(new InvalidOperationException("Current worker failure"));
        await worker;
        response.SetException(new InvalidOperationException("Obsolete import failure"));
        await import;
        Assert.That(cut.Markup, Does.Contain("Current worker failure").And.Not.Contain("Obsolete import failure"));
        await Complete(1, Start(), "Recovered result");
        Assert.That(cut.Markup, Does.Contain("Recovered result").And.Not.Contain("Current worker failure"));
    }

    public sealed class Host : ComponentBase
    {
        [Parameter] public bool Visible { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (! Visible)
            {
                return;
            }

            builder.OpenComponent<ProbabilityCalculatorComponent>(0);
            builder.CloseComponent();
        }
    }

    private sealed class SessionFile(string json) : IBrowserFile
    {
        public string Name => "fixture.json";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => Encoding.UTF8.GetByteCount(json);
        public string ContentType => "application/json";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ControlledCalculator : IBackgroundCalculator
    {
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token)
        {
            TaskCompletionSource<ProbabilityCalculationResult> job =
                new(TaskCreationOptions
                    .RunContinuationsAsynchronously);
            Jobs.Add(job);
            Tokens.Add(token);

            return job.Task; // Ignore cancellation deliberately.
        }
    }

    private sealed class FeedbackClock : TimeProvider
    {
        private ManualTimer? timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timer = new(callback, state);

        public void Expire() => timer?.Fire();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => ! disposed;

            public void Dispose() => disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }

            public void Fire()
            {
                if (! disposed)
                {
                    disposed = true;
                    callback(state);
                }
            }
        }
    }
}
