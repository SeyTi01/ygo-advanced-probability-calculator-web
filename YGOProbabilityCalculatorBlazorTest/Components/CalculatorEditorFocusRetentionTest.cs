using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorFocusRetentionTest : CalculatorEditorTestBase {
    [Test]
    public void WorkspaceReplacementDoesNotRetainRemovedFocusTargets() {
        IRenderedComponent<CategoryListEditor> categories = context.RenderComponent<CategoryListEditor>(parameters => parameters
            .Add(component => component.CategoryBases, new List<CategoryBase> { a, b }));
        IRenderedComponent<ComboListEditor> combos = context.RenderComponent<ComboListEditor>(parameters => parameters
            .Add(component => component.ComboGroups, new[] { new ComboGroup("old-group", "Old group") }));

        for (int replacement = 0; replacement < 10; replacement++) {
            categories.SetParametersAndRender(parameters => parameters
                .Add(component => component.CategoryBases, new List<CategoryBase> { new($"Renamed {replacement}"), b }));
            combos.SetParametersAndRender(parameters => parameters
                .Add(component => component.ComboGroups, new[] { new ComboGroup($"group-{replacement}", "Current group") }));
        }

        // DOM output alone cannot reveal retained references to removed controls.
        Assert.That(FocusTargets(categories.Instance, "_categoryNameButtonReferences"), Is.EquivalentTo(new[] { "Renamed 9", "B" }));
        Assert.That(FocusTargets(combos.Instance, "_groupNameButtonReferences"), Is.EqualTo(new[] { "group-9" }));
        Assert.That(categories.Find("[aria-label='Edit category B']"), Is.Not.Null);
        Assert.That(combos.Find("[aria-label='Edit group Current group']"), Is.Not.Null);
    }

    private static IEnumerable<string> FocusTargets(object component, string field) {
        Dictionary<string, ElementReference> references = (Dictionary<string, ElementReference>)component.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

        return references.Keys;
    }
}
