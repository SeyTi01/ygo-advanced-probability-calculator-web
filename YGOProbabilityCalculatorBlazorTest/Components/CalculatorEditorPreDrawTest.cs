using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorPreDrawTest : CalculatorEditorTestBase {
    [Test]
    public async Task PreDrawCountCompositeSelectorAndReferencedCategoryDeletion() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().ToArray()[0];
        OpenSimulation(editor);
        await editor.Find("#cardCost0").ChangeAsync(new() { Value = "2" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await editor.Find("#cardCostEligibility0").ChangeAsync(new() { Value = "user:A" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await AddEligibilityProperty(editor, "metadata:kind:monster");
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await AddEligibilityAlternative(editor, "A", "user:B");
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawRemovalCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        Assert.That(editor.Instance.Card.PreDrawSelector.Children.Any(child => child.Kind == CardSelectorKind.AnyOf), Is.True);
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(cut.FindAll("[aria-label='Edit category A']"), Has.Count.EqualTo(1));
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "OpeningHand" });
        Assert.That(editor.Find("#cardDrawModeHelp0").TextContent, Does.Contain("saved simulation is off"));
        await cut.Find(".calculation-settings-trigger").ClickAsync(new());
        await cut.Find("#calculationModel").ChangeAsync(new() { Value = "Fast" });
        await editor.Find("#cardCost0").ChangeAsync(new() { Value = "0" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.That(editor.FindAll("#cardCostEligibility0"), Is.Empty);
    }

    [Test]
    public async Task PreDrawEditorWorksInBasicPreservesDraftsAndRoundTrips() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPreDrawRemovalCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().ToArray()[0];
        string id = editor.Instance.Card.Id;
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        OpenSimulation(editor);
        await editor.Find("#cardCostEligibility0").ChangeAsync(new() { Value = "user:A" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        AssertPreviousResult(cut);
        await AddEligibilityAlternative(editor, "A", "metadata:attribute:dark");
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].PreDrawSelector, Is.EqualTo(editor.Instance.Card.PreDrawSelector));
        await RowAction(cut, "card First, row 1", "Move down").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>().ToArray()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        await RemoveEligibility(editor, "A");
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Category));
        await Button(editor, "Clear filter").ClickAsync(new());
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Any));
        await editor.Find("#cardCostEligibility1").ChangeAsync(new() { Value = "user:B" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        Assert.That(editor.Instance.Card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Any));
        Assert.That(editor.Instance.Card.PreDrawRemovalCount, Is.Zero);
    }

    [Test]
    public async Task PreDrawUserCategoryRenameKeepsFilterAndEditorDraft() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPreDrawRemovalCount(1).WithPreDrawSelector(CardSelector.For(a));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().ToArray()[0];
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.FindComponents<CardEditor>().ToArray()[0].Instance, Is.SameAs(editor.Instance));
        Assert.That(editor.Instance.Card.PreDrawSelector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed" }));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
    }
}
