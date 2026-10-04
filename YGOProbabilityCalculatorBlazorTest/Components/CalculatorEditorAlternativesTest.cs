using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public partial class CalculatorEditorTest {
    [Test]
    public async Task CancellingAlternativeRestoresTheSuspendedOrdinaryDraft() {
        var cut = Render(new SessionState { Categories = [a, b], Combos = [new([new(a, 1, 5)])] });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "3" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "4" });
        await editor.Find("[aria-label='Add alternative to category A']").ClickAsync(new());
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.Find("#comboCategory0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("3"));
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("4"));
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
    }
    [Test]
    public async Task OrGroupReorderingAndCategoryRenamePreserveTheScopedDraft() {
        var first = new ComboAlternativeGroup([ComboAlternative.For(new ComboCategory(a, 1, 3)), ComboAlternative.For(new ComboCategory(b, 1, 3))]);
        var second = new ComboAlternativeGroup([ComboAlternative.For(new ComboCard("r", 1, 3))]);
        var cut = Render(new SessionState { Categories = [a, b], Cards = [new([], 3, "R", id: "r")],
            Combos = [new([], alternativeGroups: [first, second])], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("This category is still used"));
        await editor.Find("[aria-label='Edit alternative 1 in group 1']").ClickAsync(new());
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await editor.Find("[aria-label='Move alternative group 1 down']").ClickAsync(new());
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#comboCategory0").GetAttribute("value"), Is.EqualTo("user:Renamed"));
        await editor.Find("[aria-label='Update alternative']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[1].Alternatives[0].Category!.MinCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Combo.AlternativeGroups[1].Alternatives[0].Category!.BaseCategory.Name, Is.EqualTo("Renamed"));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Card!.CardId, Is.EqualTo("r"));
    }
    [Test]
    public async Task OrEditorCommitsOnlyCompleteDraftsAndTargetsTheSelectedAlternative() {
        var session = new SessionState { Categories = [a, b], Cards = [new([a], 2, "Razen", id: "r"), new([b], id: "b")],
            Combos = [new([new(a, 1, 5), new(b, 1, 5)], "Route")], HandSize = 2 };
        var cut = Render(session);
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("[aria-label='Add alternative to category A']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
        await editor.Find("#constraintKind0").ChangeAsync(new() { Value = "Card" });
        await editor.Find("#comboCard0").ChangeAsync(new() { Value = "r" });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        await editor.Find("[aria-label='Add alternative']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Select(c => c.BaseCategory.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[1].Card!.MinCount, Is.EqualTo(2));
        Assert.That(editor.FindAll(".summary-group"), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".summary-group").TextContent, Does.Contain("OR").And.Contain("(").And.Contain(")"));
        Assert.That(editor.FindAll(".accordion-button button"), Is.Empty);

        await editor.Find("[aria-label='Edit alternative 1 in group 1']").ClickAsync(new());
        await editor.Find("#comboCategory0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await editor.Find("[aria-label='Update alternative']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories[0].MinCount, Is.EqualTo(1), "The same target outside the group must not change.");
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category!.MaxCount, Is.Zero);

        await editor.Find("[aria-label='Add alternative to group 1']").ClickAsync(new());
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(2));
        await editor.Find("[aria-label='Remove alternative 1 from group 1']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(1));
        await editor.Find("[aria-label='Remove alternative 1 from group 1']").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(editor.Instance.Combo.Categories, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task OrDuplicateIsIndependentAndMissingReferenceValidationTraversesEveryBranch() {
        var combo = new Combo([], "Grouped", alternativeGroups: [new([ComboAlternative.For(new ComboCard("r", 1, 3)),
            ComboAlternative.For(new ComboCategory(a, 1, 3))])]);
        var cut = Render(new SessionState { Cards = [new([a], 3, "Razen", id: "r")], Categories = [a], Combos = [combo], HandSize = 2 });
        var editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("[aria-label='Duplicate combo Grouped']").ClickAsync(new());
        var copy = cut.FindComponents<ComboEditor>()[1];
        await copy.Find("[aria-label='Remove alternative 1 from group 1']").ClickAsync(new());
        Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(2));
        Assert.That(copy.Instance.Combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(1));
        await cut.FindComponent<CardEditor>().Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Markup, Does.Contain("missing card reference").IgnoreCase);
    }
}
