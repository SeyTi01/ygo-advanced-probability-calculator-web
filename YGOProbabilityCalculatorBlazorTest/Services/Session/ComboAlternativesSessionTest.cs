using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using static YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.ComboAlternativesTest;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class ComboAlternativesSessionTest
{
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new JsonSerializer());

    [Test]
    public async Task FileRecoveryAndShareCodecRoundTripOrderedMixedAlternatives()
    {
        CategoryBase metadata = new("Fire", CategorySource.Metadata, "attribute:fire");
        Combo combo = Or(Direct("a"), Cat(metadata, 0, 0, RequirementMaximumMode.Fixed), Cat(new("Fire"), 2))
            .WithName("Mixed").WithActive(false).WithGroup("g");
        SessionState session = new()
        {
            Cards = [new([metadata], id: "a", manualMetadataCategoryKeys: ["attribute:fire"])],
            Combos = [combo], ComboGroups = [new("g", "Group")], HandSize = 3
        };
        string json = Codec().SerializeSession(session);
        Assert.That(JsonNode.Parse(json)!["SchemaVersion"]!.GetValue<int>(), Is.EqualTo(3));
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
        Assert.That(link, Does.Contain("/#ygo-session=v1."));
        SessionState shared = await Codec().LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
        Assert.That(Codec().SerializeSession(shared), Is.EqualTo(json));
        Assert.That(shared.Combos[0].AlternativeGroups[0].Alternatives.Select(a => a.Kind),
            Is.EqualTo(new[] { "Card", "Category", "Category" }));
    }

    [Test]
    public async Task Schema3SingletonGroupsRemainStructuredWhenLoaded()
    {
        ComboCategory categoryLeaf = new(new("Fire"), 1, 0, RequirementMaximumMode.HandSize);
        ComboCard cardLeaf = new("a", 0, 0);
        SessionState session = new()
        {
            SchemaVersion = 3, Combos =
            [
                new([],
                    cards: [],
                    alternativeGroups:
                    [
                        new([ComboAlternative.For(categoryLeaf)]), new([ComboAlternative.For(cardLeaf)])
                    ])
            ]
        };
        SessionService codec = Codec();

        string json = codec.SerializeSession(session);
        SessionState loaded = await codec.LoadSessionAsync(json);

        Assert.That(loaded.SchemaVersion, Is.EqualTo(3));
        Assert.That(loaded.Combos[0].AlternativeGroups, Has.Count.EqualTo(2));
        Assert.That(loaded.Combos[0].AlternativeGroups.Select(group => group.Alternatives.Count),
            Is.EqualTo(new[] { 1, 1 }));
        Assert.That(loaded.Combos[0].AlternativeGroups[0].Alternatives[0].Category!.MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(loaded.Combos[0].AlternativeGroups[0].Alternatives[0].Category!.MaxCount, Is.Zero);
        Assert.That(loaded.Combos[0].AlternativeGroups[1].Alternatives[0].Card!.MaxCount, Is.Zero);
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
    }

    [TestCase("null")]
    [TestCase("[null]")]
    [TestCase("[{\"Alternatives\":[]}]")]
    [TestCase("[{\"Operator\":\"AND\",\"Alternatives\":[]}]")]
    [TestCase(
        "[{\"Alternatives\":[{\"Kind\":\"Unknown\",\"Card\":{\"CardId\":\"a\",\"MinCount\":1,\"MaxCount\":2}}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Card\":null}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Card\":{\"CardId\":\"a\",\"MinCount\":2,\"MaxCount\":1}}]}]")]
    [TestCase("[{\"Alternatives\":[{\"Kind\":\"Card\",\"Alternatives\":[]}]}]")]
    public void MalformedExpressionsFailBeforeSessionAcceptance(string groups)
    {
        string json = "{\"SchemaVersion\":3,\"Combos\":[{\"Categories\":[],\"AlternativeGroups\":" + groups + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync(json));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task OldSchemasRetainAndRequirements(int version)
    {
        string json =
            $$"""{"SchemaVersion":{{version}},"Combos":[{"Categories":[{"BaseCategory":{"Name":"A"},"MinCount":0,"MaxCount":0}],"Cards":[{"CardId":"a","MinCount":1,"MaxCount":2}]}]}""";
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(3));
        Assert.That(loaded.Combos[0].AlternativeGroups, Is.Empty);
        Assert.That(loaded.Combos[0].Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(loaded.Combos[0].Cards[0].CardId, Is.EqualTo("a"));
    }

    [Test]
    public async Task ShippedExampleOrDefinitionHasParityWithItsTwoUnderlyingRoutes()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json");
        SessionState original = await Codec().LoadSessionAsync(await File.ReadAllTextAsync(path));
        Combo combined = original.Combos.Single(combo => combo.Name == "VS Starter + (Fire OR Dark)");
        IReadOnlyList<ComboAlternative> alternatives = combined.AlternativeGroups.Single().Alternatives;
        List<Combo> routes =
        [
            .. alternatives.Select((alternative, index) => new Combo(
                combined.Categories.Concat([alternative.Category!]),
                $"Underlying route {index + 1}",
                combined.Active,
                combined.GroupId,
                combined.Cards))
        ];
        List<Combo> expanded = [.. routes, .. original.Combos.Skip(1)];
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult bundled = service.CalculateProbabilityResults(original.Cards,
            original.Combos,
            original.HandSize,
            original.ComboGroups);
        ProbabilityCalculationResult expandedResult =
            service.CalculateProbabilityResults(original.Cards, expanded, original.HandSize, original.ComboGroups);

        Assert.That(expandedResult.TotalProbability, Is.EqualTo(bundled.TotalProbability));
        Assert.That(expandedResult.GroupProbabilities!.Select(group => group.Probability),
            Is.EqualTo(bundled.GroupProbabilities!.Select(group => group.Probability)));
        Assert.That(service.CalculateProbabilityForCombos(original.Cards, routes, original.HandSize),
            Is.EqualTo(bundled.ComboProbabilities.Single(result => result.ComboName == combined.Name).Probability));
        Assert.That(bundled.ComboProbabilities, Has.Count.EqualTo(8));
        Assert.That(expandedResult.ComboProbabilities, Has.Count.EqualTo(9));
        Assert.That(bundled.GroupProbabilities!.Single(group => group.GroupId == combined.GroupId).ActiveComboCount,
            Is.EqualTo(5));
        Assert.That(expandedResult.GroupProbabilities!.Single(group => group.GroupId == combined.GroupId)
                .ActiveComboCount,
            Is.EqualTo(6));
    }

    [Test, Explicit("Set YGO_OR_SESSION to an available supplied session; no fixture is modified.")]
    public async Task SuppliedExampleHasParity()
    {
        await VerifyCombinedExample(Environment.GetEnvironmentVariable("YGO_OR_SESSION")
                                    ?? throw new InvalidOperationException("YGO_OR_SESSION is required."));
    }

    private static async Task VerifyCombinedExample(string path)
    {
        SessionState original = await Codec().LoadSessionAsync(await File.ReadAllTextAsync(path));
        Combo first = original.Combos[0];
        Combo second = original.Combos[1];
        List<ComboCategory> common =
        [
            .. first.Categories.Where(a =>
                second.Categories.Any(b => a.BaseCategory.Identity == b.BaseCategory.Identity))
        ];
        ComboAlternative[] leaves =
        [
            .. first.Categories.Concat(second.Categories)
                .Where(c => ! common.Any(x => x.BaseCategory.Identity == c.BaseCategory.Identity))
                .Select(ComboAlternative.For)
        ];
        Assert.That(leaves, Has.Length.EqualTo(2));
        Combo merged = new(common, "Combined", groupId: first.GroupId, alternativeGroups: [new(leaves)]);
        List<Combo> changed = [merged, .. original.Combos.Skip(2)];
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult before = service.CalculateProbabilityResults(original.Cards,
            original.Combos,
            original.HandSize,
            original.ComboGroups);
        ProbabilityCalculationResult after =
            service.CalculateProbabilityResults(original.Cards, changed, original.HandSize, original.ComboGroups);
        Assert.That(after.TotalProbability, Is.EqualTo(before.TotalProbability));
        Assert.That(after.GroupProbabilities!.Select(g => g.Probability),
            Is.EqualTo(before.GroupProbabilities!.Select(g => g.Probability)));
        Assert.That(after.ComboProbabilities, Has.Count.EqualTo(8));
        Assert.That(after.ComboProbabilities[0].Probability,
            Is.EqualTo(service.CalculateProbabilityForCombos(original.Cards, [first, second], original.HandSize)));
        Assert.That(before.GroupProbabilities!.First(g => g.GroupId == first.GroupId).ActiveComboCount, Is.EqualTo(6));
        Assert.That(after.GroupProbabilities!.First(g => g.GroupId == first.GroupId).ActiveComboCount, Is.EqualTo(5));
        TestContext.Out.WriteLine(
            $"Before/after total: {before.TotalProbability:R}; combined individual: {after.ComboProbabilities[0].Probability:R}");
    }
}
