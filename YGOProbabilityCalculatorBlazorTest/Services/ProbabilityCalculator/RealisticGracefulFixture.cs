using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticGracefulFixture {
    internal static async Task<SessionState> Load() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);
        string? catalog = null;
        while (root is not null) {
            string candidate = Path.Combine(root.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");
            if (File.Exists(candidate)) {
                catalog = candidate;
                break;
            }
            root = root.Parent;
        }
        Assert.That(catalog, Is.Not.Null);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalog!));
        JsonElement entry = document.RootElement.GetProperty("cards").EnumerateArray()
            .Single(card => card.GetProperty("id").GetInt32() == 79571449);
        CardInfo info = entry.Deserialize<CardInfo>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Card original = session.Cards.Single(card => card.Name == "Mulcharmy Fuwalos");
        Assert.That(original.Copies, Is.EqualTo(3));
        Card imported = CuratedDrawEffectPresets.CreateImportedCard(info, original.Copies, info.Id);
        Card graceful = new(imported.Categories, imported.Copies, imported.Name, original.Active,
            original.Id, imported.ExternalCardId, drawCount: imported.DrawCount,
            drawOncePerTurn: imported.DrawOncePerTurn, postDrawDiscardCount: imported.PostDrawDiscardCount,
            postDrawSelector: imported.PostDrawSelector, postDrawFailureBehavior: imported.PostDrawFailureBehavior);
        session.Cards[session.Cards.IndexOf(original)] = graceful;
        Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
        return session;
    }
}
