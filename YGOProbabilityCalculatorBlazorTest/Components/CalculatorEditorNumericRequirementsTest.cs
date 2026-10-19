using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorNumericRequirementsTest : CalculatorEditorTestBase {
    [TestCase(NumericPropertyKind.Level)]
    [TestCase(NumericPropertyKind.Rank)]
    [TestCase(NumericPropertyKind.Link)]
    public void BothEditorsUseTheSameSimpleFormAndSaveInclusiveNumericRanges(NumericPropertyKind property) {
        string target = "numeric:" + property.ToString().ToLowerInvariant();
        Combo combo = new([]);
        IRenderedComponent<ComboEditor>? comboEditor = null;
        comboEditor = context.RenderComponent<ComboEditor>(parameters => parameters.Add(component => component.Combo, combo)
            .Add(component => component.CategoryBases, CardPropertyProvider.CommonCategories)
            .Add(component => component.OnReplaceCombo, value => {
                combo = value.NewCombo;
                comboEditor!.SetParametersAndRender(next => next.Add(component => component.Combo, combo));
            }));
        comboEditor.Find("[data-new-requirement]").Click();
        Assert.That(comboEditor.FindComponent<RequirementEditForm>().Instance.QuantityContent, Is.Not.Null);
        AssertCollapsedNumericChoices(comboEditor);
        comboEditor.Find("#comboTarget0").Change(target);
        SetRange(comboEditor, property, "3", "5");
        comboEditor.Find("#minCount0").Input("2");
        Assert.That(combo.Categories, Is.Empty, "Changing form fields never commits the requirement.");
        Button(comboEditor, "Add").Click();
        ComboCategory saved = combo.Categories.Single();
        Assert.That(saved.MinCount, Is.EqualTo(2));
        Assert.That(saved.Selector.NumericRange, Is.EqualTo(new NumericPropertyRange(property, 3, 5)));

        CardSelector selector = CardSelector.For(a);
        IRenderedComponent<SelectorExpressionEditor>? simulation = null;
        simulation = context.RenderComponent<SelectorExpressionEditor>(parameters => parameters.Add(component => component.Selector, selector)
            .Add(component => component.Available, CardPropertyProvider.CommonCategories.Append(a).ToArray())
            .Add(component => component.OnChanged, value => {
                selector = value;
                simulation!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        simulation.Find(".requirement-edit").Click();
        Assert.That(simulation.FindComponent<RequirementEditForm>().Instance.QuantityContent, Is.Null);
        Assert.That(simulation.FindAll(".requirement-quantity"), Is.Empty);
        AssertCollapsedNumericChoices(simulation);
        simulation.Find(".requirement-property-picker > select").Change(target);
        SetRange(simulation, property, "3", "5");
        simulation.Find($"input[aria-label='Maximum {property}']").Input("2");
        Assert.That(simulation.Find(".requirement-submit button").HasAttribute("disabled"), Is.True);
        simulation.Find("form").Submit();
        Assert.That(selector, Is.EqualTo(CardSelector.For(a)));
        Button(simulation, "Cancel").Click();
        simulation.Find(".requirement-edit").Click();
        Assert.That(simulation.Find(".requirement-property-picker > select").GetAttribute("value"), Is.EqualTo(a.Identity));
        simulation.Find(".requirement-property-picker > select").Change(target);
        SetRange(simulation, property, "3", "5");
        simulation.Find("form").Submit();
        Assert.That(selector.NumericRange, Is.EqualTo(saved.Selector.NumericRange));
    }

    [Test]
    public void RemovingAnotherPropertyKeepsInvalidNumericDraftAndSaveProtection() {
        Combo combo = new([ComboCategory.SameCard([new NumericPropertyRange(NumericPropertyKind.Level, 2, 5).ToCategory(), a, b], 1, 3)]);
        IRenderedComponent<ComboEditor> editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, combo).Add(component => component.CategoryBases, new[] { a, b }));
        editor.Find(".requirement-edit").Click();
        editor.Find("input[aria-label='Maximum Level']").Input("1");
        RequirementPropertyPicker picker = editor.FindComponents<RequirementPropertyPicker>()[0].Instance;
        editor.Find("button[aria-label='Remove property 2']").Click();
        Assert.That(editor.FindComponents<RequirementPropertyPicker>()[0].Instance, Is.SameAs(picker));
        Assert.That(editor.Find("input[aria-label='Maximum Level']").GetAttribute("value"), Is.EqualTo("1"));
        Assert.That(editor.Find(".requirement-submit button").HasAttribute("disabled"), Is.True);
        editor.Find("form").Submit();
        Assert.That(editor.Instance.Combo, Is.SameAs(combo));
    }

    [Test]
    public void OpeningLegacyExactNumberAndSavingWithoutChangesPreservesStoredPredicate() {
        CategoryBase legacyLevel = new("Level 5", CategorySource.Metadata, "level:5");
        CardSelector original = CardSelector.For(legacyLevel);
        int changes = 0;
        IRenderedComponent<PostDrawSelectorEditor> editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters
            .Add(component => component.Selector, original).Add(component => component.CategoryBases, new[] { legacyLevel })
            .Add(component => component.OnChanged, _ => changes++));
        Assert.That(editor.Find(".draw-selector-chip").TextContent, Is.EqualTo("Level 5"));
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.Find(".requirement-property-picker > select").GetAttribute("value"), Is.EqualTo("numeric:level"));
        editor.Find("form").Submit();
        Assert.That(changes, Is.Zero);
        Assert.That(editor.Instance.Selector, Is.SameAs(original));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task NumericPropertiesCanExtendSameCardAndAlternativeRequirements(bool sameCard) {
        IRenderedComponent<ProbabilityCalculatorComponent> host = Render(new SessionState {
            Categories = [a], Combos = [new([new(a, 1, 2)])], HandSize = 3
        });
        IRenderedComponent<ComboEditor> editor = host.FindComponent<ComboEditor>();
        await ExtendRequirement(editor, "category:0", sameCard);
        RequirementField(editor, "#comboTarget0").Change("numeric:rank");
        editor.Find("select[aria-label='Comparison']").Change("minimum");
        editor.Find("input[aria-label='Minimum Rank']").Input("3");
        Button(editor, sameCard ? "Add property" : "Add alternative").Click();
        CardSelector selector = sameCard ? editor.Instance.Combo.Categories.Single().Selector
            : editor.Instance.Combo.AlternativeGroups.Single().Alternatives[1].Category!.Selector;
        NumericPropertyRange range = sameCard ? selector.Children.Single(child => child.NumericRange is not null).NumericRange! : selector.NumericRange!;
        Assert.That(range, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Rank, 3, null)));
    }

    [Test]
    public void AddingAnExistingNumericTargetUpdatesItsQuantityWithoutDuplicatingIt() {
        CategoryBase range = new NumericPropertyRange(NumericPropertyKind.Link, 2, 5).ToCategory();
        Combo combo = new([new(range, 1, 3)]);
        IRenderedComponent<ComboEditor>? editor = null;
        editor = context.RenderComponent<ComboEditor>(parameters => parameters.Add(component => component.Combo, combo)
            .Add(component => component.OnReplaceCombo, value => {
                combo = value.NewCombo;
                editor!.SetParametersAndRender(next => next.Add(component => component.Combo, combo));
            }));
        editor.Find(".add-top-level-group").Click();
        editor.Find("#comboTarget0").Change("numeric:link");
        SetRange(editor, NumericPropertyKind.Link, "2", "5");
        editor.Find("#minCount0").Input("2");
        Button(editor, "Update requirement").Click();
        Assert.That(combo.Categories, Has.Count.EqualTo(1));
        Assert.That(combo.Categories[0].MinCount, Is.EqualTo(2));
        Assert.That(combo.Categories[0].Selector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Link, 2, 5)));
    }

    private static void SetRange(IRenderedFragment editor, NumericPropertyKind property, string minimum, string maximum) {
        editor.Find("select[aria-label='Comparison']").Change("range");
        editor.Find($"input[aria-label='Minimum {property}']").Input(minimum);
        editor.Find($"input[aria-label='Maximum {property}']").Input(maximum);
    }

    private static void AssertCollapsedNumericChoices(IRenderedFragment editor) {
        foreach (string key in new[] { "level", "rank", "link" }) {
            Assert.That(editor.FindAll($".requirement-property-picker option[value='numeric:{key}']"), Has.Count.EqualTo(1));
            Assert.That(editor.FindAll($".requirement-property-picker option[value^='metadata:{key}:']"), Is.Empty);
        }
    }
}
