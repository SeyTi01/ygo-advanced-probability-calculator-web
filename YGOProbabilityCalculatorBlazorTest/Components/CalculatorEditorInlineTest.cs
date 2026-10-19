using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorInlineTest : CalculatorEditorTestBase {
    [TestCase(0, 0, RequirementMaximumMode.Fixed, 0, 0, RequirementMaximumMode.Fixed, true)]
    [TestCase(1, 2, RequirementMaximumMode.Fixed, 1, 2, RequirementMaximumMode.Fixed, true)]
    [TestCase(1, 2, RequirementMaximumMode.HandSize, 1, 2, RequirementMaximumMode.HandSize, true)]
    [TestCase(1, 2, RequirementMaximumMode.Fixed, 1, 3, RequirementMaximumMode.Fixed, false)]
    [TestCase(1, 2, RequirementMaximumMode.Fixed, 1, 2, RequirementMaximumMode.HandSize, false)]
    [TestCase(1, 2, RequirementMaximumMode.HandSize, 1, 3, RequirementMaximumMode.HandSize, false)]
    [TestCase(0, 2, RequirementMaximumMode.Fixed, 1, 2, RequirementMaximumMode.Fixed, false)]
    public async Task ConnectorMenusOfferAndOnlyWhenEverySavedBoundIsCompatible(
        int firstMin,
        int firstMax,
        RequirementMaximumMode firstMode,
        int secondMin,
        int secondMax,
        RequirementMaximumMode secondMode,
        bool supportsAnd
    ) {
        Combo original = new([], alternativeGroups: [new([
            ComboAlternative.For(new ComboCategory(a, firstMin, firstMax, firstMode)),
            ComboAlternative.For(new ComboCategory(b, secondMin, secondMax, secondMode))])]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string[] options = editor.FindAll(".expression-operator option").Select(option => option.TextContent).ToArray();
        Assert.That(options, Is.EqualTo(supportsAnd ? new[] { "AND", "OR" } : new[] { "OR" }));
        Assert.That(editor.Instance.Combo, Is.SameAs(original), "Availability checks never rewrite the expression.");
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        if (supportsAnd) { await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" }); }
        options = editor.FindAll(".expression-operator option").Select(option => option.TextContent).ToArray();
        Assert.That(options, Is.EqualTo(supportsAnd ? new[] { "AND", "OR" } : new[] { "OR" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
        Assert.That(editor.FindAll(".inline-member-picker"), Is.Empty);
    }

    [Test]
    public async Task SameCardSummaryTogglesWithoutSavingAndItsOperatorProtectsTheDraft() {
        Combo original = new([ComboCategory.SameCard([a, b], 1, 3)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".requirement-interactive-summary").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find(".requirement-interactive-summary .expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.FindAll(".requirement-interactive-summary .expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "AND" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        await editor.Find(".requirement-interactive-summary").ClickAsync(new());
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.FindAll(".requirement-interactive-summary .expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "AND", "OR" }));
    }

    [Test]
    public async Task InlineAndIsHiddenForUnequalSavedAnyMaximaWithoutLosingTheirRawValues() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Combos = [new([
                new(a, 1, 2, RequirementMaximumMode.HandSize), new(b, 1, 3, RequirementMaximumMode.HandSize)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Combo original = editor.Instance.Combo;
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "+", "OR" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives.Select(leaf => leaf.Category!.MaxCount), Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public async Task AlternativeAdditionUsesTheLocalFormAndPreservesOtherMembers() {
        ComboCategory exclusion = new(a, 0, 0);
        ComboCard direct = new("r", 1, 2);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([], id: "r")],
            Combos = [new([], alternativeGroups: [new([ComboAlternative.For(exclusion), ComboAlternative.For(direct)])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "alternative:0:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "" });
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives, Has.Count.EqualTo(2));
        Assert.That(editor.Find("[role=alert]").TextContent, Does.Contain("valid"));
        await Button(editor, "Cancel").ClickAsync(new());
        await ExtendRequirement(editor, "alternative:0:1");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        IReadOnlyList<ComboAlternative> saved = editor.Instance.Combo.AlternativeGroups.Single().Alternatives;
        Assert.That(saved, Has.Count.EqualTo(3));
        Assert.That(saved[0].Category, Is.SameAs(exclusion));
        Assert.That(saved[1].Card, Is.SameAs(direct));
        Assert.That((saved[2].Category!.MinCount, saved[2].Category!.MaximumMode), Is.EqualTo((1, RequirementMaximumMode.HandSize)));
        Assert.That(editor.FindAll(".requirement-row form, .inline-member-picker"), Is.Empty);
    }

    [Test]
    public async Task SameCardPropertiesUseTheLocalFormAndCancelOnlyThePendingAddition() {
        CategoryBase c = new("C");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b, c], Combos = [new([new(a, 0, 0)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await ExtendRequirement(editor, "category:0", sameCard: true);
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add property").ClickAsync(new());
        ComboCategory committed = editor.Instance.Combo.Categories.Single();
        Assert.That(committed.CategoryBases.Select(category => category.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That((committed.MinCount, committed.MaxCount), Is.EqualTo((0, 0)));
        await ExtendRequirement(editor, "category:0", sameCard: true);
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        await RequirementField(editor, "#minCount0").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.Instance.Combo.Categories.Single(), Is.SameAs(committed));
        Assert.That(editor.FindAll(".requirement-row form, .inline-member-picker"), Is.Empty);
    }

    [Test]
    public async Task BottomPlusReturnsToTheSeparateCopyDraftAndDoesNotCommitAnOpenRow() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [a, b], Combos = [new([new(a, 1, 3)])] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await EditRequirement(editor, "category:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        OpenNewRequirement(editor);
        Assert.That(editor.FindAll(".requirement-row form"), Has.Count.EqualTo(1));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        await RequirementField(editor, "#minCount0").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.That(editor.Instance.Combo.Categories.Select(category => category.MinCount), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(editor.Find(".requirement-list > .requirement-and-separator select").GetAttribute("value"), Is.EqualTo("+"));
    }

    [Test]
    public async Task NestedSameCardCannotFlattenIntoItsContainingOrGroup() {
        ComboCategory both = ComboCategory.SameCard([b, a], 0, 0);
        ComboCard direct = new("r", 1, 2, RequirementMaximumMode.HandSize);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([], id: "r")],
            Combos = [new([], alternativeGroups: [new([ComboAlternative.For(direct), ComboAlternative.For(both)])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Combo original = editor.Instance.Combo;
        Assert.That(editor.FindAll(".requirement-interactive-summary .expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "AND" }));
        await editor.Find(".requirement-interactive-summary .expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        IReadOnlyList<ComboAlternative> leaves = editor.Instance.Combo.AlternativeGroups.Single().Alternatives;
        Assert.That(leaves[0].Card, Is.SameAs(direct));
        Assert.That(leaves, Has.Count.EqualTo(2));
        Assert.That(leaves[1].Category, Is.SameAs(both));
        Assert.That(both.CategoryBases.Select(property => property.Name), Is.EqualTo(new[] { "B", "A" }));
        Assert.That((both.MinCount, both.MaxCount), Is.EqualTo((0, 0)));
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
        Assert.That(editor.FindAll(".requirement-or-group > .alternative-separator .expression-operator option").Select(option => option.TextContent), Does.Contain("+"));
    }

    [Test]
    public async Task PendingChangesAndNestedOrCannotBeSilentlyConverted() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Combos = [new([new(a, 1, 2), new(b, 1, 2)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Combo original = editor.Instance.Combo;
        await EditRequirement(editor, "category:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.FindAll(".expression-operator option").Select(option => option.TextContent), Is.EqualTo(new[] { "+" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        await Button(editor, "Cancel").ClickAsync(new());
        await editor.Find(".expression-operator").ChangeAsync(new() { Value = "OR" });
        await Button(editor, "+ Add requirement").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Combo nested = editor.Instance.Combo;
        await editor.Find(".requirement-list > .requirement-and-separator select").ChangeAsync(new() { Value = "AND" });
        Assert.That(editor.Instance.Combo, Is.SameAs(nested));
        Assert.That(editor.Find(".requirement-list > .requirement-and-separator").QuerySelectorAll("option").Select(option => option.TextContent), Is.EqualTo(new[] { "+", "OR" }));
        Assert.That(editor.FindAll("[role=alert]"), Is.Empty);
    }

    [Test]
    public async Task InlineChangesRoundTripAuthoredOrderBoundsAndPhysicalHandMeaning() {
        Card direct = new([a, b], 2, id: "r");
        List<Card> deck = [direct, new([b], 1, id: "b"), new([], 1, id: "blank")];
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = deck, HandSize = 2,
            Combos = [new([new(a, 0, 0), new(b, 1, 1)], cards: [new("r", 1, 2)],
                requirementOrder: [new(ComboRequirementKind.Card, 0), new(ComboRequirementKind.Category, 0), new(ComboRequirementKind.Category, 1)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.FindAll(".requirement-list > .requirement-and-separator select").Last().ChangeAsync(new() { Value = "OR" });
        await ExtendRequirement(editor, "alternative:0:1");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = "card:r" });
        await Button(editor, "Add alternative").ClickAsync(new());
        Combo committed = editor.Instance.Combo;
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        SessionState roundTrip = await codec.LoadSessionAsync(codec.SerializeSession(new() { Categories = [a, b], Cards = deck, Combos = [committed], HandSize = 2 }));
        Assert.That(roundTrip.Combos[0].RequirementOrder, Is.EqualTo(committed.RequirementOrder));
        IReadOnlyList<ComboAlternative> leaves = roundTrip.Combos[0].AlternativeGroups.Single().Alternatives;
        Assert.That((leaves[0].Category!.MinCount, leaves[0].Category!.MaxCount), Is.EqualTo((0, 0)));
        Assert.That(leaves[1].Category!.MaxCount, Is.EqualTo(1));
        Assert.That(leaves[2].Card!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        double oracle = SmallDeckOracle.EnumerateProbability(deck, [committed], 2);
        double actual = context.Services.GetRequiredService<IProbabilityCalculatorService>().CalculateProbabilityForCombos(roundTrip.Cards, roundTrip.Combos, 2);
        Assert.That(actual, Is.EqualTo(oracle).Within(1e-12));
    }
}
