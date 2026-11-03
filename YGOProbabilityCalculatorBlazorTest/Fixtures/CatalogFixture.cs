using System.Net;
using System.Text.Json;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Fixtures;

internal static class CatalogFixture {
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<IReadOnlyList<CardInfo>> LoadCardsAsync(IEnumerable<int> requiredIds) {
        HashSet<int> selectedIds = requiredIds.ToHashSet();
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FindCatalogPath()));
        CardInfo[] cards = document.RootElement.GetProperty("cards").EnumerateArray()
            .Where(card => selectedIds.Contains(card.GetProperty("id").GetInt32()))
            .Select(card => card.Deserialize<CardInfo>(CatalogJsonOptions)!)
            .ToArray();
        int[] missingIds = selectedIds.Except(cards.Select(card => card.Id)).ToArray();

        if (missingIds.Length > 0) {
            throw new InvalidDataException($"The checked-in catalog is missing fixture card IDs: {string.Join(", ", missingIds)}.");
        }

        string subsetJson = JsonSerializer.Serialize(new { version = 2, cards }, CatalogJsonOptions);
        using HttpClient httpClient = new(new CatalogResponseHandler(subsetJson)) {
            BaseAddress = new Uri("https://calculator.test/")
        };
        CardInfoService service = new(Mock.Of<ILocalStorageService>(), httpClient);

        return await service.GetCatalogCardsAsync();
    }

    private static string FindCatalogPath() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null) {
            string path = Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");

            if (File.Exists(path)) {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the checked-in card catalog from the test output directory.");
    }

    private sealed class CatalogResponseHandler(string catalogJson) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/data/card-catalog.v1.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalogJson) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
