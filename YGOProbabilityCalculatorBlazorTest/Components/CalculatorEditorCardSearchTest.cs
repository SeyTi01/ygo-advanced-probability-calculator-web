using static YGOProbabilityCalculatorBlazorTest.Components.WorkspaceActions;
using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorCardSearchTest : CalculatorEditorTestBase {
    private const int AshBlossomId = 14558127;
    private const int BlueEyesId = 89631139;
    private const int PotOfGreedId = 55144522;
    private const int AllureOfDarknessId = 1475311;
    private const int GracefulCharityId = 79571449;
    private static readonly CardInfo AshBlossom = new() {
        Id = AshBlossomId,
        Name = "Ash Blossom & Joyous Spring",
        Type = "Tuner Effect Monster",
        FrameType = "effect",
        Race = "Zombie",
        Attribute = "FIRE",
        Level = 3,
        Archetype = "Floowandereeze",
        CanonicalCardId = AshBlossomId,
        ArtworkImageIds = [AshBlossomId],
        ArtworkMetadataKnown = true
    };
    private static readonly CardInfo BlueEyes = new() {
        Id = BlueEyesId,
        Name = "Blue-Eyes White Dragon",
        Type = "Normal Monster",
        FrameType = "normal",
        Race = "Dragon",
        Attribute = "LIGHT",
        Level = 8,
        CanonicalCardId = BlueEyesId,
        ArtworkImageIds = [BlueEyesId],
        ArtworkMetadataKnown = true
    };
    private static readonly CardInfo PotOfGreed = new() {
        Id = PotOfGreedId,
        Name = "Pot of Greed",
        Type = "Spell Card",
        FrameType = "spell",
        Race = "Normal",
        CanonicalCardId = PotOfGreedId,
        ArtworkImageIds = [PotOfGreedId],
        ArtworkMetadataKnown = true
    };
    private static readonly CardInfo AllureOfDarkness = new() {
        Id = AllureOfDarknessId,
        Name = "Allure of Darkness",
        Type = "Spell Card",
        FrameType = "spell",
        Race = "Normal",
        CanonicalCardId = AllureOfDarknessId,
        ArtworkImageIds = [AllureOfDarknessId],
        ArtworkMetadataKnown = true
    };
    private static readonly CardInfo GracefulCharity = new() {
        Id = GracefulCharityId,
        Name = "Graceful Charity",
        Type = "Spell Card",
        FrameType = "spell",
        Race = "Normal",
        CanonicalCardId = GracefulCharityId,
        ArtworkImageIds = [GracefulCharityId],
        ArtworkMetadataKnown = true
    };

    private SearchCatalogHandler _handler = null!;

    [SetUp]
    public void UseLocalSearchCatalog() => RegisterCardInfoService(new SearchCatalogHandler());

    [TestCase(false)]
    [TestCase(true)]
    public async Task FortyCardTypingKeepsUnchangedRowsAndDuplicateInputKeepsTheModel(bool withRule) {
        CategoryBase definition = withRule ? new(a.Name, rule: CardSelector.For(new("Monster", CategorySource.Metadata, "kind:monster"))) : a;
        Card[] cards = Enumerable.Range(0, 14)
            .Select(index => new Card([a], copies: index == 13 ? 1 : 3, id: $"row-{index}"))
            .ToArray();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [.. cards], Categories = [definition], HandSize = 5 });
        IRenderedComponent<CardEditor>[] editors = cut.FindComponents<CardEditor>().ToArray();
        IRenderedComponent<CardEditor> first = editors[0];
        IElement input = first.Find("#cardName0");
        await input.FocusAsync(new());
        int[] renders = editors.Select(editor => editor.RenderCount).ToArray();

        foreach (string query in new[] { "A", "As", "Ash", "Ash Blossom", "ash blossom", "", "Final pasted name!" }) {
            await input.InputAsync(new() { Value = query });
            Assert.That(first.Instance.Card.Name, Is.EqualTo(query.Length == 0 ? null : query));
            Assert.That(input.GetAttribute("value"), Is.EqualTo(query));
        }

        Assert.That(editors.Skip(1).Select(editor => editor.RenderCount), Is.EqualTo(renders.Skip(1)));
        Card final = first.Instance.Card;
        await input.InputAsync(new() { Value = final.Name });
        Assert.That(first.Instance.Card, Is.SameAs(final));
        string recovery = (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;
        Assert.That((await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(recovery)).Cards[0].Name,
            Is.EqualTo(final.Name));
        await cut.FindComponent<SessionShareButton>().Find("button").ClickAsync(new());
        string link = (string)context.JSInterop.Invocations["sessionSharing.copy"].Last().Arguments[0]!;
        string shared = SessionShareCodec.Decode(link[link.IndexOf('#')..]);
        Assert.That((await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(shared)).Cards[0].Name,
            Is.EqualTo(final.Name));
        await Button(cut, "Save Session").ClickAsync(new());
        using JsonDocument saved = JsonDocument.Parse(SavedSessionJson());
        Assert.That(saved.RootElement.GetProperty("Cards")[0].GetProperty("Name").GetString(), Is.EqualTo("Final pasted name!"));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task NameCallbackCapturesRecoveryAndShareBeforeBlur() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [new([], id: "stable")] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        await editor.Find("#cardName0").InputAsync(new() { Value = string.Empty });
        Card renamed = editor.Instance.Card.WithName("Final character!");
        // bUnit 1.x bubbles Input to the workspace observer despite Blazor's
        // stopPropagation modifier. Exercise the callback itself to check ownership.
        await cut.InvokeAsync(() => editor.Instance.OnRenameCard!((0, renamed)));
        string recovery = (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;
        ISessionService sessions = context.Services.GetRequiredService<ISessionService>();
        Assert.That((await sessions.LoadSessionAsync(recovery)).Cards[0].Name, Is.EqualTo(renamed.Name));
        Assert.That((await sessions.LoadSessionAsync(cut.FindComponent<SessionShareButton>().Instance.Capture())).Cards[0].Name,
            Is.EqualTo(renamed.Name));
        await cut.InvokeAsync(() => editor.Instance.OnNameEditFinished.InvokeAsync());
        Assert.That(editor.Instance.Card, Is.SameAs(renamed));
    }

    [Test]
    public async Task OfflineEntryAndRowSwitchKeepFinalNameAndHideLateResults() {
        Mock<ICardInfoService> service = new();
        TaskCompletionSource<IReadOnlyList<CardInfo>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(info => info.SearchCardsAsync("Delayed")).Returns(pending.Task);
        service.Setup(info => info.SearchCardsAsync("Manual offline!")).ThrowsAsync(new HttpRequestException());
        context.Services.RemoveAll<ICardInfoService>();
        context.Services.AddSingleton(service.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [new([], id: "first"), new([], id: "second")]
        });
        IRenderedComponent<CardEditor>[] editors = cut.FindComponents<CardEditor>().ToArray();
        IElement firstInput = editors[0].Find("#cardName0");
        Task search = firstInput.InputAsync(new() { Value = "Delayed" });
        await firstInput.BlurAsync(new());
        await editors[1].Find("#cardName1").InputAsync(new() { Value = "Manual offline!" });
        pending.SetResult(new[] { AshBlossom });
        await search;
        Assert.That(editors[0].FindAll(".card-search-result"), Is.Empty);
        Assert.That(editors[1].Find(".card-search-error").TextContent, Does.Contain("manual card"));
        Assert.That(editors[0].Instance.Card.Name, Is.EqualTo("Delayed"));
        Assert.That(editors[1].Instance.Card.Name, Is.EqualTo("Manual offline!"));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards.Select(card => card.Name), Is.EqualTo(new[] { "Delayed", "Manual offline!" }));
    }

    [Test]
    public async Task ManualRenameInvalidatesResultImmediatelyAndPreservesConfigurationThroughBlurEnterAndSave() {
        Card original = new([a], copies: 3, name: "Original", id: "stable", externalCardId: 123,
            drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, preDrawRemovalCount: 1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a], Cards = [original, new([], copies: 37)], Combos = [new([new(a, 1, 5)])], HandSize = 5, DrawEffectsEnabled = false
        });
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        IElement input = editor.Find("#cardName0");
        await input.InputAsync(new() { Value = "Manual final character!" });
        AssertPreviousResult(cut);
        await input.KeyDownAsync(new() { Key = "Enter" });
        await input.BlurAsync(new());
        Card renamed = editor.Instance.Card;
        Assert.That((renamed.Id, renamed.ExternalCardId, renamed.Copies, renamed.Active, renamed.DrawCount,
            renamed.DrawOncePerTurn, renamed.PreDrawRemovalCount, renamed.PostDrawDiscardCount),
            Is.EqualTo(("stable", 123, 3, true, 2, false, 1, 1)));
        Assert.That(renamed.Categories, Is.EqualTo(original.Categories));
        await Button(cut, "Save Session").ClickAsync(new());
        using JsonDocument saved = JsonDocument.Parse(SavedSessionJson());
        Assert.That(saved.RootElement.GetProperty("Cards")[0].GetProperty("Name").GetString(), Is.EqualTo("Manual final character!"));
    }

    [Test]
    public async Task NavigationWrapsAndShortEmptyQueriesClearResultsAndActiveDescendant() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [new([], name: "Pot")] });
        IElement input = cut.Find("#cardName0");
        await input.FocusAsync(new());
        await input.KeyDownAsync(new() { Key = "ArrowUp" });
        Assert.That(input.GetAttribute("aria-activedescendant"), Is.EqualTo($"cardSearchOption0-{PotOfGreedId}"));
        await input.KeyDownAsync(new() { Key = "ArrowDown" });
        Assert.That(cut.Find("[aria-selected='true']").Id, Is.EqualTo(input.GetAttribute("aria-activedescendant")));
        await input.InputAsync(new() { Value = "P" });
        Assert.That(cut.FindAll(".card-search-result"), Is.Empty);
        Assert.That(input.GetAttribute("aria-activedescendant"), Is.Null);
        await input.KeyDownAsync(new() { Key = "Enter" });
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.ExternalCardId, Is.Null);
        await input.InputAsync(new() { Value = "" });
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.Name, Is.Null);
        Assert.That(cut.FindAll(".card-search-status"), Is.Empty);
    }

    [TestCase("new-query")]
    [TestCase("blur")]
    [TestCase("escape")]
    [TestCase("unmount")]
    public async Task DelayedResultsCannotRestoreOldOptionsOrSelectAnOldSuggestion(string action) {
        Mock<ICardInfoService> service = new();
        TaskCompletionSource<IReadOnlyList<CardInfo>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(info => info.SearchCardsAsync("Ash")).ReturnsAsync(new[] { AshBlossom });
        service.Setup(info => info.SearchCardsAsync("Blue")).Returns(pending.Task);
        service.Setup(info => info.SearchCardsAsync("P")).ReturnsAsync(Array.Empty<CardInfo>());
        context.Services.RemoveAll<ICardInfoService>();
        context.Services.AddSingleton(service.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [new([], name: "Ash")] });
        IElement input = cut.Find("#cardName0");
        await input.FocusAsync(new());
        await input.KeyDownAsync(new() { Key = "ArrowDown" });
        Task search = input.InputAsync(new() { Value = "Blue" });
        Assert.That(cut.FindAll(".card-search-result"), Is.Empty);
        await input.KeyDownAsync(new() { Key = "Enter" });
        Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.ExternalCardId, Is.Null);

        switch (action) {
            case "new-query": await input.InputAsync(new() { Value = "P" }); break;
            case "blur": await input.BlurAsync(new()); break;
            case "escape": await input.KeyDownAsync(new() { Key = "Escape" }); break;
            case "unmount": await FirstRowAction(cut, "card", "Remove").ClickAsync(new()); break;
        }
        pending.SetResult(new[] { BlueEyes });
        await search;
        Assert.That(cut.FindAll(".card-search-result"), Is.Empty);
        if (action != "unmount") {
            Assert.That(cut.FindComponents<CardEditor>().Single().Instance.Card.Name, Is.EqualTo(action == "new-query" ? "P" : "Blue"));
        }
    }

    [Test]
    public async Task AddNewCardCanSelectLocalResultAndSaveItWithItsMetadata() {
        CategoryBase userCategory = new("Combo Starter");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [userCategory],
            HandSize = 5
        });

        await AddManualCardAsync(cut);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        Card original = editor.Instance.Card;
        await editor.Find("#cardCopies0").InputAsync(new() { Value = "2" });
        await SetActiveAsync(editor, "#cardActive0", false);
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = false });
        await SaveSimulation(editor);
        await editor.Find("select[id^='cardCategory']").ChangeAsync(new() { Value = userCategory.Identity });
        await Button(editor, "Add").ClickAsync(new());

        IElement nameInput = editor.Find("#cardName0");
        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Ash Blossom" });
        Assert.That(editor.FindAll(".card-search-result").Select(result => result.TextContent.Trim()),
            Does.Contain(AshBlossom.Name));
        await editor.FindAll(".card-search-result").Single(result => result.TextContent.Trim() == AshBlossom.Name).ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.Name, selected.ExternalCardId),
            Is.EqualTo((original.Id, AshBlossom.Name, AshBlossomId)));
        Assert.That((selected.Copies, selected.Active, selected.DrawCount, selected.DrawOncePerTurn),
            Is.EqualTo((2, false, (int?)null, true)));
        Assert.That(selected.Categories, Does.Contain(userCategory));
        Assert.That(selected.Categories.Select(category => category.Name),
            Does.Contain("Tuner Monster").And.Contain("Attribute: FIRE").And.Contain("Monster Type: Zombie")
                .And.Contain("Level 3").And.Contain("Archetype: Floowandereeze"));
        Assert.That(cut.FindComponents<CardArtwork>().All(artwork => artwork.Instance.ExternalCardId == AshBlossomId), Is.True);
        Assert.That(await context.Services.GetRequiredService<ICardArtworkService>().GetArtworkUrlAsync(AshBlossomId),
            Is.EqualTo($"{CardArtworkService.ArtworkOrigin}/small/{AshBlossomId}.jpg"));
        AssertOnlyLocalCatalogWasRequested();

        await Button(cut, "Save Session").ClickAsync(new());
        string saved = SavedSessionJson();
        using JsonDocument document = JsonDocument.Parse(saved);
        Assert.That(document.RootElement.GetProperty("Cards")[0].GetProperty("ExternalCardId").GetInt32(), Is.EqualTo(AshBlossomId));

        Mock<IBrowserFile> file = new();
        file.Setup(browserFile => browserFile.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(saved)));
        await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange
            .InvokeAsync(new InputFileChangeEventArgs([file.Object])));

        Card restored = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((restored.Id, restored.Name, restored.ExternalCardId, restored.Copies, restored.Active,
            restored.DrawCount, restored.DrawOncePerTurn),
            Is.EqualTo((original.Id, AshBlossom.Name, AshBlossomId, 2, false, (int?)null, true)));
        Assert.That(restored.Categories.Select(category => category.Identity),
            Is.EquivalentTo(selected.Categories.Select(category => category.Identity)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingCuratedCardAppliesEditablePresetAndReloadKeepsOverride() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await AddManualCardAsync(cut);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        string cardId = editor.Instance.Card.Id;
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = PotOfGreed.Name });
        await editor.FindAll(".card-search-result").Single(result => result.TextContent.Trim() == PotOfGreed.Name).ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.Name, selected.DrawCount, selected.DrawOncePerTurn),
            Is.EqualTo((cardId, PotOfGreedId, PotOfGreed.Name, 2, false)));
        OpenSimulation(cut.FindComponent<CardEditor>());
        Assert.That(cut.Find("#cardDraw0").GetAttribute("value"), Is.EqualTo("2"));
        OpenSimulation(cut.FindComponent<CardEditor>());
        Assert.That(cut.Find("#cardDrawOnce0").GetAttribute("checked"), Is.Null);

        OpenSimulation(cut.FindComponent<CardEditor>());
        await cut.Find("#cardDraw0").ChangeAsync(new() { Value = string.Empty });
        await SaveSimulation(cut.FindComponent<CardEditor>());
        Card overridden = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((overridden.DrawCount, overridden.DrawOncePerTurn), Is.EqualTo(((int?)null, false)));

        await Button(cut, "Save Session").ClickAsync(new());
        string saved = SavedSessionJson();
        Mock<IBrowserFile> file = new();
        file.Setup(browserFile => browserFile.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(saved)));
        await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange
            .InvokeAsync(new InputFileChangeEventArgs([file.Object])));

        Card restored = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((restored.Id, restored.ExternalCardId, restored.DrawCount, restored.DrawOncePerTurn),
            Is.EqualTo((cardId, PotOfGreedId, (int?)null, false)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingDifferentCuratedCardReplacesCustomizedDrawSettings() {
        Card customized = new([], copies: 2, name: "Manual", active: false, id: "customized", externalCardId: 1234,
            drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.For(new CategoryBase("Attribute: DARK", CategorySource.Metadata, "attribute:dark")),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(new CategoryBase("Level 8", CategorySource.Metadata, "level:8")));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [customized] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Pot of Greed" });
        await editor.FindAll(".card-search-result").Single(result => result.TextContent.Trim() == PotOfGreed.Name).ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.Copies, selected.Active),
            Is.EqualTo(("customized", 2, false)));
        Assert.That((selected.DrawCount, selected.DrawOncePerTurn, selected.PreDrawRemovalCount,
            selected.PreDrawSelector, selected.PostDrawDiscardCount, selected.PostDrawSelector,
            selected.PostDrawFailureBehavior),
            Is.EqualTo(((int?)2, false, 0, CardSelector.Any, 0, CardSelector.Any, PostDrawFailureBehavior.FailBranch)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingUncuratedCardResetsConfiguredDrawSettingsToOrdinaryDefaults() {
        Card configured = new([], name: "Configured", id: "configured", externalCardId: 1234,
            drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2,
            postDrawSelector: CardSelector.For(new CategoryBase("Attribute: DARK", CategorySource.Metadata, "attribute:dark")),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(new CategoryBase("Level 8", CategorySource.Metadata, "level:8")));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [configured] });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Blue-Eyes" });
        await editor.FindAll(".card-search-result").Single(result => result.TextContent.Trim() == BlueEyes.Name).ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.Name, selected.ExternalCardId),
            Is.EqualTo(("configured", BlueEyes.Name, BlueEyesId)));
        Assert.That((selected.DrawCount, selected.DrawOncePerTurn, selected.PreDrawRemovalCount,
            selected.PreDrawSelector, selected.PostDrawDiscardCount, selected.PostDrawSelector,
            selected.PostDrawFailureBehavior),
            Is.EqualTo(((int?)null, true, 0, CardSelector.Any, 0, CardSelector.Any, PostDrawFailureBehavior.FailBranch)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingAllureAfterGracefulCharityReplacesEveryDrawField() {
        Card gracefulCharity = CuratedDrawEffectPresets.CreateImportedCard(GracefulCharity, 1, GracefulCharityId)
            .WithPreDrawRemovalCount(1)
            .WithPreDrawSelector(CardSelector.For(new CategoryBase("Level 8", CategorySource.Metadata, "level:8")));
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [gracefulCharity],
            HandSize = 5
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();

        await SelectCatalogCardAsync(editor, AllureOfDarkness);

        Assert.That(SimulationDetails(editor), Does.Contain("Performance:"));

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.Name),
            Is.EqualTo((gracefulCharity.Id, AllureOfDarknessId, AllureOfDarkness.Name)));
        Assert.That((selected.DrawCount, selected.DrawOncePerTurn, selected.PreDrawRemovalCount,
            selected.PreDrawSelector, selected.PostDrawDiscardCount, selected.PostDrawSelector,
            selected.PostDrawFailureBehavior),
            Is.EqualTo(((int?)2, false, 0, CardSelector.Any, 1,
                CardSelector.For(new CategoryBase("Attribute: DARK", CategorySource.Metadata, "attribute:dark")), PostDrawFailureBehavior.FailBranch)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingGracefulCharityAfterAllureClearsTheDarkSelector() {
        Card allure = CuratedDrawEffectPresets.CreateImportedCard(AllureOfDarkness, 1, AllureOfDarknessId);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [allure],
            HandSize = 5
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        Assert.That(SimulationDetails(editor), Does.Contain("Performance:"));

        await SelectCatalogCardAsync(editor, GracefulCharity);
        Assert.That(editor.FindAll(".simulation-performance, .simulation-details-toggle"), Is.Empty);

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.Name),
            Is.EqualTo((allure.Id, GracefulCharityId, GracefulCharity.Name)));
        Assert.That((selected.DrawCount, selected.DrawOncePerTurn, selected.PreDrawRemovalCount,
            selected.PreDrawSelector, selected.PostDrawDiscardCount, selected.PostDrawSelector,
            selected.PostDrawFailureBehavior),
            Is.EqualTo(((int?)3, false, 0, CardSelector.Any, 2, CardSelector.Any, PostDrawFailureBehavior.FailBranch)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task ReselectingSameCatalogCardPreservesManuallyDisabledDrawEffect() {
        Card manuallyDisabled = new([], name: PotOfGreed.Name, id: "same-card", externalCardId: PotOfGreedId,
            drawCount: null, drawOncePerTurn: false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [manuallyDisabled],
            HandSize = 5
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();

        await SelectCatalogCardAsync(editor, PotOfGreed);

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.DrawCount, selected.DrawOncePerTurn),
            Is.EqualTo(("same-card", PotOfGreedId, (int?)null, false)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task ManualCardWithSameNameAsCatalogCardIsTreatedAsANewIdentity() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await AddManualCardAsync(cut);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        OpenSimulation(editor);
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "1" });
        await SaveSimulation(editor);
        OpenSimulation(editor);
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = false });
        await SaveSimulation(editor);
        Card manual = editor.Instance.Card;
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = PotOfGreed.Name });
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        await editor.FindAll(".card-search-result")
            .Single(result => result.TextContent.Trim() == PotOfGreed.Name)
            .ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.Name),
            Is.EqualTo((manual.Id, PotOfGreedId, PotOfGreed.Name)));
        Assert.That((selected.DrawCount, selected.DrawOncePerTurn, selected.PreDrawRemovalCount,
            selected.PreDrawSelector, selected.PostDrawDiscardCount, selected.PostDrawSelector),
            Is.EqualTo(((int?)2, false, 0, CardSelector.Any, 0, CardSelector.Any)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task RenamingAndEditingCopiesAndCategoriesDoesNotResetDrawConfiguration() {
        CategoryBase userCategory = new("Combo Starter");
        Card manuallyDisabled = CuratedDrawEffectPresets.CreateImportedCard(PotOfGreed, 1, PotOfGreedId)
            .WithDrawCount(null)
            .WithDrawOncePerTurn(false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [userCategory],
            Cards = [manuallyDisabled],
            HandSize = 5
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Personal label" });
        await editor.Find("#cardCopies0").InputAsync(new() { Value = "2" });
        await editor.Find("select[id^='cardCategory']").ChangeAsync(new() { Value = userCategory.Identity });
        await Button(editor, "Add").ClickAsync(new());

        Card edited = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((edited.Name, edited.ExternalCardId, edited.Copies, edited.DrawCount, edited.DrawOncePerTurn),
            Is.EqualTo(("Personal label", PotOfGreedId, 2, (int?)null, false)));
        Assert.That(edited.Categories, Does.Contain(userCategory));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task SelectingDifferentCardReplacesObjectivePropertiesAndPreservesManualOverrides() {
        CategoryBase userCategory = new("Custom Route");
        CategoryBase oldObjective = new("Rank 4", CategorySource.Metadata, "rank:4");
        CategoryBase manualDark = new("Attribute: DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase overlappingManualFire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        Card original = new([userCategory, oldObjective, manualDark, overlappingManualFire], 3, "Old Card", true,
            "stable-card-id", 1234, [manualDark.MetadataKey!, overlappingManualFire.MetadataKey!], 1, false);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [userCategory],
            Cards = [original],
            HandSize = 5
        });
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");
        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Ash Blossom" });
        await editor.FindAll(".card-search-result").Single(result => result.TextContent.Trim() == AshBlossom.Name).ClickAsync(new());

        Card selected = cut.FindComponents<CardEditor>().Single().Instance.Card;
        Assert.That((selected.Id, selected.ExternalCardId, selected.Name, selected.Copies, selected.DrawCount, selected.DrawOncePerTurn),
            Is.EqualTo(("stable-card-id", AshBlossomId, AshBlossom.Name, 3, (int?)null, true)));
        Assert.That(selected.Categories, Does.Contain(userCategory).And.Contain(manualDark));
        Assert.That(selected.Categories, Does.Not.Contain(oldObjective));
        Assert.That(selected.Categories, Does.Contain(overlappingManualFire));
        Assert.That(selected.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { manualDark.MetadataKey }));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task TypingAnExactDatabaseNameDoesNotBindWithoutSelectingAndEscapeKeepsText() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await AddManualCardAsync(cut);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = PotOfGreed.Name });
        Assert.That(editor.Instance.Card.Name, Is.EqualTo(PotOfGreed.Name));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        Assert.That((editor.Instance.Card.DrawCount, editor.Instance.Card.DrawOncePerTurn), Is.EqualTo(((int?)null, true)));
        Assert.That(editor.FindAll(".card-search-result").Select(result => result.TextContent.Trim()),
            Does.Contain(PotOfGreed.Name));

        await nameInput.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".card-search-results"), Is.Empty);
        Assert.That(editor.Instance.Card.Name, Is.EqualTo(PotOfGreed.Name));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        Assert.That((editor.Instance.Card.DrawCount, editor.Instance.Card.DrawOncePerTurn), Is.EqualTo(((int?)null, true)));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task KeyboardSelectionAfterReorderingUpdatesTheSameStableCard() {
        CategoryBase existingMetadata = new("Existing metadata", CategorySource.Metadata, "existing:metadata");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Cards = [
                new([existingMetadata], name: "First", id: "first-card"),
                new([existingMetadata], name: "Second", id: "second-card")
            ],
            HandSize = 5
        });
        await RowAction(cut, "card Second, row 2", "Move up").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single(item => item.Instance.Card.Id == "second-card");
        Assert.That(editor.Instance.Index, Is.Zero);

        IElement nameInput = editor.Find("#cardName0");
        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = "Blue-Eyes" });
        await nameInput.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.That(editor.FindAll(".card-search-result").Single(result => result.ClassList.Contains("active"))
            .GetAttribute("aria-selected"), Is.EqualTo("true"));
        await nameInput.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Card second = cut.FindComponents<CardEditor>().Single(item => item.Instance.Card.Id == "second-card").Instance.Card;
        Card first = cut.FindComponents<CardEditor>().Single(item => item.Instance.Card.Id == "first-card").Instance.Card;
        Assert.That((second.Id, second.Name, second.ExternalCardId), Is.EqualTo(("second-card", BlueEyes.Name, BlueEyesId)));
        Assert.That(first.ExternalCardId, Is.Null);
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task LateCatalogLoadShowsOnlyResultsForTheNewestInput() {
        DelayedSearchCatalogHandler delayed = new(CatalogJson());
        RegisterCardInfoService(delayed);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState { Cards = [new([])] });
        await cut.FindComponent<CardEditor>().Find(".accordion-button").ClickAsync(new());
        IElement nameInput = cut.Find("#cardName0");

        Task focus = nameInput.FocusAsync(new FocusEventArgs());
        Task oldSearch = nameInput.InputAsync(new() { Value = "Ash Blossom" });
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task newSearch = nameInput.InputAsync(new() { Value = "Blue-Eyes" });
        delayed.Release();
        await Task.WhenAll(focus, oldSearch, newSearch);

        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        Assert.That(editor.FindAll(".card-search-result").Select(result => result.TextContent.Trim()),
            Is.EqualTo(new[] { BlueEyes.Name }));
        Assert.That(editor.Instance.Card.Name, Is.EqualTo("Blue-Eyes"));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
    }

    private void RegisterCardInfoService(SearchCatalogHandler handler) {
        _handler = handler;
        HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new EmptyLocalStorage(), client);
        context.Services.RemoveAll<ICardInfoService>();
        context.Services.RemoveAll<ICardArtworkService>();
        context.Services.AddSingleton<ICardInfoService>(service);
        context.Services.AddSingleton<ICardArtworkService>(new CardArtworkService(service));
    }

    private static async Task SelectCatalogCardAsync(IRenderedComponent<CardEditor> editor, CardInfo selectedCard) {
        IElement nameInput = editor.Find("#cardName0");
        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = selectedCard.Name });
        await editor.FindAll(".card-search-result")
            .Single(result => result.TextContent.Trim() == selectedCard.Name)
            .ClickAsync(new());
    }

    private void RegisterCardInfoService(DelayedSearchCatalogHandler handler) {
        HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new EmptyLocalStorage(), client);
        context.Services.RemoveAll<ICardInfoService>();
        context.Services.RemoveAll<ICardArtworkService>();
        context.Services.AddSingleton<ICardInfoService>(service);
        context.Services.AddSingleton<ICardArtworkService>(new CardArtworkService(service));
    }

    private void AssertOnlyLocalCatalogWasRequested() {
        Assert.That(_handler.Requests, Has.Count.EqualTo(1),
            string.Join(Environment.NewLine, _handler.Requests.Select(request => request.AbsoluteUri)));
        Assert.That(_handler.Requests.Single().AbsoluteUri, Does.StartWith("https://calculator.test/data/card-catalog.v1.json"));
    }

    private static string CatalogJson() => JsonSerializer.Serialize(
        new { version = 1, cards = new[] { AshBlossom, BlueEyes, PotOfGreed, AllureOfDarkness, GracefulCharity } },
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class SearchCatalogHandler : HttpMessageHandler {
        private readonly string _catalogJson = CatalogJson();
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Uri address = request.RequestUri!;
            Requests.Add(address);
            HttpResponseMessage response = address.AbsolutePath.EndsWith("card-catalog.v1.json", StringComparison.Ordinal)
                ? new(HttpStatusCode.OK) { Content = new StringContent(_catalogJson, Encoding.UTF8, "application/json") }
                : new(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedSearchCatalogHandler(string catalogJson) : HttpMessageHandler {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Started.TrySetResult();
            return await _response.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _response.TrySetResult(new(HttpStatusCode.OK) {
            Content = new StringContent(catalogJson, Encoding.UTF8, "application/json")
        });
    }

    private sealed class EmptyLocalStorage : ILocalStorageService {
        public Task<T?> GetItemAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task<string?> GetRawItemAsync(string key) => Task.FromResult<string?>(null);
        public Task SetItemAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
