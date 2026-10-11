using Bunit;
using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorFilteredPostDrawTest : CalculatorEditorTestBase {
    [Test]
    public void PreAndPostDrawFiltersExposeCommonMetadataAndUseCanonicalChipColors() {
        CategoryBase role = new("Role");
        CategoryBase fire = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "attribute:fire");
        CategoryBase dark = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "attribute:dark");
        CategoryBase monster = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "kind:monster");
        CategoryBase spell = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "spell-type:quick-play");
        CategoryBase trap = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "kind:trap");
        Card card = new([role], name: "Filter source", drawCount: 2, preDrawRemovalCount: 1,
            postDrawDiscardCount: 1,
            preDrawSelector: CardSelector.AnyOfSelectors([
                CardSelector.AllOf([fire.Identity, role.Identity]),
                CardSelector.For(dark)
            ]),
            postDrawSelector: CardSelector.AnyOfSelectors([
                CardSelector.AllOf([monster.Identity, spell.Identity]),
                CardSelector.For(trap)
            ]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [role],
            Cards = [card],
            CategoryColorIndices = new() { [role.Name] = 7 }
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponent<CardEditor>();
        IRenderedComponent<PostDrawSelectorEditor> pre = editor.FindComponents<PostDrawSelectorEditor>()[0];
        IRenderedComponent<PostDrawSelectorEditor> post = editor.FindComponents<PostDrawSelectorEditor>()[1];

        foreach (IRenderedComponent<PostDrawSelectorEditor> selector in new[] { pre, post }) {
            string[] optionValues = selector.FindAll("optgroup option").Select(static option => option.GetAttribute("value")!).ToArray();
            IElement[] groups = selector.FindAll("optgroup").ToArray();
            Assert.That(optionValues, Does.Contain(monster.Identity).And.Contain(dark.Identity)
                .And.Contain(CardPropertyProvider.CommonCategories.Single(category => category.MetadataKey == "level:8").Identity)
                .And.Contain(spell.Identity));
            Assert.That(optionValues.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(optionValues.Length));
            Assert.That(groups.Select(static group => group.GetAttribute("label")),
                Is.EqualTo(new[] { "User categories", "Relevant properties", "Common properties" }));
            Assert.That(groups.All(static group => group.QuerySelectorAll("option").Length > 0), Is.True);
        }

        Assert.That(EligibilityChip(pre, fire.Name).ClassList, Does.Contain("card-property-color-attribute-fire"));
        Assert.That(EligibilityChip(pre, fire.Name).ClassList, Does.Contain("draw-selector-chip"));
        Assert.That(EligibilityChip(pre, role.Name).ClassList, Does.Contain("category-color-7").And.Not.Contain("card-property-tag"));
        Assert.That(EligibilityChip(pre, dark.Name).ClassList, Does.Contain("card-property-color-attribute-dark"));
        Assert.That(EligibilityChip(post, monster.Name).ClassList, Does.Contain("card-property-color-monster"));
        Assert.That(EligibilityChip(post, spell.Name).ClassList, Does.Contain("card-property-color-spell"));
        Assert.That(EligibilityChip(post, trap.Name).ClassList, Does.Contain("card-property-color-trap"));
        Assert.That(pre.Markup, Does.Contain("AND").And.Contain("OR"));
        Assert.That(post.Markup, Does.Contain("AND").And.Contain("OR"));
    }

    [Test]
    public void UnresolvedLegacySelectorIdentityKeepsItsLabelAndUsesGenericPropertyColor() {
        Card card = new([], name: "Legacy selector", drawCount: 2, postDrawDiscardCount: 1,
            postDrawSelector: new(CardSelectorKind.Category, ["metadata:x"]));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [card] });
        IRenderedComponent<PostDrawSelectorEditor> selector = cut.FindComponent<PostDrawSelectorEditor>();

        IElement chip = EligibilityChip(selector, "x");
        Assert.That(chip.ClassList, Does.Contain(CardPropertyColorPalette.GenericMetadataClass));
        Assert.That(chip.QuerySelector("button[aria-label='Remove eligibility x']"), Is.Not.Null);
    }

    [Test]
    public async Task FilteredPostDrawEditorWorksInBasicPreservesDraftsAndRoundTrips() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPostDrawDiscardCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().ToArray()[0];
        string id = editor.Instance.Card.Id;
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardEligibility0").ChangeAsync(new() { Value = "user:A" });
        AssertPreviousResult(cut);
        await editor.Find("#cardEligibility0").ChangeAsync(new() { Value = "metadata:attribute:dark" });
        Assert.That(editor.Instance.Card.PostDrawSelector.Kind, Is.EqualTo(CardSelectorKind.AnyOf));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].PostDrawSelector, Is.EqualTo(editor.Instance.Card.PostDrawSelector));
        await cut.Find("[aria-label='Move card First, row 1 down']").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>().ToArray()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        await editor.Find("[aria-label='Remove eligibility A']").ClickAsync(new());
        Assert.That(editor.Instance.Card.PostDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Category));
        await editor.Find("#cardEligibility1").ChangeAsync(new() { Value = "any" });
        Assert.That(editor.Instance.Card.PostDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Any));
        await editor.Find("#cardEligibility1").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        Assert.That(editor.Instance.Card.PostDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Any));
        Assert.That(editor.Instance.Card.PostDrawDiscardCount, Is.Zero);
    }

    [Test]
    public async Task FilteredPostDrawUserCategoryRenameKeepsFilterAndEditorDraft() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(2).WithPostDrawDiscardCount(1).WithPostDrawSelector(CardSelector.For(a));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().ToArray()[0];
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed" });
        await cut.Find("[aria-label='New name for category A']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.FindComponents<CardEditor>().ToArray()[0].Instance, Is.SameAs(editor.Instance));
        Assert.That(editor.Instance.Card.PostDrawSelector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed" }));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
    }

    private static IElement EligibilityChip(IRenderedFragment fragment, string label) =>
        fragment.FindAll(".badge.category-tag")
            .Single(chip => chip.TextContent.Trim().StartsWith(label, StringComparison.Ordinal));
}
