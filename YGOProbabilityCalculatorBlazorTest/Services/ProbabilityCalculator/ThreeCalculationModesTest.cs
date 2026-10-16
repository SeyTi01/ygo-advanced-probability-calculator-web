using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class ThreeCalculationModesTest {
    internal static readonly CategoryBase Target = new("Target");
    internal static readonly List<ComboGroup> Groups = [new("g", "Routes"), new("empty", "Empty")];
    internal static readonly List<Combo> Combos = [
        new([new(Target, 1, 6)], "Target", groupId: "g"),
        new([new(Target, 0, 0)], "No target", groupId: "g"),
        new([new(Target, 0, 1)], "At most one"),
        new([], "No source", cards: [new("d", 0, 0)])
    ];

    internal static List<Card> Counterexample(string kind) => kind == "consumed"
        ? [new([], 1, id: "s", search: new(CardSelector.For(Target), false)),
            new([Target], 1, id: "d", drawCount: 1), new([], 1, id: "f")]
        : [new([], 1, id: "s", search: new(CardSelector.For(Target), false)),
            new([Target], 1, id: "a"), new([], 2, id: "f"),
            new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: kind == "discard" ? 1 : 0,
                postDrawSelector: kind == "discard" ? CardSelector.For(Target) : null,
                preDrawDeckRemovalCount: kind == "banish" ? 1 : 0, postDrawDeckMillCount: kind == "mill" ? 1 : 0)];

    internal static ProbabilityCalculationResult Run(List<Card> cards, int hand, CalculationModel mode) =>
        CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, Combos, hand, Groups,
            options: new(Mode: mode)).Json), mode);

    [TestCase("consumed", 1, "1", "2", "1", "3", CalculationAccuracy.Bounded)]
    [TestCase("discard", 2, "1", "2", "1", "2", CalculationAccuracy.Exact)]
    [TestCase("banish", 2, "5", "6", "23", "30", CalculationAccuracy.Bounded)]
    [TestCase("mill", 2, "5", "6", "23", "30", CalculationAccuracy.Bounded)]
    public void FastAndPhysicalExactOwnEveryRationalRow(
        string kind,
        int hand,
        string upperN,
        string upperD,
        string physicalN,
        string physicalD,
        CalculationAccuracy accuracy
    ) {
        List<Card> cards = Counterexample(kind);
        ProbabilityCalculationResult fast = Run(cards, hand, CalculationModel.Fast);
        Assert.That(fast.Contract!.Accuracy, Is.EqualTo(accuracy));
        Assert.That(fast.Contract.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction(upperN, upperD)));
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(cards, hand, Combos, Groups);
        Assert.That(physical.Wins[1][1] * BigInteger.Parse(physicalD), Is.EqualTo(physical.Outcomes * BigInteger.Parse(physicalN)));
        ProbabilityFraction[] fractions = [fast.Contract.Fractions.Total, .. fast.Contract.Fractions.Combos, .. fast.Contract.Fractions.Groups];
        for (int row = 0; row < fractions.Length; row++) {
            Assert.That(BigInteger.Parse(fractions[row].Numerator) * physical.Outcomes,
                Is.EqualTo(physical.Wins[accuracy == CalculationAccuracy.Exact ? 1 : 2][row] * BigInteger.Parse(fractions[row].Denominator)), $"row {row}");
        }
        if (kind is "banish" or "mill") {
            Assert.That(fast.SearchAvailability!.Combos[0].Lower, Is.EqualTo(new ProbabilityFraction("7", "10")));
        }
        if (accuracy == CalculationAccuracy.Bounded) {
            SearchAvailabilityInterval[] intervals = [fast.SearchAvailability!.Total!, .. fast.SearchAvailability.Combos, .. fast.SearchAvailability.Groups];
            for (int row = 0; row < intervals.Length; row++) {
                Assert.That(BigInteger.Parse(intervals[row].Lower.Numerator) * physical.Outcomes,
                    Is.LessThanOrEqualTo(physical.Wins[1][row] * BigInteger.Parse(intervals[row].Lower.Denominator)), $"lower row {row}");
            }
        }
        ProbabilityCalculationResult exact = Run(cards, hand, CalculationModel.Exact);
        Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(exact.Contract.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction(physicalN, physicalD)));
        Assert.That(exact.SearchAvailability, Is.Null);
        PhysicalSearchTest.AssertRows(exact, physical.Wins[1], physical.Outcomes);
    }

    [TestCase("ordinary"), TestCase("count"), TestCase("ordered"), TestCase("exchangeable"), TestCase("deferred")]
    public void ProvenDrawRoutesHaveExactAllRowsAndNoCertificatePass(string kind) {
        CategoryBase material = new("Material");
        List<Card> cards = [new([Target], 2, id: "a"), new([material], 1, id: "m"), new([], 2, id: "f")];
        if (kind != "ordinary") {
            cards.Add(new([], 1, id: "d", drawCount: kind == "exchangeable" ? 2 : 1,
                drawOncePerTurn: kind != "exchangeable", firstActivationOnly: kind == "ordered", blocksLaterDraws: kind == "ordered",
                postDrawDiscardCount: kind is "exchangeable" or "deferred" ? 1 : 0,
                postDrawSelector: kind == "deferred" ? CardSelector.For(material) : null));
        }
        if (kind == "ordered") {
            cards.Add(new([], 1, id: "e", drawCount: 1));
        }
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(cards, 2, Combos, Groups);
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = Run(cards, 2, mode);
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(result.SearchAvailability, Is.Null, "proven routes need no secondary pass");
            ProbabilityFraction[] rows = [result.Contract.Fractions!.Total, .. result.Contract.Fractions.Combos, .. result.Contract.Fractions.Groups];
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(BigInteger.Parse(rows[row].Numerator) * oracle.Total,
                    Is.EqualTo(oracle.Wins[row] * BigInteger.Parse(rows[row].Denominator)), $"{kind}/{mode}/{row}");
            }
        }
    }

    [TestCase("plain"), TestCase("one-draw2"), TestCase("three-draw2")]
    public async Task ThreeSourceSearchExampleUsesIdenticalProvenFractions(string name) {
        SessionState session = await SearchAvailabilityCertificateTest.Example(name);
        ProbabilityCalculationResult RunExample(CalculationModel mode) => new ProbabilityCalculatorService().CalculateProbabilityResults(
            session.Cards, session.Combos, session.HandSize, session.ComboGroups, CalculationWorkPolicy.Interactive, new(Mode: mode));
        ProbabilityCalculationResult fast = RunExample(CalculationModel.Fast);
        ProbabilityCalculationResult exact = RunExample(CalculationModel.Exact);
        Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(JsonSerializer.Serialize(exact.Contract.Fractions), Is.EqualTo(JsonSerializer.Serialize(fast.Contract!.Fractions)));
        Assert.That(exact.SearchAvailability, Is.Null);
        Assert.That(fast.SearchAvailability, Is.Null);
    }

    [Test]
    public void OrderedNoLossNineTenthsIsExactInBothModes() {
        List<Card> cards = CalculationCapabilityTest.OrderedDeck();
        List<Combo> combos = [new([new(cards[1].Categories[0], 1, 6)], "Target")];
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(cards, combos, 2, null,
                CalculationWorkPolicy.Default, new(Mode: mode));
            Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("9", "10")));
            Assert.That(result.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(result.SearchAvailability, Is.Null);
        }
    }

    [Test]
    public void OpeningRemovesAllExecutionFieldsAndAnswersADifferentQuestion() {
        List<Card> cards = Counterexample("discard");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, Combos, 2, Groups, options: new(Mode: CalculationModel.OpeningHand));
        CalculationInput input = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
        Assert.That(input.Options!.Mode, Is.EqualTo(CalculationModel.OpeningHand));
        Assert.That(input.Cards.All(card => card.DrawCount is null && card.Search is null && card.PreDrawRemovalCount == 0
            && card.PostDrawDiscardCount == 0 && card.PreDrawDeckRemovalCount == 0 && card.PostDrawDeckMillCount == 0
            && !card.FirstActivationOnly && !card.BlocksLaterDraws && card.EffectProfile is null), Is.True);
        ProbabilityCalculationResult opening = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(opening.Contract!.Reference, Is.EqualTo(CalculationResultContract.OpeningReference));
        Assert.That(opening.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(opening.SearchAvailability, Is.Null);
        Assert.That(opening.Contract.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("2", "5")));
        Assert.That(cards[^1].PostDrawDiscardCount, Is.EqualTo(1));
        ProbabilityCalculationResult direct = new ProbabilityCalculatorService().CalculateProbabilityResults(cards, Combos, 2, Groups,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.OpeningHand));
        SearchAvailabilityCertificateTest.AssertPrimary(opening, direct);
        Assert.That(direct.ComboProbabilities[0].Probability, Is.Not.EqualTo(Run(cards, 2, CalculationModel.Fast).ComboProbabilities[0].Probability));
    }

    [TestCase("{\"Mode\":999}"), TestCase("{\"Mode\":\"Exact\"}"), TestCase("{\"Mode\":null}")]
    [TestCase("{\"CertifySearchAvailability\":false}"), TestCase("{\"CertifySearchAvailability\":true}")]
    [TestCase("{\"Mode\":0,\"mode\":2}"), TestCase("{\"Mode\":0,\"Unknown\":true}")]
    public void UnknownOrObsoleteWorkerOptionsAreInputFailures(string options) {
        JsonObject input = JsonNode.Parse(CalculationSnapshot.Capture(Counterexample("consumed"), Combos, 1, Groups).Json)!.AsObject();
        input["Options"] = JsonNode.Parse(options);
        CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(input.ToJsonString()), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.Result, Is.Null);
        Assert.That(response.FailureKind, Is.EqualTo(CalculationFailureKind.Input));
    }

    [Test]
    public void UnknownNativeModesAndContradictoryCompatibilityAdaptersFail() {
        ProbabilityCalculatorService engine = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.CalculateProbabilityResults([], [], 0, null,
            CalculationWorkPolicy.Default, new(Mode: (CalculationModel)999)));
        Assert.Throws<ArgumentException>(() => CalculationSnapshot.Capture([], [], 0, [], drawEffectsEnabled: false, options: new(Mode: CalculationModel.Exact)));
        Assert.Throws<ArgumentException>(() => CalculationSnapshot.Capture([], [], 0, [], options: new(CertifySearchAvailability: true, Mode: CalculationModel.Exact)));
        Assert.DoesNotThrow(() => engine.CalculateProbabilityForCombos(Counterexample("discard"), Combos, 2,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact)));
    }

    [Test]
    public void WorkerRejectsTamperedModesAccuracyFractionsAndIncompleteBounds() {
        JsonObject response = JsonNode.Parse(CalculationWire.Execute(CalculationSnapshot.Capture(Counterexample("consumed"), Combos, 1, Groups).Json))!.AsObject();
        foreach ((string field, JsonNode? value) in new (string, JsonNode?)[] {
            ("Version", JsonValue.Create(999)), ("RequestedMode", JsonValue.Create(999)),
            ("Accuracy", JsonValue.Create(999)), ("Reference", JsonValue.Create("other")), ("Fractions", null)
        }) {
            JsonObject changed = response.DeepClone().AsObject();
            changed["Result"]!["Contract"]![field] = value;
            Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(changed.ToJsonString()), field);
        }
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response.ToJsonString(), CalculationModel.Exact));
        response["Result"]!["SearchAvailability"]!["Combos"] = new JsonArray();
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response.ToJsonString()));
    }

    [Test]
    public void SecondaryStorageLimitKeepsAllFastRowsButPrimaryLimitIsAFailure() {
        List<ComboGroup> manyGroups = Enumerable.Range(0, 17000).Select(index => new ComboGroup("g" + index, "Group")).ToList();
        List<Card> cards = Counterexample("consumed");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, Combos, 1, manyGroups,
            CalculationWorkPolicy.Interactive, options: new(Mode: CalculationModel.Fast));
        ProbabilityCalculationResult fast = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(fast.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Uncertified));
        Assert.That(fast.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.Limited));
        Assert.That(fast.SearchAvailability.Detail, Does.Contain("Storage"));
        Assert.That(fast.GroupProbabilities, Has.Count.EqualTo(manyGroups.Count));
        Assert.That(fast.Contract.Fractions!.Groups.Length, Is.EqualTo(manyGroups.Count));
        Assert.That(fast.ComboProbabilities[0].Probability, Is.EqualTo(.5));
        CalculationSnapshot primaryLimit = CalculationSnapshot.Capture(cards, Combos, 1, Groups, new(1), options: new(Mode: CalculationModel.Exact));
        CalculationResponse failure = JsonSerializer.Deserialize(CalculationWire.Execute(primaryLimit.Json), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(failure.Result, Is.Null);
        Assert.That(failure.LimitReason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        Assert.That(failure.Contract!.Completion, Is.EqualTo(CalculationCompletion.WorkLimit));
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(primaryLimit.Json)));
    }

    [Test]
    public void TypedCancellationAndUnknownFailureMetadataNeverBecomeSuccess() {
        CalculationResponse cancellation = new(null, "Cancelled", FailureKind: CalculationFailureKind.Cancelled,
            Contract: CalculationResultContract.Failure(CalculationModel.Exact, CalculationCompletion.Cancelled, "Cancelled"));
        string json = JsonSerializer.Serialize(cancellation, CalculationJsonContext.Default.CalculationResponse);
        Assert.Throws<OperationCanceledException>(() => CalculationWire.ReadResult(json, CalculationModel.Exact));
        JsonObject corrupt = JsonNode.Parse(json)!.AsObject();
        corrupt["Contract"]!["ExecutedMode"] = 999;
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(corrupt.ToJsonString()));
    }

    [TestCase("null"), TestCase("[]")]
    [TestCase("{\"Options\":{\"Mode\":999},\"Options\":{\"Mode\":0}}")]
    public void MalformedOrDuplicateRootCannotHideAnUnknownRequestedMode(string json) {
        CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(json), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.FailureKind, Is.EqualTo(CalculationFailureKind.Input));
        Assert.That(response.Result, Is.Null);
    }

    [Test]
    public void SecondaryProofErrorPreservesEveryCompletedFastRow() {
        List<Card> cards = Counterexample("consumed");
        WorkBudget primaryBudget = new(CalculationWorkPolicy.Default);
        ExactCalculationResult exact = new DrawEffectCalculation(cards, 1, primaryBudget).Calculate(Combos, Groups);
        // Simulate a defective secondary proof owner. It must never erase a
        // complete primary or turn an exception into an exactness claim.
        DrawEffectCalculation incomplete = new(cards, 1, new(CalculationWorkPolicy.Default));
        ProbabilityCalculationResult result = ProbabilityCalculatorService.CompleteResult(Combos, Groups,
            CalculationModel.Fast, exact, primaryBudget, incomplete);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Uncertified));
        Assert.That(result.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.Error));
        Assert.That(result.SearchAvailability.Detail, Does.Contain("InvalidOperationException"));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(Combos.Count));
        Assert.That(result.GroupProbabilities, Has.Count.EqualTo(Groups.Count));
        Assert.That(result.ComboProbabilities[0].Probability, Is.EqualTo(.5));
        Assert.That(result.SearchAvailability.Total, Is.Null);
    }

    [TestCase("zero-capacity"), TestCase("zero-target"), TestCase("inactive-zero"), TestCase("deferred-unobserved-targets")]
    public void BoundaryAndDeferredSearchProofsApplyToTheCompleteAcceptedRequest(string name) {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups);
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, options: new(Mode: mode));
            ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(result.SearchAvailability, Is.Null);
            ProbabilityFraction[] rows = [result.Contract.Fractions!.Total, .. result.Contract.Fractions.Combos, .. result.Contract.Fractions.Groups];
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(BigInteger.Parse(rows[row].Numerator) * physical.Outcomes,
                    Is.EqualTo(physical.Wins[1][row] * BigInteger.Parse(rows[row].Denominator)), $"{name}/{mode}/{row}");
            }
        }
    }
}
