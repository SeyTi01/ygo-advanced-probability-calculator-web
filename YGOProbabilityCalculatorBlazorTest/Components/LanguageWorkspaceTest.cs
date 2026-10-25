using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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

    [Test]
    public async Task UnsavedWorkspaceAndUnsubmittedFormsRestoreWithoutCommittingTheirDrafts() {
        SessionState session = new() {
            HandSize = 1, Categories = [a], Cards = [new([a], 3, "Accepted card", id: "card")],
            Combos = [new([new(a, 1, 1)], name: "Accepted combo")]
        };
        LanguageWorkspace original = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> first = RenderWorkspace(original, session);
        await first.Find("#quickCardName").InputAsync(new() { Value = "Unsubmitted new card" });
        await first.Find(".card-editor .accordion-button").ClickAsync(new());
        OpenSimulation(first.FindComponent<CardEditor>());
        await first.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        await first.Find(".combo-editor .accordion-button").ClickAsync(new());
        await first.Find("button[data-requirement='category:0']").ClickAsync(new());
        await first.Find("#minCount0").InputAsync(new() { Value = "0" });
        Dictionary<string, string> states = await first.InvokeAsync(original.Capture);
        Assert.That(states.Keys, Does.Contain("/quick-add"));
        first.Dispose();

        LanguageWorkspace restored = new();
        restored.Begin(states);
        IRenderedComponent<CascadingValue<LanguageWorkspace>> second = RenderWorkspace(restored);
        second.WaitForState(() => restored.Complete || restored.Failure is not null);
        Assert.That(restored.Failure, Is.Null);
        Assert.That(second.Find("#quickCardName").GetAttribute("value"), Is.EqualTo("Unsubmitted new card"));
        Assert.That(second.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(second.Find("#cardDraw0").GetAttribute("value"), Is.EqualTo("2"));
        Card accepted = second.FindComponent<CardEditor>().Instance.Card;
        Assert.That(accepted.DrawCount, Is.EqualTo(session.Cards[0].DrawCount), "The simulation draft must remain unsubmitted.");
        Assert.That(second.FindComponent<ComboEditor>().Instance.Combo.Categories.Single().MinCount, Is.EqualTo(1));
        Assert.That(accepted.Name, Is.EqualTo("Accepted card"));
    }

    [Test]
    public async Task NestedEffectAndInvalidNumericDraftsRetainSeparateOwnership() {
        Card card = new Card([a], 3, "Effect", id: "effect")
            .WithDrawCount(2).WithPreDrawRemovalCount(1).WithPreDrawSelector(CardSelector.For(a))
            .WithPostDrawDiscardCount(1).WithPostDrawSelector(CardSelector.For(a));
        LanguageWorkspace original = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> first = RenderWorkspace(original, new() {
            Categories = [a], Cards = [card], HandSize = 1
        });
        await first.Find(".card-editor .accordion-button").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = first.FindComponent<CardEditor>();
        OpenSimulation(editor);
        await editor.FindComponents<SelectorExpressionEditor>()[0].Find(".requirement-edit").ClickAsync(new());
        await editor.Find(".selector-expression form .requirement-property-picker > select").ChangeAsync(new() { Value = "numeric:level" });
        await editor.Find(".selector-expression form .requirement-numeric-fields input").InputAsync(new() { Value = "" });
        Dictionary<string, string> states = await first.InvokeAsync(original.Capture);
        Assert.That(states.Keys.Count(key => key.StartsWith("/node:", StringComparison.Ordinal)), Is.EqualTo(2));
        first.Dispose();
        LanguageWorkspace restored = new();
        restored.Begin(states);
        IRenderedComponent<CascadingValue<LanguageWorkspace>> second = RenderWorkspace(restored);
        second.WaitForState(() => restored.Complete || restored.Failure is not null);
        Assert.That(restored.Failure, Is.Null);
        Assert.That(second.Find(".selector-expression form .requirement-numeric-fields input").GetAttribute("value"), Is.Empty);
        Assert.That(second.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        Card accepted = second.FindComponent<CardEditor>().Instance.Card;
        Assert.That(accepted.PreDrawSelector, Is.EqualTo(card.PreDrawSelector));
        Assert.That(accepted.PostDrawSelector, Is.EqualTo(card.PostDrawSelector));
    }

    [Test]
    public void AChangedApplicationBuildCannotReplayPrivateEditorState() {
        LanguageWorkspace original = new();
        Dictionary<string, string> states = original.Capture();
        states["$build"] = "different-build";
        LanguageWorkspace restored = new();
        restored.Begin(states);
        Assert.That(restored.Failure, Is.Not.Null);
        Assert.Throws<InvalidOperationException>(() => restored.Capture());
    }

    [Test]
    public void EmptyWorkspaceWithoutActiveCombosRestores() {
        LanguageWorkspace original = new();
        IRenderedComponent<CascadingValue<LanguageWorkspace>> first = RenderWorkspace(original);
        Dictionary<string, string> states = first.InvokeAsync(original.Capture).GetAwaiter().GetResult();
        first.Dispose();
        LanguageWorkspace restored = new();
        restored.Begin(states);
        IRenderedComponent<CascadingValue<LanguageWorkspace>> second = RenderWorkspace(restored);
        second.WaitForState(() => restored.Complete);
        Assert.That(second.FindComponents<ComboEditor>(), Is.Empty);
        Assert.That(second.FindComponents<CardEditor>(), Is.Empty);
    }

    [Test]
    public void ReplacedEditorCannotUnregisterTheNewOwnerAndRestoreFailureKeepsTheCheckpoint() {
        LanguageWorkspace workspace = new();
        object first = new();
        object second = new();
        workspace.Register("key", first, () => "old", _ => { });
        workspace.Register("key", second, () => "new", _ => { });
        workspace.Remove("key", first);
        Assert.That(workspace.Capture()["key"], Is.EqualTo("new"));
        Dictionary<string, string> states = workspace.Capture();
        states["key"] = "retained";
        workspace.Begin(states);
        workspace.Register("key", second, () => "new", _ => throw new InvalidOperationException("failed restoration"));
        Assert.That(workspace.Complete, Is.False);
        Assert.That(workspace.Failure, Is.EqualTo("failed restoration"));
        Assert.Throws<InvalidOperationException>(() => workspace.Capture());
    }
}
