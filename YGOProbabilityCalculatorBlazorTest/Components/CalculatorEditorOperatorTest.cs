using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorOperatorTest : CalculatorEditorTestBase {
    [Test]
    public async Task ConnectorsCommitImmediatelyAndKeepSeparateCopyAndSameCardMeaningDistinct() {
        List<Card> deck = [new([a, b], 1, "Both"), new([], 1, "Neither")];
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = deck, Combos = [new([new(a, 1, 1), new(b, 1, 1)])], HandSize = 1
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, [editor.Instance.Combo], 1), Is.Zero);
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo.Categories.Single().CategoryBases.Select(category => category.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, [editor.Instance.Combo], 1), Is.EqualTo(.5));
        await EditRequirement(editor, "category:0");
        Assert.That(editor.FindAll(".property-edit-list option").Select(option => option.TextContent), Does.Not.Contain("+"));
        await editor.Find(".requirement-interactive-summary .expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo.Categories, Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives, Has.Count.EqualTo(2));
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        Assert.That(editor.Markup, Does.Not.Contain("Edit group").And.Not.Contain("Save group"));
    }

    [Test]
    public async Task UnavailableAndAppearsOnlyAfterIndividualBoundsAreRepaired() {
        ComboAlternativeGroup group = new([ComboAlternative.For(new ComboCategory(a, 0, 0)), ComboAlternative.For(new ComboCategory(b, 1, 2))]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [new([], alternativeGroups: [group])] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Combo original = editor.Instance.Combo;
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "OR" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        await EditRequirement(editor, "alternative:0:1");
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Save").ClickAsync(new());
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo.Categories.Single().MinCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Categories.Single().MaxCount, Is.Zero);
    }

    [TestCase("category:0")]
    [TestCase("alternative:0:0")]
    public async Task RepeatClickSiblingSwitchCancelAndEscapeNeverCommitHiddenEdits(string key) {
        Combo original = new([new(a, 1, 2), new(b, 1, 2)], alternativeGroups: [new([
            ComboAlternative.For(ComboCategory.SameCard([a, b], 1, 2)), ComboAlternative.For(new ComboCategory(b, 0, 0))])]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await EditRequirement(editor, key);
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await EditRequirement(editor, key);
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        await EditRequirement(editor, key);
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await EditRequirement(editor, "category:1");
        Assert.That(editor.FindAll(".requirement-row form"), Has.Count.EqualTo(1));
        await Button(editor, "Cancel").ClickAsync(new());
        await EditRequirement(editor, key);
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        await editor.Find("#minCount0").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.FindAll(".requirement-edit[aria-expanded=true]"), Is.Empty);
    }

    [Test]
    public async Task ConversionRetainsOrdinaryAndUnrelatedInvalidRowDrafts() {
        CategoryBase c = new("C");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b, c], Combos = [new([new(a, 1, 2), new(b, 1, 2), new(c, 1, 3)])] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await EditRequirement(editor, "category:2");
        await editor.Find("#minCount0").InputAsync(new() { Value = "" });
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.Empty);
        Assert.That(editor.Find(".requirement-edit[aria-expanded=true]").GetAttribute("data-requirement"), Is.EqualTo("category:0"));
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
    }
    [Test]
    public async Task SelectorAndPropertyRuleConnectorsOfferOnlyAndOrAndRespectCancel() {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase fire = new("FIRE", CategorySource.Metadata, "attribute:fire");
        CategoryBase water = new("WATER", CategorySource.Metadata, "attribute:water");
        CardSelector selector = CardSelector.AllOfSelectors([CardSelector.For(monster), CardSelector.AnyOf([fire.Identity, water.Identity])]);
        CardSelector current = selector;
        IRenderedComponent<PostDrawSelectorEditor>? editor = null;
        editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters.Add(component => component.Selector, selector)
            .Add(component => component.CategoryBases, new[] { monster, fire, water })
            .Add(component => component.OnChanged, value => { current = value; editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value)); }));
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.All.AnyOf("AND", "OR"));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(current, Is.SameAs(selector));
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        await Button(editor, "Save").ClickAsync(new());
        Assert.That(current.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(current.CategoryIdentities, Is.EqualTo(selector.CategoryIdentities));

        CategoryBase definition = new("Rule", rule: CardSelector.AllOf([monster.Identity, fire.Identity]));
        CardSelector? saved = definition.Rule;
        IRenderedComponent<CategoryRuleEditor> rule = context.RenderComponent<CategoryRuleEditor>(parameters => parameters
            .Add(component => component.Category, definition).Add(component => component.CategoryBases, new[] { monster, fire })
            .Add(component => component.OnApply, value => saved = value));
        Assert.That(rule.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "AND", "OR" }));
        await rule.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(saved, Is.SameAs(definition.Rule));
        await Button(rule, "Apply rule").ClickAsync(new());
        Assert.That(saved!.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
    }

    [TestCase("+")]
    [TestCase("AND")]
    [TestCase("OR")]
    public void ConvertedModelsAgreeWithIndependentPhysicalHandsIncludingExclusions(string operation) {
        List<Card> deck = [new([a], 2, "A"), new([b], 1, "B"), new([a, b], 1, "Both"), new([], 1, "Neither")];
        foreach ((int minimum, int maximum) in new[] { (0, 0), (0, 1), (1, 1), (1, 2) }) {
            foreach (RequirementMaximumMode mode in Enum.GetValues<RequirementMaximumMode>()) {
                ComboAlternative[] operands = [ComboAlternative.For(new ComboCategory(a, minimum, maximum, mode)), ComboAlternative.For(new ComboCategory(b, minimum, maximum, mode))];
                Combo original = new([], alternativeGroups: [new(operands)]);
                Combo converted = ComboOperatorEditing.Apply(original, [new(ComboRequirementKind.Group, 0)], new(operation, operands));
                Combo expected = operation switch {
                    "+" => new([new(a, minimum, maximum, mode), new(b, minimum, maximum, mode)]),
                    "AND" => new([ComboCategory.SameCard([a, b], minimum, maximum, mode)]),
                    _ => new([], alternativeGroups: [new(operands)])
                };
                for (int handSize = 1; handSize <= 3; handSize++) {
                    double oracle = SmallDeckOracle.EnumerateProbability(deck, [expected], handSize);
                    double actual = context.Services.GetRequiredService<IProbabilityCalculatorService>().CalculateProbabilityForCombos(deck, [converted], handSize);
                    Assert.That(actual, Is.EqualTo(oracle).Within(1e-12), $"{operation}, {minimum}/{maximum}, {mode}, H={handSize}");
                }
            }
        }
    }

    [Test]
    public async Task RuleDraftMutationClearsAnObsoleteOperatorLimitError() {
        CategoryBase[] properties = Enumerable.Range(0, 17).Select(index => new CategoryBase($"P{index}", CategorySource.Metadata, $"test:{index}")).ToArray();
        CardSelector original = CardSelector.AnyOfSelectors([CardSelector.AllOf(properties.Take(16).Select(property => property.Identity)), CardSelector.For(properties[16])]);
        IRenderedComponent<CategoryRuleEditor> editor = context.RenderComponent<CategoryRuleEditor>(parameters => parameters
            .Add(component => component.Category, new CategoryBase("Rule", rule: original))
            .Add(component => component.CategoryBases, properties));
        await editor.Find(".expression-operator[value='OR']").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Find("[role='alert']").TextContent, Does.Contain("16 properties"));
        await editor.Find("[aria-label='Remove property P0 from alternative 1']").ClickAsync(new());
        Assert.That(editor.FindAll("[role='alert']"), Is.Empty);
    }

    [Test]
    public async Task ExistingOrGroupCanCombineWithItsPeerWithoutLosingBoundedLeaves() {
        ComboAlternative[] operands = [ComboAlternative.For(new ComboCategory(a, 0, 0)), ComboAlternative.For(new ComboCategory(b, 1, 3, RequirementMaximumMode.HandSize))];
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([], name: "Direct", id: "direct")],
            Combos = [new([], cards: [new("direct", 0, 2)], alternativeGroups: [new(operands)],
                requirementOrder: [new(ComboRequirementKind.Group, 0), new(ComboRequirementKind.Card, 0)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".requirement-list > .requirement-and-separator select").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        IReadOnlyList<ComboAlternative> saved = editor.Instance.Combo.AlternativeGroups.Single().Alternatives;
        Assert.That(saved[0].Category!.MinCount, Is.Zero);
        Assert.That(saved[0].Category!.MaxCount, Is.Zero);
        Assert.That(saved[1].Category!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(saved[1].Category!.MaxCount, Is.EqualTo(3));
        Assert.That(saved[2].Card!.CardId, Is.EqualTo("direct"));
        Assert.That(saved[2].Card!.MaxCount, Is.EqualTo(2));
    }

    [Test]
    public void UnsupportedDirectCardAndAndConflictingAnyBoundsAreExplicitlyBlocked() {
        Combo combo = new([], cards: [new("direct", 0, 0)]);
        Assert.That(() => ComboOperatorEditing.Apply(combo, [new(ComboRequirementKind.Card, 0)],
            new("AND", [ComboAlternative.For(combo.Cards[0]), ComboAlternative.For(new ComboCategory(a, 0, 0))])), Throws.ArgumentException.With.Message.Contains("Direct-card"));
        ComboAlternative[] operands = [ComboAlternative.For(new ComboCategory(a, 1, 2, RequirementMaximumMode.HandSize)), ComboAlternative.For(new ComboCategory(b, 1, 3, RequirementMaximumMode.HandSize))];
        Assert.That(() => ComboOperatorEditing.Apply(combo, [new(ComboRequirementKind.Card, 0)], new("AND", operands)),
            Throws.ArgumentException.With.Message.Contains("saved Any maxima differ"));
        Assert.That(combo.Cards.Single().MaxCount, Is.Zero);
    }

    [Test]
    public async Task FlatEligibilityConnectorOpensSharedDraftWithoutOfferingSeparateCopies() {
        CardSelector original = CardSelector.AllOf([b.Identity, a.Identity]);
        CardSelector saved = original;
        IRenderedComponent<PostDrawSelectorEditor>? editor = null;
        editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters
            .Add(component => component.Selector, original).Add(component => component.CategoryBases, new[] { a, b })
            .Add(component => component.OnChanged, value => { saved = value; editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value)); }));
        await editor.Find("[aria-label='Grouped eligibility filter'] .expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.FindAll("form .selector-operand"), Has.Count.EqualTo(2));
        Assert.That(editor.Find("[aria-label='Row operator']").TextContent, Is.EqualTo("ANDOR"));
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(saved, Is.SameAs(original));
    }
}
