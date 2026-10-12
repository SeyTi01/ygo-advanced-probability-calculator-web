using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorProgressiveTest : CalculatorEditorTestBase {
    [Test]
    public async Task ChangingACardToACategoryThenExtendingUsesItsNewCategoryPosition() {
        ComboCategory unchanged = new(a, 0, 3);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b], Cards = [new([], id: "r")],
            Combos = [new([unchanged], cards: [new("r", 1, 3)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "card:0");
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("[data-action='property']").ClickAsync(new());
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add property").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Cards, Is.Empty);
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(unchanged));
        Assert.That(editor.Instance.Combo.Categories[1].Selector.CategoryIdentities, Is.EquivalentTo(new[] { a.Identity, b.Identity }));
    }

    [Test]
    public async Task InlineCompositeDraftSurvivesCategoryRenameAndOuterReorder() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b], Combos = [new([ComboCategory.SameCard([a, b], 1, 3)], "First"), new([], "Second")]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponents<ComboEditor>().First();
        await EditRequirement(editor, "category:0");
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        await editor.Find("[aria-label='Move combo First, row 1 down']").ClickAsync(new());
        Assert.That(editor.Find("#minCount1").GetAttribute("value"), Is.EqualTo("2"));
        await Button(editor, "Save changes").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Categories.Single().MinCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Combo.Categories.Single().Selector.CategoryIdentities, Is.EquivalentTo(new[] { "user:Renamed", b.Identity }));
    }

    [Test]
    public async Task BlankEditorOffersOnePickerWithCardsFirstAndNoAdvancedActions() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a], Cards = [new([a], name: "Razen", id: "r")], Combos = [new([])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll(".requirement-fields select"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll("#comboTarget0 optgroup").First().GetAttribute("label"), Is.EqualTo("Cards"));
        Assert.That(editor.FindAll("[data-action], .add-or-button, .alternative-target-instruction"), Is.Empty);
        Assert.That(editor.Find("details").HasAttribute("open"), Is.False);
        AssertAnyMaximumDraft(editor);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = "card:r" });
        await editor.Find("#minCount0").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Cards.Single().CardId, Is.EqualTo("r"));
        Assert.That(editor.Instance.Combo.Categories.Single().BaseCategory, Is.SameAs(a));
        Assert.That(editor.FindAll(".requirement-edit"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ContextualActionsAreHiddenUntilEditingAndSameCardIsOnlyForCategories() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b], Cards = [new([], id: "r")], Combos = [new([new(a, 1, 3)], cards: [new("r", 1, 3)])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(editor.FindAll("[data-action]"), Is.Empty);
        await EditRequirement(editor, "card:0");
        Assert.That(editor.FindAll("[data-action='property']"), Is.Empty);
        Assert.That(editor.Find("[data-action='alternative']"), Is.Not.Null);
        await EditRequirement(editor, "category:0");
        Assert.That(editor.FindAll("[data-action='property']"), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".requirement-edit[aria-expanded='true']").GetAttribute("data-requirement"), Is.EqualTo("category:0"));
        Assert.That(editor.FindAll(".requirement-edit button"), Is.Empty, "Native edit buttons never contain nested controls.");
    }

    [TestCase("category:1")]
    [TestCase("alternative:0:1")]
    public async Task InlineEditingReplacesTheExactDuplicateOccurrence(string target) {
        ComboCategory repeated = new(a, 1, 3);
        ComboAlternative leaf = ComboAlternative.For(repeated);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b], Combos = [new([repeated, repeated], alternativeGroups: [new([leaf, leaf])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, target);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Save changes").ClickAsync(new());
        Combo combo = editor.Instance.Combo;
        ComboCategory changed = target.StartsWith("category", StringComparison.Ordinal) ? combo.Categories[1] : combo.AlternativeGroups[0].Alternatives[1].Category!;
        Assert.That(changed.BaseCategory, Is.SameAs(b));
        Assert.That((changed.MinCount, changed.MaxCount, changed.MaximumMode), Is.EqualTo((0, 0, RequirementMaximumMode.Fixed)));
        Assert.That(combo.Categories[0], Is.SameAs(repeated));
        Assert.That(combo.AlternativeGroups[0].Alternatives[0], Is.SameAs(leaf));
    }

    [Test]
    public async Task EscapeDiscardsAnEditAndRestoresTheOrdinaryDraft() {
        Combo original = new([new(a, 1, 3)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Categories = [a, b], Combos = [original] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await EditRequirement(editor, "category:0");
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#minCount0").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.Instance.Combo, Is.SameAs(original));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("#minCount0").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.FindAll("[aria-expanded='true'].requirement-edit"), Is.Empty);
    }

    [Test]
    public async Task ChangedBoundsAreExplicitlySavedWhenExtendingAndInvalidDraftsCannotExtend() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Categories = [a, b], Combos = [new([new(a, 1, 3)])] });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "category:0");
        await editor.Find("#minCount0").InputAsync(new() { Value = "4" });
        Assert.That(editor.Find("[data-action='alternative']").TextContent, Does.StartWith("Save &"));
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        Assert.That(editor.FindAll("[role='alert']"), Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Combo.Categories.Single().MinCount, Is.EqualTo(1));
        await editor.Find("#maxCount0").InputAsync(new() { Value = "" });
        await editor.Find("[data-action='alternative']").ClickAsync(new());
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Category!.MinCount, Is.EqualTo(4));
    }

    [Test]
    public async Task CompositeAlternativesHaveExplicitGroupingAndEditablePropertiesAndBounds() {
        ComboCategory composite = ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize);
        CategoryBase c = new("C");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b, c], Cards = [new([], id: "r")],
            Combos = [new([], alternativeGroups: [new([ComboAlternative.For(composite), ComboAlternative.For(new ComboCard("r", 0, 0))])])]
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(editor.Find(".requirement-or-group").GetAttribute("aria-label"), Is.EqualTo("Alternative group 1"));
        Assert.That(editor.Find(".requirement-or-group .alternative-separator").TextContent, Is.EqualTo("OR"));
        Assert.That(editor.Find(".requirement-or-group .same-card-and").TextContent, Is.EqualTo("AND"));
        Assert.That(editor.FindAll(".combo-header-content .composite-alternative"), Has.Count.EqualTo(1));
        await EditRequirement(editor, "alternative:0:0");
        await editor.Find("[aria-label='Same-card property 2']").ChangeAsync(new() { Value = c.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "2" });
        await Button(editor, "Save changes").ClickAsync(new());
        ComboCategory changed = editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Category!;
        Assert.That(changed.Selector.CategoryIdentities, Is.EqualTo(new[] { a.Identity, c.Identity }));
        Assert.That(changed.MaxCount, Is.Zero, "Unchanged Any preserves the saved raw maximum.");
        Assert.That(changed.MinCount, Is.EqualTo(2));
        await RemoveProperty(editor, "alternative:0:0", 2);
        Assert.That(editor.Instance.Combo.AlternativeGroups.Single().Alternatives[0].Category!.Selector.Kind, Is.EqualTo(CardSelectorKind.Category));
        await DeleteRequirement(editor, "alternative:0:0");
        Assert.That(editor.Instance.Combo.Cards.Single().MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
    }

    [Test]
    public async Task LegacySessionInlineEditRoundTripPreservesGroupCardAndSelectorMeaning() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        const string json = """
            {"SchemaVersion":4,"HandSize":2,"Categories":[{"Name":"A"},{"Name":"B"}],
             "Cards":[{"Id":"r","Name":"Razen","Copies":2,"Categories":[{"Name":"A"},{"Name":"B"}]}],
             "ComboGroups":[{"Id":"g","Name":"Routes"}],
             "Combos":[{"Name":"Legacy","GroupId":"g","Categories":[],"Cards":[],"AlternativeGroups":[{"Alternatives":[
               {"Kind":"Category","Category":{"Selector":{"Kind":"AllOf","Categories":["user:A","user:B"]},"CategoryBases":[{"Name":"A"},{"Name":"B"}],"MinCount":1,"MaxCount":0,"MaximumMode":"HandSize"}},
               {"Kind":"Card","Card":{"CardId":"r","MinCount":0,"MaxCount":0,"MaximumMode":"Fixed"}}]}]}]}
            """;
        SessionState loaded = await codec.LoadSessionAsync(json);
        string before = codec.SerializeSession(loaded);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(loaded);
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "alternative:0:0");
        await Button(editor, "Save changes").ClickAsync(new());
        await EditRequirement(editor, "alternative:0:1");
        await Button(editor, "Save changes").ClickAsync(new());
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(before));
        SessionState roundTrip = await codec.LoadSessionAsync(codec.SerializeSession(loaded));
        Assert.That(roundTrip.Combos[0].GroupId, Is.EqualTo("g"));
        Assert.That(roundTrip.Combos[0].AlternativeGroups[0].Alternatives[0].Category!.Selector.Kind, Is.EqualTo(CardSelectorKind.AllOf));
        Assert.That(roundTrip.Combos[0].AlternativeGroups[0].Alternatives[1].Card!.CardId, Is.EqualTo("r"));
    }

    [Test]
    public async Task SameCardAndSeparateRequirementsFollowIndependentPhysicalHandOracle() {
        Card both = new([a, b], 1, id: "both");
        Card blank = new([], 1, id: "blank");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b], Cards = [both, blank], Combos = [new([new(a, 1, 1)])], HandSize = 1
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await ExtendRequirement(editor, "category:0", sameCard: true);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add property").ClickAsync(new());
        double sameCard = SmallDeckOracle.EnumerateProbability([both, blank], [editor.Instance.Combo], 1);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(sameCard.ToString("P2")));
        await RemoveProperty(editor, "category:0", 2);
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Add").ClickAsync(new());
        double separate = SmallDeckOracle.EnumerateProbability([both, blank], [editor.Instance.Combo], 1);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total").TextContent, Does.Contain(separate.ToString("P2")));
        Assert.That(sameCard, Is.EqualTo(.5));
        Assert.That(separate, Is.Zero);
    }
}
