using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public sealed class ModelNotesTest {
    private static readonly CardEffectRegistry Registry = CardEffectRegistry.Default;

    [TestCase(35261759)]
    [TestCase(691925)]
    public async Task CompatibleBuiltInRefreshesOnlyDisplayedTextAndRoundTripsSavedMetadata(int identity) {
        MaintainedCardEffect current = Registry.Find(identity)!;
        EffectProfile savedProfile = current.Profile with { Summary = "Saved summary", Limitations = "Hidden identities are never tracked." };
        Card saved = new Card([], name: current.Name, externalCardId: identity).WithEffect(current.Plan, savedProfile);
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string json = codec.SerializeSession(new() { Cards = [saved] });
        SessionState loaded = await codec.LoadSessionAsync(json);
        Card retained = loaded.Cards.Single();
        EffectiveCardEffect display = Registry.Describe(retained);
        Assert.Multiple(() => {
            Assert.That(display.UsesCurrentDescription, Is.True);
            Assert.That(display.Summary, Is.EqualTo(current.Profile.Summary));
            Assert.That(display.Limitations, Is.EqualTo(current.Profile.Limitations));
            Assert.That(display.Limitations, Does.Contain("Fast may be exact or uncertain").And.Contain("completed physical Exact"));
            Assert.That(display.Plan, Is.EqualTo(saved.EffectPlan));
            Assert.That(retained.EffectProfile, Is.EqualTo(savedProfile));
        });
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json), "Describe cannot rewrite serialized snapshot bytes.");
        string shared = SessionShareCodec.Decode(new Uri(SessionShareCodec.CreateLink("https://example.test/", codec.SerializeSession(loaded))).Fragment);
        Card sharedCard = (await codec.LoadSessionAsync(shared)).Cards.Single();
        Assert.That(sharedCard.EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(sharedCard.EffectProfile, Is.EqualTo(savedProfile));
        Card restored = Registry.RestoreBuiltIn(retained);
        Assert.That(restored.EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(restored.EffectProfile, Is.EqualTo(current.Profile));
        Card fresh = Registry.CreateImportedCard(new CardInfo { Id = identity, Name = current.Name }, 1, identity);
        Assert.That(fresh.EffectProfile, Is.EqualTo(current.Profile));
    }

    [TestCase("canonical")]
    [TestCase("revision")]
    [TestCase("plan")]
    [TestCase("support")]
    [TestCase("execution")]
    [TestCase("custom")]
    public void DifferingSnapshotKeepsItsPlanProvenanceAndSavedBuiltInDescription(string difference) {
        MaintainedCardEffect current = Registry.Find(difference == "revision" ? 49238328 : 35261759)!;
        EffectProfile profile = current.Profile with { Summary = "Historical summary", Limitations = "Historical scope" };
        DrawEffectPlan plan = current.Plan;
        profile = difference switch {
            "canonical" => profile with { CanonicalCardId = 55144522 },
            "revision" => profile with { Revision = 1 },
            "support" => profile with { Support = EffectSupport.Partial },
            "execution" => profile with { ExecutionModel = EffectExecutionModel.Unsupported },
            "custom" => profile with { Source = EffectSource.Custom },
            _ => profile
        };
        if (difference == "plan") {
            plan = plan with { HiddenDeckCost = 0 };
        }
        Card saved = new Card([], externalCardId: current.CanonicalCardId).WithEffect(plan, profile);
        EffectiveCardEffect display = Registry.Describe(saved);
        Assert.Multiple(() => {
            Assert.That(display.UsesCurrentDescription, Is.False);
            Assert.That(display.Plan, Is.EqualTo(plan));
            Assert.That(display.Source, Is.EqualTo(profile.Source));
            Assert.That(saved.EffectProfile, Is.EqualTo(profile));
            Assert.That(display.ExecutionModel, Is.EqualTo(profile.ExecutionModel));
            Assert.That(display.Capabilities, Is.EqualTo(plan.Capabilities));
            Assert.That(display.CanExecute, Is.EqualTo(difference != "execution"));
            Assert.That(display.Limitations, Is.EqualTo(difference == "custom" ? "" : "Historical scope"));
        });
    }

    [Test]
    public void AllRegistryNotesHaveAnAuditedPurposeWithoutChangingSupportOrPlans() {
        int[] notes = [691925, 6153210, 7925734, 20508881, 24940422, 34041788, 35261759, 42544773,
            45171524, 49238328, 52782439, 56506740, 56725612, 63166095, 78679226, 80845034, 81101309,
            92221402, 96729612, 11317977];
        Assert.That(Registry.All, Has.Count.EqualTo(44));
        Assert.That(Registry.All.Where(effect => !string.IsNullOrWhiteSpace(effect.Profile.Limitations)).Select(effect => effect.CanonicalCardId), Is.EquivalentTo(notes));
        Assert.That(Registry.All.Where(effect => effect.Profile.Support == EffectSupport.Partial).Select(effect => effect.CanonicalCardId), Is.EquivalentTo(new[] { 20508881, 24940422 }));
        foreach (MaintainedCardEffect effect in Registry.All) {
            Assert.That(effect.Profile.Limitations, Does.Not.Contain("identities are not tracked"));
            Assert.That(effect.Profile.Limitations, Does.Not.Contain("only unseen deck depletion"));
            Assert.That(Registry.Describe(new Card([]).WithEffect(effect.Plan, effect.Profile)).UsesCurrentDescription, Is.True);
        }
        Assert.That(Registry.Find(20508881)!.Profile.Limitations, Does.Contain("Mystical Space Typhoon"));
        Assert.That(Registry.Find(24940422)!.Profile.Limitations, Does.Contain("Spells/Traps in the GY").And.Contain("restriction").And.Contain("redraw"));
        Assert.That(Registry.Find(49238328)!.Profile.Limitations, Does.Contain("6 Extra Deck"));
        Assert.That(Registry.Find(96729612)!.Profile.Limitations, Does.Contain("Ritual Spell recovery"));
    }
}
