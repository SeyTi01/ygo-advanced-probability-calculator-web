// net10.0 console; args: base test-bin, candidate test-bin, fixtures.json, output.json.
// Both builds externally include fast-certificate-benchmark-probe.cs; define
// ADAPTIVE_FAST only for candidate. DOTNET_TieredCompilation=0. Three arms:
// frozen full Fast, frozen direct Exact (Physical fixtures), adaptive Fast.
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
    static string Field(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement field) ? field.GetRawText() : "null";

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
            int[] sides = fixture.GetProperty("Physical").GetBoolean() ? [0, 1, 2] : [0, 2];
            JsonElement?[] first = new JsonElement?[3];
            List<JsonElement>[] samples = Enumerable.Range(0, 3).Select(_ => new List<JsonElement>()).ToArray();
            for (int trial = 0; trial < 25; trial++) {
                foreach (int side in sides.OrderBy(_ => random.Next())) {
                    JsonNode input = JsonNode.Parse(fixture.GetProperty("Input").GetRawText())!;
                    JsonObject request = new() { ["Input"] = input, ["MeasureCertificate"] = true };
                    if (side == 1) { input["Options"]!["Mode"] = 2; }
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    string text = (string)probes[side == 2 ? 1 : 0].Invoke(null, [request.ToJsonString()])!;
                    JsonElement measured = JsonDocument.Parse(text).RootElement.Clone();
                    if (trial == 0) { first[side] = measured; }
                    if (trial >= 5) { samples[side].Add(measured); }
                }
            }
            bool selected = Field(samples[2][0], "Route") != Field(samples[0][0], "Route");
            JsonElement expected = samples[selected ? 1 : 0][0];
            foreach (JsonElement sample in samples[2]) {
                foreach (string field in new[] { "Total", "Combos", "Groups", "Route", "Outcome", "Work", "Entries", "Cells" }) {
                    if (Field(sample, field) != Field(expected, field)) {
                        throw new InvalidOperationException($"{fixture.GetProperty("Name")}/{field} parity failed");
                    }
                }
                if (!selected && Field(sample, "Result") != Field(expected, "Result")) {
                    throw new InvalidOperationException($"{fixture.GetProperty("Name")}/complete result parity failed");
                }
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), Selected = selected, First = first, Samples = samples });
            File.WriteAllText(args[3], JsonSerializer.Serialize(output));
            Console.WriteLine($"{fixture.GetProperty("Name")}: " + string.Join(" / ", samples.Select(side =>
                side.Count > 0 ? side.Select(value => value.GetProperty("Milliseconds").GetDouble()).Order().ElementAt(10).ToString("F3") : "-")));
        }
    }
}
