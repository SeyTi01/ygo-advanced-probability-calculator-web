using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorPerformanceTest : CalculatorEditorTestBase {
    private static Card BuiltIn(int identity) => CardEffectRegistry.Default.RestoreBuiltIn(new([], 1, externalCardId: identity));

    [TestCase("Fast", true, 1)]
    [TestCase("OpeningHand", true, 1)]
    [TestCase("Fast", false, 1)]
    [TestCase("OpeningHand", false, 0)]
    public async Task PerformanceIsPermanentWithoutGlobalRiskAndPreservesDisclosure(string mode, bool active, int copies) {
        Card allure = BuiltIn(1475311).WithActive(active).WithCopies(copies);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [allure, new([], 39)] });
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        Assert.That(editor.FindAll(".simulation-performance"), Is.Empty, "collapsed notes add no paragraph to the default view");
        SimulationDetails(editor);
        Assert.That(editor.FindAll(".simulation-performance"), Has.Count.EqualTo(1));
        await cut.Find("#cardName0").InputAsync(new() { Value = "Renamed effect" });
        Assert.That(editor.Find(".simulation-details-toggle").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        await editor.Find(".simulation-details-toggle").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".simulation-details"), Is.Empty);
        Assert.That(context.JSInterop.Invocations.Any(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus"), Is.True);
    }

    [Test]
    public async Task SavedPlanControlsNoteThroughDraftsCustomizationAndRestore() {
        Card allure = BuiltIn(1475311);
        allure = allure.WithEffect(allure.EffectPlan, allure.EffectProfile with { Revision = 7, Limitations = "Saved limitation" });
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [allure] });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        Assert.That(SimulationDetails(editor), Does.Contain(allure.EffectProfile.Limitations));
        Assert.That(editor.Find(".simulation-performance strong").TextContent, Is.EqualTo("Performance:"));
        Assert.That(editor.Find(".simulation-limitations strong").TextContent, Is.EqualTo("Model limits:"));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        Assert.That(editor.FindAll(".simulation-performance"), Has.Count.EqualTo(1), "unsaved drafts do not change the effective plan");
        await editor.Find(".simulation-cancel").ClickAsync(new());
        OpenSimulation(editor);
        await editor.Find("#cardDiscard0").ChangeAsync(new() { Value = "0" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.FindAll(".simulation-details-toggle, .simulation-performance"), Is.Empty);
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Custom simulation"));
        await editor.Find(".simulation-restore").ClickAsync(new());
        SimulationDetails(editor);
        Assert.That(editor.FindAll(".simulation-performance"), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".simulation-source").TextContent, Is.EqualTo("Built-in simulation"));
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "" });
        await editor.Find(".simulation-save").ClickAsync(new());
        Assert.That(editor.FindAll(".simulation-details-toggle, .simulation-performance"), Is.Empty);
    }

    [Test]
    public async Task GlobalWarningNamesOnlyIntrinsicCandidatesAndKeepsNamesCurrent() {
        Mock<IProbabilityCalculatorService> compiler = new(MockBehavior.Strict);
        context.Services.AddSingleton(compiler.Object);
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        Card allure = BuiltIn(1475311).WithName("Allure candidate");
        Card desires = BuiltIn(35261759).WithName("Desires counterexample");
        session.Cards[session.Cards.IndexOf(graceful)] = desires;
        session.Cards.Add(allure);
        // Unfiltered Graceful removal prevents the common-pool proof. Keep this
        // warning/rename test on a genuinely expensive ordered configuration.
        session.Cards.Add(graceful.WithCopies(1));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.Find(".draw-complexity-notice").TextContent,
            Does.Contain(allure.Name).And.Not.Contain(desires.Name));
        IRenderedComponent<CardEditor> desiresEditor = cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == desires.Id);
        SimulationDetails(desiresEditor);
        Assert.That(desiresEditor.FindAll(".simulation-performance"), Is.Empty);
        int index = session.Cards.IndexOf(allure);
        await cut.Find($"#cardName{index}").InputAsync(new() { Value = "Current candidate name" });
        await cut.Find($"#cardName{index}").BlurAsync(new());
        Assert.That(cut.Find(".draw-complexity-notice").TextContent, Does.Contain("Current candidate name").And.Not.Contain("Allure candidate"));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        IRenderedComponent<CardEditor> allureEditor = cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == allure.Id);
        SimulationDetails(allureEditor);
        Assert.That(allureEditor.FindAll(".simulation-performance"), Has.Count.EqualTo(1));
        compiler.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SafeMixedPoolRemovesGlobalWarningWithoutRunningTheCalculator() {
        Mock<IProbabilityCalculatorService> compiler = new(MockBehavior.Strict);
        context.Services.AddSingleton(compiler.Object);
        SessionState session = await RealisticMixedFixture.Load(vision: false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        compiler.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CombinationOnlyWarningDoesNotAttributeCostToGracefulOrGreed() {
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        session.Cards[session.Cards.IndexOf(graceful)] = graceful.WithCopies(2);
        session.Cards.Add(BuiltIn(55144522));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.FindAll(".draw-complexity-notice"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll(".simulation-performance"), Is.Empty);
        foreach (IRenderedComponent<CardEditor> editor in cut.FindComponents<CardEditor>().Where(editor => editor.Instance.Card.DrawCount is not null)) {
            if (editor.FindAll(".simulation-details-toggle").Count > 0) {
                SimulationDetails(editor);
                Assert.That(editor.FindAll(".simulation-performance"), Is.Empty);
            }
        }
    }
}
