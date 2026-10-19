using System.Text.Json;
using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorReversibleOperatorsTest : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task ComboCannotMergeNestedGroupsOrRepeatedProperties(bool repeated) {
        ComboAlternative[] leaves = repeated
            ? [ComboAlternative.For(new ComboCategory(a, 1, 3)), ComboAlternative.For(new ComboCategory(a, 1, 3))]
            : [ComboAlternative.For(ComboCategory.SameCard([b, a], 1, 3)), ComboAlternative.For(new ComboCategory(b, 1, 3))];
        Combo original = new([], alternativeGroups: [new(leaves)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string before = JsonSerializer.Serialize(original);
        foreach (AngleSharp.Dom.IElement separator in editor.FindAll(".expanded-expression .expression-operator[value='OR']")) {
            Assert.That(separator.QuerySelectorAll("option").Select(option => option.TextContent), Does.Not.Contain("AND"));
            await separator.ChangeAsync(new() { Value = "AND" });
        }
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(JsonSerializer.Serialize(editor.Instance.Combo), Is.EqualTo(before));
        Assert.That(ComboOperatorEditing.CanApply(original, [new(ComboRequirementKind.Group, 0)], new("AND", leaves)), Is.False);
        Assert.That(() => ComboOperatorEditing.Apply(original, [new(ComboRequirementKind.Group, 0)], new("AND", leaves)), Throws.ArgumentException);
    }

    [Test]
    public async Task SeparateRowsCannotBecomeAndEvenAfterChangingPlusToOr() {
        Combo original = new([new(b, 0, 0), new(a, 0, 0)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "+", "OR" }));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Combo changed = editor.Instance.Combo;
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "+", "OR" }));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo, Is.SameAs(changed));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "+" });
        Assert.That(JsonSerializer.Serialize(editor.Instance.Combo), Is.EqualTo(JsonSerializer.Serialize(original)));
    }

    [Test]
    public async Task FlatOrCanRoundTripThroughAndWithOrderNumericRangeAndSavedAnyBounds() {
        CategoryBase numeric = new NumericPropertyRange(NumericPropertyKind.Level, 2, 5).ToCategory();
        Combo original = new([], alternativeGroups: [new(new[] { b, numeric, a }
            .Select(property => ComboAlternative.For(new ComboCategory(property, 1, 2, RequirementMaximumMode.HandSize))).ToArray())]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b, numeric], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string before = JsonSerializer.Serialize(original);
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo.Categories.Single().CategoryBases.Select(property => property.Identity), Is.EqualTo(new[] { b.Identity, numeric.Identity, a.Identity }));
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(1));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(JsonSerializer.Serialize(editor.Instance.Combo), Is.EqualTo(before));
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SimulationRejectsNestedSwitchesThatEraseSiblingConfiguration(bool outerAnd) {
        CardSelector nested = outerAnd ? CardSelector.AnyOf([b.Identity, a.Identity]) : CardSelector.AllOf([b.Identity, a.Identity]);
        CardSelector[] terms = [nested, CardSelector.For(a), CardSelector.For(b)];
        CardSelector original = outerAnd ? CardSelector.AllOfSelectors(terms) : CardSelector.AnyOfSelectors(terms);
        CardSelector current = original;
        IRenderedComponent<SelectorExpressionEditor>? editor = null;
        editor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, current).Add(component => component.Available, new[] { a, b })
            .Add(component => component.OnChanged, value => {
                current = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        string operation = outerAnd ? "AND" : "OR";
        IRenderedComponent<SelectorExpressionEditor> child = editor.FindComponents<SelectorExpressionEditor>()
            .Single(component => component.Instance.Selector.Equals(nested));
        Assert.That(child.FindAll(".expression-operator option").Select(option => option.TextContent), Does.Not.Contain(operation));
        await child.Find(".expression-operator").ChangeAsync(new() { Value = operation });
        bool applied = await editor.InvokeAsync(() => editor.Instance.EditGroupOperatorAsync(nested, operation));
        Assert.That(applied, Is.False);
        Assert.That(current, Is.SameAs(original));
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(2));
    }
}
