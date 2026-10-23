using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class ShippedExampleSessionTest {
    private const string SubmittedSourceHash = "975d8335a33f34dbec7d78d19ce8ad5a782dfbfa7b26b3d098a0239ad8fb0d82";
    private static readonly int[] RefreshedCardIds = [32807846, 92221402];

    [Test]
    public async Task ShippedExampleRefreshesOnlyOutdatedBuiltInSearchPlansAndKeepsTheSubmittedSource() {
        string fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
        string submittedJson = await File.ReadAllTextAsync(Path.Combine(fixtures, "submitted_example_session_state.json"));
        string shippedJson = await File.ReadAllTextAsync(Path.Combine(fixtures, "shipped_example_session_state.json"));
        string submittedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(submittedJson)));
        Assert.That(submittedHash, Is.EqualTo(SubmittedSourceHash));
        AssertOnlySimulationFieldsRefreshed(submittedJson, shippedJson);

        SessionService service = new(null!, new JsonSerializer());
        SessionState submitted = await service.LoadSessionAsync(submittedJson);
        SessionState shipped = await service.LoadSessionAsync(shippedJson);
        Assert.Multiple(() => {
            Assert.That(shipped.Cards, Has.Count.EqualTo(24));
            Assert.That(shipped.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(shipped.Combos, Has.Count.EqualTo(8));
            Assert.That(shipped.ComboGroups.Select(group => group.Name), Is.EqualTo(new[] { "Full VS", "Full K9" }));
            Assert.That(shipped.HandSize, Is.EqualTo(5));
            Assert.That(shipped.CalculationModel, Is.EqualTo(CalculationModel.Exact));
            Assert.That(shipped.Categories, Is.Empty);
        });

        foreach (int cardId in RefreshedCardIds) {
            Card sourceCard = submitted.Cards.Single(card => card.ExternalCardId == cardId);
            Card shippedCard = shipped.Cards.Single(card => card.ExternalCardId == cardId);
            Card currentBuiltIn = CardEffectRegistry.Default.RestoreBuiltIn(sourceCard);
            Assert.Multiple(() => {
                Assert.That(shippedCard.EffectProfile, Is.EqualTo(currentBuiltIn.EffectProfile), $"profile {cardId}");
                Assert.That(shippedCard.EffectPlan, Is.EqualTo(currentBuiltIn.EffectPlan), $"plan {cardId}");
            });
        }

        Card rota = shipped.Cards.Single(card => card.ExternalCardId == 32807846);
        Card chaoticElements = shipped.Cards.Single(card => card.ExternalCardId == 92221402);
        Card caseForK9 = shipped.Cards.Single(card => card.ExternalCardId == 80181649);
        Assert.Multiple(() => {
            Assert.That(rota.EffectProfile.Revision, Is.EqualTo(4));
            Assert.That(rota.Search!.Selector.HasNumericPredicate, Is.True);
            Assert.That(chaoticElements.EffectProfile.Revision, Is.EqualTo(3));
            Assert.That(caseForK9.EffectProfile.Revision, Is.EqualTo(1));
            Assert.That(caseForK9.Search, Is.EqualTo(CardEffectRegistry.Default.RestoreBuiltIn(
                submitted.Cards.Single(card => card.ExternalCardId == 80181649)).Search));
        });

        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            shipped.Cards, shipped.Combos, shipped.HandSize, shipped.ComboGroups,
            CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact));
        ProbabilityFraction fraction = result.Contract!.Fractions!.Total;
        Assert.Multiple(() => {
            Assert.That(result.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(fraction.Numerator, Is.EqualTo("565375"));
            Assert.That(fraction.Denominator, Is.EqualTo("658008"));
        });
    }

    private static void AssertOnlySimulationFieldsRefreshed(string submittedJson, string shippedJson) {
        JsonNode submitted = JsonNode.Parse(submittedJson)!;
        JsonNode shipped = JsonNode.Parse(shippedJson)!;
        foreach (int cardId in RefreshedCardIds) {
            RemoveSimulationFields(FindCard(submitted, cardId));
            RemoveSimulationFields(FindCard(shipped, cardId));
        }
        Assert.That(JsonNode.DeepEquals(shipped, submitted), Is.True,
            "The shipped session may update only EffectProfile and Search for outdated built-in cards.");
    }

    private static JsonObject FindCard(JsonNode session, int cardId) => session["Cards"]!.AsArray()
        .Single(card => card!["ExternalCardId"]?.GetValue<int>() == cardId)!.AsObject();

    private static void RemoveSimulationFields(JsonObject card) {
        card.Remove("EffectProfile");
        card.Remove("Search");
    }
}
