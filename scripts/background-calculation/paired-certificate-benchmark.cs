// Temporary net10.0 console project, no packages. Args: baseline test-bin,
// candidate test-bin, SearchCertificateBenchmark export, output.json.
// DOTNET_TieredCompilation=0; randomized interleaving, 5 warmups + 15 samples.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

sealed class Isolated(string directory) : AssemblyLoadContext {
    protected override Assembly? Load(AssemblyName name) {
        string file = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
    }
}

static class Program {
    static void Main(string[] args) {
        MethodInfo[] probes = args.Take(2).Select(path => {
            Isolated context = new(path);
            Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(path, "YGOProbabilityCalculatorBlazorTest.dll"));
            return assembly.GetType("YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.SearchPerformanceProbe")!.GetMethod("Prepare")!;
        }).ToArray();
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[2]))!;
        Random random = new(205206);
        List<object> output = [];
        foreach (JsonElement fixture in fixtures) {
            string input = fixture.GetProperty("Input").GetRawText();
            JsonNode certified = JsonNode.Parse(input)!;
            certified["Options"] = new JsonObject { ["CertifySearchAvailability"] = true };
            Func<string>[] runs = [
                (Func<string>)probes[0].Invoke(null, [input])!,
                (Func<string>)probes[1].Invoke(null, [input])!,
                (Func<string>)probes[1].Invoke(null, [certified.ToJsonString()])!
            ];
            List<JsonElement>[] samples = [[], [], []];
            for (int trial = 0; trial < 20; trial++) {
                foreach (int side in Enumerable.Range(0, 3).OrderBy(_ => random.Next())) {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    JsonElement measured = JsonDocument.Parse(runs[side]()).RootElement.Clone();
                    if (trial >= 5) {
                        samples[side].Add(measured);
                    }
                }
            }
            foreach (string field in new[] { "Outcome", "Total", "Combos", "Groups", "Work", "Entries", "Cells" }) {
                string expected = samples[0][0].GetProperty(field).GetRawText();
                foreach (JsonElement sample in samples[1]) {
                    if (sample.GetProperty(field).GetRawText() != expected) {
                        throw new InvalidOperationException($"Disabled parity: {fixture.GetProperty("Name")}/{field}");
                    }
                }
                if (field is "Outcome" or "Total" or "Combos" or "Groups") {
                    foreach (JsonElement sample in samples[2]) {
                        if (sample.GetProperty(field).GetRawText() != expected) {
                            throw new InvalidOperationException($"Enabled primary parity: {fixture.GetProperty("Name")}/{field}");
                        }
                    }
                }
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), Samples = samples });
            File.WriteAllText(args[3], JsonSerializer.Serialize(output));
            Console.WriteLine($"{fixture.GetProperty("Name")}: " + string.Join(" / ", samples.Select(side =>
                side.Select(sample => sample.GetProperty("Milliseconds").GetDouble()).Order().ElementAt(7).ToString("F3"))));
        }
    }
}
