using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class ShippedExampleSessionTest {
    [Test]
    public async Task ShippedExampleRoundTripsInProgressiveModeWithTheSubmittedDeckAndCurrentEffects() {
        string fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
        string submittedJson = await File.ReadAllTextAsync(Path.Combine(fixtures, "submitted_example_session_state.json"));
        string shippedJson = await File.ReadAllTextAsync(Path.Combine(fixtures, "shipped_example_session_state.json"));
        SessionService service = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState submitted = await service.LoadSessionAsync(submittedJson);
        SessionState loaded = await service.LoadSessionAsync(shippedJson);

        string savedJson = service.SerializeSession(loaded);
        using JsonDocument savedDocument = JsonDocument.Parse(savedJson);
        SessionState roundTrip = await service.LoadSessionAsync(savedJson);
        Assert.Multiple(() => {
            Assert.That(savedDocument.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
            Assert.That(savedDocument.RootElement.GetProperty("CalculationModel").GetString(), Is.EqualTo("Fast"));
            Assert.That(savedDocument.RootElement.GetProperty("FastExecutionStrategy").GetString(), Is.EqualTo("Progressive"));
            Assert.That(roundTrip.CalculationModel, Is.EqualTo(CalculationModel.Fast));
            Assert.That(roundTrip.FastExecutionStrategy, Is.EqualTo(FastExecutionStrategy.Progressive));
            Assert.That(roundTrip.HandSize, Is.EqualTo(5));
            Assert.That(roundTrip.Cards, Has.Count.EqualTo(24));
            Assert.That(roundTrip.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(roundTrip.Combos, Has.Count.EqualTo(8));
            Assert.That(roundTrip.ComboGroups.Select(group => (group.Id, group.Name)),
                Is.EqualTo(submitted.ComboGroups.Select(group => (group.Id, group.Name))));
            Assert.That(roundTrip.Categories, Is.Empty);
        });

        Assert.That(roundTrip.Cards.Select(CardSnapshot), Is.EqualTo(submitted.Cards.Select(CardSnapshot)),
            "The example must keep the submitted card identities, copy counts, order and card metadata.");
        Assert.That(roundTrip.Combos.Select(ComboSnapshot), Is.EqualTo(submitted.Combos.Select(ComboSnapshot)),
            "The example must keep the submitted combo requirements, order and group assignments.");

        foreach (Card sourceCard in submitted.Cards.Where(card => card.EffectProfile.Source == EffectSource.BuiltIn)) {
            Card expectedBuiltIn = CardEffectRegistry.Default.RestoreBuiltIn(sourceCard);
            Card actual = roundTrip.Cards.Single(card => card.Id == sourceCard.Id);
            Assert.Multiple(() => {
                Assert.That(actual.EffectProfile, Is.EqualTo(expectedBuiltIn.EffectProfile), sourceCard.Name);
                Assert.That(actual.EffectPlan, Is.EqualTo(expectedBuiltIn.EffectPlan), sourceCard.Name);
            });
        }

        Card rota = roundTrip.Cards.Single(card => card.ExternalCardId == 32807846);
        Card chaoticElements = roundTrip.Cards.Single(card => card.ExternalCardId == 92221402);
        Card caseForK9 = roundTrip.Cards.Single(card => card.ExternalCardId == 80181649);
        Assert.Multiple(() => {
            Assert.That(rota.EffectProfile.Revision, Is.EqualTo(4));
            Assert.That(chaoticElements.EffectProfile.Revision, Is.EqualTo(3));
            Assert.That(caseForK9.EffectProfile.Revision, Is.EqualTo(1));
            Assert.That(caseForK9.Search, Is.EqualTo(CardEffectRegistry.Default.RestoreBuiltIn(
                submitted.Cards.Single(card => card.ExternalCardId == 80181649)).Search));
        });
        AssertRotaHasOneWarriorAndInclusiveLevelRange(rota);

        Card submittedRedLayer = submitted.Cards.Single(card => card.ExternalCardId == 59975920);
        Card shippedRedLayer = roundTrip.Cards.Single(card => card.ExternalCardId == 59975920);
        Assert.Multiple(() => {
            Assert.That(shippedRedLayer.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
            Assert.That(shippedRedLayer.EffectProfile, Is.EqualTo(submittedRedLayer.EffectProfile));
            Assert.That(shippedRedLayer.EffectPlan, Is.EqualTo(submittedRedLayer.EffectPlan));
        });

        ProbabilityCalculationResult exact = new ProbabilityCalculatorService().CalculateProbabilityResults(
            roundTrip.Cards, roundTrip.Combos, roundTrip.HandSize, roundTrip.ComboGroups,
            CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact));
        ProbabilityFraction fraction = exact.Contract!.Fractions!.Total;
        Assert.Multiple(() => {
            Assert.That(exact.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(fraction.Numerator, Is.EqualTo("565375"));
            Assert.That(fraction.Denominator, Is.EqualTo("658008"));
        });
    }

    private static void AssertRotaHasOneWarriorAndInclusiveLevelRange(Card rota) {
        Assert.That(rota.Search, Is.Not.Null);
        Assert.That(rota.Search!.ObjectiveMetadataOnly, Is.True);

        CardSelector[] terms = rota.Search.Selector.AuthoredTerms.ToArray();
        Assert.That(terms, Has.Length.EqualTo(2));

        CardSelector warrior = terms.Single(term => term.CategoryIdentities.Contains("metadata:monster-race:warrior"));
        CardSelector numeric = terms.Single(term => NumericPropertyRange.TryFromSelector(term, out _));
        Assert.That(warrior.Kind, Is.EqualTo(CardSelectorKind.Category));
        Assert.That(NumericPropertyRange.TryFromSelector(numeric, out NumericPropertyRange? range), Is.True);
        Assert.That((range!.Property, range.Minimum, range.Maximum), Is.EqualTo((NumericPropertyKind.Level, 1, 4)));
    }

    private static string CardSnapshot(Card card) => System.Text.Json.JsonSerializer.Serialize(new {
        card.Id,
        card.ExternalCardId,
        card.Name,
        card.Copies,
        card.Active,
        Categories = card.Categories.Select(category => category.Identity).ToArray(),
        ManualMetadata = card.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal).ToArray()
    });

    private static string ComboSnapshot(Combo combo) => System.Text.Json.JsonSerializer.Serialize(new {
        combo.Name,
        combo.Active,
        combo.GroupId,
        RequirementOrder = combo.RequirementOrder.Select(position => new { position.Kind, position.Index }).ToArray(),
        Categories = combo.Categories.Select(category => new {
            Selector = category.Selector.Signature,
            category.MinCount,
            category.MaxCount,
            category.MaximumMode
        }).ToArray(),
        Cards = combo.Cards.Select(card => new { card.CardId, card.MinCount, card.MaxCount, card.MaximumMode }).ToArray(),
        AlternativeGroups = combo.AlternativeGroups.Select(group => group.Alternatives.Select(alternative => new {
            alternative.Kind,
            Category = alternative.Category is null ? null : new {
                Selector = alternative.Category.Selector.Signature,
                alternative.Category.MinCount,
                alternative.Category.MaxCount,
                alternative.Category.MaximumMode
            },
            Card = alternative.Card is null ? null : new {
                alternative.Card.CardId,
                alternative.Card.MinCount,
                alternative.Card.MaxCount,
                alternative.Card.MaximumMode
            }
        }).ToArray()).ToArray()
    });
}
