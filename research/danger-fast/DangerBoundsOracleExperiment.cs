using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Isolated approximation oracle validation"), NonParallelizable]
public class DangerBoundsOracleExperiment {
    [TestCase("distinct", "beam")]
    [TestCase("same", "beam")]
    [TestCase("search", "beam")]
    [TestCase("growth", "beam")]
    [TestCase("lock", "beam")]
    [TestCase("failure", "beam")]
    [TestCase("distinct", "continuation")]
    [TestCase("same", "continuation")]
    [TestCase("search", "continuation")]
    [TestCase("growth", "continuation")]
    [TestCase("lock", "continuation")]
    [TestCase("failure", "continuation")]
    [TestCase("distinct", "anonymous")]
    [TestCase("same", "anonymous")]
    [TestCase("search", "anonymous")]
    [TestCase("growth", "anonymous")]
    [TestCase("lock", "anonymous")]
    [TestCase("failure", "anonymous")]
    [TestCase("ordinary-same-identity", "anonymous")]
    [TestCase("duplicate-copies", "anonymous")]
    [TestCase("max-only", "anonymous")]
    [TestCase("direct-danger", "anonymous")]
    [TestCase("distinct", "anonymous-batch")]
    [TestCase("same", "anonymous-batch")]
    [TestCase("search", "anonymous-batch")]
    [TestCase("growth", "anonymous-batch")]
    [TestCase("lock", "anonymous-batch")]
    [TestCase("failure", "anonymous-batch")]
    [TestCase("ordinary-same-identity", "anonymous-batch")]
    [TestCase("duplicate-copies", "anonymous-batch")]
    [TestCase("max-only", "anonymous-batch")]
    [TestCase("direct-danger", "anonymous-batch")]
    [TestCase("distinct", "anonymous-stratum")]
    [TestCase("same", "anonymous-stratum")]
    [TestCase("search", "anonymous-stratum")]
    [TestCase("growth", "anonymous-stratum")]
    [TestCase("lock", "anonymous-stratum")]
    [TestCase("failure", "anonymous-stratum")]
    [TestCase("ordinary-same-identity", "anonymous-stratum")]
    [TestCase("duplicate-copies", "anonymous-stratum")]
    [TestCase("max-only", "anonymous-stratum")]
    [TestCase("direct-danger", "anonymous-stratum")]
    [TestCase("two-strata", "anonymous")]
    [TestCase("two-strata", "anonymous-stratum")]
    [TestCase("repeated-observer", "anonymous-stratum")]
    public void ContainsIndependentPhysicalOracle(string shape, string method) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<CategoryBase> firstCategories = shape is "two-strata" or "repeated-observer" ? [] : [a];
        Card first = new(firstCategories, copies: shape == "duplicate-copies" ? 2 : 1, id: "first", externalCardId: 43316238,
            drawOncePerTurn: false, drawOperation: DrawOperation.DangerReveal);
        Card second = new([], id: "second", externalCardId: shape is "same" or "duplicate-copies" ? 43316238 : 70711847,
            drawOncePerTurn: false, drawOperation: DrawOperation.DangerReveal);
        Card special = shape switch {
            "search" => new([], id: "special", search: new(CardSelector.For(a)), oncePerTurn: false),
            "growth" => new([], id: "special", drawCount: 2),
            "lock" => new([], id: "special", drawCount: 1, blocksLaterDraws: true),
            "failure" => new([], id: "special", drawCount: 1, postDrawDiscardCount: 1,
                postDrawSelector: CardSelector.For(new CategoryBase("Absent"))),
            "ordinary-same-identity" => new([], id: "special", externalCardId: 43316238),
            "max-only" or "direct-danger" => new([], id: "special", drawCount: 1,
                postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(a)),
            "two-strata" or "repeated-observer" => new([], id: "special", drawCount: 2, postDrawDiscardCount: 1,
                postDrawSelector: CardSelector.For(a)),
            _ => new([], id: "special")
        };
        List<Card> cards = [first, second, new([a, b], id: "overlap"), new([b], id: "b"), special];
        if (shape == "two-strata") {
            cards.Add(new([a], 2, id: "a"));
        }
        if (shape == "repeated-observer") {
            cards.Add(new([a], id: "a"));
            // Filtering excludes any newly drawn Danger, so its tier cannot
            // shield the uncertain retained A/B pool from this observation.
            cards.Add(new([], id: "again", drawCount: 1, postDrawDiscardCount: 1,
                postDrawSelector: CardSelector.For(a)));
        }
        List<Combo> combos = [new([new(a, 1, 5), new(b, 1, 5)], groupId: "g"),
            new([ComboCategory.SameCard([a, b], 1, 5)], groupId: "g"),
            new([new(a, 0, 0), new(b, 1, 5)], groupId: "h")];
        if (shape == "max-only") {
            // Every row is maximum-only: unknown ordinary A cards are tier4,
            // so a held usable Danger cannot justify skipping revelation.
            combos = [new([new(a, 0, 0)], groupId: "g"), new([new(b, 0, 0)], groupId: "g"),
                new([new(a, 0, 0), new(b, 0, 0)], groupId: "h")];
        }
        if (shape == "direct-danger") {
            // Direct-card relevance protects the explicit source at tier1.
            combos = [new([], groupId: "g", cards: [new("first", 1, 5)]),
                new([new(b, 1, 5)], groupId: "g"), new([new(a, 0, 0)], groupId: "h")];
        }
        List<ComboGroup> groups = [new("g", "Positive"), new("h", "Exclusion"), new("empty", "Empty")];
        Dictionary<string, int?> identities = cards.ToDictionary(card => card.Id, card => card.ExternalCardId);
        DrawActivationPriority custom = new(DrawActivationMode.CustomPriority, ["special", "second", "first"]);
        DrawActivationPriority[] priorities = shape is "two-strata" or "repeated-observer" ? [custom] : [DrawActivationPriority.Default, custom];
        foreach (DrawActivationPriority priority in priorities) {
            TwoPhaseSearchOracle.Rational[] exact = TwoPhaseSearchOracle.Count(cards, 3, combos, groups, priority, identities);
            foreach (int capacity in new[] { 1, 4, 1000 }) {
                (BigInteger[] Lower, BigInteger[] Upper, BigInteger Denominator)? previous = null;
                foreach (long allowance in new[] { 10_000L, 100_000L, 5_000_000L }) {
                    (BigInteger[] Lower, BigInteger[] Upper, BigInteger Denominator) result = Calculate();
                    for (int row = 0; row < exact.Length; row++) {
                        Assert.That(result.Lower[row] * exact[row].Denominator <= exact[row].Numerator * result.Denominator,
                            Is.True, $"{shape}, cap={capacity}, Work={allowance}, row={row} lower");
                        Assert.That(result.Upper[row] * exact[row].Denominator >= exact[row].Numerator * result.Denominator,
                            Is.True, $"{shape}, cap={capacity}, Work={allowance}, row={row} upper");
                        if (previous is not null) {
                            Assert.That(result.Lower[row] >= previous.Value.Lower[row], Is.True, "Nested Work lower must tighten");
                            Assert.That(result.Upper[row] <= previous.Value.Upper[row],
                                Is.True, "Nested Work upper must tighten");
                        }
                    }
                    previous = result;
                    (BigInteger[] Lower, BigInteger[] Upper, BigInteger Denominator) repeat = Calculate();
                    Assert.That(repeat.Lower, Is.EqualTo(result.Lower));
                    Assert.That(repeat.Upper, Is.EqualTo(result.Upper));

                    (BigInteger[], BigInteger[], BigInteger) Calculate() {
                        PostDrawCalculation calculation = new(cards, 3, new(new(allowance)), priority);
                        if (method == "continuation") {
                            PostDrawCalculation.ContinuationResult bounds = calculation.ResearchContinuation(combos, groups, capacity, 20);
                            return (bounds.Lower.Select(value => new BigInteger(value)).ToArray(),
                                bounds.Upper.Select(value => new BigInteger(value)).ToArray(), bounds.Scale);
                        }
                        if (method is "anonymous" or "anonymous-batch" or "anonymous-stratum") {
                            PostDrawCalculation.AnonymousResult bounds = calculation.ResearchAnonymous(combos, groups, capacity,
                                method == "anonymous-batch", method == "anonymous-stratum");
                            if ((shape is "two-strata" or "repeated-observer") && method == "anonymous-stratum" && capacity == 1000 && allowance == 5_000_000L) {
                                Assert.That(bounds.RetainedDiscards, Is.GreaterThan(0), "Exercise deletion from the observed-hand stratum");
                                Assert.That(bounds.RetainedReveals, Is.GreaterThan(0), "Exercise a genuinely uncertain retained subset, not only V=0 or V=K");
                                if (shape == "two-strata") {
                                    Assert.That(bounds.JointStratumReveals, Is.GreaterThan(0), "Exercise uncertain K jointly with newly drawn anonymous U");
                                    Assert.That(bounds.EmptyRetainedReveals, Is.GreaterThan(0), "Lost K labels must stay outside I even when V becomes zero");
                                }
                                else {
                                    Assert.That(bounds.RetainedObserverReveals, Is.GreaterThan(0), "A later policy removal must observe uncertain K again");
                                }
                            }
                            BigInteger unresolvedMass = bounds.Denominator - bounds.Resolved;
                            Assert.That(unresolvedMass.Sign, Is.GreaterThanOrEqualTo(0));
                            return (bounds.Wins, bounds.Wins.Select(value => value + unresolvedMass).ToArray(), bounds.Denominator);
                        }
                        PostDrawCalculation.BeamResult boundsBeam = calculation.ResearchBeam(combos, groups, capacity);
                        BigInteger unresolved = boundsBeam.Denominator - boundsBeam.Resolved;
                        Assert.That(unresolved.Sign, Is.GreaterThanOrEqualTo(0));
                        return (boundsBeam.Wins, boundsBeam.Wins.Select(value => value + unresolved).ToArray(), boundsBeam.Denominator);
                    }
                }
                if (capacity == 1000 && shape != "failure") {
                    for (int row = 0; row < exact.Length; row++) {
                        BigInteger width = previous!.Value.Upper[row] - previous.Value.Lower[row];
                        Assert.That(width <= (method == "continuation" ? 64 : 0), Is.True,
                            "Exhausted small problem converges exactly or to the proved fixed-point rounding envelope");
                    }
                }
            }
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AnonymousInterruptionBeforeLastTerminalCommitRetainsResidual(bool wholeBatches, bool retainedStratum) {
        CategoryBase a = new("A");
        List<Card> cards = [new([], id: "danger", externalCardId: 43316238,
            drawOncePerTurn: false, drawOperation: DrawOperation.DangerReveal),
            new([a], id: "a"), new([], id: "blank"), new([], id: "search", search: new(CardSelector.For(a)))];
        List<Combo> combos = [new([new(a, 1, 4)]), new([new(a, 0, 0)])];
        List<ComboGroup> groups = [];
        Dictionary<string, int?> identities = cards.ToDictionary(card => card.Id, card => card.ExternalCardId);
        TwoPhaseSearchOracle.Rational[] exact = TwoPhaseSearchOracle.Count(cards, 2, combos, groups, DrawActivationPriority.Default, identities);
        WorkBudget fullBudget = new(CalculationWorkPolicy.Unlimited);
        PostDrawCalculation.AnonymousResult full = new PostDrawCalculation(cards, 2, fullBudget).ResearchAnonymous(combos, groups, 1000, wholeBatches, retainedStratum);
        Assert.That(full.Resolved, Is.EqualTo(full.Denominator));
        // Work may still be charged while scanning empty draw rows after the
        // last terminal. Find the first budget that actually resolves all mass,
        // then stop immediately before that threshold rather than before the
        // last incidental charge of the unlimited execution.
        long lower = 1, upper = fullBudget.Spent;
        while (lower < upper) {
            long middle = lower + (upper - lower) / 2;
            PostDrawCalculation.AnonymousResult? trial = TryCalculate(middle);
            if (trial is not null && trial.Resolved == trial.Denominator) {
                upper = middle;
            }
            else {
                lower = middle + 1;
            }
        }
        PostDrawCalculation.AnonymousResult? interrupted = TryCalculate(lower - 1);
        Assert.That(interrupted, Is.Not.Null, "Fixture must pass preparation before its final terminal commit");
        Assert.That(interrupted.Stop, Is.EqualTo("Work"));
        Assert.That(interrupted.Resolved < interrupted.Denominator, Is.True, "Last terminal remains wholly unresolved");
        BigInteger residual = interrupted.Denominator - interrupted.Resolved;
        for (int row = 0; row < exact.Length; row++) {
            Assert.That(interrupted.Wins[row] * exact[row].Denominator <= exact[row].Numerator * interrupted.Denominator, Is.True);
            Assert.That((interrupted.Wins[row] + residual) * exact[row].Denominator >= exact[row].Numerator * interrupted.Denominator, Is.True);
        }

        PostDrawCalculation.AnonymousResult? TryCalculate(long allowance) {
            try {
                WorkBudget limited = new(new(allowance));
                return new PostDrawCalculation(cards, 2, limited).ResearchAnonymous(combos, groups, 1000, wholeBatches, retainedStratum);
            }
            catch (ProbabilityCalculationLimitException) {
                // Preparation has not yet established a bounded result.
                return null;
            }
        }
    }
}
