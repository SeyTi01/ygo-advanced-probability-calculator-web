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

    private SearchCatalogHandler _handler = null!;

    [SetUp]
    public void UseLocalSearchCatalog() => RegisterCardInfoService(new SearchCatalogHandler());

    [Test]
    public async Task AddNewCardCanSelectLocalResultAndSaveItWithItsMetadata() {
        CategoryBase userCategory = new("Combo Starter");
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [userCategory],
            HandSize = 5
        });

        await Button(cut, "Add New Card").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        Card original = editor.Instance.Card;
        await editor.Find("#cardCopies0").InputAsync(new() { Value = "2" });
        await editor.Find("#cardActive0").ChangeAsync(new() { Value = false });
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        await editor.Find("#cardDrawOnce0").ChangeAsync(new() { Value = false });
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
            Is.EqualTo((2, false, 2, false)));
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
            Is.EqualTo((original.Id, AshBlossom.Name, AshBlossomId, 2, false, 2, false)));
        Assert.That(restored.Categories.Select(category => category.Identity),
            Is.EquivalentTo(selected.Categories.Select(category => category.Identity)));
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
            Is.EqualTo(("stable-card-id", AshBlossomId, AshBlossom.Name, 3, 1, false)));
        Assert.That(selected.Categories, Does.Contain(userCategory).And.Contain(manualDark));
        Assert.That(selected.Categories, Does.Not.Contain(oldObjective));
        Assert.That(selected.Categories, Does.Contain(overlappingManualFire));
        Assert.That(selected.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { manualDark.MetadataKey }));
        AssertOnlyLocalCatalogWasRequested();
    }

    [Test]
    public async Task TypingAnExactDatabaseNameDoesNotBindWithoutSelectingAndEscapeKeepsText() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Button(cut, "Add New Card").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>().Single();
        IElement nameInput = editor.Find("#cardName0");

        await nameInput.FocusAsync(new FocusEventArgs());
        await nameInput.InputAsync(new() { Value = AshBlossom.Name });
        Assert.That(editor.Instance.Card.Name, Is.EqualTo(AshBlossom.Name));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        Assert.That(editor.FindAll(".card-search-result").Select(result => result.TextContent.Trim()),
            Does.Contain(AshBlossom.Name));

        await nameInput.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(editor.FindAll(".card-search-results"), Is.Empty);
        Assert.That(editor.Instance.Card.Name, Is.EqualTo(AshBlossom.Name));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
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
        await cut.Find("[aria-label='Move card Second, row 2 up']").ClickAsync(new());
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
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Button(cut, "Add New Card").ClickAsync(new());
        IElement nameInput = cut.Find("#cardName0");

        Task focus = nameInput.FocusAsync(new FocusEventArgs());
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task oldSearch = nameInput.InputAsync(new() { Value = "Ash Blossom" });
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
        new { version = 1, cards = new[] { AshBlossom, BlueEyes } },
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
