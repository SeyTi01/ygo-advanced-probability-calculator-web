using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorCategoryDeletionTest : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task CategoryCancelAndEscapePreserveWorkspaceAndDrafts(bool escape) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Unsaved" });
        await Button(cut, "Save Session").ClickAsync(new());
        string before = SavedSessionJson();
        Card original = cut.FindComponents<CardEditor>()[0].Instance.Card;
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        Assert.That(cut.Find("[role=alertdialog]").TextContent, Does.Contain("A").And.Contain("inactive").And.Contain("match no cards"));
        if (escape) {
            await cut.Find("[role=alertdialog]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else {
            await Button(cut.FindComponent<DeletionConfirmation>(), "Cancel").ClickAsync(new());
        }
        Assert.That(cut.FindAll("[role=alertdialog]"), Is.Empty);
        Assert.That(cut.Find("[aria-label='New name for category A']").GetAttribute("value"), Is.EqualTo("Unsaved"));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card, Is.SameAs(original));
        await Button(cut, "Save Session").ClickAsync(new());
        Assert.That(SavedSessionJson(1), Is.EqualTo(before));
    }

    [Test]
    public async Task ConfirmedDeletionCleansInactiveNestedFiltersAndBoundsAndRoundTrips() {
        CardSelector nested = CardSelector.AllOfSelectors([CardSelector.For(a), CardSelector.AnyOf([a.Identity, b.Identity])]);
        Card draw = new([a, b], 3, "Draw", active: false, drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: nested, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(a));
        SearchEffectMode[] modes = [new(CardSelector.For(a), 1), new(CardSelector.For(b), 2)];
        Card search = new([a], name: "Search", active: false,
            search: new(CardSelector.AnyOfSelectors(modes.Select(mode => mode.Selector)), false, modes));
        Combo mixed = new([ComboCategory.SameCard([a, b], 0, 0)], "Mixed", false,
            alternativeGroups: [new([ComboAlternative.For(new ComboCategory(a, 1, 2)), ComboAlternative.For(new ComboCategory(b, 0, 3, RequirementMaximumMode.HandSize))])]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [draw, search], Combos = [mixed, new([new(a, 1, 2)], "Emptied")], HandSize = 2
        });
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        await cut.Find("[role=alertdialog] .btn-danger").ClickAsync(new());
        await Button(cut, "Save Session").ClickAsync(new());
        string json = SavedSessionJson();
        Assert.That(json, Does.Not.Contain("user:A"));
        SessionState loaded = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(json);
        Assert.That(loaded.Categories.Select(category => category.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(loaded.Cards[0].Categories.Select(category => category.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(loaded.Cards[0].PreDrawSelector, Is.EqualTo(CardSelector.For(b)));
        Assert.That(loaded.Cards[0].PostDrawSelector.Matches(new Card([b])), Is.False);
        Assert.That(loaded.Cards[0].PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(loaded.Cards[0].Active, Is.False);
        Assert.That(loaded.Cards[1].Search!.Modes!.Select(mode => mode.ActivationLimit), Is.EqualTo(new int?[] { 1, 2 }));
        Assert.That(loaded.Cards[1].Search!.Modes![0].Selector.Matches(new Card([b])), Is.False);
        Assert.That(loaded.Cards[1].Search!.Modes![1].Selector, Is.EqualTo(CardSelector.For(b)));
        Assert.That(loaded.Combos.Select(combo => combo.Name), Is.EqualTo(new[] { "Mixed", "Emptied" }));
        Assert.That(loaded.Combos[0].Categories.Single().MinCount, Is.Zero);
        Assert.That(loaded.Combos[0].Categories.Single().MaxCount, Is.Zero);
        Assert.That(loaded.Combos[0].AlternativeGroups.Single().Alternatives.Single().Category!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(loaded.Combos[1].RequirementCount, Is.Zero);
        Assert.That(loaded.Combos[1].Active, Is.False);
        Assert.That(cut.FindComponents<ComboEditor>()[1].Markup, Does.Contain("Inactive"));
        Assert.That(DrawEffectPresentation.DescribeSelector(loaded.Cards[0].PostDrawSelector, loaded.Categories), Is.EqualTo("no eligible cards"));
    }

    [Test]
    public async Task DeletionKeepsAcceptedPinAndMarksDisplayedResultStale() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        await cut.Find("[aria-label='Pin result']").ClickAsync(new());
        string pinned = cut.Find(".pinned-result").TextContent;
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        await cut.Find("[role=alertdialog] .btn-danger").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".pinned-result").TextContent, Is.EqualTo(pinned));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PopulatedGroupRequiresConfirmationAndPreservesEveryCombo(bool escape) {
        SessionState session = Session();
        session.ComboGroups.Add(new("g", "Group"));
        session.Combos[0].GroupId = "g";
        session.Combos[1] = session.Combos[1].WithGroup("g").WithActive(false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        Combo[] before = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo).ToArray();
        await cut.Find("[aria-label='Remove group Group']").ClickAsync(new());
        if (escape) {
            await cut.Find("[role=alertdialog]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else {
            await Button(cut.FindComponent<DeletionConfirmation>(), "Cancel").ClickAsync(new());
        }
        Assert.That(before.Select(combo => combo.GroupId), Is.EqualTo(new[] { "g", "g" }));
        await cut.Find("[aria-label='Remove group Group']").ClickAsync(new());
        await cut.Find("[role=alertdialog] .btn-danger").ClickAsync(new());
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo), Is.EqualTo(before));
        Assert.That(before.Select(combo => combo.GroupId), Is.All.Null);
        Assert.That(before[1].Active, Is.False);
        Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
    }

    [Test]
    public void RulesCannotContainCustomCategoryReferencesAndMetadataIsProtected() {
        Assert.Throws<ArgumentException>(() => new CategoryBase("Invalid", rule: CardSelector.For(a)));
        CategoryBase metadata = new("Monster", CategorySource.Metadata, "kind:monster");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() { Categories = [metadata], Cards = [new([metadata])] });
        Assert.That(cut.FindAll("[aria-label='Remove category Monster']"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RenameDismissesAnObsoleteDeletionConfirmation(bool alreadyEditing) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        if (alreadyEditing) {
            await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        }
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        if (!alreadyEditing) {
            await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
            Assert.That(cut.FindAll("[role=alertdialog]"), Is.Empty);
        }
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(cut.FindAll("[role=alertdialog]"), Is.Empty);
        await cut.Find("[aria-label='Remove category Renamed']").ClickAsync(new());
        Assert.That(cut.Find("[role=alertdialog]").TextContent, Does.Contain("Delete Renamed"));
        await cut.Find("[role=alertdialog] .btn-danger").ClickAsync(new());
        Assert.That(cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Any(category => category.Name == "Renamed"), Is.False);
    }
}
