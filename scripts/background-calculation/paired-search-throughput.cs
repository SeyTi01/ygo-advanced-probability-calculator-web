// net10.0 console; args: base test-bin, candidate test-bin, fixtures.json, output.json.
// Both builds externally include search-throughput-benchmark-probe.cs with ADAPTIVE_FAST.
// Run with DOTNET_TieredCompilation=0, alone. Both sides measure the complete request.
// Two warmups and ten interleaved samples; first observed call retained separately.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

sealed class ProbeContext(string directory) : AssemblyLoadContext {
    protected override Assembly? Load(AssemblyName name) {
        string file = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
    }
}

static class Program {
    static void Main(string[] args) {
        MethodInfo[] probes = args.Take(2).Select(directory => {
            ProbeContext context = new(directory);
            Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "YGOProbabilityCalculatorBlazor.dll"));
            return assembly.GetType("YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe")!.GetMethod("Measure")!;
        }).ToArray();
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[2]))!;
        Random random = new(210211);
        List<object> output = [];
        foreach (JsonElement fixture in fixtures) {
            const int sides = 2;
            JsonElement[] first = new JsonElement[sides];
            List<JsonElement>[] samples = Enumerable.Range(0, sides).Select(_ => new List<JsonElement>()).ToArray();
            for (int trial = 0; trial < (args.Length > 4 ? int.Parse(args[4]) : 12); trial++) {
                foreach (int side in Enumerable.Range(0, sides).OrderBy(_ => random.Next())) {
                    JsonNode input = JsonNode.Parse(fixture.GetProperty("Input").GetRawText())!;
                    bool certify = !fixture.TryGetProperty("Primary", out JsonElement primary) || !primary.GetBoolean();
                    JsonObject request = new() { ["Input"] = input, ["MeasureCertificate"] = certify, ["Diagnose"] = Environment.GetEnvironmentVariable("YGO_DIAG") == "1" };
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    int[] collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    string text = (string)probes[side].Invoke(null, [request.ToJsonString()])!;
                    JsonNode measurement = JsonNode.Parse(text)!;
                    measurement["Collections"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 3)
                        .Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray());
                    if (certify && measurement["Outcome"]!.GetValue<string>() == "Success") {
                        int requested = input["Options"]?["Mode"]?.GetValue<int>() ?? 0;
                        if (measurement["Result"]!["Contract"]!["RequestedMode"]!.GetValue<int>() != requested
                            || (requested == 2 && (measurement["Result"]!["SearchAvailability"] is not null
                                || measurement["Result"]!["Contract"]!["Accuracy"]!.GetValue<int>() != 0))) {
                            throw new InvalidOperationException("Benchmark mode/accuracy mismatch.");
                        }
                    }
                    text = measurement.ToJsonString();
                    if (trial == 0) { first[side] = JsonDocument.Parse(text).RootElement.Clone(); }
                    if (trial >= (args.Length > 4 ? 0 : 2)) { samples[side].Add(JsonDocument.Parse(text).RootElement.Clone()); }
                }
            }
            for (int side = 0; side < sides; side++) {
                foreach (string field in new[] { "Total", "Combos", "Groups", "Route", "Outcome", "Work", "Entries", "Cells" }) {
                    string expected = samples[side][0].GetProperty(field).GetRawText();
                    if (samples[side].Any(value => value.GetProperty(field).GetRawText() != expected)) {
                        throw new InvalidOperationException($"{fixture.GetProperty("Name")}/{field}: nondeterministic result");
                    }
                }
            }

            bool unchanged = fixture.TryGetProperty("Unchanged", out JsonElement control) && control.GetBoolean();
            foreach (int side in new[] { 0 }) {
                bool completed = samples[side][0].GetProperty("Outcome").GetString() == "Success";
                foreach (string field in unchanged ? new[] { "Total", "Combos", "Groups", "Route", "Outcome", "Work", "Entries", "Cells", "Result" }
                    : completed ? new[] { "Total", "Combos", "Groups", "Route", "Outcome", "Result" } : Array.Empty<string>()) {
                    if (samples[side][0].GetProperty(field).GetRawText() != samples[side + 1][0].GetProperty(field).GetRawText()) {
                        throw new InvalidOperationException($"{fixture.GetProperty("Name")}/{field}: successful baseline changed");
                    }
                }
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), First = first, Samples = samples });
            File.WriteAllText(args[3], JsonSerializer.Serialize(output));
            Console.WriteLine($"{fixture.GetProperty("Name")}: " + string.Join(" / ", samples.Select(side =>
                side.Select(value => value.GetProperty("Milliseconds").GetDouble()).Order().ElementAt(samples[0].Count / 2).ToString("F3"))));
        }
    }
}
