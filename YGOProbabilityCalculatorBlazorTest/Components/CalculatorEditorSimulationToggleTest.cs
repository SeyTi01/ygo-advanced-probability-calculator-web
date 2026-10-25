using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorSimulationToggleTest : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task TogglePreservesEntryIdentityPlansDraftsAndAllSavedChannels(bool custom) {
        Card source = SearchEffectsTest.Rota();
        if (custom) {
            source = source.WithSearch(new(CardSelector.For(a), false)).WithOncePerTurn(true);
        }
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Cards = [source, source.Duplicate(), new([a], 2)], Categories = [a], HandSize = 2,
            Combos = [new([new(a, 1, 2)])]
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        editor.Find(".accordion-button").Click();
        Assert.That(editor.FindAll(".simulation-editor"), Is.Empty);
        Assert.That(editor.Find(".simulation-toggle").GetAttribute("type"), Is.EqualTo("button"));
        await Button(cut, "Calculate").ClickAsync(new());
        await Button(cut, "Pin result").ClickAsync(new());
        editor.Find(".simulation-edit").Click();
        editor.Find("#cardSearchOnce0").Change(!source.OncePerTurn);
        editor.Find(".simulation-toggle").Click();
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.False);
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.SimulationEnabled, Is.True);
        Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(source.EffectPlan));
        Assert.That(editor.Instance.Card.EffectProfile, Is.EqualTo(source.EffectProfile));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(source.Id));
        Assert.That(editor.Find("#cardSearchOnce0").HasAttribute("checked"), Is.EqualTo(!source.OncePerTurn));
        Assert.That(editor.FindAll(".simulation-disabled"), Has.Count.EqualTo(1));

        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.False);
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.SimulationEnabled, Is.True);
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Fast" });
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        foreach (string saved in new[] { SavedSessionJson(), cut.FindComponent<SessionShareButton>().Instance.Snapshot,
            context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!.ToString()! }) {
            Card loaded = (await codec.LoadSessionAsync(saved)).Cards[0];
            Assert.That(loaded.SimulationEnabled, Is.False);
            Assert.That(loaded.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(loaded.EffectProfile, Is.EqualTo(source.EffectProfile));
        }
        editor.Find(".simulation-save").Click();
        Assert.That(editor.Instance.Card.OncePerTurn, Is.EqualTo(!source.OncePerTurn));
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.False, "saving a preserved draft keeps the switch");
        editor.Find(".simulation-toggle").Click();
        Assert.That(editor.Instance.Card.SimulationEnabled, Is.True);
        Assert.That(editor.Instance.Card.OncePerTurn, Is.EqualTo(!source.OncePerTurn));
        Assert.That(cut.FindComponents<CardEditor>()[2].FindAll(".simulation-toggle"), Is.Empty);
    }

    [Test]
    public async Task ToggleCancelsInFlightWorkerAndRejectsItsLateReply() {
        TaskCompletionSource<ProbabilityCalculationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        Mock<IBackgroundCalculator> background = new();
        background.Setup(calculator => calculator.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => { captured = token; return completion.Task; });
        context.Services.AddSingleton(background.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [SearchEffectsTest.Rota(), SearchEffectsTest.Warrior()],
            HandSize = 1, Combos = [new([], cards: [new("target", 1, 1)])] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        editor.Find(".accordion-button").Click();
        Task calculation = Button(cut, "Calculate").ClickAsync(new());
        editor.Find(".simulation-toggle").Click();
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(new(0.99, [new(0, "Late result", 0.99)]));
        await calculation;
        Assert.That(cut.FindAll(".probability-total-value"), Is.Empty);
    }
}
