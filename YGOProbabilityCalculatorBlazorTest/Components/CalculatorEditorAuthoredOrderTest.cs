using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorAuthoredOrderTest : CalculatorEditorTestBase {
    [Test]
    public async Task CascadingDeletionKeepsRewrittenAndSurvivingAuthoredSlotsAndSelectorTerms() {
        CategoryBase c = new("C");
        CategoryBase d = new("D");
        Combo combo = new([new(a, 1, 2), ComboCategory.SameCard([a, c], 0, 0), new(b, 1, 3)], cards: [new("physical", 0, 0)],
            alternativeGroups: [new([ComboAlternative.For(new ComboCategory(a, 1, 2))]),
                new([ComboAlternative.For(new ComboCategory(a, 1, 2)), ComboAlternative.For(new ComboCategory(c, 0, 3, RequirementMaximumMode.HandSize))]),
                new([ComboAlternative.For(new ComboCategory(b, 1, 2))])],
            requirementOrder: [new(ComboRequirementKind.Group, 1), new(ComboRequirementKind.Category, 1), new(ComboRequirementKind.Card, 0),
                new(ComboRequirementKind.Group, 0), new(ComboRequirementKind.Category, 0), new(ComboRequirementKind.Group, 2), new(ComboRequirementKind.Category, 2)]);
        Card draw = new([a, b], 3, "Draw", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.AllOf([c.Identity, a.Identity, b.Identity]), postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.AllOfSelectors([CardSelector.Not(CardSelector.For(a)), CardSelector.AnyOf([c.Identity, b.Identity]), CardSelector.For(d)]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b, c, d], Cards = [draw, new([], 1, "Physical", id: "physical")], Combos = [combo], HandSize = 2
        });
        await cut.Find("[aria-label='Remove category A']").ClickAsync(new());
        await cut.Find("[role=alertdialog] .btn-danger").ClickAsync(new());
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "alternative:0:0", "category:0", "card:0", "alternative:1:0", "category:1" }));
        Assert.That(editor.Instance.Combo.Categories[0].MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.AlternativeGroups[0].Alternatives[0].Category!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState loaded = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(loaded.Combos.Single().RequirementOrder, Is.EqualTo(editor.Instance.Combo.RequirementOrder));
        Assert.That(loaded.Cards[0].PreDrawSelector.AuthoredTerms.SelectMany(term => term.CategoryIdentities), Is.EqualTo(new[] { c.Identity, b.Identity }));
        CardSelector[] outer = loaded.Cards[0].PostDrawSelector.AuthoredTerms.ToArray();
        Assert.That(outer[0].AuthoredTerms.SelectMany(term => term.CategoryIdentities), Is.EqualTo(new[] { c.Identity, b.Identity }));
        Assert.That(outer[1], Is.EqualTo(CardSelector.For(d)));
    }

    [Test]
    public async Task MixedRequirementsAppendAndTypeChangingEditsKeepTheirPosition() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([a], 2, "Physical", id: "physical")], Combos = [new([])], HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = "card:physical" });
        await Button(editor, "Add").ClickAsync(new());
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = a.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "card:0", "category:0" }));
        Assert.That(editor.Find(".combo-header-requirements").TextContent.IndexOf("Physical", StringComparison.Ordinal),
            Is.LessThan(editor.Find(".combo-header-requirements").TextContent.IndexOf("A", StringComparison.Ordinal)));
        await EditRequirement(editor, "card:0");
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#minCount0").InputAsync(new() { Value = "0" });
        await editor.Find("#maxCount0").InputAsync(new() { Value = "0" });
        await Button(editor, "Save").ClickAsync(new());
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "category:1", "category:0" }));
        Assert.That(editor.Instance.Combo.Categories[1].MinCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Categories[1].MaxCount, Is.Zero);
        await ExtendRequirement(editor, "category:1");
        await editor.Find("#comboTarget0").ChangeAsync(new() { Value = "card:physical" });
        await Button(editor, "Add alternative").ClickAsync(new());
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "alternative:0:0", "alternative:0:1", "category:0" }));
        await DeleteRequirement(editor, "alternative:0:1");
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "category:1", "category:0" }));
    }

    [Test]
    public async Task OrderSurvivesRenameDraftCancellationDuplicationAndSessionShare() {
        Combo combo = new([new(a, 1, 2)], "Route", cards: [new("physical", 0, 0)],
            requirementOrder: [new(ComboRequirementKind.Card, 0), new(ComboRequirementKind.Category, 0)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([a], 2, "Physical", id: "physical")], Combos = [combo], HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await EditRequirement(editor, "category:0");
        await editor.Find("#maxCount0").InputAsync(new() { Value = "1" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Z" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(editor.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("1"));
        await Button(editor, "Cancel").ClickAsync(new());
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "card:0", "category:0" }));
        await DuplicateComboAsync(cut, 0);
        Assert.That(cut.FindComponents<ComboEditor>().Select(component => component.Instance.Combo.RequirementOrder), Is.All.EqualTo(combo.RequirementOrder));
        await Button(cut, "Save Session").ClickAsync(new());
        string json = SavedSessionJson();
        string shared = SessionShareCodec.Decode(new Uri(SessionShareCodec.CreateLink("https://example.test/", json)).Fragment);
        SessionState loaded = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(shared);
        Assert.That(loaded.Combos[0].RequirementOrder, Is.EqualTo(combo.RequirementOrder));
        Assert.That(loaded.Combos[0].Categories.Single().BaseCategory.Name, Is.EqualTo("Z"));
        double oracle = SmallDeckOracle.EnumerateProbability(loaded.Cards, loaded.Combos, 2);
        double actual = context.Services.GetRequiredService<IProbabilityCalculatorService>().CalculateProbabilityForCombos(loaded.Cards, loaded.Combos, 2);
        Assert.That(actual, Is.EqualTo(oracle));
    }

    [Test]
    public async Task LegacyOrderIsDeterministicAndMalformedOrderIsRejected() {
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        const string legacy = """{"Categories":[],"Cards":[],"Combos":[{"Categories":[],"Cards":[{"CardId":"missing","MinCount":0,"MaxCount":0}]}],"HandSize":1}""";
        SessionState loaded = await codec.LoadSessionAsync(legacy);
        Assert.That(loaded.Combos.Single().RequirementOrder, Is.EqualTo(new[] { new ComboRequirementPosition(ComboRequirementKind.Card, 0) }));
        string json = codec.SerializeSession(loaded);
        string invalid = json.Replace("\"Active\": true", "\"RequirementOrder\": [{\"Kind\":\"Card\",\"Index\":2}], \"Active\": true", StringComparison.Ordinal);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await codec.LoadSessionAsync(invalid));
    }

    [Test]
    public void SelectorAndSameCardPropertyOrderRoundTripWithoutChangingCanonicalMeaning() {
        CategoryBase c = new("C");
        CardSelector original = CardSelector.AllOfSelectors([CardSelector.For(c), CardSelector.AnyOf([b.Identity, a.Identity])]);
        CardSelector reversed = CardSelector.AllOfSelectors([CardSelector.AnyOf([a.Identity, b.Identity]), CardSelector.For(c)]);
        Assert.That(original.Signature, Is.EqualTo(reversed.Signature));
        CardSelector loaded = System.Text.Json.JsonSerializer.Deserialize<CardSelector>(System.Text.Json.JsonSerializer.Serialize(original))!;
        Assert.That(loaded.AuthoredTerms.First().CategoryIdentities.Single(), Is.EqualTo(c.Identity));
        Assert.That(loaded.AuthoredTerms.Last().AuthoredTerms.Select(term => term.CategoryIdentities.Single()), Is.EqualTo(new[] { b.Identity, a.Identity }));
        ComboCategory requirement = ComboCategory.SameCard([c, a, b], 0, 0);
        Assert.That(requirement.CategoryBases.Select(category => category.Name), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(requirement.Selector.Signature, Is.EqualTo(CardSelector.AllOf([a.Identity, b.Identity, c.Identity]).Signature));
        for (int mask = 0; mask < 8; mask++) {
            Card card = new(new[] { a, b, c }.Where((category, index) => (mask & (1 << index)) != 0));
            Assert.That(loaded.Matches(card), Is.EqualTo(reversed.Matches(card)));
        }
    }

    [Test]
    public void DuplicateOccurrencesAndLegacyListMutationKeepSurvivingSlots() {
        ComboCategory shared = new(a, 1, 2);
        ComboRequirementPosition[] order = [new(ComboRequirementKind.Category, 0), new(ComboRequirementKind.Card, 0), new(ComboRequirementKind.Category, 1)];
        Combo original = new([shared, shared], cards: [new("physical", 1, 2)], requirementOrder: order);
        Combo edited = original.WithCategories([new(b, 0, 0), shared]);
        Assert.That(edited.RequirementOrder, Is.EqualTo(order));
        Combo removed = original.WithoutRequirement(new(ComboRequirementKind.Category, 0));
        Assert.That(removed.RequirementOrder, Is.EqualTo(new[] { new ComboRequirementPosition(ComboRequirementKind.Card, 0), new ComboRequirementPosition(ComboRequirementKind.Category, 0) }));

        Combo mutable = new([new(a, 1, 2), new(b, 1, 2)], cards: [new("physical", 1, 2)], requirementOrder: order);
        mutable.Categories.RemoveAt(0);
        Assert.That(mutable.RequirementOrder, Is.EqualTo(removed.RequirementOrder));
    }

    [Test]
    public async Task RepeatedGroupObjectsRenderAndUnwrapTheSelectedOccurrenceInPlace() {
        ComboAlternativeGroup shared = new([ComboAlternative.For(new ComboCategory(a, 1, 2)), ComboAlternative.For(new ComboCategory(b, 1, 2))]);
        Combo combo = new([], cards: [new("physical", 1, 2)], alternativeGroups: [shared, shared],
            requirementOrder: [new(ComboRequirementKind.Group, 0), new(ComboRequirementKind.Card, 0), new(ComboRequirementKind.Group, 1)]);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = [a, b], Cards = [new([a, b], 2, "Physical", id: "physical")], Combos = [combo], HandSize = 2
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await DeleteRequirement(editor, "alternative:0:1");
        Assert.That(RowKinds(editor), Is.EqualTo(new[] { "category:0", "card:0", "alternative:0:0", "alternative:0:1" }));
    }

    private static string[] RowKinds(IRenderedComponent<ComboEditor> editor) =>
        editor.FindAll(".requirement-list button[data-requirement]").Select(element => element.GetAttribute("data-requirement")!).ToArray();

    [Test]
    public async Task CompactEligibilityPeersAppendWithoutCanonicalReordering() {
        CardSelector selector = CardSelector.Any;
        IRenderedComponent<PostDrawSelectorEditor>? editor = null;
        editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters
            .Add(component => component.Selector, selector).Add(component => component.CategoryBases, new[] { a, b })
            .Add(component => component.OnChanged, value => {
                selector = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        await editor.Find("#removalSelector").ChangeAsync(new() { Value = b.Identity });
        await editor.Find("#removalSelector").ChangeAsync(new() { Value = a.Identity });
        Assert.That(editor.FindAll(".draw-selector-chip").Select(element => element.TextContent), Is.EqualTo(new[] { "B", "A" }));
        Assert.That(selector.Signature, Is.EqualTo(CardSelector.AnyOf([a.Identity, b.Identity]).Signature));
    }
}
