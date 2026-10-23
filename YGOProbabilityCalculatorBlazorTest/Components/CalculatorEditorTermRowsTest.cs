using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorTermRowsTest : CalculatorEditorTestBase {
    private static readonly CategoryBase[] Properties = [new("A"), new("B"), new("C"), new("D"), new("E")];
    private static ComboCategory Requirement(int index) => new(Properties[index], 1, 3);
    private static ComboAlternative Leaf(int index) => ComboAlternative.For(Requirement(index));
    private static ComboCategory Same(params int[] indices) => ComboCategory.SameCard(indices.Select(index => Properties[index]), 1, 3);

    [Test]
    public async Task BothEditorsApplyOperatorsImmediatelyAndUseTheSameActions() {
        Combo combo = new([Same(0, 1)]);
        CardSelector selector = CardSelector.AllOf(Properties.Take(2).Select(property => property.Identity));
        IRenderedComponent<ComboEditor>? comboEditor = null;
        IRenderedComponent<SelectorExpressionEditor>? selectorEditor = null;
        comboEditor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, combo).Add(component => component.CategoryBases, Properties)
            .Add(component => component.OnReplaceCombo, change => {
                combo = change.NewCombo;
                comboEditor!.SetParametersAndRender(next => next.Add(component => component.Combo, combo));
            }));
        selectorEditor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, selector).Add(component => component.Available, Properties)
            .Add(component => component.OnChanged, change => {
                selector = change;
                selectorEditor!.SetParametersAndRender(next => next.Add(component => component.Selector, selector));
            }));
        await comboEditor.Find(".expanded-expression .expression-operator").ChangeAsync(new() { Value = "OR" });
        await selectorEditor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(combo.AlternativeGroups, Has.Count.EqualTo(1));
        Assert.That(selector.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(comboEditor.FindAll("form"), Is.Empty);
        Assert.That(selectorEditor.FindAll("form"), Is.Empty);
        comboEditor.Find(".requirement-edit").Click();
        selectorEditor.Find(".requirement-edit").Click();
        foreach (IRenderedComponent<RequirementActions> actions in new[] { comboEditor.FindComponent<RequirementActions>(), selectorEditor.FindComponent<RequirementActions>() }) {
            Assert.That(actions.FindAll("button").Select(button => button.TextContent.Trim()),
                Is.EqualTo(new[] { "Add alternative (OR)", "Add property (AND)", "Remove requirement" }));
        }
        Assert.That(selectorEditor.FindAll("details"), Is.Empty);
    }

    [Test]
    public async Task SimulationOperatorChangesPreserveRowsAndProtectPendingEdits() {
        CardSelector original = CardSelector.AllOfSelectors([
            CardSelector.AnyOf(Properties.Take(2).Select(property => property.Identity)),
            CardSelector.AnyOf(Properties.Skip(2).Take(2).Select(property => property.Identity))
        ]);
        CardSelector current = original;
        IRenderedComponent<SelectorExpressionEditor>? editor = null;
        editor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, current).Add(component => component.Available, Properties)
            .Add(component => component.OnChanged, value => {
                current = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, current));
            }));
        await editor.Find("[data-selector-path='root'] > .requirement-or-group > .requirement-and-separator select").ChangeAsync(new() { Value = "OR" });
        Assert.That(current.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll("form"), Is.Empty);
        editor.FindAll(".requirement-edit").First().Click();
        editor.Find("form .requirement-property-picker > select").Change(Properties[4].Identity);
        await editor.Find("[data-selector-path='root'] > .requirement-or-group > .alternative-separator select").ChangeAsync(new() { Value = "AND" });
        Assert.That(current.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(editor.Find("form .requirement-property-picker > select").GetAttribute("value"), Is.EqualTo(Properties[4].Identity));
        Assert.That(editor.FindAll("[role='alert']").Select(alert => alert.TextContent), Has.Some.Contains("Save or cancel"));
        Button(editor, "Cancel").Click();
        await editor.Find("[data-selector-path='root'] > .requirement-or-group > .alternative-separator select").ChangeAsync(new() { Value = "AND" });
        Assert.That(current.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        Assert.That(current, Is.EqualTo(original));
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll("form"), Is.Empty);
    }

    [Test]
    public async Task SwitchingPlusToOrPreservesRowsAndCanBeReversed() {
        Combo current = new([], alternativeGroups: [new([Leaf(0), Leaf(1)]), new([Leaf(2), Leaf(3)])]);
        IRenderedComponent<ComboEditor>? editor = null;
        editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, current).Add(component => component.CategoryBases, Properties)
            .Add(component => component.OnReplaceCombo, value => {
                current = value.NewCombo;
                editor!.SetParametersAndRender(next => next.Add(component => component.Combo, current));
            }));

        await editor.Find(".expanded-expression > .requirement-and-separator select").ChangeAsync(new() { Value = "OR" });
        Assert.That(current.AlternativeGroups, Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(2));
        Assert.That(RowContents(), Is.EqualTo(new[] { "AB", "CD" }));

        await editor.Find(".requirement-or-group > .alternative-separator select").ChangeAsync(new() { Value = "+" });
        Assert.That(current.AlternativeGroups, Has.Count.EqualTo(2));
        Assert.That(current.AlternativeGroups.Select(group => group.Alternatives.Count), Is.EqualTo(new[] { 2, 2 }));
        Assert.That(RowContents(), Is.EqualTo(new[] { "AB", "CD" }));
        await editor.Find(".expanded-expression > .requirement-and-separator select").ChangeAsync(new() { Value = "OR" });
        Assert.That(RowContents(), Is.EqualTo(new[] { "AB", "CD" }));

        string[] RowContents() => editor.FindAll(".expanded-expression .requirement-row").Select(row =>
            string.Concat(row.QuerySelectorAll(".requirement-description .category-tag").Select(chip => chip.TextContent.Trim()[0]))).ToArray();
    }

    [Test]
    public async Task OrRowSurvivesRemovingItsFirstMemberUntilItsLastMemberIsRemoved() {
        Combo current = new([Requirement(2)], alternativeGroups: [new([Leaf(0), Leaf(1)])]);
        current = current.WithRequirementAt(new(ComboRequirementKind.Group, 0), 0);
        IRenderedComponent<ComboEditor>? editor = null;
        editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, current).Add(component => component.CategoryBases, Properties)
            .Add(component => component.OnReplaceCombo, value => {
                current = value.NewCombo;
                editor!.SetParametersAndRender(next => next.Add(component => component.Combo, current));
            }));
        await editor.Find(".expanded-expression > .requirement-and-separator select").ChangeAsync(new() { Value = "OR" });
        ExpressionRow first = editor.FindComponents<ExpressionRow>().First().Instance;
        await editor.Find("[data-requirement='alternative:0:0']").ClickAsync(new());
        await Button(editor, "Remove requirement").ClickAsync(new());
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(2));
        Assert.That(editor.FindComponents<ExpressionRow>().First().Instance, Is.SameAs(first));
        Assert.That(editor.FindAll(".expanded-expression .requirement-row").First().TextContent, Does.Contain("B"));
        await editor.Find("[data-requirement='alternative:0:0']").ClickAsync(new());
        await Button(editor, "Remove requirement").ClickAsync(new());
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(1));
        Assert.That(current.AllCategories.Single().BaseCategory.Name, Is.EqualTo("C"));
    }

    [Test]
    public async Task AddingAnAlternativeToAPreservedRowKeepsItInThatRow() {
        IRenderedComponent<ProbabilityCalculatorComponent> calculator = Render(new SessionState {
            Categories = Properties.ToList(), Combos = [new([Requirement(0), Requirement(1), Requirement(2)])]
        });
        IRenderedComponent<ComboEditor> editor = calculator.FindComponent<ComboEditor>();
        await editor.FindAll(".expanded-expression > .requirement-and-separator select").First().ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(3));
        await EditRequirement(editor, "alternative:0:0");
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = Properties[3].Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.FindAll(".expanded-expression .requirement-row"), Has.Count.EqualTo(3));
        Assert.That(editor.FindAll(".expanded-expression .requirement-row").Select(row =>
            string.Concat(row.QuerySelectorAll(".requirement-description .category-tag").Select(chip => chip.TextContent.Trim()[0]))),
            Is.EqualTo(new[] { "AD", "B", "C" }));
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives.Select(leaf => leaf.Category!.BaseCategory.Name),
            Is.EqualTo(new[] { "A", "D", "B" }));
    }

    [TestCase("AND")]
    [TestCase("OR")]
    public void ComboAndSimulationTermsUseTheSamePresentationComponents(string operation) {
        Combo combo = operation == "AND" ? new([Same(0, 1)]) : new([], alternativeGroups: [new([Leaf(0), Leaf(1)])]);
        CardSelector selector = new(operation == "AND" ? CardSelectorKind.AllOf : CardSelectorKind.AnyOf, Properties.Take(2).Select(property => property.Identity));
        IRenderedComponent<ComboEditor> comboEditor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, combo).Add(component => component.CategoryBases, Properties));
        IRenderedComponent<SelectorExpressionEditor> selectorEditor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, selector).Add(component => component.Available, Properties));

        foreach (IRenderedComponent<ExpressionRow> row in new[] { comboEditor.FindComponents<ExpressionRow>().Single(), selectorEditor.FindComponent<ExpressionRow>() }) {
            IRenderedComponent<ExpressionTerm> term = row.FindComponents<ExpressionTerm>().Single(component => !component.Instance.Compact && component.Instance.Count > 1);
            Assert.That(term.Instance.Count, Is.EqualTo(2));
            Assert.That(term.Instance.Operator, Is.EqualTo(operation));
            Assert.That(term.FindComponents<ExpressionBadge>(), Has.Count.EqualTo(2));
            Assert.That(term.FindComponents<ExpressionSeparator>().Single().Instance.Inline, Is.True);
            Assert.That(term.Find("select.expression-operator").GetAttribute("value"), Is.EqualTo(operation));
            Assert.That(row.FindAll("button button, button select"), Is.Empty);
        }
    }

    [TestCase("plus", 3, "A;B;C")]
    [TestCase("and", 1, "ABC")]
    [TestCase("or", 1, "ABC")]
    [TestCase("and-plus-or", 2, "AB;CD")]
    [TestCase("leaf-plus-or-plus-leaf", 3, "A;BC;D")]
    [TestCase("or-plus-or", 2, "AB;CD")]
    [TestCase("nested-or", 2, "AB;CD")]
    [TestCase("mixed-or", 3, "A;BC;DE")]
    public void ComboFieldsFollowTermsWithoutChangingStoredRequirements(string shape, int count, string contents) {
        Combo original = shape switch {
            "plus" => new([Requirement(0), Requirement(1), Requirement(2)]),
            "and" => new([Same(0, 1, 2)]),
            "or" => new([], alternativeGroups: [new([Leaf(0), Leaf(1), Leaf(2)])]),
            "and-plus-or" => new([Same(0, 1)], alternativeGroups: [new([Leaf(2), Leaf(3)])]),
            "leaf-plus-or-plus-leaf" => new Combo([Requirement(0), Requirement(3)], alternativeGroups: [new([Leaf(1), Leaf(2)])])
                .WithRequirementAt(new(ComboRequirementKind.Group, 0), 1),
            "or-plus-or" => new([], alternativeGroups: [new([Leaf(0), Leaf(1)]), new([Leaf(2), Leaf(3)])]),
            "nested-or" => new([], alternativeGroups: [new([ComboAlternative.For(Same(0, 1)), ComboAlternative.For(Same(2, 3))])]),
            _ => new([], alternativeGroups: [new([Leaf(0), ComboAlternative.For(Same(1, 2)), Leaf(3), Leaf(4)])])
        };
        string json = JsonSerializer.Serialize(original);
        IRenderedComponent<ComboEditor> editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, original).Add(component => component.CategoryBases, Properties));
        foreach (bool expanded in new[] { false, true }) {
            editor.SetParametersAndRender(parameters => parameters.Add(component => component.ActiveComboIndex, expanded ? 0 : -1));
            IElement[] rows = editor.FindAll(".expanded-expression .requirement-row").ToArray();
            Assert.That(rows, Has.Length.EqualTo(count));
            Assert.That(rows.Select(row => string.Concat(row.QuerySelectorAll(".requirement-description .category-tag")
                .Select(chip => chip.TextContent.Trim()[0]))), Is.EqualTo(contents.Split(';')));
            for (int index = 0; index < count; index++) {
                IElement row = editor.FindAll(".expanded-expression .requirement-row").ToArray()[index];
                Assert.That(row.QuerySelectorAll("button button, button select"), Is.Empty);
                row.QuerySelector(".requirement-edit")!.Click();
                Assert.That(editor.FindAll(".requirement-row form"), Has.Count.EqualTo(1));
                Button(editor.FindComponent<ExpressionForm>(), "Cancel").Click();
                Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
            }
        }
        Assert.That(JsonSerializer.Serialize(editor.Instance.Combo), Is.EqualTo(json));
        Assert.That(editor.FindAll(".expanded-expression > .requirement-and-separator select").Select(select => select.GetAttribute("value")),
            Is.All.EqualTo("+"));
        if (shape == "nested-or") {
            Assert.That(editor.FindAll(".requirement-or-group > .alternative-separator select").Single().GetAttribute("value"), Is.EqualTo("OR"));
            Assert.That(editor.FindAll(".expanded-expression .expression-parenthesis, .expanded-expression .composite-alternative"), Is.Empty);
        }
    }

    [TestCase("and", 1, "ABC", "AND")]
    [TestCase("or", 1, "ABC", "OR")]
    [TestCase("and-of-or", 2, "AB;CD", "AND")]
    [TestCase("or-of-and", 2, "AB;CD", "OR")]
    [TestCase("leaf-or-and", 2, "A;BC", "OR")]
    [TestCase("run-and-or", 2, "AB;CD", "AND")]
    [TestCase("separated-runs", 3, "A;BC;DE", "AND")]
    [TestCase("deep", 4, "A;BC;D;E", "AND")]
    public void SelectorFieldsUseMaximalAdjacentRunsAndRetainNestedBoundaries(string shape, int count, string contents, string operation) {
        CardSelector A(int index) => CardSelector.For(Properties[index]);
        CardSelector And(params int[] indices) => CardSelector.AllOf(indices.Select(index => Properties[index].Identity));
        CardSelector Or(params int[] indices) => CardSelector.AnyOf(indices.Select(index => Properties[index].Identity));
        CardSelector selector = shape switch {
            "and" => And(0, 1, 2),
            "or" => Or(0, 1, 2),
            "and-of-or" => CardSelector.AllOfSelectors([Or(0, 1), Or(2, 3)]),
            "or-of-and" => CardSelector.AnyOfSelectors([And(0, 1), And(2, 3)]),
            "leaf-or-and" => CardSelector.AnyOfSelectors([A(0), And(1, 2)]),
            "run-and-or" => CardSelector.AllOfSelectors([A(0), A(1), Or(2, 3)]),
            "deep" => CardSelector.AllOfSelectors([A(0), CardSelector.AnyOfSelectors([And(1, 2), A(3)]), A(4)]),
            _ => CardSelector.AllOfSelectors([A(0), Or(1, 2), A(3), A(4)])
        };
        string json = JsonSerializer.Serialize(selector);
        int changes = 0;
        IRenderedComponent<SelectorExpressionEditor> editor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, selector).Add(component => component.Available, Properties)
            .Add(component => component.OnChanged, _ => changes++));
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(count));
        Assert.That(editor.FindAll(".requirement-row").Select(row => string.Concat(row.QuerySelectorAll(".draw-selector-chip").Select(chip => chip.TextContent))),
            Is.EqualTo(contents.Split(';')));
        if (count > 1) {
            IElement[] connectors = editor.FindAll("[data-selector-path='root'] > .requirement-or-group > .requirement-and-separator select, [data-selector-path='root'] > .requirement-or-group > .alternative-separator select").ToArray();
            Assert.That(connectors, Has.Length.EqualTo(shape is "deep" or "separated-runs" ? 2 : 1));
            Assert.That(connectors.Select(select => select.GetAttribute("value")), Is.All.EqualTo(operation));
            Assert.That(editor.FindAll(".selector-nested-term"), Is.Not.Empty);
        }
        foreach (IRenderedComponent<ExpressionRow> row in editor.FindComponents<ExpressionRow>().ToArray()) {
            row.Find(".requirement-edit").Click();
            Assert.That(editor.FindAll("form"), Has.Count.EqualTo(1));
            int operands = row.FindAll(".draw-selector-chip").Count;
            Assert.That(editor.FindAll("form .requirement-operand"), Has.Count.EqualTo(operands));
            row.Find(".requirement-edit").Click();
            Assert.That(editor.FindAll("form"), Is.Empty, "Clicking the same field closes its draft.");
            row.Find(".requirement-edit").Click();
            editor.Find("form").Submit();
        }
        Assert.That(changes, Is.Zero);
        Assert.That(JsonSerializer.Serialize(selector), Is.EqualTo(json));
    }

    [Test]
    public void ThreeOperandSelectorDraftSupportsCancelEscapeAndAtomicSave() {
        CardSelector current = CardSelector.AnyOf(Properties.Take(3).Select(property => property.Identity));
        CardSelector original = current;
        IRenderedComponent<SelectorExpressionEditor>? editor = null;
        editor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, current).Add(component => component.Available, Properties)
            .Add(component => component.OnChanged, value => {
                current = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        foreach (string action in new[] { "Cancel", "Escape", "Save" }) {
            editor.Find(".requirement-edit").Click();
            editor.FindAll(".requirement-operand .requirement-property-picker > select").ToArray()[2].Change(Properties[3].Identity);
            if (action == "Cancel") { Button(editor, "Cancel").Click(); }
            else if (action == "Escape") { editor.Find("form").KeyDown(new KeyboardEventArgs { Key = "Escape" }); }
            else { editor.Find("form").Submit(); }
            Assert.That(current, Is.EqualTo(action == "Save" ? CardSelector.AnyOf([Properties[0].Identity, Properties[1].Identity, Properties[3].Identity]) : original));
            Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(1));
        }
    }
    [Test]
    public async Task FlatOrPreservesEachMemberThroughLocalAdditionAndRemovalOfFourthMember() {
        ComboCategory first = new(Properties[0], 0, 0);
        ComboCard second = new("target", 2, 19, RequirementMaximumMode.HandSize);
        ComboCategory third = new(Properties[1], 1, 2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = Properties.ToList(), Cards = [new([], id: "target")],
            Combos = [new([], alternativeGroups: [new([ComboAlternative.For(first), ComboAlternative.For(second), ComboAlternative.For(third)])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "alternative:0:2");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "0" });
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = Properties[2].Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        ComboAlternativeGroup group = editor.Instance.Combo.AlternativeGroups.Single();
        Assert.That(group.Alternatives, Has.Count.EqualTo(4));
        Assert.That(group.Alternatives[0].Category, Is.SameAs(first));
        Assert.That(group.Alternatives[1].Card, Is.SameAs(second));
        Assert.That((group.Alternatives[2].Category!.MinCount, group.Alternatives[2].Category!.MaxCount), Is.EqualTo((0, 0)));
        await EditRequirement(editor, "alternative:0:1");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = Properties[3].Identity });
        await Button(editor, "Save").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[1].Category!.MaxCount, Is.EqualTo(19));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[1].Category!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        await EditRequirement(editor, "alternative:0:3");
        await Button(editor, "Remove requirement").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(3));
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(1));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState session = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Combo restored = session.Combos[0];
        Assert.That(JsonSerializer.Serialize(restored), Is.EqualTo(JsonSerializer.Serialize(editor.Instance.Combo)));
    }

    [Test]
    public void SelectorRunKeepsDraftAndNewAdjacentPeerInAuthoredOrder() {
        CardSelector nested = CardSelector.AnyOf(Properties.Skip(2).Take(2).Select(property => property.Identity));
        CardSelector current = CardSelector.AllOfSelectors([CardSelector.For(Properties[0]), CardSelector.For(Properties[1]), nested]);
        IRenderedComponent<SelectorExpressionEditor>? editor = null;
        editor = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters
            .Add(component => component.Selector, current).Add(component => component.Available, Properties)
            .Add(component => component.OnChanged, value => {
                current = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        SelectorExpressionEditor first = editor.FindComponents<SelectorExpressionEditor>().Single(child => !child.Instance.IsRoot && child.Instance.Selector.Kind == CardSelectorKind.AllOf).Instance;
        editor.Find(".requirement-edit").Click();
        editor.FindAll("form .requirement-operand .requirement-property-picker > select").ToArray()[1].Change(Properties[4].Identity);
        current = CardSelector.AllOfSelectors([CardSelector.For(Properties[0]), CardSelector.For(Properties[1]), CardSelector.LevelAtLeast(5), nested]);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Selector, current));
        Assert.That(editor.FindComponents<SelectorExpressionEditor>().Any(child => ReferenceEquals(child.Instance, first)), Is.True);
        Assert.That(editor.FindAll("form .requirement-operand .requirement-property-picker > select").ToArray()[1].GetAttribute("value"), Is.EqualTo(Properties[4].Identity));
        editor.Find("form").Submit();
        Assert.That(current.AuthoredTerms.Take(3), Is.EqualTo(new[] { CardSelector.For(Properties[0]), CardSelector.For(Properties[4]), CardSelector.LevelAtLeast(5) }));
        Assert.That(current.AuthoredTerms.Last(), Is.SameAs(nested));
    }

    [Test]
    public async Task EmptyComboUsesOneClosedDraftRowAndOnlyConcreteRowsOpenForms() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Categories = Properties.ToList(), Combos = [new([])] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll("[data-new-requirement]"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-form, .add-top-level-group"), Is.Empty);
        Assert.That(editor.Instance.Combo.RequirementCount, Is.Zero);
        await editor.Find("[data-new-requirement]").ClickAsync(new());
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = Properties[0].Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Combo.RequirementCount, Is.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-form, [data-new-requirement]"), Is.Empty);
        await Button(editor, "+ Add requirement").ClickAsync(new());
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll(".requirement-row form"), Has.Count.EqualTo(1));
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.FindAll(".requirement-row"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-form"), Is.Empty);
        await DeleteRequirement(editor, "category:0");
        Assert.That(editor.Instance.Combo.RequirementCount, Is.Zero);
        Assert.That(editor.FindAll("[data-new-requirement]"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-form"), Is.Empty);
        await editor.Find("[data-new-requirement]").ClickAsync(new());
        await editor.Find("#comboTarget0").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll("[data-new-requirement]"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-form"), Is.Empty);
    }

}
