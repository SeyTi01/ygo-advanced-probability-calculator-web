using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using NUnit.Framework;
using Core = CardProbability.Internal.Contracts;
using M = CardProbability.Internal.Model;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
internal sealed class DrawCountStateRegressionTest {
    [Test]
    public void CountStateScenariosMatchEveryLabeledFinalSourceComposition() {
        (int Copies, int Draw)[][] configurations = [[(1, 1)], [(1, 2)], [(1, 3)], [(2, 2)], [(3, 1)],
            [(1, 2), (1, 2)], [(1, 1), (2, 2)], [(1, 3), (1, 1)], [(2, 1), (2, 3)]];
        foreach ((int Copies, int Draw)[] configuration in configurations) {
            for (int limits = 0; limits < 1 << configuration.Length; limits++) {
                DeckEntry[] entries = [new("ordinary", 3), .. configuration.Select((effect, index) =>
                    new DeckEntry("effect" + index, effect.Copies, new(DrawCount: effect.Draw,
                        ActivationLimit: (limits & (1 << index)) != 0 ? 1 : null)))];
                foreach (int hand in new[] { 0, 1, 2, entries.Sum(entry => entry.Copies) }) {
                    EngineCase fixture = new("count-state", new(entries, hand, []), new());
                    PreparedCalculation input = fixture.Prepare();
                    List<M.Card> effects = input.Cards.Where(card => card.DrawCount is not null).ToList();
                    int copies = entries.Sum(entry => entry.Copies);
                    if (!DrawEffectResolver.CanUseCountStateFastPath(effects, copies, hand)) {
                        Assert.Throws<InvalidOperationException>(() => DrawEffectResolver.Resolve(3, effects, hand, new(Core.CalculationWorkPolicy.Interactive)));
                        continue;
                    }
                    Dictionary<string, BigInteger> expected = Compositions(fixture);
                    CardProbability.Internal.DrawResolution.DrawResolution actual = DrawEffectResolver.Resolve(3, effects, hand, new(Core.CalculationWorkPolicy.Interactive));
                    BigInteger total = Factorial(copies);
                    ExactProbability mass = ExactProbability.Zero;
                    WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
                    foreach ((DrawComposition composition, ExactProbability weight) in actual.Scenarios) {
                        string key = composition.OrdinaryCount + "|" + string.Join(',', composition.EffectCounts);
                        Assert.That(weight.Numerator * total, Is.EqualTo(expected[key] * weight.Denominator), key);
                        mass = mass.Add(weight, budget);
                    }
                    Assert.That(actual.Scenarios, Has.Count.EqualTo(expected.Count));
                    Assert.That(mass, Is.EqualTo(ExactProbability.One));
                }
            }
        }
    }

