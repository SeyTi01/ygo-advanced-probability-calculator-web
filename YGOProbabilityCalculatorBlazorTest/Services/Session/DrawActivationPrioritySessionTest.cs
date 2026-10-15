using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class DrawActivationPrioritySessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
    private static SessionState Session(DrawActivationPriority priority) => new() {
        Cards = [new([], id: "a", drawCount: 1), new([], id: "b", drawCount: 2), new([], 2, id: "f")],
        Combos = [new([], cards: [new("b", 1, 1)])], HandSize = 2, DrawActivationPriority = priority
    };

    [Test]
    public async Task OptionalPolicyRoundTripsSaveRecoveryCodecAndShareWithoutSchemaBump() {
        SessionState session = Session(new(DrawActivationMode.CustomPriority, ["b", "a", "removed"]));
        string saved = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", saved)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.DrawActivationPriority.Mode, Is.EqualTo(DrawActivationMode.CustomPriority));
        Assert.That(loaded.DrawActivationPriority.CardIds, Is.EqualTo(new[] { "b", "a" }));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(saved));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 2, [], options: new(loaded.DrawActivationPriority));
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Options!.DrawActivationPriority!.CardIds, Is.EqualTo(new[] { "b", "a" }));
        Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public async Task MissingPolicyInReleasedSchemasDefaultsToEncounterOrder(int schema) {
        SessionState session = await Codec().LoadSessionAsync($$"""{"SchemaVersion":{{schema}},"Cards":[],"HandSize":5}""");
        Assert.That(session.DrawActivationPriority.Mode, Is.EqualTo(DrawActivationMode.EncounterOrder));
        Assert.That(session.DrawActivationPriority.CardIds, Is.Empty);
    }

    [TestCase("{\"Mode\":99}"), TestCase("{\"Mode\":1,\"CardIds\":[\"a\",\"a\"]}")]
    [TestCase("{\"Mode\":1,\"CardIds\":[null]}"), TestCase("{\"Mode\":1,\"CardIds\":[\" \"]}")]
    public void InvalidPolicyIsRejectedAtSessionAndWorkerBoundaries(string policy) {
        string json = Codec().SerializeSession(Session(DrawActivationPriority.Default));
        JsonObject document = JsonNode.Parse(json)!.AsObject();
        document[nameof(SessionState.DrawActivationPriority)] = JsonNode.Parse(policy);
        Assert.That(async () => await Codec().LoadSessionAsync(document.ToJsonString()), Throws.Exception);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(Session(DrawActivationPriority.Default).Cards, [], 2, [], options: new());
        JsonObject input = JsonNode.Parse(snapshot.Json)!.AsObject();
        input["Options"]!["DrawActivationPriority"] = JsonNode.Parse(policy);
        CalculationResponse response = JsonSerializer.Deserialize<CalculationResponse>(CalculationWire.Execute(input.ToJsonString()))!;
        Assert.That(response.Result, Is.Null);
        Assert.That(response.FailureKind, Is.EqualTo(CalculationFailureKind.Input));
    }

    [Test]
    public void PolicyOwnsInputAndSnapshotCannotBeMutatedByLaterEdits() {
        string[] ids = ["b", "a"];
        DrawActivationPriority policy = new(DrawActivationMode.CustomPriority, ids);
        SessionState session = Session(policy);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, 2, [], options: new(policy));
        ids[0] = "f";
        session.Cards.Clear();
        Assert.That(policy.CardIds, Is.EqualTo(new[] { "b", "a" }));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.CardIds)[0] = "f");
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Options!.DrawActivationPriority!.CardIds, Is.EqualTo(new[] { "b", "a" }));
    }

    [Test]
    public void InactiveAndEffectlessPreferencesSurviveAndNewIdentitiesAppend() {
        DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, ["b", "a"]);
        Card a = new([], id: "a", drawCount: 1);
        Card b = new([], id: "b", drawCount: 2);
        Card duplicate = a.Duplicate();
        DrawActivationPriority retained = priority.ForWorkspace([b.WithActive(false).WithCopies(0), a.WithName("Renamed").WithDrawCount(null), duplicate]);
        Assert.That(retained.CardIds, Is.EqualTo(new[] { "b", "a", duplicate.Id }));
        Assert.That(retained.Rank(duplicate.Id), Is.EqualTo(2));
        Assert.That(retained.ForWorkspace([a, duplicate]).CardIds, Is.EqualTo(new[] { "a", duplicate.Id }));
    }

    [Test]
    public void PinnedContextDistinguishesPoliciesAndRanksButIgnoresDisabledPreferences() {
        SessionState session = Session(DrawActivationPriority.Default);
        PinnedCalculationContext Capture(DrawActivationPriority policy, bool draws = true) => PinnedCalculationContext.Capture(
            0, 2, session.Cards, session.Combos, [], _ => Guid.Empty, _ => Guid.Empty, draws ? CalculationModel.Fast : CalculationModel.OpeningHand, priority: policy);
        PinnedCalculationContext fifo = Capture(DrawActivationPriority.Default);
        PinnedCalculationContext first = Capture(new(DrawActivationMode.CustomPriority, ["a", "b"]));
        PinnedCalculationContext reverse = Capture(new(DrawActivationMode.CustomPriority, ["b", "a"]));
        Assert.That(fifo.DeckDefinition, Is.Not.EqualTo(first.DeckDefinition));
        Assert.That(first.DeckDefinition, Is.Not.EqualTo(reverse.DeckDefinition));
        Assert.That(first.Description, Does.Contain("Custom draw priority"));
        Assert.That(Capture(new(DrawActivationMode.EncounterOrder, ["b", "a"])).DeckDefinition, Is.EqualTo(fifo.DeckDefinition));
        Assert.That(Capture(new(DrawActivationMode.CustomPriority, ["b", "a"]), false).DeckDefinition,
            Is.EqualTo(Capture(DrawActivationPriority.Default, false).DeckDefinition));
    }
}
