using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public partial class CalculatorEditorTest {
    private static async Task ChooseAlternativeTarget(IRenderedComponent<ComboEditor> editor, string target) {
        if (Button(editor, "Add OR").GetAttribute("aria-expanded") == "false")
            await Button(editor, "Add OR").ClickAsync(new());
        await editor.Find($"#alternativeTarget{editor.Instance.Index}").ChangeAsync(new() { Value = target });
    }

    private static Task RemoveOrLeaf(IRenderedComponent<ComboEditor> editor, int group, int leaf) =>
        editor.Find($"[aria-label^='Remove alternative {leaf} ('][aria-label$='from group {group}']").ClickAsync(new());

    [Test]
    public async Task BasicAndEditorKeepsOrdinaryAddUpdateAndDeleteWithoutAlternativeControls() {
        var cut = Render(new SessionState { Categories = [a, b], Cards = [new([], name: "R", id: "r")], Combos = [new([new(a, 1, 5)])] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(Button(editor, "Add OR").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(editor.Find(".add-or-chevron").ClassList.Contains("is-open"), Is.False);
        await Button(editor, "Add OR").ClickAsync(new());
        Assert.That(Button(editor, "Add OR").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(editor.Find(".add-or-chevron").ClassList.Contains("is-open"), Is.True);
        await Button(editor, "Add OR").ClickAsync(new());
        Assert.That(editor.FindAll(".expanded-expression button").ToArray().All(e => e.ClassList.Contains("btn-close")), Is.True);
        Assert.That(editor.FindAll(".alternative-action, [aria-label^='Edit alternative'], [aria-label^='Move alternative group'], select[id^='alternativeTarget']"), Is.Empty);
        Assert.That(editor.Find("#alternativeTargets0").HasAttribute("hidden"), Is.True);
        Assert.That(editor.FindAll("button").ToArray().Select(e => e.TextContent.Trim()), Does.Not.Contain("OR").And.Not.Contain("OR +"));
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = a.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Update").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Single().MinCount, Is.EqualTo(2));
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = "r" });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("[aria-label^='Remove category A from combo']").ClickAsync(new());
        await editor.Find("[aria-label^='Remove card R #1 from combo']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Select(c => c.BaseCategory.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClosingOrCancellingRestoresEveryOrdinaryDraftValueWithoutPublishing(bool close) {
        var cut = Render(new SessionState { Categories = [a, b], Cards = [new([], name: "R", id: "r")], Combos = [new([new(a, 1, 5)])] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = "r" });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "4" });
        var before = editor.Instance.Combo;
        await Button(editor, "Add OR").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before));
        Assert.That(editor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo("r"));
        await ChooseAlternativeTarget(editor, "category:0");
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before), "Invalid incomplete additions publish nothing.");
        await editor.Find("#minCount0").InputAsync(new() { Value = "8" });
        await Button(editor, close ? "Add OR" : "Cancel").ClickAsync(new());
        Assert.That(editor.Instance.Combo, Is.SameAs(before));
        Assert.That(editor.Find("#constraintKind0").GetAttribute("value"), Is.EqualTo("Card"));
        Assert.That(editor.Find("#comboCard0").GetAttribute("value"), Is.EqualTo("r"));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(Button(editor, "Add"), Is.Not.Null);
    }

    [TestCase("Category", "Category")]
    [TestCase("Card", "Card")]
    [TestCase("Category", "Card")]
    public async Task ExplicitTargetsCreateTypedAlternativesAndReturnToOrdinaryAddition(string seedKind, string addedKind) {
        var cut = Render(new SessionState { Categories = [a, b], Cards = [new([a], 2, "Razen", id: "r"), new([b], name: "Razen", id: "b")],
            Combos = [new([new(a, 1, 5), new(b, 1, 5)], cards: [new("r", 1, 5)])], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, seedKind == "Card" ? "card:0" : "category:0");
        Assert.That(editor.FindAll("#alternativeTarget0 option").ToArray().Select(e => e.TextContent), Has.Some.Contains("Requirement 1: Category: A (1–5)"));
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = addedKind });
        await editor.Find(addedKind == "Card" ? "#comboCard0" : "#comboCategory0").ChangeAsync(new() { Value = addedKind == "Card" ? "b" : b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Button(editor, "Add alternative").ClickAsync(new());
        var group = editor.Instance.Combo.AlternativeGroups.Single();
        Assert.That(group.Alternatives.Select(x => x.Kind), Is.EqualTo(new[] { seedKind, addedKind }));
        Assert.That(group.Alternatives[1].Category?.MinCount ?? group.Alternatives[1].Card!.MinCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories.Single(c => c.BaseCategory.Identity == b.Identity).MinCount, Is.EqualTo(1));
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(editor.Find("#alternativeTarget0").GetAttribute("value"), Is.Empty);
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Category" });
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Update").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Single(c => c.BaseCategory.Identity == b.Identity).MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(group));
    }

    [Test]
    public async Task RemovingLeavesPromotesTheFinalCategoryAndCardWithoutChangingExistingRequirements() {
        var ordinary = new ComboCategory(a, 2, 4);
        var survivor = new ComboCategory(a, 1, 0, RequirementMaximumMode.HandSize);
        var cardSurvivor = new ComboCard("r", 1, 0, RequirementMaximumMode.HandSize);
        var first = new ComboAlternativeGroup([ComboAlternative.For(survivor), ComboAlternative.For(new ComboCategory(b, 0, 0))]);
        var second = new ComboAlternativeGroup([ComboAlternative.For(cardSurvivor)]);
        var cut = Render(new SessionState { Categories = [a, b], Cards = [new([], 3, "R", id: "r")], Combos = [new([ordinary], cards: [], alternativeGroups: [first, second])] });
        var editor = cut.FindComponent<ComboEditor>();
        var header = editor.Find(".combo-header-content").OuterHtml;
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(Button(editor, "Add OR").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        await ChooseAlternativeTarget(editor, "group:1");
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = a.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Add alternative").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "group:0");
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Select(g => g.Alternatives.Count), Is.EqualTo(new[] { 3, 2 }));
        Assert.That(editor.FindAll(".expanded-expression button").ToArray().All(e => e.ParentElement!.ClassList.Contains("badge")), Is.True);
        Assert.That(editor.FindAll(".accordion-button button, [aria-label^='Edit alternative'], [aria-label^='Move alternative group']"), Is.Empty);
        await RemoveOrLeaf(editor, 1, 3);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Select(g => g.Alternatives.Count), Is.EqualTo(new[] { 2, 2 }), "Three leaves become a two-leaf OR group.");
        await RemoveOrLeaf(editor, 2, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1), "A one-leaf group promotes its card into the ordinary card list.");
        Assert.That(editor.Instance.Combo.Cards.Single(), Is.SameAs(cardSurvivor));
        Assert.That(editor.Find(".combo-header-content").OuterHtml, Is.Not.EqualTo(header));
        Assert.That(editor.FindAll(".summary-group"), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".combo-header-content").TextContent, Does.Contain("Card: R"));

        await RemoveOrLeaf(editor, 1, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty, "A two-leaf group promotes its last category into the ordinary category list.");
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(ordinary));
        Assert.That(editor.Instance.Combo.Categories[1], Is.SameAs(survivor));
        Assert.That(editor.Instance.Combo.Categories[1].MinCount, Is.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[1].MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Categories[1].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));

        if (Button(editor, "Add OR").GetAttribute("aria-expanded") == "false")
            await Button(editor, "Add OR").ClickAsync(new());
        var targetValues = editor.FindAll("#alternativeTarget0 option").Select(option => option.GetAttribute("value"));
        Assert.That(targetValues, Does.Contain("category:1").And.Contain("card:0"));
        await editor.Find("#alternativeTarget0").ChangeAsync(new() { Value = "card:0" });
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Card, Is.SameAs(cardSurvivor));
        var restoredCardGroup = editor.Instance.Combo.AlternativeGroups.Single();

        var ordinaryCategoryButtons = editor.FindAll(".expanded-expression > span.badge.category-tag:not(.combo-card-tag)").ToArray();
        Assert.That(ordinaryCategoryButtons, Has.Length.EqualTo(2));
        await ordinaryCategoryButtons[1].QuerySelector("button")!.ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(ordinary), "Deleting the promoted occurrence leaves the pre-existing same-target requirement untouched.");
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(restoredCardGroup));
    }

    [Test]
    public async Task CategoryRenameAndOuterReorderPreserveThePreciseSeedAndDraft() {
        var cut = Render(new SessionState { Categories = [a, b], Combos = [new([new(a, 1, 3)], "First"), new([new(b, 1, 3)], "Second")] });
        var editor = cut.FindComponents<ComboEditor>().ToArray()[0];
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = a.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await ChooseAlternativeTarget(editor, "category:0");
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = a.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        await editor.Find("[aria-label='Move combo First, row 1 down']").ClickAsync(new());
        Assert.That(editor.Instance.Index, Is.EqualTo(1));
        Assert.That(editor.Find("#minCount1").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo("user:Renamed"));
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[1].Category!.MinCount, Is.EqualTo(2));
        Assert.That(editor.Find("#minCount1").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(cut.FindComponents<ComboEditor>().ToArray()[0].Instance.Combo.AlternativeGroups, Is.Empty);
    }

    [Test]
    public async Task RemovedTargetCannotRedirectPendingAdditionToTheNextGroup() {
        var first = new ComboAlternativeGroup([ComboAlternative.For(new ComboCategory(a, 1, 3))]);
        var second = new ComboAlternativeGroup([ComboAlternative.For(new ComboCategory(b, 1, 3))]);
        var cut = Render(new SessionState { Categories = [a, b], Combos = [new([], alternativeGroups: [first, second])] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "group:0");
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await RemoveOrLeaf(editor, 1, 1);
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single(), Is.SameAs(second));
        Assert.That(editor.Find("#alternativeTarget0").GetAttribute("value"), Is.Empty);
        await ChooseAlternativeTarget(editor, "category:99");
        Assert.That(editor.FindAll(".alternative-scope"), Is.Empty);
    }

    [Test]
    public async Task RepeatedTargetsRetainPositionalOwnershipEvenWhenTheyShareAnObject() {
        var repeated = new ComboCategory(a, 1, 3);
        var leaf = ComboAlternative.For(repeated);
        var cut = Render(new SessionState { Categories = [a, b], Combos = [new([repeated, repeated], alternativeGroups: [new([leaf, leaf])])] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "category:1");
        Assert.That(editor.Find("#alternativeTarget0").GetAttribute("value"), Is.EqualTo("category:1"));
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        await RemoveOrLeaf(editor, 1, 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category, Is.SameAs(repeated));
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories.All(category => ReferenceEquals(category, repeated)), Is.True);
        var categories = editor.FindAll(".expanded-expression > span.badge.category-tag:not(.combo-card-tag)").ToArray();
        Assert.That(categories, Has.Length.EqualTo(2));
        await categories[1].QuerySelector("button")!.ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(repeated));
        Assert.That(editor.Instance.Combo.AlternativeGroups, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AcceptedReplacementSessionDisposesTheObsoleteAlternativeDraft() {
        var cut = Render(new SessionState { Categories = [a, b], Combos = [new([new(a, 1, 3)], "Old")] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await ChooseAlternativeTarget(editor, "category:0");
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        var codec = new SessionService(context.JSInterop.JSRuntime, new JsonSerializer());
        var json = codec.SerializeSession(new SessionState { Categories = [a, b], Combos = [new([new(b, 2, 4)], "Replacement")] });
        var obsolete = editor.Instance;
        var file = new Mock<IBrowserFile>();
        file.Setup(x => x.Name).Returns("replacement.json");
        file.Setup(x => x.Size).Returns(System.Text.Encoding.UTF8.GetByteCount(json));
        file.Setup(x => x.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        var input = cut.FindComponents<InputFile>().ToArray()[1];
        await cut.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        var replacement = cut.FindComponent<ComboEditor>();
        Assert.That(replacement.Instance, Is.Not.SameAs(obsolete));
        Assert.That(replacement.Instance.Combo.Name, Is.EqualTo("Replacement"));
        Assert.That(replacement.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(Button(replacement, "Add OR").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(replacement.FindAll(".alternative-scope"), Is.Empty);
    }

    [Test]
    public async Task OrDuplicateIsIndependentAndMissingReferenceValidationTraversesEveryBranch() {
        var combo = new Combo([], "Grouped", alternativeGroups: [new([ComboAlternative.For(new ComboCard("r", 1, 3)), ComboAlternative.For(new ComboCategory(a, 1, 3))])]);
        var cut = Render(new SessionState { Cards = [new([a], 3, "Razen", id: "r")], Categories = [a], Combos = [combo], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("This category is still used"));
        await editor.Find("[aria-label='Duplicate combo Grouped']").ClickAsync(new());
        var copy = cut.FindComponents<ComboEditor>().ToArray()[1];
        await RemoveOrLeaf(copy, 1, 1);
        Assert.That(cut.FindComponents<ComboEditor>().ToArray()[0].Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(2));
        Assert.That(copy.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(copy.Instance.Combo.Categories.Single().BaseCategory.Identity, Is.EqualTo(a.Identity));
        await cut.FindComponent<CardEditor>().Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference").IgnoreCase);
    }
}
