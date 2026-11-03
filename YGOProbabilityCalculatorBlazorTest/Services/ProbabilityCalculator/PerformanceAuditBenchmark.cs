using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Explicit Release measurements, run alone with DOTNET_TieredCompilation=0.
// Export: YGO_AUDIT_OUTPUT=<frozen.json>; optional YGO_AUDIT_CASE=<name substring>.
// Replay: YGO_AUDIT_INPUT=<frozen.json>, YGO_AUDIT_OUTPUT=<measurements.json>,
// YGO_AUDIT_TRIALS=<1..30 warm samples, default 3>; optional YGO_AUDIT_CASE.
// Freeze the exported file before comparing revisions; replay never replaces its references.
[TestFixture, Explicit("Frozen public-wire measurements; no timing assertions"), NonParallelizable]
public sealed class PerformanceAuditBenchmark {
    [Test]
    public async Task Export() {
        using MeasurementCulture culture = new();
        string output = FrozenCalculationFixture.OutputPath("YGO_AUDIT_OUTPUT");
        string? filter = Environment.GetEnvironmentVariable("YGO_AUDIT_CASE");
        CategoryBase target = new("Target");
        List<Combo> combos = [new([new(target, 1, 2)], "Target", groupId: "routes"),
            new([new(target, 0, 0)], "No target", groupId: "routes")];
        List<ComboGroup> groups = [new("routes", "Routes"), new("empty", "Empty")];
        List<Card> ordinary = [new([target], 2, "Target", id: "target"), new([], 3, "Other", id: "other")];
        List<Card> search = [.. ordinary, new([], 1, "Search", id: "search", search: new(CardSelector.For(target), false))];
        List<Card> excavation = [.. ordinary, new([], 1, "Excavate", id: "excavate", excavation: new(2))];
        List<(string Name, CalculationSnapshot Snapshot, string? Execution)> cases = [
            ("tiny-opening", CalculationSnapshot.Capture(ordinary, combos, 2, groups, options: new(Mode: CalculationModel.OpeningHand)), null),
            ("tiny-search", CalculationSnapshot.Capture(search, combos, 2, groups), null),
            ("tiny-excavation", CalculationSnapshot.Capture(excavation, combos, 2, groups), null),
            ("tiny-exact", CalculationSnapshot.Capture(search, combos, 2, groups, options: new(Mode: CalculationModel.Exact)), "interactive-exact-v1"),
            ("tiny-work-limit", CalculationSnapshot.Capture(ordinary, combos, 2, groups, new(1)), null)
        ];
        if (filter is null || "example-opening".Contains(filter, StringComparison.OrdinalIgnoreCase)) {
            string json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
            SessionService codec = new(Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
            SessionState session = await codec.LoadSessionAsync(json);
            cases.Add(("example-opening", CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                session.ComboGroups, options: new(Mode: CalculationModel.OpeningHand)), null));
        }
        List<FrozenCalculationFixture> fixtures = [];
        foreach ((string name, CalculationSnapshot snapshot, string? execution) in cases) {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            JsonElement response = JsonSerializer.Deserialize<JsonElement>(FrozenCalculationFixture.Execute(snapshot.Json, execution));
            if (name == "tiny-work-limit") {
                Assert.That(response.GetProperty("LimitReason").GetInt32(), Is.EqualTo((int)ProbabilityCalculationLimitReason.Work));
            } else {
                Assert.That(response.GetProperty("Result").ValueKind, Is.EqualTo(JsonValueKind.Object), name);
                Assert.That(response.GetProperty("Error").ValueKind, Is.EqualTo(JsonValueKind.Null), name);
            }
            fixtures.Add(new(name, snapshot.Json, execution, response));
        }
        FrozenCalculationFixture.Validate(fixtures);
        File.WriteAllText(output, JsonSerializer.Serialize(fixtures));
    }

