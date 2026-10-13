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

    [TestCase("Configured", true, 1)]
    [TestCase("Simplified", true, 1)]
    [TestCase("OpeningHand", true, 1)]
    [TestCase("Configured", false, 1)]
    [TestCase("Simplified", false, 0)]
    [TestCase("OpeningHand", false, 0)]
    public async Task PerformanceIsPermanentWithoutGlobalRiskAndPreservesDisclosure(string mode, bool active, int copies) {
        Card allure = BuiltIn(1475311).WithActive(active).WithCopies(copies);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Cards = [allure, new([], 39)] });
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = mode });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        Assert.That(editor.FindAll(".simulation-performance"), Is.Empty, "collapsed notes add no paragraph to the default view");
        Assert.That(editor.Find(".simulation-details-toggle").GetAttribute("title"), Does.Contain("performance"));
        Assert.That(SimulationDetails(editor), Does.Contain("Performance:").And.Contain("in Configured mode").And.Contain("can become expensive"));
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
        Assert.That(SimulationDetails(editor), Does.Contain("Performance:").And.Contain("Saved limitation").And.Contain("revision 7"));
        Assert.That(editor.Find(".simulation-performance strong").TextContent, Is.EqualTo("Performance:"));
        Assert.That(editor.Find(".simulation-limitations strong").TextContent, Is.EqualTo("Model limits:"));
        Assert.That(editor.Find(".simulation-limitations").TextContent.Trim(), Is.EqualTo("Model limits: Saved limitation"));
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
        Assert.That(SimulationDetails(editor), Does.Contain("Performance:"));
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
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.Find(".draw-complexity-notice").TextContent,
            Does.Contain("Potential contributors: Allure candidate").And.Contain("Model notes (Performance)").And.Not.Contain("Desires counterexample"));
        IRenderedComponent<CardEditor> desiresEditor = cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == desires.Id);
        Assert.That(SimulationDetails(desiresEditor), Does.Not.Contain("Performance:"));
        int index = session.Cards.IndexOf(allure);
        await cut.Find($"#cardName{index}").InputAsync(new() { Value = "Current candidate name" });
        await cut.Find($"#cardName{index}").BlurAsync(new());
        Assert.That(cut.Find(".draw-complexity-notice").TextContent, Does.Contain("Current candidate name").And.Not.Contain("Allure candidate"));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Simplified" });
        Assert.That(cut.FindAll(".draw-complexity-notice"), Is.Empty);
        Assert.That(SimulationDetails(cut.FindComponents<CardEditor>().Single(editor => editor.Instance.Card.Id == allure.Id)), Does.Contain("Performance:"));
        compiler.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CombinationOnlyWarningDoesNotAttributeCostToGracefulOrGreed() {
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        session.Cards[session.Cards.IndexOf(graceful)] = graceful.WithCopies(2);
        session.Cards.Add(BuiltIn(55144522));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Assert.That(cut.Find(".draw-complexity-notice").TextContent,
            Does.Contain("effect combinations").And.Contain("no individual effect is identified").And.Not.Contain("Potential contributors:"));
        Assert.That(cut.FindAll(".simulation-performance"), Is.Empty);
        foreach (IRenderedComponent<CardEditor> editor in cut.FindComponents<CardEditor>().Where(editor => editor.Instance.Card.DrawCount is not null)) {
            if (editor.FindAll(".simulation-details-toggle").Count > 0) {
                Assert.That(SimulationDetails(editor), Does.Not.Contain("Performance:"));
            }
        }
    }
}
