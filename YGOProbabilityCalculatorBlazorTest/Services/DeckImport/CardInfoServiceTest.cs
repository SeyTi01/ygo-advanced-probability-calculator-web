using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardInfoServiceTests {
    [Test]
    [SetUICulture("fr")]
    public async Task ArtworkSharesPendingCatalogWithoutWaitingForLocalizedNames() {
        TaskCompletionSource<HttpResponseMessage> pendingCatalog = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<HttpResponseMessage> pendingNames = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
            ? pendingNames.Task : pendingCatalog.Task);
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        TestLocalStorage storage = new(null);
        CardInfoService service = new(storage, client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });

        Task<IReadOnlyList<CardInfo>> catalog = service.GetCatalogCardsAsync();
        Task<CardInfo> artwork = service.GetCardArtworkInfoAsync(1);
        Task display = service.EnsureLocalizedCardNamesAsync([new Card([], name: "Card", externalCardId: 1)]);
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        pendingCatalog.SetResult(Response(HttpStatusCode.OK, CreateLocalCatalogJson([
            new CardInfo { Id = 1, Name = "Card", ArtworkMetadataKnown = true, ArtworkImageIds = [1] }
        ])));

        Assert.That(await artwork.WaitAsync(TimeSpan.FromSeconds(1)), Is.SameAs((await catalog).Single()));
        Assert.That(service.GetLocalizedCardName(await artwork), Is.Null);
        pendingNames.SetResult(Response(HttpStatusCode.OK, """{"version":1,"locale":"fr","names":[[1,"Label"]]}"""));
        await display;
        Assert.That(service.GetLocalizedCardName(await artwork), Is.EqualTo("Label"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    [SetUICulture("fr-CA")]
    public async Task SavedCardDisplayVerifiesIdentityAndNameWithoutChangingTheSession() {
        CardInfo[] catalogCards = [
            new() { Id = 35261759, CanonicalCardId = 35261759, Name = "Pot of Desires", Type = "Spell Card",
                ArtworkMetadataKnown = true, ArtworkImageIds = [35261759, 91, 91, 99, 2], ResolvedArchetypes = [] },
            new() { Id = 2, Name = "Other", ArtworkMetadataKnown = true, ArtworkImageIds = [2, 99] },
            new() { Id = 3, Name = "Missing", ArtworkMetadataKnown = true }
        ];
        TaskCompletionSource<HttpResponseMessage> pendingNames = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
            ? pendingNames.Task : Task.FromResult(Response(HttpStatusCode.OK, CreateLocalCatalogJson(catalogCards))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        CategoryBase customCategory = new("My starter");
        Card canonical = new([customCategory], copies: 2, name: "Pot of Desires", externalCardId: 35261759,
            drawCount: 2, preDrawDeckRemovalCount: 10);
        Card artwork = new([], name: "Pot of Desires", externalCardId: 91);
        Card customName = new([], name: "My favourite Pot", externalCardId: 35261759);
        Card manual = new([], name: "Pot of Desires");
        Card ambiguousArtwork = new([], name: "Pot of Desires", externalCardId: 99);
        Card unknown = new([], name: "Pot of Desires", externalCardId: 123);
        Card changedCase = new([], name: "pot of desires", externalCardId: 35261759);
        Card wrongArtworkOwner = new([], name: "Pot of Desires", externalCardId: 2);
        Card otherCanonical = new([], name: "Other", externalCardId: 2);
        Card missingTranslation = new([], name: "Missing", externalCardId: 3);
        SessionState session = new() { Cards = [canonical, artwork, customName, manual], Categories = [customCategory] };
        YGOProbabilityCalculatorBlazor.Services.Session.SessionService codec = new(
            Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string before = codec.SerializeSession(session);

        Assert.That(service.GetLocalizedCardName(canonical), Is.Null);
        await service.EnsureLocalizedCardNamesAsync([]);
        await service.EnsureLocalizedCardNamesAsync([manual, new Card([], name: " ", externalCardId: 35261759)]);
        Assert.That(service.GetLocalizedCardName(manual), Is.Null);
        Assert.That(handler.Requests, Is.Empty, "manual names and synchronous reads must not start a request");
        await service.GetCatalogCardsAsync();
        await service.EnsureLocalizedCardNamesAsync([customName, ambiguousArtwork, unknown, changedCase, wrongArtworkOwner]);
        Assert.That(service.GetLocalizedCardName(customName), Is.Null);
        Assert.That(service.GetLocalizedCardName(ambiguousArtwork), Is.Null,
            "ambiguous artwork cannot borrow a canonical name");
        Assert.That(service.GetLocalizedCardName(unknown), Is.Null);
        Assert.That(service.GetLocalizedCardName(changedCase), Is.Null,
            "case changes may be authored and must remain verbatim");
        Assert.That(service.GetLocalizedCardName(wrongArtworkOwner), Is.Null,
            "an exact canonical ID takes precedence over another card's artwork alias");
        Assert.That(handler.Requests, Has.Length.EqualTo(1), "unverified/custom names do not demand-load localized names");

        Task display = service.EnsureLocalizedCardNamesAsync([canonical, artwork, otherCanonical, missingTranslation]);
        Task<IReadOnlyList<CardInfo>> search = service.SearchCardsAsync("vessel");
        pendingNames.SetResult(Response(HttpStatusCode.OK, """{"version":1,"locale":"fr","names":[[35261759,"⟦Desire vessel⟧"],[2,"Other label"]]}"""));
        await display;
        Assert.That(service.GetLocalizedCardName(canonical), Is.EqualTo("⟦Desire vessel⟧"));
        Assert.That(service.GetLocalizedCardName(canonical.WithCategories([new("Rule membership")])), Is.EqualTo("⟦Desire vessel⟧"),
            "category-rule materialization must retain the verified display name");
        Assert.That(service.GetLocalizedCardName(artwork), Is.EqualTo("⟦Desire vessel⟧"),
            "repeated artwork IDs on one canonical record must not count as multiple owners");
        Assert.That(service.GetLocalizedCardName(otherCanonical), Is.EqualTo("Other label"));
        Assert.That(service.GetLocalizedCardName(wrongArtworkOwner), Is.Null);
        Assert.That(service.GetLocalizedCardName(ambiguousArtwork), Is.Null);
        Assert.That(service.GetLocalizedCardName(customName), Is.Null);
        Assert.That(service.GetLocalizedCardName(manual), Is.Null);
        Assert.That(service.GetLocalizedCardName(missingTranslation), Is.Null,
            "a verified card without a supplied Localized name keeps its stored English name");
        Assert.That(service.GetLocalizedCardName((await search).Single()), Is.EqualTo("⟦Desire vessel⟧"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2), "saved display and search share the same name request");
        Assert.That(codec.SerializeSession(session), Is.EqualTo(before));
        Assert.That(canonical.Name, Is.EqualTo("Pot of Desires"));
        Assert.That(canonical.Categories.Single(), Is.SameAs(customCategory));
    }

    [Test]
    [SetUICulture("fr")]
    public async Task ColdSavedCardDisplayStartsCatalogAndNamesTogetherAndSharesThemWithSearch() {
        TaskCompletionSource<HttpResponseMessage> pendingCatalog = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<HttpResponseMessage> pendingNames = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
            ? pendingNames.Task : pendingCatalog.Task);
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        Card card = new([], name: "Pot of Desires", externalCardId: 35261759);

        Task display = service.EnsureLocalizedCardNamesAsync([card]);
        Assert.That(handler.Requests.Select(url => new Uri(url).AbsolutePath), Is.EquivalentTo(new[] {
            "/data/card-catalog.v1.json", "/data/card-names.fr.test.json"
        }), "both requests must start while the controlled catalog response remains pending");
        Task<IReadOnlyList<CardInfo>> search = service.SearchCardsAsync("vessel");
        Task repeatedDisplay = service.EnsureLocalizedCardNamesAsync([card]);
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        pendingNames.SetResult(Response(HttpStatusCode.OK,
            """{"version":1,"locale":"fr","names":[[35261759,"⟦Desire vessel⟧"]]}"""));
        pendingCatalog.SetResult(Response(HttpStatusCode.OK, CreateLocalCatalogJson([
            new CardInfo { Id = 35261759, Name = "Pot of Desires", ArtworkMetadataKnown = true }
        ])));
        await Task.WhenAll(display, repeatedDisplay);

        Assert.That(service.GetLocalizedCardName(card), Is.EqualTo("⟦Desire vessel⟧"));
        Assert.That((await search).Single().Id, Is.EqualTo(35261759));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [Test]
    [SetUICulture("en")]
    public async Task EnglishSavedCardDisplayDoesNotLoadCatalogOrNames() {
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        Card card = new([], name: "Pot of Desires", externalCardId: 35261759);
        await service.EnsureLocalizedCardNamesAsync([card]);
        Assert.That(service.GetLocalizedCardName(card), Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    [SetUICulture("fr")]
    public async Task SavedCardDisplayFailurePreservesTheNameWithoutExternalEnrichment(bool catalogUnavailable) {
        CardInfo[] cards = [new() { Id = 35261759, Name = "Pot of Desires", ArtworkMetadataKnown = true }];
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            catalogUnavailable || request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
                ? Response(HttpStatusCode.ServiceUnavailable, "{}") : Response(HttpStatusCode.OK, CreateLocalCatalogJson(cards))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        TestLocalStorage storage = new(null);
        CardInfoService service = new(storage, client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        Card card = new([], name: "Pot of Desires", externalCardId: 35261759);
        await service.EnsureLocalizedCardNamesAsync([card]);
        await service.EnsureLocalizedCardNamesAsync([card]);
        Assert.That(service.GetLocalizedCardName(card), Is.Null);
        Assert.That(card.Name, Is.EqualTo("Pot of Desires"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        Assert.That(handler.Requests.All(url => url.StartsWith("https://calculator.test/data/", StringComparison.Ordinal)), Is.True);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    [SetUICulture("fr-CA")]
    public async Task LocalizedSearchSharesDemandLoadAndKeepsCanonicalNamesMetadataAndEffects() {
        CardInfo[] cards = [
            new() { Id = 35261759, CanonicalCardId = 35261759, Name = "Pot of Desires", Type = "Spell Card",
                ArtworkMetadataKnown = true, ArtworkImageIds = [35261759, 99], ResolvedArchetypes = [] },
            new() { Id = 2, Name = "White Dragon", Type = "Effect Monster", ArtworkMetadataKnown = true,
                ArtworkImageIds = [2, 99], ResolvedArchetypes = ["Blue-Eyes"] },
            new() { Id = 3, Name = "Another White Dragon", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 4, Name = "Missing Translation", Type = "Spell Card", ArtworkMetadataKnown = true }
        ];
        string catalog = JsonSerializer.Serialize(new { version = 2, cards }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        TaskCompletionSource<HttpResponseMessage> pendingNames = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
            ? pendingNames.Task : Task.FromResult(Response(HttpStatusCode.OK, catalog)));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });

        Assert.That(service.GetLocalizedCardName(cards[0]), Is.Null);
        Assert.That(await service.SearchCardsAsync(" T "), Is.Empty);
        await service.GetCatalogCardsAsync();
        Assert.That(handler.Requests, Has.Length.EqualTo(1), "catalog/import use must not load names");
        Task<IReadOnlyList<CardInfo>> localizedSearch = service.SearchCardsAsync("⟦Desire vessel⟧");
        Task<IReadOnlyList<CardInfo>> englishSearch = service.SearchCardsAsync("Pot of Desires");
        pendingNames.SetResult(Response(HttpStatusCode.OK, """
            {"version":1,"locale":"fr","names":[[35261759,"⟦Desire vessel⟧"],[2,"⟦White drágon⟧"],
             [3,"⟦White drágon⟧"],[99,"Alias label"],[123,"Unknown label"]]}
            """));
        CardInfo localized = (await localizedSearch).Single();
        CardInfo english = (await englishSearch).Single();
        Assert.That(localized, Is.SameAs(english));
        Assert.That(localized.Name, Is.EqualTo("Pot of Desires"));
        Assert.That(service.GetLocalizedCardName(localized), Is.EqualTo("⟦Desire vessel⟧"));
        Assert.That(service.GetLocalizedCardName(localized with { }), Is.Null, "an unverified clone/ID cannot acquire a data label");
        Assert.That(service.GetLocalizedCardName(new CardInfo { Id = 99, Name = "Artwork" }), Is.Null);
        Card imported = CuratedDrawEffectPresets.CreateImportedCard(localized, 1, localized.Id);
        Assert.That(imported.Name, Is.EqualTo("Pot of Desires"));
        Assert.That((imported.DrawCount, imported.PreDrawDeckRemovalCount), Is.EqualTo(((int?)2, 10)));
        Assert.That((await service.SearchCardsAsync("vessel")).Select(card => card.Id), Is.EqualTo(new[] { 35261759 }));
        Assert.That((await service.SearchCardsAsync("WHITE DRÁGON")).Select(card => card.Id), Is.EqualTo(new[] { 3, 2 }));
        Assert.That((await service.SearchCardsAsync("White Dragon")).Select(card => card.Id), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(await service.SearchCardsAsync("⟦White dragon⟧"), Is.Empty, "do not fold distinct spellings or IDs");
        Assert.That(await service.SearchCardsAsync("Alias label"), Is.Empty, "artwork aliases never join names");
        Assert.That(await service.SearchCardsAsync("Unknown label"), Is.Empty);
        Assert.That((await service.SearchCardsAsync("Missing")).Single().Name, Is.EqualTo("Missing Translation"));
        Assert.That((await service.SearchCardsAsync("White Dragon")).Single(card => card.Id == 2).ResolvedArchetypes,
            Is.EqualTo(new[] { "Blue-Eyes" }));
        Assert.That(handler.Requests, Has.Length.EqualTo(2), "both overlapping and warm searches share the name request");
    }

    [TestCase(null)]
    [TestCase("{bad json")]
    [TestCase("{\"version\":1,\"locale\":\"xx\",\"names\":[[1,\"Æther\"]]}")]
    [TestCase("{\"version\":1,\"locale\":\"fr\",\"names\":[[1,\"Æther\"],[1,\"Different\"]]}")]
    [SetUICulture("fr")]
    public async Task FailedLocalizedNamesKeepEnglishSearchAndCacheFailure(string? names) {
        CardInfo[] cards = [new() { Id = 1, Name = "Aether", ArtworkMetadataKnown = true }];
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json")
                ? Response(names is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, names ?? "")
                : Response(HttpStatusCode.OK, CreateLocalCatalogJson(cards))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        Assert.That((await service.SearchCardsAsync("Aether")).Single().Id, Is.EqualTo(1));
        Assert.That((await service.SearchCardsAsync("Aeth")).Single().Name, Is.EqualTo("Aether"));
        Assert.That(service.GetLocalizedCardName(cards[0]), Is.Null);
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [Test]
    [SetUICulture("fr")]
    public async Task LocalizedSearchRanksBothLanguagesAndCapsDeduplicatedCanonicalResults() {
        CardInfo[] cards = Enumerable.Range(1, 30).Select(index => new CardInfo {
            Id = index, Name = $"Æther {index:00}", ArtworkMetadataKnown = true
        }).ToArray();
        string names = JsonSerializer.Serialize(new {
            version = 1, locale = "fr", names = cards.Select(card => new object[] { card.Id, card.Name })
        });
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(Response(HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath.EndsWith("card-names.fr.test.json") ? names : CreateLocalCatalogJson(cards))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, null, new Dictionary<string, string> { ["fr"] = "data/card-names.fr.test.json" });
        Assert.That((await service.SearchCardsAsync("æTH")).Select(card => card.Id), Is.EqualTo(Enumerable.Range(1, 20)));
        Assert.That((await service.SearchCardsAsync("æther 29")).Single().Id, Is.EqualTo(29));
    }

    [Test]
    public async Task VerifiedCatalogMembershipWinsForSearchImportCanonicalAndArtworkIds() {
        CardInfo[] cards = [
            new() { Id = 691925, CanonicalCardId = 691925, Name = "Solar Recharge", Type = "Spell Card",
                ArtworkMetadataKnown = true, ResolvedArchetypes = [], Archetype = "Lightsworn" },
            new() { Id = 70781052, CanonicalCardId = 70781052, Name = "Summoned Skull", Type = "Normal Monster",
                ArtworkMetadataKnown = true, ArtworkImageIds = [70781052, 70781053], ResolvedArchetypes = ["Archfiend"] }
        ];
        string json = JsonSerializer.Serialize(new { version = 2, cards }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, json)));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client);
        CardInfo solar = (await service.SearchCardsAsync("Solar Recharge")).Single();
        Assert.That(CardPropertyProvider.GetCategories(solar).Any(category => category.MetadataKey == "archetype:lightsworn"), Is.False);
        Assert.That((await service.GetCardInfoAsync(691925)).ResolvedArchetypes, Is.Empty);
        CardInfo artwork = await service.GetCardInfoAsync(70781053);
        Assert.That((artwork.Id, artwork.CanonicalCardId), Is.EqualTo((70781053, (int?)70781052)));
        Assert.That(artwork.ResolvedArchetypes, Is.EqualTo(new[] { "Archfiend" }));
        Card imported = CuratedDrawEffectPresets.CreateImportedCard(artwork, 2, artwork.Id);
        Assert.That(imported.Categories.Select(category => category.MetadataKey), Does.Contain("archetype:archfiend"));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Summoned Skull"]))["Summoned Skull"].ResolvedArchetypes,
            Is.EqualTo(new[] { "Archfiend" }));
        Assert.That(await service.GetCatalogCardsAsync(), Has.Count.EqualTo(2));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LiveAndHistoricalCacheFallbackDoNotTrustAssociationOrStoredResolvedClaims(bool offline) {
        string cached = """
            {"schemaVersion":2,"cards":{"691925":{"id":691925,"name":"Solar Recharge","type":"Spell Card",
             "archetype":"Lightsworn","resolvedArchetypes":["Lightsworn"]}}}
            """;
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(offline
            ? Response(HttpStatusCode.ServiceUnavailable, "{}")
            : Response(HttpStatusCode.OK, """{"data":[{"id":691925,"name":"Solar Recharge","type":"Spell Card","archetype":"Lightsworn"}]}""")));
        using HttpClient client = new(handler);
        CardInfoService service = new(new TestLocalStorage(cached), client);
        CardInfo card = await service.GetCardInfoAsync(691925);
        Assert.That(card.ResolvedArchetypes, Is.Null);
        Assert.That(CardPropertyProvider.GetCategories(card).Select(category => category.MetadataKey), Does.Not.Contain("archetype:lightsworn"));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Solar Recharge"]))["Solar Recharge"].ResolvedArchetypes, Is.Null);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BoundedSearchMatchesFullSortForCaseTiesAndAllRanks() {
        CardInfo[] cards = Enumerable.Range(1, 200).Select(index => new CardInfo {
            Id = index,
            Name = (index % 4) switch {
                0 => "FIRE",
                1 => "fire",
                2 => $"Fire collector {index % 9}",
                _ => $"The fire collector {index % 7}"
            },
            ArtworkMetadataKnown = true
        }).Reverse().ToArray();
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, CreateLocalCatalogJson(cards))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client);

        foreach (string query in new[] { "FiRe", "collector", "collector 2", "absent" }) {
            IEnumerable<int> expected = cards
                .Where(card => card.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(card => card.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
                    : card.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.Name, StringComparer.Ordinal)
                .ThenBy(card => card.Id)
                .Take(20)
                .Select(card => card.Id);
            Assert.That((await service.SearchCardsAsync(query)).Select(card => card.Id), Is.EqualTo(expected));
        }

        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task ShortQueriesDoNotLoadUnavailableCatalogAndFailureNeverFallsBackToExternalSearch() {
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client);

        Assert.That(await service.SearchCardsAsync("  "), Is.Empty);
        Assert.That(await service.SearchCardsAsync(" A "), Is.Empty);
        Assert.That(handler.Requests, Is.Empty);
        Assert.ThrowsAsync<HttpRequestException>(async () => await service.SearchCardsAsync("Ash"));
        Assert.ThrowsAsync<HttpRequestException>(async () => await service.SearchCardsAsync("Blue"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests.Single(), Does.Contain("calculator.test/data/card-catalog.v1.json"));
    }

    [Test]
    public async Task LocalSearchRanksExactPrefixAndSubstringMatchesAndLoadsOnceWithoutApiRequests() {
        CardInfo[] cards = [
            new() { Id = 4, Name = "Legendary Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 3, Name = "Ash Blossom & Joyous Spring (Promo)", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 9, Name = "Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 2, Name = "Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            .. Enumerable.Range(10, 25).Select(index => new CardInfo {
                Id = index,
                Name = $"Fire Collector {index:00}",
                Type = "Effect Monster",
                ArtworkMetadataKnown = true
            })
        ];
        string catalogJson = CreateLocalCatalogJson(cards);
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("card-catalog.v1.json", StringComparison.Ordinal)
                ? Response(HttpStatusCode.OK, catalogJson)
                : Response(HttpStatusCode.NotFound, "{}")));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        Assert.That(await service.SearchCardsAsync("  "), Is.Empty);
        Assert.That(await service.SearchCardsAsync(" A "), Is.Empty);
        IReadOnlyList<CardInfo> ranked = await service.SearchCardsAsync("  aSh Blossom & Joyous Spring  ");
        Assert.That(ranked.Select(card => card.Id), Is.EqualTo(new[] { 2, 9, 3, 4 }));
        IReadOnlyList<CardInfo> capped = await service.SearchCardsAsync("Fire");
        Assert.That(capped, Has.Count.EqualTo(20));
        string[] sortedNames = capped.Select(card => card.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.That(capped.Select(card => card.Name).ToArray(), Is.EqualTo(sortedNames));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests.Single(), Does.StartWith("https://calculator.test/data/card-catalog.v1.json"));
        Assert.That(handler.Requests.Any(request => request.Contains("ygoprodeck", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task LocalSearchReturnsCompleteCardInfoAndArtworkWithoutAnotherRequest() {
        CardInfo expected = new() {
            Id = 14558127,
            Name = "Ash Blossom & Joyous Spring",
            Type = "XYZ Pendulum Effect Monster",
            FrameType = "xyz_pendulum",
            Race = "Zombie",
            Attribute = "FIRE",
            Level = 4,
            LinkVal = 2,
            Scale = 8,
            Archetype = "Floowandereeze",
            CanonicalCardId = 14558127,
            ArtworkImageIds = [14558127, 999999],
            ArtworkMetadataKnown = true
        };
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            CreateLocalCatalogJson([expected]))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        CardInfo selected = (await service.SearchCardsAsync("Ash Blossom")).Single();
        Assert.That((selected.Id, selected.Name, selected.Type, selected.FrameType, selected.Race, selected.Attribute,
            selected.Level, selected.LinkVal, selected.Scale, selected.Archetype, selected.CanonicalCardId,
            selected.ArtworkMetadataKnown),
            Is.EqualTo((expected.Id, expected.Name, expected.Type, expected.FrameType, expected.Race, expected.Attribute,
                expected.Level, expected.LinkVal, expected.Scale, expected.Archetype, expected.CanonicalCardId,
                expected.ArtworkMetadataKnown)));
        Assert.That(selected.ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That(CardPropertyProvider.GetCategories(selected).Select(category => category.Name),
            Does.Contain("Xyz Monster").And.Contain("Pendulum Monster").And.Contain("Rank 4")
                .And.Contain("Pendulum Scale 8").And.Contain("Attribute: FIRE").And.Contain("Monster Type: Zombie"));
        CardInfo loaded = await service.GetCardInfoAsync(expected.Id);
        Assert.That((loaded.Id, loaded.Name, loaded.Type, loaded.FrameType, loaded.Race, loaded.Attribute,
            loaded.Level, loaded.LinkVal, loaded.Scale, loaded.Archetype, loaded.CanonicalCardId,
            loaded.ArtworkMetadataKnown),
            Is.EqualTo((expected.Id, expected.Name, expected.Type, expected.FrameType, expected.Race, expected.Attribute,
                expected.Level, expected.LinkVal, expected.Scale, expected.Archetype, expected.CanonicalCardId,
                expected.ArtworkMetadataKnown)));
        Assert.That(loaded.ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That((await service.GetCardArtworkInfoAsync(expected.Id)).ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task IdOutsideLocalCatalogUsesOnlyTargetedMetadataFallback() {
        string catalogJson = CreateLocalCatalogJson([
            new CardInfo { Id = 123, Name = "Local", Type = "Spell Card", ArtworkMetadataKnown = true }
        ]);
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("card-catalog.v1.json", StringComparison.Ordinal)
                ? Response(HttpStatusCode.OK, catalogJson)
                : Response(HttpStatusCode.OK, "{\"data\":[{\"id\":987654,\"name\":\"Fallback\",\"type\":\"Effect Monster\"}]}")));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        CardInfo info = await service.GetCardInfoAsync(987654);
        Assert.That(info.Name, Is.EqualTo("Fallback"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        Assert.That(handler.Requests[0], Does.Contain("card-catalog.v1.json"));
        Assert.That(handler.Requests[1], Does.EndWith("cardinfo.php?id=987654"));
        Assert.That(handler.Requests.Any(request => request.EndsWith("cardinfo.php", StringComparison.Ordinal)), Is.False);
    }

    private const string ArtworkCardJson = """
        {"id":1234,"name":"Art","type":"Effect Monster","card_images":[
          {"id":2222,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/2222.jpg"},
          {"id":1234,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/1234.jpg"},
          {"id":100000101,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/100000101.jpg"},
          {"id":6666,"image_url_small":"https://evil.test/6666.jpg"},
          {"id":"bad"},null,42]}
        """;

    [Test]
    public async Task ArtworkApiCacheRoundTripPreservesCanonicalAndAlternateIdsWithoutProviderUrls() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.ArtworkImageIds, Is.EqualTo(new[] {2222, 1234, 100000101}));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(1234), Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(info.SelectArtworkImageId(5555), Is.EqualTo(1234));
        Assert.That((info with { CanonicalCardId = 9999 }).SelectArtworkImageId(5555), Is.EqualTo(2222));
        Assert.That(storage.RawCache, Does.Not.Contain("images.ygoprodeck").And.Not.Contain("evil.test")
            .And.Not.Contain("data:image").And.Not.Contain("blob:"));
        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        CardInfo restored = await next.GetCardArtworkInfoAsync(1234);
        Assert.That(restored.ArtworkImageIds, Is.EqualTo(info.ArtworkImageIds));
        Assert.That(restored.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AlternateQueryPasscodeRemainsDistinctFromCanonicalAndInternalIdentity() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(2222);
        Assert.That(info.Id, Is.EqualTo(2222));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        string? url = await new CardArtworkService(service).GetArtworkUrlAsync(2222);
        Assert.That(url, Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/2222.jpg"));
        Assert.That(handler.Requests.Single(), Does.EndWith("?id=2222"));
    }

    [TestCase("")]
    [TestCase(",\"card_images\":null")]
    [TestCase(",\"card_images\":42")]
    [TestCase(",\"card_images\":[]")]
    [TestCase(",\"card_images\":[{\"id\":1,\"image_url_small\":\"https://evil.test/image.jpg\"}]")]
    public async Task AuthoritativeMissingOrMalformedArtworkPreservesCardAndDoesNotRefetch(string images) {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\"" + images + "}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.Type, Is.EqualTo("Spell Card"));
        Assert.That(info.ArtworkMetadataKnown, Is.True);
        Assert.That(info.SelectArtworkImageId(1234), Is.Null);
        CardArtworkService urls = new(service);
        Assert.That(await urls.GetArtworkUrlAsync(1234), Is.Null);
        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await next.GetCardArtworkInfoAsync(1234)).ArtworkMetadataKnown, Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshExistingV2CacheEnrichesOnlyRequestedArtworkOnce() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (1234, "Saved"), (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Saved"));
        Assert.That(handler.Requests, Is.Empty);
        CardInfo[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetCardArtworkInfoAsync(1234)));
        Assert.That(results.All(info => info.ArtworkImageIds.Count == 3), Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0], Does.EndWith("?id=1234"));
        using JsonDocument doc = JsonDocument.Parse(storage.RawCache!);
        Assert.That(doc.RootElement.GetProperty("LastFullRefreshUtc").GetDateTimeOffset(), Is.EqualTo(Now));
        Assert.That((await service.GetCardInfoAsync(5678)).Name, Is.EqualTo("Other"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task DisplayLookupsShareInFlightResolutionAndCacheUnavailableIds() {
        MockCardInfo info = new();
        CardArtworkService artwork = new(info);
        Task<string?> first = artwork.GetArtworkUrlAsync(1234);
        Task<string?> second = artwork.GetArtworkUrlAsync(1234);
        Assert.That(info.Calls, Is.EqualTo(1));
        info.Result.SetResult(new CardInfo { Id = 1234, CanonicalCardId = 1234,
            ArtworkImageIds = new[] {1234}, ArtworkMetadataKnown = true });
        Assert.That(await first, Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/1234.jpg"));
        Assert.That(await second, Is.EqualTo(await first));
        Assert.That(await artwork.GetArtworkUrlAsync(0), Is.Null);
        Assert.That(info.Calls, Is.EqualTo(1));
    }

    private sealed class MockCardInfo : ICardInfoService {
        public int Calls;
        public TaskCompletionSource<CardInfo> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query) => throw new NotSupportedException();
        public Task<CardInfo> GetCardInfoAsync(int id) {
            Calls++;

            return Result.Task;
        }

        public Task<string> GetCardNameAsync(int id) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names) => throw new NotSupportedException();
    }

    private const string RichCardJson = """
        {"id":1234,"name":"Pendulum","type":"XYZ Pendulum Effect Monster","frameType":"xyz_pendulum",
         "race":"Warrior","attribute":"FIRE","level":4,"linkval":2,"scale":8,"archetype":"Vanquish Soul",
         "desc":"Not cached","card_prices":[{"price":"100"}],"card_images":[{"image_url":"not cached"}]}
        """;

    [Test]
    public async Task ExactNamesReuseRichCacheIncludingStaleEntriesWithoutNetwork() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-10), (1234, "Exact")));
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]);
        Assert.That(result["Exact"].Id, Is.EqualTo(1234));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task ExactNamesEnrichIncompleteCacheDeduplicateEscapeAndPersist(int version) {
        const string name = "Ash Blossom & Joyous Spring";
        TestLocalStorage storage = new(JsonSerializer.Serialize(new {
            SchemaVersion = version, LastFullRefreshUtc = Now,
            Cards = new Dictionary<int, object> { [14558127] = new { Name = name } }
        }));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":14558127,\"name\":\"Ash Blossom & Joyous Spring\",\"type\":\"Tuner Monster\",\"level\":3}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync([name, name]);
        Assert.That(result[name].Id, Is.EqualTo(14558127));
        Assert.That(result[name].Type, Is.EqualTo("Tuner Monster"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(new Uri(handler.Requests.Single()).Query), Is.EqualTo("?name=" + name));
        Assert.That(handler.Requests.Single(), Does.Not.Contain("fname=").And.Not.Contain("?id="));
        using JsonDocument document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        if (version == 1) {
            Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await next.GetCardInfoByExactNamesAsync([name]))[name], Is.EqualTo(result[name]));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase("{\"data\":[{\"id\":1,\"name\":\"exact\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact extra\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact\",\"type\":\"Spell Card\"},{\"id\":2,\"name\":\"Exact\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[]}")]
    [TestCase("{bad")]
    public async Task ExactNamesRejectNonmatchingAmbiguousIncompleteOrInvalidResponses(string response) {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, response)));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That(await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]), Is.Empty);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(new Uri(handler.Requests.Single()).Query, Is.EqualTo("?name=Exact"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesStorageFailuresDoNotLoseSuccessfulResolution(bool readFailure) {
        TestLocalStorage storage = new("{bad") { ThrowOnRead = readFailure, ThrowOnWrite = true };
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Id, Is.EqualTo(1234));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Type, Is.Not.Null);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesNetworkFailuresDoNotFailTheOtherNames(bool throws) {
        RecordingHttpMessageHandler handler = new((request, _) => {
            if (request.RequestUri!.Query.Contains("Missing")) {
                if (throws) {
                    throw new HttpRequestException("Offline");
                }

                return Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));
            }
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}"));
        });
        using HttpClient client = new(handler);
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync(["Missing", "Pendulum"]);
        Assert.That(result.Keys, Is.EqualTo(new[] { "Pendulum" }));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task TargetedEndpointParsesReusableMetadata() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info, Is.EqualTo(new CardInfo { Id = 1234, Name = "Pendulum", Type = "XYZ Pendulum Effect Monster",
            FrameType = "xyz_pendulum", Race = "Warrior", Attribute = "FIRE", Level = 4, LinkVal = 2, Scale = 8, Archetype = "Vanquish Soul",
            CanonicalCardId = 1234, ArtworkMetadataKnown = true }));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0], Does.EndWith("?id=1234"));
        Assert.That(storage.RawCache, Does.Not.Contain("Not cached").And.Not.Contain("card_prices").And.Not.Contain("image_url"));
        RecordingHttpMessageHandler nextHandler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient nextClient = new(nextHandler);
        CardInfoService next = new(storage, nextClient, new TestTimeProvider(Now));
        Assert.That(await next.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
        Assert.That(nextHandler.Requests.Single(), Does.EndWith("cardinfo.php?id=1234"));
    }

    [Test]
    public async Task FreshV1NameCacheRequiresMetadataRefreshAndUpgradesToV2() {
        TestLocalStorage storage = new(CreateV1CacheJson());
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Attribute, Is.EqualTo("FIRE"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (1234, "Pendulum"), (5678, "Other old name"));
    }

    [Test]
    public async Task FailedV1EnrichmentRetainsCachedNameAndNeverInventsProperties() {
        string original = CreateV1CacheJson();
        TestLocalStorage storage = new(original);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Name, Is.EqualTo("Old name"));
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(CardPropertyProvider.GetCategories(info), Is.Empty);
        Assert.That(storage.RawCache, Is.EqualTo(original));
        Assert.That(await service.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(handler.Requests, Has.Length.EqualTo(1), "Legacy metadata uses one targeted lookup.");
    }

    [Test]
    public async Task PerCardV1EnrichmentDoesNotClaimAFullMetadataRefresh() {
        TestLocalStorage storage = new(CreateV1CacheJson());
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query.Length == 0 ? Response(HttpStatusCode.ServiceUnavailable, "{}")
                : Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Scale, Is.EqualTo(8));
        using JsonDocument document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(document.RootElement.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("Other old name"));
        RecordingHttpMessageHandler nextHandler = HandlerWithTargetedCards(200, (1234, "Current"));
        using HttpClient nextClient = new(nextHandler);
        CardInfoService nextService = new(storage, nextClient, new TestTimeProvider(Now));
        Assert.That((await nextService.GetCardInfoAsync(1234)).Name, Is.EqualTo("Current"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task OptionalMalformedFieldsDoNotDiscardReliableNameOrType() {
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"bad\",\"name\":\"\"},{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\",\"race\":\"Quick-Play\",\"level\":\"bad\",\"scale\":{},\"attribute\":42}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Name, Is.EqualTo("Spell"));
        Assert.That(info.Level, Is.Null);
        Assert.That(info.Attribute, Is.Null);
        Assert.That(CardPropertyProvider.GetCategories(info).Select(c => c.Name), Is.EqualTo(new[] { "Spell", "Quick-Play Spell" }));
    }

    private static string CreateV1CacheJson() => JsonSerializer.Serialize(new {
        SchemaVersion = 1, LastFullRefreshUtc = Now,
        Cards = new Dictionary<string, object> { ["1234"] = new { Name = "Old name" }, ["5678"] = new { Name = "Other old name" } }
    });

    [Test]
    public async Task ServiceConstructionDoesNotStartAnApiRequestForSavedSessions() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Imported"));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Imported"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task SingleCardLookupKeepsQueriedPasscodeAndReliableNameFallback() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":4321,\"name\":\"Alternate passcode\",\"type\":\"Normal Monster\"}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(info.Name, Is.EqualTo("Alternate passcode"));
        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo(info.Name));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshCurrentCache_ReturnsCachedNameWithoutApiRequest() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-7).AddHours(1), (1234, "Blue-Eyes White Dragon")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Blue-Eyes White Dragon"));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ExpiredCache_RefreshesOnlyRequestedCardAndPersistsCurrentEnvelope() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-7), (1234, "Old Name")));
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Updated Name"), (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Updated Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now.AddDays(-7), (1234, "Updated Name"));
        Assert.That(handler.Requests.Single(), Does.EndWith("cardinfo.php?id=1234"));
    }

    [Test]
    public async Task ExpiredCacheAndFailedTargetedLookup_RetainsStaleNameAndTimestamp() {
        DateTimeOffset oldTimestamp = Now.AddDays(-10);
        string originalJson = CreateCacheJson(oldTimestamp, (1234, "Stale Name"));
        TestLocalStorage storage = new(originalJson);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ExpiredCacheAndEmptyTargetedResponse_RetainsStaleSnapshot() {
        string originalJson = CreateCacheJson(Now.AddDays(-9), (1234, "Stale Name"));
        TestLocalStorage storage = new(originalJson);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[]}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task LegacyCache_UpgradesAfterSuccessfulTargetedLookup() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Refreshed Name"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Refreshed Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (1234, "Refreshed Name"));
    }

    [Test]
    public async Task LegacyCacheAndFailedTargetedLookup_RemainsUsableForKnownCard() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Legacy Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MalformedCache_DoesNotBreakImportAndAttemptsFreshFetch() {
        TestLocalStorage storage = new("{malformed json");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [TestCase(null)]
    [TestCase("{}")]
    public async Task MissingOrEmptyCache_FetchesOnlyRequestedCard(string? rawCache) {
        TestLocalStorage storage = new(rawCache);
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task UnsupportedCacheVersion_IsDiscardedAndRefreshed() {
        TestLocalStorage storage = new("{\"SchemaVersion\":99,\"Cards\":{\"1234\":{\"Name\":\"Future Name\"}}}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task MalformedEnvelopeWithFreshTimestamp_IsDiscardedAndRefreshed() {
        TestLocalStorage storage = new("{\"SchemaVersion\":1,\"LastFullRefreshUtc\":\"2026-10-01T09:00:00Z\",\"Cards\":{\"not-a-card-id\":{\"Name\":\"Unusable\"}}}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(5678), Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task CacheMiss_UsesSingleCardEndpointAndPersistsMetadataEnvelope() {
        DateTimeOffset refreshedAt = Now.AddDays(-2);
        TestLocalStorage storage = new(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"Dark Magician\",\"type\":\"Normal Monster\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "Dark Magician"));
    }

    [Test]
    public async Task SingleCardFetch_DoesNotAdvanceFullRefreshTimestamp() {
        DateTimeOffset refreshedAt = Now.AddDays(-6);
        TestLocalStorage storage = new(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("New Card"));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "New Card"));
    }

    [Test]
    public async Task SingleCardFetchAfterLegacyCache_PersistsUnknownTimestampAndNextInstanceUsesTargetedLookup() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler firstHandler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}")
                : Response(HttpStatusCode.ServiceUnavailable, "{}")));

        using (HttpClient firstHttpClient = new(firstHandler)) {
            CardInfoService firstService = new(storage, firstHttpClient, new TestTimeProvider(Now));
            Assert.That(await firstService.GetCardNameAsync(5678), Is.EqualTo("New Card"));
        }

        using (JsonDocument document = JsonDocument.Parse(storage.RawCache!)) {
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("Cards").GetProperty("1234").GetProperty("Name").GetString(), Is.EqualTo("Legacy Name"));
            Assert.That(root.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("New Card"));
        }

        RecordingHttpMessageHandler nextHandler = HandlerWithTargetedCards(200, (1234, "Refreshed Name"));
        using HttpClient nextHttpClient = new(nextHandler);
        CardInfoService nextService = new(storage, nextHttpClient, new TestTimeProvider(Now));

        Assert.That(await nextService.GetCardNameAsync(1234), Is.EqualTo("Refreshed Name"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task TargetedHttpFailure_FallsBackToNumericId() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(9999);

        Assert.That(result, Is.EqualTo("9999"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task LocalStorageReadAndWriteFailures_DoNotBreakImport() {
        TestLocalStorage storage = new(null) { ThrowOnRead = true, ThrowOnWrite = true };
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ParallelStartupLookups_UseOnlyTargetedRequestsForRequestedCards() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-8), (1234, "Old A"), (5678, "Old B")));
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "New A"), (5678, "New B"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string[] results = await Task.WhenAll(service.GetCardNameAsync(1234), service.GetCardNameAsync(5678));

        Assert.That(results, Is.EqualTo(new[] { "New A", "New B" }));
        Assert.That(handler.Requests.Select(request => new Uri(request).Query),
            Is.EquivalentTo(new[] { "?id=1234", "?id=5678" }));
    }

    private static RecordingHttpMessageHandler HandlerWithTargetedCards(int statusCode, params (int Id, string Name)[] cards) =>
        new((request, _) => {
            string query = request.RequestUri!.Query;
            (int Id, string Name)[] responseCards = cards
                .Where(card => query == $"?id={card.Id}")
                .ToArray();
            return Task.FromResult(Response((HttpStatusCode)statusCode, CreateBulkJson(responseCards)));
        });

    private static RecordingHttpMessageHandler HandlerWithStatus(HttpStatusCode statusCode) =>
        new((_, _) => Task.FromResult(Response(statusCode, "{}")));

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string json) => new() {
        StatusCode = statusCode,
        Content = new StringContent(json)
    };

    private static string CreateBulkJson(params (int Id, string Name)[] cards) =>
        JsonSerializer.Serialize(new {
            data = cards.Select(card => new { id = card.Id, name = card.Name, type = "Effect Monster" })
        });

    private static string CreateLocalCatalogJson(IEnumerable<CardInfo> cards) =>
        JsonSerializer.Serialize(new { version = 1, cards = cards.ToArray() }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string CreateCacheJson(DateTimeOffset refreshedAt, params (int Id, string Name)[] cards) =>
        JsonSerializer.Serialize(new {
            SchemaVersion = 2,
            LastFullRefreshUtc = refreshedAt,
            Cards = cards.ToDictionary(card => card.Id.ToString(), card => new { Id = card.Id, Name = card.Name, Type = "Effect Monster" })
        });

    private static void AssertPersistedCache(string? json, DateTimeOffset? expectedTimestamp, params (int Id, string Name)[] expectedCards) {
        Assert.That(json, Is.Not.Null);
        using JsonDocument document = JsonDocument.Parse(json!);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        JsonElement timestamp = root.GetProperty("LastFullRefreshUtc");
        if (expectedTimestamp is DateTimeOffset expected) {
            Assert.That(timestamp.GetDateTimeOffset(), Is.EqualTo(expected));
        }
        else {
            Assert.That(timestamp.ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        JsonElement cards = root.GetProperty("Cards");
        Assert.That(cards.EnumerateObject().Count(), Is.EqualTo(expectedCards.Length));
        foreach ((int id, string name) in expectedCards) {
            Assert.That(cards.GetProperty(id.ToString()).GetProperty("Name").GetString(), Is.EqualTo(name));
        }
    }

    private sealed class TestLocalStorage(string? rawCache) : ILocalStorageService {
        public string? RawCache { get; private set; } = rawCache;
        public bool ThrowOnRead { get; init; }
        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }

        public Task<T?> GetItemAsync<T>(string key) =>
            Task.FromResult(RawCache is null ? default : JsonSerializer.Deserialize<T>(RawCache));

        public Task<string?> GetRawItemAsync(string key) {
            if (ThrowOnRead) {
                throw new InvalidOperationException("Storage read failed.");
            }

            return Task.FromResult(RawCache);
        }

        public Task SetItemAsync<T>(string key, T value) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("Storage write failed.");
            }

            RawCache = JsonSerializer.Serialize(value);
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory
    ) : HttpMessageHandler {
        private readonly ConcurrentQueue<string> _requests = new();

        public string[] Requests => _requests.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            _requests.Enqueue(request.RequestUri!.ToString());
            return responseFactory(request, cancellationToken);
        }
    }
}
