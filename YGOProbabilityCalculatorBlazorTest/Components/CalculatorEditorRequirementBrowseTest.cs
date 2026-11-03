using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorRequirementBrowseTest : CalculatorEditorTestBase {
    [Test]
    public void BrowsingStartsAtTopAndRestoresEachGroupsLastScrollPosition() {
        double scroll = 0;
        context.JSInterop.Setup<double>("workspaceMenus.getScroll", _ => true).SetResult(scroll);
        IRenderedComponent<RequirementPropertyPicker> picker = context.RenderComponent<RequirementPropertyPicker>(parameters => parameters
            .Add(component => component.Value, "card:chant")
            .Add(component => component.Cards, new[] { new Card([], name: "Chant", id: "chant") }));

        picker.Find(".requirement-picker-trigger").Click();
        Assert.That(context.JSInterop.Invocations["workspaceMenus.restoreScroll"].Last().Arguments[1], Is.EqualTo(0d));
        context.JSInterop.Setup<double>("workspaceMenus.getScroll", _ => true).SetResult(240d);
        Button(picker, "Properties").Click();
        Assert.That(context.JSInterop.Invocations["workspaceMenus.restoreScroll"].Last().Arguments[1], Is.EqualTo(0d));
        context.JSInterop.Setup<double>("workspaceMenus.getScroll", _ => true).SetResult(80d);
        Button(picker, "All").Click();
        Assert.That(context.JSInterop.Invocations["workspaceMenus.restoreScroll"].Last().Arguments[1], Is.EqualTo(240d));
        Button(picker, "Properties").Click();
        Assert.That(context.JSInterop.Invocations["workspaceMenus.restoreScroll"].Last().Arguments[1], Is.EqualTo(80d));
    }

    [Test]
    public void DropdownContainsGroupsAndFiltersChoicesWithoutChangingTheSelectedRequirement() {
        List<RequirementPropertyPicker.Change> changes = [];
        Card card = new([], name: "Radiant Typhoon Chant", id: "chant");
        CategoryBase starter = new("Starter");
        IRenderedComponent<RequirementPropertyPicker> picker = context.RenderComponent<RequirementPropertyPicker>(parameters => parameters
            .Add(component => component.Value, starter.Identity)
            .Add(component => component.Cards, new[] { card })
            .Add(component => component.Categories, CardPropertyProvider.CommonCategories.Append(starter).ToArray())
            .Add(component => component.OnChanged, changes.Add));

        Assert.That(picker.Find(".requirement-picker-trigger").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(picker.Find(".requirement-picker-panel").HasAttribute("hidden"), Is.True);
        picker.Find(".requirement-picker-trigger").Click();
        Assert.That(picker.Find(".requirement-picker-trigger").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(picker.Find(".requirement-picker-panel").HasAttribute("hidden"), Is.False);
        Assert.That(picker.FindAll(".requirement-groups").All(group => group.Closest(".requirement-picker-panel") is not null), Is.True);
        Button(picker, "Cards").Click();
        Assert.That(picker.FindAll("option").Select(option => option.GetAttribute("value")),
            Is.EquivalentTo(new[] { "", "card:chant" }));
        Assert.That(picker.FindAll("option[value='user:Starter']"), Is.Empty);
        Button(picker, "Properties").Click();
        Assert.That(picker.Find("option[value='numeric:level']"), Is.Not.Null);
        Assert.That(picker.FindAll("option[value='card:chant']"), Is.Empty);
        Assert.That(changes, Is.Empty);
        picker.Find(".requirement-picker-panel select").Change("numeric:level");
        Assert.That(changes.Single().Value, Does.StartWith("metadata:numeric:level:"));
    }

    [Test]
    public void OneAvailableGroupDoesNotNeedCategoryButtons() {
        IRenderedComponent<RequirementPropertyPicker> picker = context.RenderComponent<RequirementPropertyPicker>(parameters => parameters
            .Add(component => component.Categories, CardPropertyProvider.CommonCategories));
        picker.Find(".requirement-picker-trigger").Click();
        Assert.That(picker.FindAll(".requirement-groups button"), Is.Empty);
        Assert.That(picker.Find("option[value='numeric:level']"), Is.Not.Null);
        Assert.That(picker.Find("option[value='numeric:rank']"), Is.Not.Null);
        Assert.That(picker.Find("option[value='numeric:link']"), Is.Not.Null);
    }

    [Test]
    public void BrowsingPreservesInvalidNumericDraftAndValidation() {
        List<RequirementPropertyPicker.Change> changes = [];
        IRenderedComponent<RequirementPropertyPicker> picker = context.RenderComponent<RequirementPropertyPicker>(parameters => parameters
            .Add(component => component.Value, new NumericPropertyRange(NumericPropertyKind.Level, 1, 4, true).Identity)
            .Add(component => component.Categories, new[] { new CategoryBase("Starter") })
            .Add(component => component.OnChanged, changes.Add));

        picker.Find("input[id$='-maximum']").Input("0");
        Assert.That(changes.Single().Error, Is.Not.Null);
        picker.Find(".requirement-picker-trigger").Click();
        Button(picker, "Categories").Click();
        Assert.That(picker.Find("input[id$='-maximum']").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(picker.FindAll("option[value='numeric:level']"), Is.Empty);
        Assert.That(changes, Has.Count.EqualTo(1));
        picker.Find("input[id$='-maximum']").Input("6");
        Assert.That(NumericPropertyRange.TryFromIdentity(changes.Last().Value, out NumericPropertyRange? range), Is.True);
        Assert.That(range, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, 1, 6, true)));
    }

    [TestCase("Escape")]
    [TestCase("Enter")]
    public void KeyboardDismissalLeavesTheParentConditionOpenWithoutSaving(string key) {
        Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 67115133));
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        IRenderedComponent<PostDrawSelectorEditor> secondMode = editor.FindComponents<PostDrawSelectorEditor>().Last();
        secondMode.Find(".requirement-edit").Click();
        secondMode.Find(".requirement-picker-trigger").Click();
        secondMode.Find(".requirement-picker-panel select").KeyDown(key);
        Assert.That(secondMode.Find(".requirement-picker-trigger").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(secondMode.FindComponents<RequirementEditForm>(), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        Assert.That(saved, Is.Null);
        Assert.That(secondMode.Instance.Selector, Is.EqualTo(source.Search!.Modes![1].Selector));
    }
}
