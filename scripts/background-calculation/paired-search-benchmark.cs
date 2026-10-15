// Compile in a temporary net10.0 console project with ImplicitUsings (no packages).
// Args: <dev-test-bin> <original-PR-test-bin> <candidate-test-bin>
//       <baseline-export.json> <candidate-export.json> <output.json> [warmups=8] [samples=24]
// Exports come from SearchFeatureBenchmark, using each revision's example fixture.
// DOTNET_TieredCompilation=0; run alone. Exact comparisons happen outside timing.
// YGO_SEARCH_SUBSETS=1 measures all controlled source subsets on the current fixture
// (original PR versus candidate); otherwise includes the historical ROTA fixture
// as the first arm and frozen no-search controls on all three revisions.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

class Isolated(string directory) : AssemblyLoadContext {
    protected override Assembly Load(AssemblyName name) {
        string file = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
    }
}

class Program {
    static void Main(string[] args) {
        MethodInfo[] probes = args.Take(3).Select(path => {
            Isolated context = new(path);
            Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(path, "YGOProbabilityCalculatorBlazorTest.dll"));
            return assembly.GetType("YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.SearchPerformanceProbe")!.GetMethod("Prepare")!;
        }).ToArray();
        Dictionary<string, JsonElement> baseline = Read(args[3]).ToDictionary(fixture => fixture.GetProperty("Name").GetString()!);
        JsonElement[] fixtures = Read(args[4]);
        int warmups = args.Length > 6 ? int.Parse(args[6]) : 8;
        int trials = args.Length > 7 ? int.Parse(args[7]) : 24;
        bool subsets = Environment.GetEnvironmentVariable("YGO_SEARCH_SUBSETS") == "1";
        List<object> output = [];
        Random random = new(2042026);
        foreach (JsonElement fixture in fixtures) {
            string name = fixture.GetProperty("Name").GetString()!;
            if (subsets && !name.StartsWith("search-example")) {
                continue;
            }
            foreach (int mask in subsets ? Enumerable.Range(0, 8) : [-1]) {
                string current = fixture.GetProperty("Input").GetRawText();
                if (mask >= 0) {
                    JsonNode input = JsonNode.Parse(current)!;
                    int source = 0;
                    foreach (JsonNode card in input["Cards"]!.AsArray()) {
                        if (card["Search"] is not null && (mask & (1 << source++)) == 0) {
                            card["Search"] = null;
                        }
                    }
                    current = input.ToJsonString();
                }
                int[] sides = subsets ? [1, 2] : [0, 1, 2];
                Func<string>[] run = sides.Select(side => (Func<string>)probes[side].Invoke(null,
                    [side == 0 ? baseline[name].GetProperty("Input").GetRawText() : current])!).ToArray();
                for (int warmup = 0; warmup < warmups; warmup++) {
                    foreach (int side in Enumerable.Range(0, run.Length).OrderBy(_ => random.Next())) {
                        run[side]();
                    }
                }
                List<JsonElement>[] samples = sides.Select(_ => new List<JsonElement>()).ToArray();
                for (int trial = 0; trial < trials; trial++) {
                    foreach (int side in Enumerable.Range(0, run.Length).OrderBy(_ => random.Next())) {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        samples[side].Add(JsonDocument.Parse(run[side]()).RootElement.Clone());
                    }
                }
                // Every result row must match exactly. Work/storage equality is
                // required for no-search controls; old proxy fixtures intentionally differ.
                foreach (string field in new[] { "Outcome", "Total", "Combos", "Groups" }
                    .Concat(!name.StartsWith("search") || mask == 0 ? ["Work", "Entries", "Cells"] : Array.Empty<string>())) {
                    string expected = samples[^2][0].GetProperty(field).GetRawText();
                    foreach (JsonElement sample in samples[^1]) {
                        if (sample.GetProperty(field).GetRawText() != expected) {
                            throw new InvalidOperationException($"{name}/{mask}: {field} mismatch");
                        }
                    }
                    if (!subsets && !name.StartsWith("search") && samples[0].Any(sample => sample.GetProperty(field).GetRawText() != expected)) {
                        throw new InvalidOperationException($"{name}: dev {field} mismatch");
                    }
                }
                output.Add(new { Name = name, Mask = mask, Sides = sides, Samples = samples });
                Console.WriteLine($"{name}/{mask}: " + string.Join(" / ", samples.Select(sample => sample.Select(item => item.GetProperty("Milliseconds").GetDouble())
                    .Order().ElementAt(trials / 2).ToString("F2"))));
                File.WriteAllText(args[5], JsonSerializer.Serialize(output));
            }
        }
    }

    static JsonElement[] Read(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
}