    [Test]
    public void Replay() {
        using MeasurementCulture culture = new();
        string input = FrozenCalculationFixture.RequiredPath("YGO_AUDIT_INPUT");
        string output = FrozenCalculationFixture.OutputPath("YGO_AUDIT_OUTPUT");
        string? requested = Environment.GetEnvironmentVariable("YGO_AUDIT_TRIALS");
        if (!int.TryParse(requested ?? "3", NumberStyles.None, CultureInfo.InvariantCulture, out int trials) || trials is < 1 or > 30) {
            throw new ArgumentException("YGO_AUDIT_TRIALS must be an integer from 1 to 30.");
        }
        byte[] fixtureBytes = File.ReadAllBytes(input);
        FrozenCalculationFixture[] fixtures = JsonSerializer.Deserialize<FrozenCalculationFixture[]>(fixtureBytes)
            ?? throw new ArgumentException("The frozen fixture file must contain an array.");
        FrozenCalculationFixture.Validate(fixtures);
        string? filter = Environment.GetEnvironmentVariable("YGO_AUDIT_CASE");
        FrozenCalculationFixture[] selected = fixtures.Where(fixture => filter is null
            || fixture.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0) {
            throw new ArgumentException("YGO_AUDIT_CASE matched no frozen fixtures.");
        }
        List<object> measurements = [];
        string fixtureHash = Convert.ToHexStringLower(SHA256.HashData(fixtureBytes));
        string assemblyHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CalculationWire).Assembly.Location)));
        Save();
        foreach (FrozenCalculationFixture fixture in selected) {
            List<object> samples = [];
            measurements.Add(new {
                fixture.Name, fixture.Execution,
                JsonSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Json))),
                Samples = samples, fixture.ReferenceResponse
            });
            for (int trial = 0; trial <= trials; trial++) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                string reply = FrozenCalculationFixture.Execute(fixture.Json, fixture.Execution);
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                JsonElement response = JsonSerializer.Deserialize<JsonElement>(reply);
                bool parity = JsonElement.DeepEquals(response, fixture.ReferenceResponse);
                samples.Add(new {
                    Phase = trial == 0 ? "first" : "warm", Trial = trial, Milliseconds = milliseconds,
                    AllocatedBytes = allocated, Response = response, Parity = parity
                });
                Save();
                Assert.That(parity, Is.True, fixture.Name);
                if (response.GetProperty("Result").ValueKind == JsonValueKind.Null) {
                    break;
                }
            }
            TestContext.Out.WriteLine(fixture.Name);
        }

        void Save() => File.WriteAllText(output, JsonSerializer.Serialize(new {
            SchemaVersion = 1, Runtime = RuntimeInformation.FrameworkDescription,
            FixtureSha256 = fixtureHash, AssemblySha256 = assemblyHash, Measurements = measurements
        }));
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FrozenCalculationFixture(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Json,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Execution,
    [property: JsonRequired] JsonElement ReferenceResponse
) {
    internal static string Execute(string json, string? execution) => execution switch {
        null => CalculationWire.Execute(json),
        "interactive-exact-v1" => CalculationWire.ExecuteInteractiveExact(json),
        _ => throw new ArgumentException("Unknown fixture execution entry point.")
    };

    internal static void Validate(IReadOnlyCollection<FrozenCalculationFixture> fixtures) {
        if (fixtures.Count == 0) {
            throw new ArgumentException("At least one frozen fixture is required.");
        }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (FrozenCalculationFixture fixture in fixtures) {
            if (fixture is null || string.IsNullOrWhiteSpace(fixture.Name) || !names.Add(fixture.Name)
                || string.IsNullOrWhiteSpace(fixture.Json)) {
                throw new ArgumentException("Frozen fixtures need unique nonempty names and exact input Json strings.");
            }
            CalculationInput input = JsonSerializer.Deserialize(fixture.Json, CalculationJsonContext.Default.CalculationInput)
                ?? throw new ArgumentException("Fixture Json must contain a calculation input.");
            if (fixture.Execution is not null && (fixture.Execution != "interactive-exact-v1" || input.Options?.Mode != CalculationModel.Exact)) {
                throw new ArgumentException("interactive-exact-v1 requires an Exact input.");
            }
            if (fixture.ReferenceResponse.ValueKind != JsonValueKind.Object
                || !new[] { "Result", "Error", "LimitReason", "FailureKind", "Contract" }.All(property => fixture.ReferenceResponse.TryGetProperty(property, out _))) {
                throw new ArgumentException("Every fixture requires a complete frozen production ReferenceResponse object.");
            }
        }
    }

    internal static string RequiredPath(string variable) {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Set {variable} to a file path.")
            : Path.GetFullPath(value);
    }

    internal static string OutputPath(string variable) {
        string output = RequiredPath(variable);
        string? input = Environment.GetEnvironmentVariable("YGO_AUDIT_INPUT");
        if (!string.IsNullOrWhiteSpace(input) && string.Equals(output, Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Measurement output must not overwrite frozen input.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        return output;
    }
}

internal sealed class MeasurementCulture : IDisposable {
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    internal MeasurementCulture() {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose() {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
