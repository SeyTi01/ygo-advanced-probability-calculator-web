using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Components.Shared;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class WorkspaceEditorTest : CalculatorEditorTestBase {
    [TestCase("card")]
    [TestCase("combo")]
    public async Task RowMenusShareCommandsCloseAfterActionAndPreserveCollapsedState(string kind) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IElement row = cut.Find($".{kind}-editor");
        IElement trigger = row.QuerySelector(".action-menu-trigger")!;
        string panelId = trigger.GetAttribute("aria-controls")!;
        Assert.That(cut.Find($"#{panelId}").HasAttribute("hidden"), Is.True);
        await trigger.ClickAsync(new());
        Assert.That(cut.Find($"#{panelId}").HasAttribute("hidden"), Is.False);
        Assert.That(row.QuerySelector(".accordion-button")!.GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(cut.Find($"#{panelId}").QuerySelectorAll("button").Select(button => button.TextContent.Trim()),
            Is.EqualTo(new[] { "Move up", "Move down", "Move to position…", "Duplicate", "Deactivate", "Remove" }));
        Assert.That(cut.Find($"#{panelId} button").HasAttribute("disabled"), Is.True);
        await cut.Find($"#{kind}Active0").ClickAsync(new());
        Assert.That(cut.Find($"#{panelId}").HasAttribute("hidden"), Is.True);
        Assert.That(cut.Find($"#{kind}Active0").TextContent, Is.EqualTo("Activate"));
        Assert.That(cut.Find($".{kind}-editor").ClassList, Does.Contain("entry-inactive"));
        await trigger.ClickAsync(new());
        await cut.Find($"#{panelId}").ParentElement!.KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.Find($"#{panelId}").HasAttribute("hidden"), Is.True);
        Assert.That(context.JSInterop.Invocations["workspaceMenus.close"], Is.Not.Empty);
    }

    [Test]
    public async Task DuplicateCardCopiesEverySettingWithIndependentIdAndOriginalReferences() {
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        Card original = new([a, dark], 3, "Custom draw", false, "original", 123,
            [dark.MetadataKey!], 2, false, 1, CardSelector.For(a), preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(dark));
        SessionState session = new() {
            Categories = [a], Cards = [original], Combos = [new([], cards: [new(original.Id, 1, 2)])]
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await FirstRowAction(cut, "card", "Duplicate").ClickAsync(new());
        Card[] cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToArray();
        Assert.That(cards, Has.Length.EqualTo(2));
        Card copy = cards[1];
        Assert.Multiple(() => {
            Assert.That(cards[0], Is.SameAs(original));
            Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
            Assert.That((copy.Name, copy.Copies, copy.Active, copy.ExternalCardId), Is.EqualTo((original.Name, original.Copies, original.Active, original.ExternalCardId)));
            Assert.That(copy.Categories, Is.EqualTo(original.Categories));
            Assert.That(copy.Categories, Is.Not.SameAs(original.Categories));
            Assert.That(copy.ManualMetadataCategoryKeys, Is.EquivalentTo(original.ManualMetadataCategoryKeys));
            Assert.That((copy.DrawCount, copy.DrawOncePerTurn, copy.PreDrawRemovalCount, copy.PostDrawDiscardCount, copy.PostDrawFailureBehavior),
                Is.EqualTo((original.DrawCount, original.DrawOncePerTurn, original.PreDrawRemovalCount, original.PostDrawDiscardCount, original.PostDrawFailureBehavior)));
            Assert.That(copy.PreDrawSelector, Is.EqualTo(original.PreDrawSelector));
            Assert.That(copy.PostDrawSelector, Is.EqualTo(original.PostDrawSelector));
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Cards.Single().CardId, Is.EqualTo(original.Id));
        });
        await cut.FindComponents<CardEditor>()[1].Find("input[id^='cardCopies']").InputAsync(new() { Value = "1" });
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Copies, Is.EqualTo(3));
        await Button(cut, "Save Session").ClickAsync(new());
        Assert.That(SavedSessionJson(), Does.Contain(copy.Id).And.Contain(original.Id));
    }

    [Test]
    public async Task QuickAddDoesNotCreateRowsOnFocusAndSupportsRepeatedManualAndCatalogAddition() {
        CardInfo pot = new() { Id = 55144522, Name = "Pot of Greed", Type = "Spell Card", Race = "Normal" };
        Mock<ICardInfoService> catalog = new();
        catalog.Setup(service => service.SearchCardsAsync(It.IsAny<string>())).ReturnsAsync(new[] { pot });
        context.Services.AddSingleton(catalog.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await cut.Find("#quickCardName").FocusAsync(new());
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty);
        await cut.Find("#quickCardName").InputAsync(new() { Value = "Pot" });
        await cut.Find("#quickCardName").KeyDownAsync(new() { Key = "ArrowDown" });
        await cut.Find("#quickCardName").KeyDownAsync(new() { Key = "Enter" });
        Card first = cut.FindComponent<CardEditor>().Instance.Card;
        Assert.That((first.Name, first.ExternalCardId, first.DrawCount, first.DrawOncePerTurn, first.Copies, first.Active),
            Is.EqualTo((pot.Name, (int?)pot.Id, (int?)2, false, 1, true)));
        Assert.That(first.Categories, Is.Not.Empty);
        Assert.That(cut.Find("#quickCardName").GetAttribute("value"), Is.Empty);
        Assert.That(cut.FindAll(".card-search-result"), Is.Empty);
        Assert.That(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"], Is.Not.Empty);
        await cut.Find("#quickCardName").InputAsync(new() { Value = "Pot" });
        await cut.Find(".card-search-result").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Id).Distinct().Count(), Is.EqualTo(2));
        await cut.Find("#quickCardName").InputAsync(new() { Value = "  Custom name  " });
        await cut.Find("#quickCardName").KeyDownAsync(new() { Key = "Escape" });
        await Button(cut, "Add card").ClickAsync(new());
        Card manual = cut.FindComponents<CardEditor>()[2].Instance.Card;
        Assert.That((manual.Name, manual.ExternalCardId), Is.EqualTo(("Custom name", (int?)null)));
        await cut.Find("#quickCardName").KeyDownAsync(new() { Key = "Enter" });
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task QuickAddRejectsStaleSearchAndWorksWithUnavailableCatalog() {
        TaskCompletionSource<IReadOnlyList<CardInfo>> pending = new();
        Mock<ICardInfoService> catalog = new();
        catalog.Setup(service => service.SearchCardsAsync("Slow")).Returns(pending.Task);
        catalog.Setup(service => service.SearchCardsAsync("Manual offline")).ThrowsAsync(new HttpRequestException());
        context.Services.AddSingleton(catalog.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        Task search = cut.Find("#quickCardName").InputAsync(new() { Value = "Slow" });
        await cut.Find("#quickCardName").InputAsync(new() { Value = "Manual offline" });
        Assert.That(cut.Find(".card-search-error").TextContent, Does.Contain("unavailable"));
        await cut.Find("#quickCardName").KeyDownAsync(new() { Key = "Enter" });
        pending.SetResult([new() { Id = 1, Name = "Stale result" }]);
        await search;
        Assert.That(cut.FindAll(".card-search-result"), Is.Empty);
        Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Manual offline"));
    }

    [Test]
    public async Task MoreMenuOnlyRequestsClearAndCategoryDraftSurvivesWorkspaceChanges() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        Assert.That(cut.Find("details.category-management").HasAttribute("open"), Is.False);
        IRenderedComponent<CategoryListEditor> categories = cut.FindComponent<CategoryListEditor>();
        await categories.Find("input[aria-label='Category name']").InputAsync(new() { Value = "Draft role" });
        await cut.Find("[aria-label='More workspace actions']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2));
        await Button(cut, "Clear workspace").ClickAsync(new());
        Assert.That(cut.Find(".clear-workspace-confirmation"), Is.Not.Null);
        Assert.That(cut.Find("[aria-label='More workspace actions']").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        await Button(cut, "Cancel").ClickAsync(new());
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(categories.Find("input[aria-label='Category name']").GetAttribute("value"), Is.EqualTo("Draft role"));
        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2));
    }
}
