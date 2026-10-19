using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestAlternatives : CalculatorEditorTestBase {
    private static Task ChooseAlternativeTarget(IRenderedComponent<ComboEditor> editor, string target) => ExtendRequirement(editor, target);

    private static Task RemoveOrLeaf(IRenderedComponent<ComboEditor> editor, int group, int leaf) =>
        DeleteRequirement(editor, $"alternative:{group - 1}:{leaf - 1}");

    [Test]
    public async Task CancellingAlternativeAuthoringRestoresEveryOrdinaryDraftValueWithoutPublishing() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [new([], name: "R", id: "r")],
            Combos = [new([new(a, 1, 5)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = CardTarget("r") });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "3" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "4" });
        Combo before = editor.Instance.Combo;
        Assert.That(editor.Instance.Combo, Is.SameAs(before));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(CardTarget("r")));
        await ChooseAlternativeTarget(editor, "category:0");
        Assert.That(editor.Find(".alternative-scope").TextContent, Does.Contain("requirement 1: Category: A"));
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before), "Invalid incomplete additions publish nothing.");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "8" });
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(CardTarget("r")));
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(Button(editor, "Add"), Is.Not.Null);
    }

    [Test]
    public async Task AlternativeCategoryChangesPreserveScopedBoundsAndRestoreOrdinaryDraftOnCancelAndCommit() {
        CategoryBase c = new("C");
        Combo original = new([new(a, 1, 5)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, c],
            Combos = [original]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "4" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "5" });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });

        await ChooseAlternativeTarget(editor, "category:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "3" });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("3"));
        await Button(editor, "Cancel").ClickAsync(new());

        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("4"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("5"));

        await ChooseAlternativeTarget(editor, "category:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "3" });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = c.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());

        ComboAlternativeGroup group = editor.Instance.Combo.AlternativeGroups.Single();
        ComboCategory added = group.Alternatives[1].Category!;
        Assert.That(added.BaseCategory, Is.SameAs(c));
        Assert.That(added.MinCount, Is.EqualTo(2));
        Assert.That(added.MaxCount, Is.EqualTo(3));
        Assert.That(group.Alternatives[0].Category, Is.SameAs(original.Categories[0]));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("4"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("5"));
        Assert.That(Button(editor, "Add").TextContent.Trim(), Is.EqualTo("Add"));
    }

    [TestCase("Category", "Category")]
    [TestCase("Card", "Card")]
    [TestCase("Category", "Card")]
    public async Task ExplicitTargetsCreateTypedAlternativesAndReturnToOrdinaryAddition(string seedKind, string addedKind) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [new([a], 2, "Razen", id: "r"), new([b], name: "Razen", id: "b")],
            Combos = [new([new(a, 1, 5), new(b, 1, 5)], cards: [new("r", 1, 5)])],
            HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, seedKind == "Card" ? "card:0" : "category:0");
        Assert.That(editor.FindAll("select[id^='alternativeTarget']"), Is.Empty);
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = addedKind == "Card" ? CardTarget("b") : b.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Button(editor, "Add alternative").ClickAsync(new());
        ComboAlternativeGroup group = editor.Instance.Combo.AlternativeGroups.Single();
        Assert.That(group.Alternatives.Select(x => x.Kind), Is.EqualTo(new[] { seedKind, addedKind }));
        Assert.That(group.Alternatives[1].Category?.MinCount ?? group.Alternatives[1].Card!.MinCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories.Single(c => c.BaseCategory.Identity == b.Identity).MinCount, Is.EqualTo(1));
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(editor.FindAll("select[id^='alternativeTarget']"), Is.Empty);
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Update requirement").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Single(c => c.BaseCategory.Identity == b.Identity).MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(group));
    }

    [Test]
    public async Task RemovingLeavesPromotesTheFinalCategoryAndCardWithoutChangingExistingRequirements() {
        ComboCategory ordinary = new(a, 2, 4);
        ComboCategory survivor = new(a, 1, 0, RequirementMaximumMode.HandSize);
        ComboCard cardSurvivor = new("r", 1, 0, RequirementMaximumMode.HandSize);
        ComboAlternativeGroup first = new([ComboAlternative.For(survivor), ComboAlternative.For(new ComboCategory(b, 0, 0))]);
        ComboAlternativeGroup second = new([ComboAlternative.For(cardSurvivor)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [new([], 3, "R", id: "r")],
            Combos = [new([ordinary], cards: [], alternativeGroups: [first, second])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        string header = editor.Find(".combo-header-content").OuterHtml;
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "group:1");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Add alternative").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "group:0");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Select(g => g.Alternatives.Count), Is.EqualTo(new[] { 3, 2 }));
        Assert.That(editor.FindAll(".accordion-button button, [aria-label^='Move alternative group']"), Is.Empty);
        await RemoveOrLeaf(editor, 1, 3);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Select(g => g.Alternatives.Count), Is.EqualTo(new[] { 2, 2 }), "Three leaves become a two-leaf OR group.");
        await RemoveOrLeaf(editor, 2, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1), "A one-leaf group promotes its card into the ordinary card list.");
        Assert.That(editor.Instance.Combo.Cards.Single(), Is.SameAs(cardSurvivor));
        Assert.That(editor.Find(".combo-header-content").OuterHtml, Is.Not.EqualTo(header));
        Assert.That(editor.FindAll(".combo-header-requirements .alternative-group"), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".combo-header-content").TextContent, Does.Contain("Card: R"));

        await RemoveOrLeaf(editor, 1, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty, "A two-leaf group promotes its last category into the ordinary category list.");
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(ordinary));
        Assert.That(editor.Instance.Combo.Categories[1], Is.SameAs(survivor));
        Assert.That(editor.Instance.Combo.Categories[1].MinCount, Is.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[1].MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Categories[1].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));


        IEnumerable<string?> targetValues = editor.FindAll(".requirement-edit").Select(target => target.GetAttribute("data-requirement"));
        Assert.That(targetValues, Does.Contain("category:1").And.Contain("card:0"));
        await ChooseAlternativeTarget(editor, "card:0");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Card, Is.SameAs(cardSurvivor));
        ComboAlternativeGroup restoredCardGroup = editor.Instance.Combo.AlternativeGroups.Single();

        await DeleteRequirement(editor, "category:1");
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(ordinary), "Deleting the promoted occurrence leaves the pre-existing same-target requirement untouched.");
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(restoredCardGroup));
    }

    [Test]
    public async Task CategoryRenameAndOuterReorderPreserveThePreciseSeedAndDraft() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([new(a, 1, 3)], "First"), new([new(b, 1, 3)], "Second")]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponents<ComboEditor>().ToArray()[0];
        await editor.Find(".accordion-button").ClickAsync(new());
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "3" });
        await ChooseAlternativeTarget(editor, "category:0");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        await RowAction(editor, "combo First, row 1", "Move down").ClickAsync(new());
        Assert.That(editor.Instance.Index, Is.EqualTo(1));
        Assert.That(RequirementField(editor, "#minCount1").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(RequirementField(editor, "#comboTarget1").GetAttribute("value"), Is.EqualTo("user:Renamed"));
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[1].Category!.MinCount, Is.EqualTo(2));
        Assert.That(RequirementField(editor, "#minCount1").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(cut.FindComponents<ComboEditor>().ToArray()[0].Instance.Combo.AlternativeGroups, Is.Empty);
    }

    [Test]
    public async Task RemovedTargetCannotRedirectPendingAdditionToTheNextGroup() {
        ComboAlternativeGroup first = new([ComboAlternative.For(new ComboCategory(a, 1, 3))]);
        ComboAlternativeGroup second = new([ComboAlternative.For(new ComboCategory(b, 1, 3))]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([], alternativeGroups: [first, second])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "group:0");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await cut.InvokeAsync(() => editor.Instance.OnReplaceCombo.InvokeAsync((0, editor.Instance.Combo.WithAlternativeGroups([second]))));
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(second));
        Assert.That(editor.FindAll("select[id^='alternativeTarget']"), Is.Empty);
        Assert.That(editor.FindAll("button[data-requirement='category:99']"), Is.Empty);
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);

    }

    [Test]
    public async Task RepeatedTargetsRetainPositionalOwnershipEvenWhenTheyShareAnObject() {
        ComboCategory repeated = new(a, 1, 3);
        ComboAlternative leaf = ComboAlternative.For(repeated);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([repeated, repeated], alternativeGroups: [new([leaf, leaf])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "category:1");
        Assert.That(editor.Find(".alternative-scope").TextContent, Does.Contain("requirement 2: Category: A"));
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        await RemoveOrLeaf(editor, 1, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category, Is.SameAs(repeated));
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories.All(category => ReferenceEquals(category, repeated)), Is.True);
        await DeleteRequirement(editor, "category:1");
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(repeated));
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SelectingAnOrGroupAppendsToThatExactGroupAsOneTarget() {
        ComboAlternative repeatedLeaf = ComboAlternative.For(new ComboCategory(a, 1, 3));
        ComboAlternativeGroup first = new([repeatedLeaf]);
        ComboAlternativeGroup second = new([repeatedLeaf]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Combos = [new([], alternativeGroups: [first, second])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());

        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(2));
        await ChooseAlternativeTarget(editor, "group:1");
        Assert.That(editor.Find(".alternative-scope").TextContent, Does.Contain("OR group 2"));
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());

        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0], Is.SameAs(first));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.AlternativeGroups[1].Alternatives, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.AlternativeGroups[1].Alternatives[0], Is.SameAs(repeatedLeaf));
        Assert.That(editor.Instance.Combo.AlternativeGroups[1].Alternatives[1].Category!.BaseCategory.Identity, Is.EqualTo(b.Identity));
    }

    [Test]
    public async Task AcceptedReplacementSessionDisposesTheObsoleteAlternativeDraft() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Categories = [a, b], Combos = [new([new(a, 1, 3)], "Old")] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "category:0");
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        SessionService codec = new(context.JSInterop.JSRuntime, new JsonSerializer());
        string json = codec.SerializeSession(new SessionState { Categories = [a, b], Combos = [new([new(b, 2, 4)], "Replacement")] });
        ComboEditor obsolete = editor.Instance;
        Mock<IBrowserFile> file = new();
        file.Setup(x => x.Name).Returns("replacement.json");
        file.Setup(x => x.Size).Returns(System.Text.Encoding.UTF8.GetByteCount(json));
        file.Setup(x => x.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        IRenderedComponent<InputFile> input = cut.FindComponents<InputFile>().ToArray()[1];
        await cut.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        IRenderedComponent<ComboEditor> replacement = cut.FindComponent<ComboEditor>();
        Assert.That(replacement.Instance, Is.Not.SameAs(obsolete));
        Assert.That(replacement.Instance.Combo.Name, Is.EqualTo("Replacement"));
        Assert.That(replacement.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(replacement.FindAll(".alternative-scope"), Is.Empty);
    }

    [Test]
    public async Task OrDuplicateIsIndependentAndMissingReferenceValidationTraversesEveryBranch() {
        Combo combo = new([], "Grouped", alternativeGroups: [new([ComboAlternative.For(new ComboCard("r", 1, 3)), ComboAlternative.For(new ComboCategory(a, 1, 3))])]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [new([a], 3, "Razen", id: "r")],
            Categories = [a],
            Combos = [combo],
            HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("remove every membership"));
        await FirstRowAction(editor, "combo", "Duplicate").ClickAsync(new());
        IRenderedComponent<ComboEditor> copy = cut.FindComponents<ComboEditor>().ToArray()[1];
        await RemoveOrLeaf(copy, 1, 1);
        Assert.That(cut.FindComponents<ComboEditor>().ToArray()[0].Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(2));
        Assert.That(copy.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(copy.Instance.Combo.Categories.Single().BaseCategory.Identity, Is.EqualTo(a.Identity));
        await FirstRowAction(cut.FindComponent<CardEditor>(), "card", "Remove").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference").IgnoreCase);
    }
}
