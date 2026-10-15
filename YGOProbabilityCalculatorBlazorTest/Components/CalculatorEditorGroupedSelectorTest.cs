using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorGroupedSelectorTest : CalculatorEditorTestBase {
    [Test]
    public void ThreePairedRowsEachOpenTwoConditionFieldsAndOneOperator() {
        Card source = SearchEffectsTest.Preset(92221402);
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindAll(".requirement-edit").Select(row => row.GetAttribute("aria-label")), Is.EqualTo(new[] {
            "Edit Monster AND Level >= 5", "Edit DARK OR LIGHT", "Edit Aqua OR Pyro"
        }));
        Assert.That(editor.Markup, Does.Not.Contain("Any condition"));
        foreach (IRenderedComponent<ExpressionRow> row in editor.FindComponents<ExpressionRow>().ToArray()) {
            row.Find(".requirement-edit").Click();
            Assert.That(editor.FindAll(".selector-condition-row > .selector-operand"), Has.Count.EqualTo(2));
            Assert.That(editor.FindAll(".selector-condition-row > .selector-operator select"), Has.Count.EqualTo(1));
            SaveRow(editor);
        }
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null, "Presentation pairing does not rewrite the saved plan or provenance.");
    }

    [Test]
    public void RenameKeepsPairedRowsAndTheActiveConditionDraftWhenCanonicalOrderChanges() {
        CategoryBase[] categories = new[] { "A", "B", "C", "D", "E" }.Select(name => new CategoryBase(name)).ToArray();
        CardSelector original = CardSelector.AllOf(categories.Take(4).Select(category => category.Identity));
        Card source = SearchEffectsTest.Rota().WithSearch(new(original, false));
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, categories));
        editor.Find(".simulation-edit").Click();
        Button(editor, "Edit groups").Click();
        IRenderedComponent<SelectorExpressionEditor> first = editor.FindComponents<SelectorExpressionEditor>()
            .Single(component => !component.Instance.IsRoot && component.Instance.Selector.CategoryIdentities.Contains("user:A"));
        first.Find(".requirement-edit").Click();
        editor.Find(".selector-operand select").Change("user:E");
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card,
            source.WithSearch(source.Search! with { Selector = original.RenameCategory("user:A", "user:Z") }))
            .Add(component => component.CategoryBases, categories.Skip(1).Append(new CategoryBase("Z")).ToArray())
            .Add(component => component.CategoryRename, new CategoryRenameNotice(1, "A", "Z")));
        Assert.That(editor.Find(".selector-operand select").GetAttribute("value"), Is.EqualTo("user:E"));
        Assert.That(editor.FindComponents<SelectorExpressionEditor>().Any(component => ReferenceEquals(component.Instance, first.Instance)), Is.True);
        SaveRow(editor);
        Assert.That(editor.Find(".selector-expression").TextContent, Does.Contain("E"));
        Assert.That(editor.Find(".selector-expression").TextContent, Does.Not.Contain("Z"));
    }

    private static IRenderedComponent<SelectorExpressionEditor> Node(IRenderedFragment editor, CardSelectorKind kind) =>
        editor.FindComponents<SelectorExpressionEditor>().Where(component => component.Instance.Selector.Kind == kind || kind == CardSelectorKind.LevelAtLeast && component.Instance.Selector.HasLevelPredicate)
            .OrderByDescending(component => component.Instance.IsRoot).Last();

    private static void OpenLevel(IRenderedFragment editor) {
        Node(editor, CardSelectorKind.LevelAtLeast).Find(".requirement-edit").Click();
        for (int depth = 0; editor.FindAll("input[aria-label='Minimum Level']").Count == 0 && depth < CardSelector.MaximumDepth; depth++) {
            editor.FindAll(".selector-operand button").Single(button => button.TextContent.Contains("Level")).Click();
        }
    }
    private static void SaveRow(IRenderedFragment editor) => editor.Find(".selector-expression .requirement-form").Submit();
    private static void CancelRow(IRenderedFragment editor) => Button(editor.FindComponent<ExpressionForm>(), "Cancel").Click();

    [Test]
    public void CompactExpressionEditsThresholdAndGroupsWithPreviewSaveCancelAndRestore() {
        Card source = SearchEffectsTest.Preset(92221402);
        Card target = SelectorExpressionTest.Target();
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.Deck, new[] { source, target })
            .Add(component => component.OnChanged, card => saved = card));
        Assert.That(editor.Markup, Does.Not.Contain("Partial"));
        editor.Find(".simulation-edit").Click();
        Assert.That(editor.FindComponents<ExpressionGroup>(), Has.Count.EqualTo(1));
        Assert.That(editor.FindComponents<ExpressionRow>(), Has.Count.EqualTo(3));
        Assert.That(editor.Find(".selector-expression").TextContent, Does.Not.Contain("Any condition"));
        Assert.That(editor.FindAll(".draw-selector-chip"), Has.Count.EqualTo(6));
        Assert.That(editor.FindAll(".selector-expression select, .selector-expression input, .selector-extend"), Is.Empty);
        foreach (IElement separator in editor.FindAll(".selector-expression [role='separator']")) {
            Assert.That(separator.PreviousElementSibling, Is.Not.Null);
            Assert.That(separator.NextElementSibling, Is.Not.Null);
            Assert.That(separator.TextContent.Trim(), Is.AnyOf("AND", "OR"));
        }
        SelectorExpressionEditor threshold = Node(editor, CardSelectorKind.LevelAtLeast).Instance;
        OpenLevel(editor);
        Assert.That(editor.FindComponents<ExpressionForm>(), Has.Count.EqualTo(1));
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("input[aria-label='Minimum Level']").Input("6");
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain(target.Name), "Row draft does not commit before Save.");
        CancelRow(editor);
        Assert.That(Node(editor, CardSelectorKind.LevelAtLeast).Instance.Selector.HasLevelPredicate, Is.True);
        OpenLevel(editor);
        editor.Find("input[aria-label='Minimum Level']").Input("6");
        SaveRow(editor);
        Assert.That(Node(editor, CardSelectorKind.LevelAtLeast).Instance, Is.SameAs(threshold));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        editor.Find(".simulation-cancel").Click();
        Assert.That(saved, Is.Null);
        editor.Find(".simulation-edit").Click();
        OpenLevel(editor);
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.EqualTo("5"));
        editor.Find("input[aria-label='Minimum Level']").Input("6");
        SaveRow(editor);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        Assert.That(saved.Search!.Matches(target), Is.False);
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-edit").Click();
        Node(editor, CardSelectorKind.AnyOf).Find(".requirement-edit").Click();
        editor.Find("[aria-label='Row operator']").Change("AllOf");
        SaveRow(editor);
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Selector.Signature, Does.Contain("AllOf"));
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, saved));
        editor.Find(".simulation-restore").Click();
        Assert.That(saved!.EffectPlan, Is.EqualTo(source.EffectPlan));
        Assert.That(saved.EffectProfile.Revision, Is.EqualTo(2));
    }

    [Test]
    public void InvalidThresholdBlocksSaveAcrossOtherDraftEditsUntilCorrectedCanceledOrRemoved() {
        Card source = SearchEffectsTest.Preset(92221402);
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source));
        editor.Find(".simulation-edit").Click();
        OpenLevel(editor);
        editor.Find("input[aria-label='Minimum Level']").Input("-1");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("#cardSearchOnce0").Change(false);
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.EqualTo("-1"));
        Assert.That(editor.Find(".selector-expression [role='alert']").TextContent, Does.Contain("Enter a Level"));
        editor.Find("input[aria-label='Minimum Level']").Input("7");
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True, "Finish the valid local edit first.");
        SaveRow(editor);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        OpenLevel(editor);
        editor.Find("input[aria-label='Minimum Level']").Input("");
        editor.Find(".selector-expression form").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".simulation-editor"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".selector-expression form"), Is.Empty);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        OpenLevel(editor);
        editor.Find("input[aria-label='Minimum Level']").Input("");
        Button(editor, "Remove condition 2").Click();
        SaveRow(editor);
        Assert.That(editor.FindAll("input[aria-label='Minimum Level']"), Is.Empty);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void ExistingFlatSelectorCanBecomeGroupedWithoutLosingDraftOnParentUpdate() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        Card source = SearchEffectsTest.Rota().WithSearch(new(CardSelector.For(a), false));
        Card target = new([a, b], name: "Target");
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, new[] { a, b })
            .Add(component => component.Deck, new[] { source, target }).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        Button(editor, "Edit groups").Click();
        Node(editor, CardSelectorKind.Category).Find(".requirement-edit").Click();
        Button(editor, "Add alternative (OR)").Click();
        editor.Find(".selector-expression form .selector-operand select").Change(b.Identity);
        SaveRow(editor);
        Node(editor, CardSelectorKind.AnyOf).Find(".requirement-edit").Click();
        Button(editor, "Add property (AND)").Click();
        editor.Find(".selector-expression form .selector-operand select").Change("metadata:kind:monster");
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.DrawEffectsEnabled, false));
        Assert.That(editor.Find(".selector-expression form .selector-operand select").GetAttribute("value"), Is.EqualTo("metadata:kind:monster"));
        SaveRow(editor);
        Assert.That(editor.FindComponents<ExpressionRow>(), Has.Count.EqualTo(2));
        Assert.That(editor.Find(".simulation-targets").TextContent, Does.Contain("None"));
        editor.Find(".simulation-save").Click();
        Assert.That(saved!.Search!.Selector.Matches(new Card([a, new("Monster", CategorySource.Metadata, "kind:monster")])), Is.True);
        Assert.That(saved.Search.Selector.Matches(target), Is.False);
        Assert.That(saved.Search.ObjectiveMetadataOnly, Is.False);
        AssertRoundTrip(saved.Search.Selector);
    }

    [Test]
    public void BothEditorsUseTheSameRowFormGroupAndSeparatorComponents() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        Combo combo = new([new(a, 1, 3)], alternativeGroups: [new([
            ComboAlternative.For(new ComboCategory(a, 1, 3)), ComboAlternative.For(new ComboCategory(b, 1, 3))])]);
        IRenderedComponent<ComboEditor> editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, combo).Add(component => component.CategoryBases, new[] { a, b }));
        editor.Find(".accordion-button").Click();
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.FindComponents<ExpressionRow>(), Has.Count.EqualTo(3));
        Assert.That(editor.FindComponents<ExpressionForm>(), Has.Count.EqualTo(1));
        Assert.That(editor.FindComponents<ExpressionGroup>(), Has.Count.EqualTo(1));
        Assert.That(editor.FindComponents<ExpressionSeparator>(), Has.Count.EqualTo(2));
        Button(editor.FindComponent<ExpressionForm>(), "Cancel").Click();
        Assert.That(editor.FindAll(".requirement-row form"), Is.Empty);
    }

    [Test]
    public void RenameAndTargetChangesPreserveAnInvalidActiveThresholdAndSiblingDraftIdentity() {
        CategoryBase a = new("A");
        CategoryBase renamed = new("Z");
        CardSelector selector = CardSelector.AllOfSelectors([CardSelector.For(a), CardSelector.LevelAtLeast(5)]);
        Card source = SearchEffectsTest.Rota().WithSearch(new(selector, false));
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.CategoryBases, new[] { a }));
        editor.Find(".simulation-edit").Click();
        OpenLevel(editor);
        SelectorExpressionEditor instance = Node(editor, CardSelectorKind.LevelAtLeast).Instance;
        editor.Find("input[aria-label='Minimum Level']").Input("");
        Card changed = source.WithSearch(source.Search! with { Selector = selector.RenameCategory(a.Identity, renamed.Identity) });
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card, changed)
            .Add(component => component.CategoryBases, new[] { renamed }).Add(component => component.CategoryRename, new CategoryRenameNotice(1, "A", "Z"))
            .Add(component => component.Deck, new[] { changed, new Card([renamed], name: "New target") }));
        Assert.That(Node(editor, CardSelectorKind.LevelAtLeast).Instance, Is.SameAs(instance));
        Assert.That(editor.Find("input[aria-label='Minimum Level']").GetAttribute("value"), Is.Empty);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.True);
        editor.Find("input[aria-label='Minimum Level']").Input("8");
        SaveRow(editor);
        Assert.That(editor.Find(".simulation-save").HasAttribute("disabled"), Is.False);
        Assert.That(editor.Find(".selector-expression").TextContent, Does.Contain("Z"));
        OpenLevel(editor);
        editor.Find(".selector-expression form .selector-operand select").Change("level-at-least");
        editor.Find(".selector-operand input").Input("12");
        CategoryBase final = new("Final");
        editor.SetParametersAndRender(parameters => parameters.Add(component => component.Card,
            changed.WithSearch(changed.Search! with { Selector = changed.Search.Selector.RenameCategory(renamed.Identity, final.Identity) }))
            .Add(component => component.CategoryBases, new[] { final }).Add(component => component.CategoryRename, new CategoryRenameNotice(2, "Z", "Final")));
        Assert.That(editor.Find(".selector-operand input").GetAttribute("value"), Is.EqualTo("12"));
        CancelRow(editor);
    }

    [Test]
    public void NestedLegacyBranchesCanBeExtendedReplacedAndCollapsedWithoutChangingOtherBranches() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        CategoryBase c = new("C");
        CategoryBase d = new("D");
        CardSelector untouched = CardSelector.AllOf([c.Identity, d.Identity]);
        CardSelector initial = CardSelector.AnyOfSelectors([CardSelector.AllOf([a.Identity, b.Identity]), untouched]);
        CardSelector current = initial;
        IRenderedComponent<PostDrawSelectorEditor>? editor = null;
        editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters.Add(component => component.Selector, current)
            .Add(component => component.CategoryBases, new[] { a, b, c, d }).Add(component => component.OnChanged, value => {
                current = value;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        Button(editor, "Edit groups").Click();
        IRenderedComponent<SelectorExpressionEditor> branch = editor.FindComponents<SelectorExpressionEditor>()
            .Single(component => !component.Instance.IsRoot && component.Instance.Selector.CategoryIdentities.Contains(a.Identity));
        branch.Find(".requirement-edit").Click();
        Button(editor, "Group condition 1").Click();
        editor.Find("form .selector-operand select").Change("level-at-least");
        editor.Find("input[aria-label='Minimum Level']").Input("6");
        SaveRow(editor);
        Assert.That(current.Children, Does.Contain(untouched));
        Assert.That(current.Matches(new Card([b, .. SelectorExpressionTest.Target(6).Categories], externalCardId: 100)), Is.True);
        Assert.That(current.Matches(new Card([b, .. SelectorExpressionTest.Target(5).Categories], externalCardId: 100)), Is.False);
        AssertRoundTrip(current);
        IRenderedComponent<SelectorExpressionEditor> changedBranch = editor.FindComponents<SelectorExpressionEditor>()
            .Single(component => !component.Instance.IsRoot && component.Instance.Selector.HasLevelPredicate);
        changedBranch.Find(".requirement-edit").Click();
        editor.Find(".selector-operand button").Click();
        IRenderedComponent<SelectorExpressionEditor> nested = editor.FindComponents<SelectorExpressionEditor>()
            .Single(component => !component.Instance.IsRoot && component.Instance.Selector.Kind == CardSelectorKind.AnyOf);
        editor.Find("[aria-label='Row operator']").Change("AllOf");
        CancelRow(editor);
        Assert.That(current.Children, Does.Contain(untouched));
        nested.Find(".requirement-edit").Click();
        Button(editor, "Replace group").Click();
        editor.Find("form .selector-operand select").Change(a.Identity);
        SaveRow(editor);
        Assert.That(current, Is.EqualTo(initial));
        editor.FindComponents<SelectorExpressionEditor>().Single(component => !component.Instance.IsRoot && component.Instance.Selector.CategoryIdentities.Contains(a.Identity)).Find(".requirement-edit").Click();
        Button(editor, "Remove group").Click();
        Assert.That(current, Is.EqualTo(untouched), "Removing one OR branch promotes the remaining complete AND branch.");
        AssertRoundTrip(current);
    }

    [Test]
    public void SavingUntouchedRowPreservesObjectivePolicyAndBuiltInProvenance() {
        Card source = SearchEffectsTest.Preset(92221402);
        Card? saved = null;
        IRenderedComponent<CardSimulationEditor> editor = context.RenderComponent<CardSimulationEditor>(parameters => parameters
            .Add(component => component.Card, source).Add(component => component.OnChanged, card => saved = card));
        editor.Find(".simulation-edit").Click();
        OpenLevel(editor);
        SaveRow(editor);
        editor.Find(".simulation-save").Click();
        Assert.That(saved, Is.Null);
        Assert.That(editor.Instance.Card.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn));
    }

    [Test]
    public void AncestorBoundsRejectOnlyTheLocalEditAndRemainRecoverable() {
        CardSelector original = CardSelector.LevelAtLeast(1);
        for (int index = 0; index < CardSelector.MaximumDepth - 1; index++) {
            CardSelector sibling = CardSelector.For(new CategoryBase($"Sibling {index}"));
            original = index % 2 == 0 ? CardSelector.AllOfSelectors([original, sibling]) : CardSelector.AnyOfSelectors([original, sibling]);
        }
        CardSelector current = original;
        int changes = 0;
        IRenderedComponent<PostDrawSelectorEditor>? editor = null;
        editor = context.RenderComponent<PostDrawSelectorEditor>(parameters => parameters.Add(component => component.Selector, current)
            .Add(component => component.OnChanged, value => {
                current = value;
                changes++;
                editor!.SetParametersAndRender(next => next.Add(component => component.Selector, value));
            }));
        OpenLevel(editor);
        Button(editor, "Group condition 2").Click();
        editor.Find("form .selector-operand select").Change("metadata:kind:monster");
        SaveRow(editor);
        Assert.That(changes, Is.Zero);
        Assert.That(current, Is.SameAs(original));
        Assert.That(editor.Find(".selector-expression [role='alert']").TextContent, Does.Contain("too deeply grouped"));
        Assert.That(editor.FindAll("form"), Has.Count.EqualTo(1));
        CancelRow(editor);
        OpenLevel(editor);
        editor.Find("input[aria-label='Minimum Level']").Input("2");
        SaveRow(editor);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(current.Depth, Is.EqualTo(CardSelector.MaximumDepth));
        Assert.That(editor.FindAll("[role='alert']"), Is.Empty);
    }

    private static void AssertRoundTrip(CardSelector selector) {
        CardSelector restored = JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(selector))!;
        Assert.That(restored, Is.EqualTo(selector));
        CategoryBase[] categories = selector.CategoryIdentities.Select(identity => identity.StartsWith("user:", StringComparison.Ordinal)
            ? new CategoryBase(identity[5..]) : new CategoryBase(identity[9..], CategorySource.Metadata, identity[9..])).ToArray();
        for (int mask = 0; mask < 1 << categories.Length; mask++) {
            CategoryBase[] selected = categories.Where((category, index) => (mask & (1 << index)) != 0).ToArray();
            Assert.That(restored.Matches(new Card(selected)), Is.EqualTo(selector.Matches(new Card(selected))));
            foreach (int level in new[] { 0, 5, 6, 20 }) {
                Card target = new(selected.Concat(SelectorExpressionTest.Target(level).Categories).DistinctBy(category => category.Identity), externalCardId: 100);
                Assert.That(restored.Matches(target), Is.EqualTo(selector.Matches(target)));
            }
        }
    }
}
