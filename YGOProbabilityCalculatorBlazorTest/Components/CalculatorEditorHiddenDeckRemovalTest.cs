using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public class CalculatorEditorHiddenDeckRemovalTest : CalculatorEditorTestBase {
    [Test]
    public async Task EditorPreservesDraftsSavesRecoveryAndClearsWithDrawNone() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().First();
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        OpenSimulation(editor);
        await editor.Find("#cardDeckCost0").ChangeAsync(new() { Value = "10" });
        await SaveSimulation(editor);
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        Assert.That(editor.Markup, Does.Contain("Banish 10"));
        Assert.That(SimulationDetails(editor), Does.Contain("not banished identities"));
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        Assert.That((await codec.LoadSessionAsync(SavedSessionJson())).Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        string recovery = (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;
        Assert.That((await codec.LoadSessionAsync(recovery)).Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Simplified" });
        Assert.That(editor.Find("#cardDrawModeHelp0").TextContent, Does.Contain("costs and removals ignored"));
        foreach (string invalid in new[] { "-1", "61", "wrong" }) {
            await editor.Find("#cardDeckCost0").ChangeAsync(new() { Value = invalid });
            await SaveSimulation(editor);
            Assert.That(editor.Instance.Card.PreDrawDeckRemovalCount, Is.EqualTo(10));
        }
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await SaveSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawDeckRemovalCount, Is.Zero);
        Assert.That(editor.FindAll("#cardDeckCost0"), Is.Empty);
    }

    [Test]
    public async Task EditingHiddenCostCancelsAndRejectsLateResult() {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => { captured = token; return completion.Task; });
        context.Services.AddSingleton(background.Object);
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        OpenSimulation(cut.FindComponent<CardEditor>());
        await cut.Find("#cardDeckCost0").ChangeAsync(new() { Value = "10" });
        await SaveSimulation(cut.FindComponent<CardEditor>());
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(new(0.99, [new(0, "Late result", 0.99)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-total-value"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("Late result"));
    }
}
