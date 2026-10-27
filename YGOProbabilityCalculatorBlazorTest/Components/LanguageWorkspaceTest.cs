using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class LanguageWorkspaceTest : CalculatorEditorTestBase {
    private IRenderedComponent<CascadingValue<LanguageWorkspace>> RenderWorkspace(LanguageWorkspace workspace, SessionState? session = null) {
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return context.RenderComponent<CascadingValue<LanguageWorkspace>>(parameters => parameters
            .Add(component => component.Value, workspace)
            .Add(component => component.IsFixed, true)
            .AddChildContent<ProbabilityCalculatorComponent>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OneCheckpointRestoresAcceptedEmptyOrPopulatedWorkspace(bool populated) {
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        SessionState? session = populated ? await codec.LoadSessionAsync(await File.ReadAllTextAsync(
            Path.Combine(NUnit.Framework.TestContext.CurrentContext.TestDirectory, "Fixtures", "shipped_example_session_state.json"))) : null;
        LanguageWorkspace original = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> first = RenderWorkspace(original, session);
        first.WaitForState(() => original.Capture is not null);
        string checkpoint = await first.InvokeAsync(original.Capture!);
        await first.InvokeAsync(() => first.FindComponent<ProbabilityCalculatorComponent>().Instance.DisposeAsync().AsTask());
        first.Dispose();
        Assert.That(original.Capture, Is.Null, "Navigation must release the old calculator owner.");

        LanguageWorkspace restored = new();
        restored.Begin(checkpoint);
        IRenderedComponent<CascadingValue<LanguageWorkspace>> second = RenderWorkspace(restored);
        second.WaitForState(() => restored.Restored || restored.Failure is not null);
        Assert.That(restored.Failure, Is.Null);
        restored.Finish();
        Assert.That(await second.InvokeAsync(restored.Capture!), Is.EqualTo(checkpoint));
        Assert.That(second.FindComponents<CardEditor>().Count, Is.EqualTo(session?.Cards.Count ?? 0));
    }

    [Test]
    public async Task AcceptedExactResultAndManualPinSurviveTheCentralCheckpoint() {
        LanguageWorkspace original = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> first = RenderWorkspace(original, new() {
            HandSize = 1, Categories = [a], Cards = [new([a], 2, "Target", id: "a"), new([], 1, "Filler", id: "b")],
            Combos = [new([new(a, 1, 1)], name: "Route")]
        });
        await Button(first, "Calculate").ClickAsync(new());
        await Button(first, "Pin result").ClickAsync(new());
        string checkpoint = await first.InvokeAsync(original.Capture!);
        await first.InvokeAsync(() => first.FindComponent<ProbabilityCalculatorComponent>().Instance.DisposeAsync().AsTask());
        first.Dispose();
        LanguageWorkspace restored = new();
        restored.Begin(checkpoint);
        IRenderedComponent<CascadingValue<LanguageWorkspace>> second = RenderWorkspace(restored);
        second.WaitForState(() => restored.Restored || restored.Failure is not null);
        Assert.That(restored.Failure, Is.Null);
        Assert.That(second.Find(".pinned-result").TextContent, Does.Contain("66"));
        Assert.That(second.Find(".probability-total .probability-with-accuracy").GetAttribute("title"), Does.Contain("2/3"));
        restored.Finish();
        Assert.That(await second.InvokeAsync(restored.Capture!), Is.EqualTo(checkpoint));
    }

    [Test]
    public void MalformedCheckpointReportsFailureWithoutStartingAutosaveOrChangingAcceptedWork() {
        LanguageWorkspace workspace = new();
        workspace.Begin("invalid checkpoint");
        IRenderedComponent<CascadingValue<LanguageWorkspace>> cut = RenderWorkspace(workspace);
        cut.WaitForState(() => workspace.Failure is not null);
        Assert.That(workspace.Pending, Is.EqualTo("invalid checkpoint"));
        Assert.That(workspace.Restoring, Is.True);
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        Assert.That(cut.FindComponent<SessionRecovery>().Instance.Suspended, Is.True);
    }

    [Test]
    public async Task PendingDeckImportBlocksCaptureUntilItsOwnedOperationFinishes() {
        TaskCompletionSource<List<Card>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IDeckImportService> imports = new();
        imports.Setup(service => service.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).Returns(() => { started.SetResult(); return gate.Task; });
        context.Services.AddSingleton(imports.Object);
        LanguageWorkspace workspace = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> cut = RenderWorkspace(workspace);
        Task load = cut.InvokeAsync(() => cut.FindComponents<InputFile>().First().Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([Mock.Of<IBrowserFile>()])));
        await started.Task;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await cut.InvokeAsync(workspace.Capture!));
        gate.SetResult([new([], 1, "Imported", id: "imported")]);
        await load;
        Assert.That(await cut.InvokeAsync(workspace.Capture!), Does.Contain("imported"));
    }

    [Test]
    public async Task RequirementDraftMarkerBlocksEvenACollapsedEditAndClearsAfterCancel() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a], Cards = [new([a], 1, "A", id: "a")], Combos = [new([new(a, 1, 1)])]
        });
        await cut.Find(".combo-editor .accordion-button").ClickAsync(new());
        await cut.Find("button[data-requirement='category:0']").ClickAsync(new());
        Assert.That(cut.Find(".combo-editor").GetAttribute("data-language-edit"), Is.EqualTo("true"));
        await cut.Find(".combo-editor .accordion-button").ClickAsync(new());
        Assert.That(cut.Find(".combo-editor").GetAttribute("data-language-edit"), Is.EqualTo("true"));
        await cut.Find(".requirement-form button").ClickAsync(new());
        Assert.That(cut.Find(".combo-editor").GetAttribute("data-language-edit"), Is.Null);
    }
}
