using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.ComboAlternativesTest;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class ComboAlternativesSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new JsonSerializer());

    [Test]
    public async Task FileRecoveryAndShareCodecRoundTripOrderedMixedAlternatives() {
        var metadata = new CategoryBase("Fire", CategorySource.Metadata, "attribute:fire");
        var combo = Or(Direct("a"), Cat(metadata, 0, 0, RequirementMaximumMode.Fixed), Cat(new("Fire"), 2))
            .WithName("Mixed").WithActive(false).WithGroup("g");
        var session = new SessionState { Cards = [new([metadata], id: "a", manualMetadataCategoryKeys: ["attribute:fire"])],
            Combos = [combo], ComboGroups = [new("g", "Group")], HandSize = 3 };
        var json = Codec().SerializeSession(session);
        Assert.That(JsonNode.Parse(json)!["SchemaVersion"]!.GetValue<int>(), Is.EqualTo(3));
        var loaded = await Codec().LoadSessionAsync(json);
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        var link = SessionShareCodec.CreateLink("https://example.invalid/", json);
        Assert.That(link, Does.Contain("/#ygo-session=v1."));
        var shared = await Codec().LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
        Assert.That(Codec().SerializeSession(shared), Is.EqualTo(json));
        Assert.That(shared.Combos[0].AlternativeGroups[0].Alternatives.Select(a => a.Kind), Is.EqualTo(new[] { "Card", "Category", "Category" }));
    }

    [TestCase("null")]
    [TestCase("[null]")]
    [TestCase("[{\"Alternatives\":[]}]")]
    [TestCase("[{\"Operator\":\"AND\",\"Alternatives\":[]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Unknown\",\"Card\":{\"CardId\":\"a\",\"MinCount\":1,\"MaxCount\":2}}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Card\":null}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Card\":{\"CardId\":\"a\",\"MinCount\":2,\"MaxCount\":1}}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Alternatives\":[]}]}]")]
    public void MalformedExpressionsFailBeforeSessionAcceptance(string groups) {
        var json = "{\"SchemaVersion\":3,\"Combos\":[{\"Categories\":[],\"AlternativeGroups\":" + groups + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync(json));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task OldSchemasRetainAndRequirements(int version) {
        var json = $$"""{"SchemaVersion":{{version}},"Combos":[{"Categories":[{"BaseCategory":{"Name":"A"},"MinCount":0,"MaxCount":0}],"Cards":[{"CardId":"a","MinCount":1,"MaxCount":2}]}]}""";
        var loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(3));
        Assert.That(loaded.Combos[0].AlternativeGroups, Is.Empty);
        Assert.That(loaded.Combos[0].Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(loaded.Combos[0].Cards[0].CardId, Is.EqualTo("a"));
    }

    [Test]
    public async Task ShippedNineRouteExampleHasParityWhenFirstTwoRoutesAreCombinedOnACopy() {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json");
        await VerifyCombinedExample(path);
    }

    [Test, Explicit("Set YGO_OR_SESSION to an available supplied session; no fixture is modified.")]
    public async Task SuppliedExampleHasParity() {
        await VerifyCombinedExample(Environment.GetEnvironmentVariable("YGO_OR_SESSION")
            ?? throw new InvalidOperationException("YGO_OR_SESSION is required."));
    }

    private static async Task VerifyCombinedExample(string path) {
        var original = await Codec().LoadSessionAsync(await File.ReadAllTextAsync(path));
        var first = original.Combos[0];
        var second = original.Combos[1];
        var common = first.Categories.Where(a => second.Categories.Any(b => a.BaseCategory.Identity == b.BaseCategory.Identity)).ToList();
        var leaves = first.Categories.Concat(second.Categories).Where(c => !common.Any(x => x.BaseCategory.Identity == c.BaseCategory.Identity)).Select(ComboAlternative.For).ToArray();
        Assert.That(leaves, Has.Length.EqualTo(2));
        var merged = new Combo(common, "Combined", groupId: first.GroupId, alternativeGroups: [new(leaves)]);
        var changed = new[] { merged }.Concat(original.Combos.Skip(2)).ToList();
        var service = new ProbabilityCalculatorService();
        var before = service.CalculateProbabilityResults(original.Cards, original.Combos, original.HandSize, original.ComboGroups);
        var after = service.CalculateProbabilityResults(original.Cards, changed, original.HandSize, original.ComboGroups);
        Assert.That(after.TotalProbability, Is.EqualTo(before.TotalProbability));
        Assert.That(after.GroupProbabilities!.Select(g => g.Probability), Is.EqualTo(before.GroupProbabilities!.Select(g => g.Probability)));
        Assert.That(after.ComboProbabilities, Has.Count.EqualTo(8));
        Assert.That(after.ComboProbabilities[0].Probability, Is.EqualTo(service.CalculateProbabilityForCombos(original.Cards, [first, second], original.HandSize)));
        Assert.That(before.GroupProbabilities!.First(g => g.GroupId == first.GroupId).ActiveComboCount, Is.EqualTo(6));
        Assert.That(after.GroupProbabilities!.First(g => g.GroupId == first.GroupId).ActiveComboCount, Is.EqualTo(5));
        TestContext.Out.WriteLine($"Before/after total: {before.TotalProbability:R}; combined individual: {after.ComboProbabilities[0].Probability:R}");
    }
}
