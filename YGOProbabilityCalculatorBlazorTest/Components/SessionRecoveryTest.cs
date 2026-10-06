using System.Text;
using AngleSharp.Dom;
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
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class SessionRecoveryTest
{
    private TestContext context = null!;
    private ISessionService sessions = null!;
    private Mock<ILegacyCardMetadataEnricher> enricher = null!;

    [SetUp]
    public void Setup()
    {
        context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<
            IProbabilityCalculatorService,
            ProbabilityCalculatorService
        >();
        context.Services.AddSingleton<IBackgroundCalculator, BackgroundCalculatorTestAdapter>();
        context.Services.AddSingleton(Mock.Of<IDeckImportService>());
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        enricher = new Mock<ILegacyCardMetadataEnricher>();
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(Task.CompletedTask);
        context.Services.AddSingleton(enricher.Object);
        sessions = context.Services.GetRequiredService<ISessionService>();
    }

    [TearDown]
    public void Cleanup() => context.Dispose();

    private static SessionState Working() =>
        new()
        {
            HandSize = 5,
            Categories = [new("Role")],
            Cards = [new([], 3, "Fixture", false, "stable-id", 1234)],
            Combos = [new([], "Incomplete", false)],
            ComboGroups = [new("group", "Group")],
            CategoryColorIndices = new() { ["Role"] = 3 },
        };

    private void Recovery(string payload) =>
        context
            .JSInterop.Setup<SessionRecovery.Inspection?>("sessionRecovery.initialize", _ => true)
            .SetResult(
                new()
                {
                    Exists = true,
                    Payload = payload,
                    SavedAt = "2026-10-03T08:00:00Z",
                }
            );

    private IRenderedComponent<SessionRecovery> Render(
        SessionState session,
        Func<SessionState, Task<bool>>? apply = null
    ) =>
        context.RenderComponent<SessionRecovery>(p =>
            p.Add(x => x.Session, session)
                .Add(x => x.ApplyRecovery, apply ?? (_ => Task.FromResult(true)))
        );

    private string LastSnapshot() =>
        (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;

    private int Writes => context.JSInterop.Invocations["sessionRecovery.update"].Count;

    private Task<string> ObserveNextRecoveryUpdate()
    {
        TaskCompletionSource<string> queued = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        context
            .JSInterop.SetupVoid(
                "sessionRecovery.update",
                invocation =>
                {
                    queued.TrySetResult((string)invocation.Arguments[2]!);

                    return true;
                }
            )
            .SetVoidResult();

        return queued.Task;
    }

    private static async Task<T> AwaitMilestone<T>(Task<T> milestone, string description)
    {
        try
        {
            return await milestone.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            throw new AssertionException($"Timed out waiting for {description}.");
        }
    }

    private static IElement Button(IRenderedFragment cut, string text) =>
        cut.FindAll("button").Single(x => x.TextContent.Trim() == text);

    [Test]
    public void DefaultStartupAndUnrelatedRendersNeverQueueRecovery()
    {
        SessionState session = new() { HandSize = 5 };
        IRenderedComponent<SessionRecovery> cut = Render(session);
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        Assert.That(Writes, Is.Zero);
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
    }

    [Test]
    public async Task ColorAndNestedChangesCaptureImmutableBytesWithoutDownloadsOrRerenderWrites()
    {
        SessionState session = Working();
        IRenderedComponent<SessionRecovery> cut = Render(session);
        session.CategoryColorIndices["Role"] = 7;
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        string bytes = LastSnapshot();
        session.Cards[0].Categories.Add(new("Later"));
        Assert.That(bytes, Does.Not.Contain("Later"));
        Assert.That(
            (await sessions.LoadSessionAsync(bytes)).CategoryColorIndices["Role"],
            Is.EqualTo(7)
        );
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        int count = Writes;
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        Assert.That(Writes, Is.EqualTo(count));
        Assert.That(LastSnapshot(), Does.Contain("Later"));
        Assert.That(context.JSInterop.Invocations["saveSessionFile"], Is.Empty);
    }

    [Test]
    public async Task RecoveryRequiresAcceptanceAndUsesNormalValidationAndAllFields()
    {
        SessionState previous = Working();
        CategoryBase metadata = new(
            "Attribute: FIRE",
            CategorySource.Metadata,
            "metadata:attribute:fire"
        );
        previous.Cards[0] = new(
            [metadata],
            0,
            "Manual",
            false,
            "stable-id",
            1234,
            [metadata.MetadataKey!]
        );
        previous.Combos[0] = new(
            [new(previous.Categories[0], 0, 0)],
            "Route",
            false,
            "group",
            [new("stable-id", 1, 0, RequirementMaximumMode.HandSize)]
        );
        string json = sessions.SerializeSession(previous);
        Recovery(json);
        SessionState? applied = null;
        IRenderedComponent<SessionRecovery> cut = Render(
            new() { HandSize = 5 },
            s =>
            {
                applied = s;

                return Task.FromResult(true);
            }
        );
        Assert.That(applied, Is.Null);
        Assert.That(Writes, Is.Zero);
        await Button(cut, "Restore previous session").ClickAsync(new());
        Assert.That(sessions.SerializeSession(applied!), Is.EqualTo(json));
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
        Assert.That(
            context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[3],
            Is.True
        );
    }

    [Test]
    public async Task DismissKeepsDraftPausedAndEditingNeedsExplicitReplacementConfirmation()
    {
        Recovery(sessions.SerializeSession(Working()));
        SessionState session = new() { HandSize = 5 };
        int applied = 0;
        IRenderedComponent<SessionRecovery> cut = Render(
            session,
            _ =>
            {
                applied++;

                return Task.FromResult(true);
            }
        );
        await Button(cut, "Dismiss").ClickAsync(new());
        IElement dismissed = cut.Find(".session-recovery-dismissed");
        Assert.That(dismissed.ClassList, Does.Contain("mb-3"));
        Assert.That(
            dismissed.TextContent,
            Does.Contain("Recovery draft kept; autosave paused.")
                .And.Contain("Show recovery")
                .And.Contain("Use this workspace for recovery")
        );
        session.Cards.Add(new([], 1, "New"));
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
        await Button(cut, "Show recovery").ClickAsync(new());
        await Button(cut, "Restore previous session").ClickAsync(new());
        Assert.That(applied, Is.Zero);
        await Button(cut, "Replace current work and restore").ClickAsync(new());
        Assert.That(applied, Is.EqualTo(1));
    }

    [Test]
    public async Task DismissedRecoveryKeepsItsBoundaryWhenStorageWarningAppears()
    {
        Recovery(sessions.SerializeSession(Working()));
        IRenderedComponent<SessionRecovery> cut = Render(new() { HandSize = 5 });
        await Button(cut, "Dismiss").ClickAsync(new());
        await cut.InvokeAsync(() =>
            cut.Instance.AutosaveStatus(1, "Local recovery could not be saved.")
        );
        Assert.That(cut.Find(".session-recovery-dismissed").ClassList, Does.Contain("mb-3"));
        Assert.That(
            cut.Find(".alert-warning").TextContent,
            Does.Contain("Local recovery could not be saved.")
        );
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [TestCase("\"Combos\":[{\"Categories\":[null]}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":\"One\"},{\"Id\":\"g\",\"Name\":\"Two\"}]")]
    public void InvalidNestedRecoveryModelStaysPausedAndCannotBeRestored(string invalidField)
    {
        Recovery($"{{\"SchemaVersion\":3,{invalidField}}}");
        bool applied = false;
        IRenderedComponent<SessionRecovery> cut = Render(
            new() { HandSize = 5 },
            _ =>
            {
                applied = true;

                return Task.FromResult(true);
            }
        );
        Assert.That(cut.Markup, Does.Contain("The local draft is invalid"));
        Assert.That(
            cut.FindAll("button")
                .Any(button => button.TextContent.Trim() == "Restore previous session"),
            Is.False
        );
        Assert.That(applied, Is.False);
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [Test]
    public async Task DiscardWaitsForNextEditAndExplicitReplacementResumesPausedDraft()
    {
        Recovery(sessions.SerializeSession(Working()));
        context.JSInterop.Setup<bool>("sessionRecovery.discard", _ => true).SetResult(true);
        SessionState session = new() { HandSize = 5 };
        IRenderedComponent<SessionRecovery> cut = Render(session);
        await Button(cut, "Discard saved draft").ClickAsync(new());
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        Assert.That(Writes, Is.Zero);
        session.Cards.Add(new([], 1, "New"));
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        Assert.That(Writes, Is.EqualTo(1));
    }

    [TestCase("{")]
    [TestCase("{\"SchemaVersion\":999}")]
    [TestCase(
        "{\"SchemaVersion\":2,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":\"A\",\"Id\":\"\"}]}"
    )]
    public void InvalidDraftIsPreservedWithDiscardAndNoWrite(string json)
    {
        Recovery(json);
        IRenderedComponent<SessionRecovery> cut = Render(new() { HandSize = 5 });
        Assert.That(cut.Markup, Does.Contain("invalid or from an unsupported"));
        Assert.That(
            cut.FindAll("button").Any(x => x.TextContent == "Restore previous session"),
            Is.False
        );
        Assert.That(Writes, Is.Zero);
        Assert.That(context.JSInterop.Invocations["sessionRecovery.discard"], Is.Empty);
    }

    [Test]
    public async Task LegacyRecoveryMigratesAndOfflineEnrichmentDoesNotBreakRestore()
    {
        Recovery(
            "{\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":\"Legacy\"}],\"HandSize\":5}"
        );
        enricher
            .Setup(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .ThrowsAsync(new InvalidOperationException());
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        await Button(cut, "Restore previous session").ClickAsync(new());
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Legacy"));
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Id, Is.Not.Empty);
    }

    [Test]
    public async Task InitialExamplePrecedesUnrelatedRecoveryAndExplicitFileLoadReplacesIt()
    {
        Recovery(sessions.SerializeSession(new() { HandSize = 7 }));
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Working();
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(cut.Markup, Does.Not.Contain("Restore previous session"));
        Assert.That(LastSnapshot(), Does.Contain("Fixture"));
        SessionState replacement = Working();
        replacement.Cards[0] = replacement.Cards[0].WithName("File replacement");
        Task<string> update = ObserveNextRecoveryUpdate();
        cut.FindComponents<InputFile>()[1]
            .UploadFiles(
                InputFileContent.CreateFromText(
                    sessions.SerializeSession(replacement),
                    "fixture.json"
                )
            );
        string payload = await AwaitMilestone(
            update,
            "sessionRecovery.update after explicit file replacement"
        );
        Assert.That(payload, Does.Contain("File replacement"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SlowRecoveryEnrichmentCannotUndoNewEditOrDraft(bool draft)
    {
        Recovery(sessions.SerializeSession(Working()));
        TaskCompletionSource response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(response.Task);
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        Task restore = Button(cut, "Restore previous session").ClickAsync(new());

        if (draft)
        {
            await cut.Find("[aria-label='Category name']")
                .InputAsync(new() { Value = "Uncommitted" });
        }
        else
        {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        }

        response.SetResult();
        await restore;
        Assert.That(cut.FindAll(".card-entry"), Is.Empty);
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo(draft ? "5" : "6"));
        Assert.That(cut.Markup, Does.Contain("Current work changed"));
    }

    [Test]
    public async Task NormalFileSaveSharesExactSnapshotAndStillInvokesPickerInterop()
    {
        SessionState session = Working();
        IRenderedComponent<SessionRecovery> cut = Render(session);
        session.Cards.Add(new([], 1, "Added"));
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        await sessions.SaveSessionAsync(session, "fixture");
        JSRuntimeInvocation call = context.JSInterop.Invocations["saveSessionFile"].Single();
        string bytes = Encoding.UTF8.GetString(
            Convert.FromBase64String((string)call.Arguments[1]!)
        );
        Assert.That(call.Arguments[0], Is.EqualTo("fixture.json"));
        Assert.That(bytes, Is.EqualTo(LastSnapshot()));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExampleEnrichmentDoesNotMutateIncomingSessionEvenWhenRejected(bool reject)
    {
        SessionState source = Working();
        Card original = source.Cards[0];
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher
            .Setup(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(
                async (SessionState session) =>
                {
                    started.SetResult();
                    await completion.Task;
                    session.Cards[0] = session.Cards[0].WithName("Enriched fixture");
                }
            );
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = source;
        Task<string> update = ObserveNextRecoveryUpdate();
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        if (reject)
        {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        }

        completion.SetResult();
        string payload = await AwaitMilestone(update, "recovery update after example preparation");

        Assert.That(source.Cards[0], Is.SameAs(original));
        Assert.That(source.Cards[0].Name, Is.EqualTo("Fixture"));

        if (reject)
        {
            Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
            Assert.That(payload, Does.Not.Contain("Enriched fixture"));
            Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("6"));
        }
        else
        {
            Assert.That(
                cut.FindComponent<CardEditor>().Instance.Card.Name,
                Is.EqualTo("Enriched fixture")
            );
            Assert.That(payload, Does.Contain("Enriched fixture"));
        }
    }

    [Test]
    public async Task DelayedInspectionDoesNotOverwriteEdits()
    {
        JSRuntimeInvocationHandler<SessionRecovery.Inspection?> plan =
            context.JSInterop.Setup<SessionRecovery.Inspection?>(
                "sessionRecovery.initialize",
                _ => true
            );
        SessionState session = new() { HandSize = 5 };
        IRenderedComponent<SessionRecovery> cut = Render(session);
        session = new() { HandSize = 6 };
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        plan.SetResult(new() { Exists = true, Payload = sessions.SerializeSession(Working()) });
        cut.WaitForState(() =>
            cut.Markup.Contains("Restore previous session", StringComparison.Ordinal)
        );
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
        await Button(cut, "Restore previous session").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("Replace current work and restore"));
    }

    [Test]
    public async Task TimedOutEnrichmentCannotMutateAppliedWorkspaceOnLateCompletion()
    {
        SessionState source = Working();
        SessionState? enrichmentInput = null;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher
            .Setup(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(
                async (SessionState session) =>
                {
                    enrichmentInput = session;
                    await completion.Task;
                    session.Cards[0].Categories.Add(new("Late category"));
                    session.Cards[0] = session.Cards[0].WithName("Late enrichment");
                }
            );
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = source;
        Task<string> update = ObserveNextRecoveryUpdate();
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        string payload = await update.WaitAsync(TimeSpan.FromSeconds(5));
        Card appliedCard = cut.FindComponent<CardEditor>().Instance.Card;
        Assert.That(payload, Is.EqualTo(sessions.SerializeSession(source)));
        Assert.That(appliedCard, Is.SameAs(source.Cards[0]));
        Assert.That(enrichmentInput, Is.Not.Null);
        completion.SetResult();
        // Observe the enrichment task itself, not a render that may precede it.
        Task enrichment = (Task)enricher.Invocations.Single().ReturnValue!;
        await enrichment;
        cut.Render();
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card, Is.SameAs(appliedCard));
        Assert.That(appliedCard.Name, Is.EqualTo("Fixture"));
        Assert.That(appliedCard.Categories, Is.Empty);
        Assert.That(LastSnapshot(), Is.EqualTo(payload));
    }

    [Test]
    public async Task DiscardCompletionDoesNotDropAnEditMadeWhileInteropIsPending()
    {
        Recovery(sessions.SerializeSession(Working()));
        JSRuntimeInvocationHandler<bool> plan = context.JSInterop.Setup<bool>(
            "sessionRecovery.discard",
            _ => true
        );
        SessionState session = new() { HandSize = 5 };
        IRenderedComponent<SessionRecovery> cut = Render(session);
        Task discard = Button(cut, "Discard saved draft").ClickAsync(new());
        session.Cards.Add(new([], 1, "During discard"));
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        plan.SetResult(true);
        await discard;
        Assert.That(LastSnapshot(), Does.Contain("During discard"));
    }

    [Test]
    public async Task PendingExampleCannotUndoEditsAndThoseEditsAreAutosavedAfterInspection()
    {
        TaskCompletionSource<bool> inspectionStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        JSRuntimeInvocationHandler<SessionRecovery.Inspection?> inspection =
            context.JSInterop.Setup<SessionRecovery.Inspection?>(
                "sessionRecovery.initialize",
                _ =>
                {
                    inspectionStarted.TrySetResult(true);

                    return true;
                }
            );
        Task<string> update = ObserveNextRecoveryUpdate();
        TaskCompletionSource response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher.Setup(x => x.EnrichAsync(It.IsAny<SessionState>())).Returns(response.Task);
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = Working();
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(context.JSInterop.Invocations["sessionRecovery.initialize"], Is.Empty);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        response.SetResult();
        await AwaitMilestone(
            inspectionStarted.Task,
            "recovery inspection after pending example rejection"
        );

        // The parent warning is an intermediate render, not an autosave barrier.
        // Keep inspection pending to exercise that ordering deterministically.
        try
        {
            cut.WaitForState(() =>
                cut.Markup.Contains(
                    "Current work changed while loading the example",
                    StringComparison.Ordinal
                )
            );
            Assert.That(cut.Markup, Does.Contain("Current work changed while loading the example"));
            Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("6"));
            Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
            Assert.That(
                Writes,
                Is.Zero,
                "inspection must complete before an edited workspace is queued"
            );
        }
        finally
        {
            inspection.SetResult(new() { Exists = false });
        }

        string payload = await AwaitMilestone(
            update,
            "sessionRecovery.update for the edited workspace after inspection"
        );
        SessionState saved = await sessions.LoadSessionAsync(payload);
        Assert.That(saved.HandSize, Is.EqualTo(6));
        Assert.That(saved.Cards, Is.Empty, "the queued payload must exclude the rejected example");
        Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("6"));
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
    }

    [Test]
    public async Task LateSaveStatusCannotClearOrReplaceStatusForANewerGeneration()
    {
        SessionState session = Working();
        IRenderedComponent<SessionRecovery> cut = Render(session);
        long first = (long)
            context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[1]!;
        session.Cards.Add(new([], 1, "Next"));
        cut.SetParametersAndRender(p => p.Add(x => x.Session, session));
        long second = (long)
            context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[1]!;
        await cut.InvokeAsync(() => cut.Instance.AutosaveStatus(second, "Current failure"));
        await cut.InvokeAsync(() => cut.Instance.AutosaveStatus(first, null));
        Assert.That(cut.Markup, Does.Contain("Current failure"));
    }

    [Test]
    public async Task NewExplicitFileLoadWinsOverOlderRecoveryCompletion()
    {
        Recovery(sessions.SerializeSession(Working()));
        TaskCompletionSource response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher
            .SetupSequence(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(response.Task)
            .Returns(Task.CompletedTask);
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        Task restore = Button(cut, "Restore previous session").ClickAsync(new());
        SessionState replacement = Working();
        replacement.Cards[0] = replacement.Cards[0].WithName("New explicit file");
        Task<string> update = ObserveNextRecoveryUpdate();
        cut.FindComponents<InputFile>()[1]
            .UploadFiles(
                InputFileContent.CreateFromText(
                    sessions.SerializeSession(replacement),
                    "fixture.json"
                )
            );
        string payload = await AwaitMilestone(
            update,
            "sessionRecovery.update for the newer explicit file"
        );
        Assert.That(payload, Does.Contain("New explicit file"));
        Assert.That(
            cut.FindComponent<CardEditor>().Instance.Card.Name,
            Is.EqualTo("New explicit file")
        );
        response.SetResult();
        await restore;
        Assert.That(
            cut.FindComponent<CardEditor>().Instance.Card.Name,
            Is.EqualTo("New explicit file")
        );
        Assert.That(LastSnapshot(), Does.Contain("New explicit file"));
    }

    [Test]
    public void FailedExplicitFileKeepsWorkingStateAndRecoveryPaused()
    {
        Recovery(sessions.SerializeSession(Working()));
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        cut.FindComponents<InputFile>()[1]
            .UploadFiles(InputFileContent.CreateFromText("{", "fixture.json"));
        cut.WaitForState(() =>
            cut.Markup.Contains("Failed to load session", StringComparison.Ordinal)
        );
        Assert.That(cut.Markup, Does.Contain("Failed to load session"));
        Assert.That(cut.Markup, Does.Contain("Restore previous session"));
        Assert.That(Writes, Is.Zero);
    }

    [Test]
    public async Task DisposedParentRejectsLateRecoveryAndComponentReinitializesWithNewOwner()
    {
        Recovery(sessions.SerializeSession(Working()));
        TaskCompletionSource response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        enricher
            .SetupSequence(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(response.Task)
            .Returns(Task.CompletedTask);
        IRenderedComponent<RecoveryHost> host = context.RenderComponent<RecoveryHost>(p =>
            p.Add(x => x.Visible, true)
        );
        IRenderedComponent<ProbabilityCalculatorComponent> cut =
            host.FindComponent<ProbabilityCalculatorComponent>();
        Task restore = Button(cut, "Restore previous session").ClickAsync(new());
        host.SetParametersAndRender(p => p.Add(x => x.Visible, false));
        response.SetResult();
        await restore;
        IRenderedComponent<ProbabilityCalculatorComponent> next =
            context.RenderComponent<ProbabilityCalculatorComponent>();
        Assert.That(next.Markup, Does.Contain("Restore previous session"));
        Assert.That(
            context
                .JSInterop.Invocations["sessionRecovery.initialize"]
                .Select(x => x.Arguments[0])
                .Distinct()
                .Count(),
            Is.EqualTo(2)
        );
        Assert.That(Writes, Is.Zero);
    }

    public sealed class RecoveryHost : ComponentBase
    {
        [Parameter]
        public bool Visible { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (!Visible)
            {
                return;
            }

            builder.OpenComponent<ProbabilityCalculatorComponent>(0);
            builder.CloseComponent();
        }
    }
}
