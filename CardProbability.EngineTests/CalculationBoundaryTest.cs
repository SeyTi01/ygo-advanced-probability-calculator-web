using CardProbability;

namespace CardProbability.EngineTests;

[TestFixture]
public class CalculationBoundaryTest {
    [Test]
    public void OverlappingMembershipsRequireDistinctCopiesAndZeroMaximumExcludesMatches() {
        CalculationRequest request = OverlappingRequest();

        CalculationResult result = CalculationEngine.Calculate(request);

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(result.Accuracy, Is.EqualTo(ProbabilityAccuracy.Exact));
            Assert.That(result.Goals[0].Value.Primary, Is.EqualTo(new RationalProbability("1", "3")));
            Assert.That(result.Goals[1].Value.Primary, Is.EqualTo(new RationalProbability("1", "3")));
            Assert.That(result.Total!.Primary, Is.EqualTo(new RationalProbability("2", "3")));
        });
    }

    [Test]
    public void RequestOwnsNestedCollectionsAndCanBeReused() {
        List<string> members = ["overlap", "left"];
        List<Requirement> requirements = [Requirement.InMembership("left")];
        List<DeckEntry> entries = [new("overlap", 1), new("left", 1), new("other", 1)];
        List<Membership> memberships = [new("left", members)];
        List<Goal> goals = [new("goal", requirements)];
        CalculationRequest request = new(entries, 1, goals, memberships);

        members.Clear();
        requirements.Clear();
        entries.Clear();
        memberships.Clear();
        goals.Clear();

        CalculationResult first = CalculationEngine.Calculate(request);
        CalculationResult second = CalculationEngine.Calculate(request);
        Assert.Multiple(() => {
            Assert.That(first.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(first.Total!.Primary, Is.EqualTo(new RationalProbability("2", "3")));
            Assert.That(second.Total, Is.EqualTo(first.Total));
        });
    }

    [Test]
    public void PreCancelledCallReturnsTerminalCancellationWithoutRows() {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CalculationResult result = CalculationEngine.Calculate(OverlappingRequest(), cancellationToken: cancellation.Token);

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(CalculationStatus.Cancelled));
            Assert.That(result.IsTerminal, Is.True);
            Assert.That(result.Total, Is.Null);
            Assert.That(result.Goals, Is.Empty);
        });
    }

    [Test]
    public async Task ParallelCallsOnSameRequestAreIndependent() {
        CalculationRequest request = OverlappingRequest();
        Task<CalculationResult>[] calls = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => CalculationEngine.Calculate(request)))
            .ToArray();

        CalculationResult[] results = await Task.WhenAll(calls);

        Assert.That(results.All(result =>
            result.Status == CalculationStatus.Completed &&
            result.Total!.Primary == new RationalProbability("2", "3")), Is.True);
    }

    [Test]
    public void LargerHandAndDrawCountsAreSupportedForGenericInputs() {
        CalculationRequest largeHand = new(
            [new("target", 1), new("filler", 7)], 7,
            [new("goal", [Requirement.OfEntry("target")])]
        );
        CalculationRequest largeDraw = new(
            [new("source", 1, new EffectPlan(DrawCount: 7)), new("target", 7)], 1,
            [new("goal", [Requirement.OfEntry("target")])]
        );

        CalculationResult hand = CalculationEngine.Calculate(largeHand);
        CalculationResult draw = CalculationEngine.Calculate(largeDraw);
        Assert.Multiple(() => {
            Assert.That(hand.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(hand.Total!.Primary, Is.EqualTo(new RationalProbability("7", "8")));
            Assert.That(draw.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(draw.Total!.Primary, Is.EqualTo(new RationalProbability("1", "1")));
        });
    }

    [Test]
    public void LargestRepresentablePopulationSupportsCompactOrdinaryCounting() {
        CalculationRequest request = new(
            [new("target", 1), new("filler", int.MaxValue - 1)], 1,
            [new("goal", [Requirement.OfEntry("target")])]);

        CalculationResult result = CalculationEngine.Calculate(request);

        Assert.That(result.Status, Is.EqualTo(CalculationStatus.Completed));
        Assert.That(result.Total!.Primary, Is.EqualTo(new RationalProbability("1", int.MaxValue.ToString())));
    }

    [TestCase(CalculationMode.Fast, 0, 1, "1", "2")]
    [TestCase(CalculationMode.Exact, 0, 1, "1", "2")]
    [TestCase(CalculationMode.Fast, 1, 2, "2", "3")]
    [TestCase(CalculationMode.Exact, 1, 2, "2", "3")]
    public void HugeInfeasibleEffectDoesNotOverflowIntoAnActivation(
        CalculationMode mode, int removal, int handSize, string numerator, string denominator
    ) {
        List<DeckEntry> entries = [
            new("source", 1, new EffectPlan(DrawCount: int.MaxValue, HiddenDeckCost: 1, RemovalCount: removal)),
            new("target", 1)
        ];
        if (handSize == 2) {
            entries.Add(new("filler", 1));
        }
        CalculationRequest request = new(entries, handSize, [new("goal", [Requirement.OfEntry("target")])]);

        CalculationResult result = CalculationEngine.Calculate(request, new CalculationOptions(mode));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(result.Total!.Primary, Is.EqualTo(new RationalProbability(numerator, denominator)));
        });
    }

    [Test]
    public void EqualPolicyKeysDoNotMergeDistinctGoalMemberships() {
        CalculationRequest request = new(
            [
                new("filler", 1, PolicyOrderKey: "filler"),
                new("a", 1, PolicyOrderKey: "same"),
                new("b", 1, PolicyOrderKey: "same")
            ],
            2,
            [new("both", [Requirement.InMembership("a-role"), Requirement.InMembership("b-role")])],
            [new("a-role", ["a"]), new("b-role", ["b"])]
        );

        CalculationResult result = CalculationEngine.Calculate(request);

        // Only the A+B pair satisfies both distinct requirements.
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(CalculationStatus.Completed));
            Assert.That(result.Total!.Primary, Is.EqualTo(new RationalProbability("1", "3")));
        });
    }

    [Test]
    public void DuplicateEntryIdsProduceAnInvalidTerminalResult() {
        CalculationRequest request = new(
            [new("same", 1), new("same", 1)], 1,
            [new("goal", [Requirement.OfEntry("same")])]
        );

        CalculationResult result = CalculationEngine.Calculate(request);
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(CalculationStatus.InvalidInput));
            Assert.That(result.IsTerminal, Is.True);
            Assert.That(result.Total, Is.Null);
        });
    }
    private static CalculationRequest OverlappingRequest() => new(
        [new("overlap", 1), new("left", 1), new("other", 1)],
        2,
        [
            new("distinct", [Requirement.InMembership("left"), Requirement.InMembership("right")]),
            new("exclude", [Requirement.OfEntry("overlap", 0, 0)])
        ],
        [new("left", ["overlap", "left"]), new("right", ["overlap"]) ]
    );
}