    [Test]
    public void OneBudgetCoversResolutionAndEveryScenarioAndResult() {
        EngineCase fixture = new("shared-budget", new([
            new("a", 8), new("b", 8), new("effect", 3, new(DrawCount: 2, ActivationLimit: 1))
        ], 4, [new("ab", [Requirement.InMembership("a", 1), Requirement.InMembership("b", 1)]),
            new("source", [Requirement.OfEntry("effect", 1, 3)])], [new("a", ["a", "effect"]), new("b", ["b", "effect"])]), new());
        PreparedCalculation input = fixture.Prepare();
        WorkBudget measured = new(Core.CalculationWorkPolicy.Interactive);
        new DrawEffectCalculation(input.Cards, 4, measured).Calculate(input.Combos, input.Groups);
        Core.CalculationWorkPolicy allowance = new(measured.Spent - 1);
        List<M.Card> effects = [input.Cards[2]];
        List<M.Card> ordinary = input.Cards.Take(2).ToList();
        CardProbability.Internal.DrawResolution.DrawResolution resolution = DrawEffectResolver.Resolve(16, effects, 4, new(allowance));
        foreach (DrawComposition composition in resolution.Scenarios.Keys) {
            List<M.Card> retained = composition.EffectCounts[0] == 0 ? [] : [effects[0].WithCopies(composition.EffectCounts[0])];
            Assert.DoesNotThrow(() => new ProbabilityCalculation(ordinary, composition.OrdinaryCount, new(allowance), retained)
                .CalculateExactResults(input.Combos, input.Groups));
        }
        Core.ProbabilityCalculationLimitException error = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            new DrawEffectCalculation(input.Cards, 4, new(allowance)).Calculate(input.Combos, input.Groups))!;
        Assert.That(error.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void ResolutionStateStorageRemainsBoundedWithUnlimitedWork() {
        EngineCase fixture = new("state-limit", new(Enumerable.Range(0, 20).Select(index =>
            new DeckEntry("effect" + index, 1, new(DrawCount: 1, ActivationLimit: 1))), 1, []), new());
        PreparedCalculation input = fixture.Prepare();
        Core.ProbabilityCalculationLimitException error = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            new DrawEffectCalculation(input.Cards, 1, new(Core.CalculationWorkPolicy.Unlimited)).Calculate([], []))!;
        Assert.That(error.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void ExactScenarioArithmeticPreservesRareRatiosAndZeroMass() {
        WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
        ExactProbability rare = new(1, BigInteger.One << 1074);
        ExactProbability combined = rare.Multiply(new(1, 3), budget).Add(rare.Multiply(new(2, 3), budget), budget);
        Assert.That(combined.ToDouble(budget), Is.EqualTo(double.Epsilon));
        Assert.That(ExactProbability.Zero.Multiply(rare, budget), Is.EqualTo(ExactProbability.Zero));
    }

    [Test]
    public void SharedTwoActivationLimitRetainsTheThirdSourceCopy() {
        EngineCase fixture = new("activation-limit", new([new("effect", 3, new(DrawCount: 1, ActivationLimit: 2)), new("ordinary", 3)], 3, []), new());
        M.Card effect = fixture.Prepare().Cards[0];
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        CardProbability.Internal.DrawResolution.DrawResolution result = DrawEffectResolver.Resolve(3, [effect], 3, budget);
        Assert.That(result.Scenarios.Keys.Any(composition => composition.EffectCounts[0] == 1), Is.True);
        Assert.That(DrawEffectResolver.CanUseCountStateFastPath([effect], 6, 3), Is.True);
    }

    [Test]
    public void SelectorCompilationHonorsEntryStorageIndependentlyOfWork() {
        M.CardSelector selector = M.CardSelector.AnyOfSelectors(Enumerable.Range(0, 1024).Select(index => M.CardSelector.Category("role" + index)));
        M.Card entry = new("entry", 1, [], M.DrawEffectPlan.Draw(1, false));
        M.Card[] cards = Enumerable.Repeat(entry, 32769).ToArray();
        Core.ProbabilityCalculationLimitException error = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            CardProbability.Internal.Compilation.CardSelectorCompiler.Compile(selector, cards, new(Core.CalculationWorkPolicy.Interactive)))!;
        Assert.That(error.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void OrderedCompressedStorageFailureRemainsTyped() {
        EngineCase fixture = new("compressed-storage", new([
            new("source", 1, new(DrawCount: 2, HiddenDeckCost: 2)), new("ordinary", 262144)
        ], 1, [new("complete", [])]), new());
        PreparedCalculation input = fixture.Prepare();
        Core.ProbabilityCalculationLimitException error = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            new CardProbability.Internal.DrawResolution.PostDrawCalculation(input.Cards, 1, new(Core.CalculationWorkPolicy.Interactive))
                .Calculate(input.Combos, input.Groups))!;
        Assert.That(error.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }
    private static Dictionary<string, BigInteger> Compositions(EngineCase fixture) {
        CalculationRequest request = fixture.Request;
        OracleModel model = new(request);
        DeckEntry[] order = request.Entries.SelectMany(entry => Enumerable.Repeat(entry, entry.Copies)).ToArray();
        string[] effects = request.Entries.Where(entry => entry.Effect?.DrawCount is not null).Select(entry => entry.Id).ToArray();
        Dictionary<string, BigInteger> counts = new(StringComparer.Ordinal);
        Visit(0);
        return counts;
        void Visit(int position) {
            if (position == order.Length) {
                List<DeckEntry>? hand = DrawSequenceOracle.Resolve(fixture, model, order, out _);
                Assert.That(hand, Is.Not.Null);
                string key = hand!.Count(entry => entry.Effect?.DrawCount is null) + "|" + string.Join(',', effects.Select(id => hand.Count(entry => entry.Id == id)));
                counts[key] = counts.GetValueOrDefault(key) + 1;
                return;
            }
            for (int selected = position; selected < order.Length; selected++) {
                (order[position], order[selected]) = (order[selected], order[position]);
                Visit(position + 1);
                (order[position], order[selected]) = (order[selected], order[position]);
            }
        }
    }

    private static BigInteger Factorial(int copies) {
        BigInteger value = BigInteger.One;
        for (int copy = 2; copy <= copies; copy++) {
            value *= copy;
        }
        return value;
    }
}
