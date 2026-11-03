using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazorTest.Fixtures;

namespace YGOProbabilityCalculatorBlazorTest.Models;

[TestFixture]
public sealed class CardPreservationTest {
    private static readonly CategoryBase Fire = new("Fire", CategorySource.Metadata, "attribute:fire");
    private static readonly CategoryBase Dark = new("DARK", CategorySource.Metadata, "attribute:dark");
    private static readonly CategoryBase Role = new("Role");

    private static IEnumerable<TestCaseData> ConfigurationPreservingChanges() {
        yield return Change("Rename", card => card.WithName("Renamed"));
        yield return Change("ChangeCopies", card => card.WithCopies(3));
        yield return Change("ChangeActiveState", card => card.WithActive(false));
        yield return Change("ReplaceCategories", card => card.WithCategories([Role]));
        yield return Change("AddManualMetadata", card => card.WithManualMetadataCategory(Dark));
        yield return Change("RemoveManualMetadata", card => card.WithoutManualMetadataCategory(Fire.MetadataKey!));
        yield return Change("UpdateObjectiveMetadata", card => card.WithObjectiveMetadata([Fire], 123));
    }

    [TestCaseSource(nameof(ConfigurationPreservingChanges))]
    public void ReplacementPreservesDrawConfigurationAndIdentity(Func<Card, Card> change) {
        Card source = DrawCard().WithManualMetadataCategory(Fire);

        Card changed = change(source);

        Assert.Multiple(() => {
            Assert.That(changed.Id, Is.EqualTo(source.Id));
            Assert.That(changed.DrawCount, Is.EqualTo(source.DrawCount));
            Assert.That(changed.DrawOncePerTurn, Is.EqualTo(source.DrawOncePerTurn));
            Assert.That(changed.PostDrawDiscardCount, Is.EqualTo(source.PostDrawDiscardCount));
            Assert.That(changed.PreDrawDeckRemovalCount, Is.EqualTo(source.PreDrawDeckRemovalCount));
            Assert.That(changed.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(changed.EffectProfile, Is.EqualTo(source.EffectProfile));
            Assert.That(changed.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
        });
    }

    [TestCaseSource(nameof(ConfigurationPreservingChanges))]
    public void ReplacementPreservesMaintainedSearchConfigurationAndIdentity(Func<Card, Card> change) {
        Card source = SearchCardFixtures.Rota().WithSimulationEnabled(false).WithManualMetadataCategory(Fire);

        Card changed = change(source);

        Assert.Multiple(() => {
            Assert.That(changed.Id, Is.EqualTo(source.Id));
            Assert.That(changed.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(changed.EffectProfile, Is.EqualTo(source.EffectProfile));
            Assert.That(changed.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
        });
    }

    private static IEnumerable<TestCaseData> DrawPlanChanges() {
        yield return Change("ChangeDrawCount", card => card.WithDrawCount(3));
        yield return Change("ChangeDrawOncePerTurn", card => card.WithDrawOncePerTurn(true));
        yield return Change("ChangePostDrawDiscardCount", card => card.WithPostDrawDiscardCount(2));
        yield return Change("ChangePostDrawSelector", card => card.WithPostDrawSelector(CardSelector.Any));
        yield return Change("ChangePostDrawFailureBehavior", card => card.WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch));
        yield return Change("ChangePreDrawRemovalCount", card => card.WithPreDrawRemovalCount(1));
        yield return Change("ChangePreDrawSelector", card => card.WithPreDrawSelector(CardSelector.Any));
    }

    [TestCaseSource(nameof(DrawPlanChanges))]
    public void DrawPlanEditPreservesHiddenCostAndSimulationSwitch(Func<Card, Card> change) {
        Card source = DrawCard();

        Card changed = change(source);

        Assert.That(changed.PreDrawDeckRemovalCount, Is.EqualTo(source.PreDrawDeckRemovalCount));
        Assert.That(changed.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
    }

    private static IEnumerable<TestCaseData> SearchPlanChanges() {
        yield return Change("ChangeOncePerTurn", card => card.WithOncePerTurn(true));
        yield return Change("ChangeSearch", card => card.WithSearch(new(CardSelector.For(Role), false)));
    }

    [TestCaseSource(nameof(SearchPlanChanges))]
    public void SearchPlanEditPreservesSimulationSwitch(Func<Card, Card> change) {
        Card source = SearchCardFixtures.Rota().WithSimulationEnabled(false);

        Card changed = change(source);

        Assert.That(changed.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DuplicatePreservesConfigurationAndChangesIdentity(bool search) {
        Card source = search ? SearchCardFixtures.Rota().WithSimulationEnabled(false) : DrawCard();

        Card duplicate = source.Duplicate();

        Assert.Multiple(() => {
            Assert.That(duplicate.Id, Is.Not.EqualTo(source.Id));
            Assert.That(duplicate.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(duplicate.EffectProfile, Is.EqualTo(source.EffectProfile));
            Assert.That(duplicate.PreDrawDeckRemovalCount, Is.EqualTo(source.PreDrawDeckRemovalCount));
            Assert.That(duplicate.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
        });
    }

    [Test]
    public void CategoryMaterializationPreservesHiddenCost() {
        CategoryBase role = new("Dark role", rule: CardSelector.For(Dark));
        Card source = DrawCard().WithManualMetadataCategory(Dark);

        Card materialized = CategoryMembership.Materialize([source], [role])[0];

        Assert.That(materialized.PreDrawDeckRemovalCount, Is.EqualTo(source.PreDrawDeckRemovalCount));
        Assert.That(materialized.Categories, Does.Contain(role));
    }

    [Test]
    public void RestoringBuiltInDoesNotEnableSimulation() {
        Card source = SearchCardFixtures.Rota().WithSimulationEnabled(false);

        Card restored = CardEffectRegistry.Default.RestoreBuiltIn(source.WithSearch(new(CardSelector.Any, false)));

        Assert.That(restored.SimulationEnabled, Is.EqualTo(source.SimulationEnabled));
        Assert.That(restored.EffectProfile, Is.EqualTo(source.EffectProfile));
    }

    [Test]
    public void DrawEditsResetDependentCostOnlyWhenDrawingIsCleared() {
        Card source = DrawCard();

        Card cleared = source.WithDrawCount(null);

        Assert.That(cleared.Copies, Is.EqualTo(source.Copies));
        Assert.That(cleared.PostDrawDiscardCount, Is.Zero);
        Assert.That(cleared.PreDrawDeckRemovalCount, Is.Zero);
        Assert.That(cleared.Id, Is.EqualTo(source.Id));
        Assert.That(cleared.WithDrawCount(3).DrawOncePerTurn, Is.EqualTo(source.DrawOncePerTurn));
        Assert.That(source.WithDrawCount(3).PostDrawDiscardCount, Is.EqualTo(source.PostDrawDiscardCount));
        Card oncePerTurn = source.WithDrawOncePerTurn(true);
        Assert.That(oncePerTurn.PostDrawDiscardCount, Is.EqualTo(source.PostDrawDiscardCount));
        Assert.That(oncePerTurn.Id, Is.EqualTo(source.Id));
        Assert.That(oncePerTurn.DrawCount, Is.EqualTo(source.DrawCount));
    }

    [Test]
    public void DrawDefaultsAndInvalidCostsRemainValidated() {
        Card source = DrawCard();

        Assert.That(new Card([]).DrawCount, Is.Null);
        Assert.That(new Card([]).DrawOncePerTurn, Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithDrawCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithDrawCount(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], postDrawDiscardCount: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithPostDrawDiscardCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithPostDrawDiscardCount(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithPreDrawDeckRemovalCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithPreDrawDeckRemovalCount(61));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], preDrawDeckRemovalCount: 1));
    }

    private static Card DrawCard() => new(
        [],
        2,
        "Card",
        id: "stable",
        drawCount: 2,
        drawOncePerTurn: false,
        postDrawDiscardCount: 1,
        preDrawDeckRemovalCount: 10,
        simulationEnabled: false
    );

    private static TestCaseData Change(string name, Func<Card, Card> change) => new TestCaseData(change).SetName("{m}_" + name);
}
