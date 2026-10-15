// net10.0 console project, no packages. Args: baseline app bin, candidate app bin,
// fixtures.json, output.json. Both apps include mode-benchmark-probe.cs only for
// measurement. Run with DOTNET_TieredCompilation=0, without concurrent builds/tests.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

sealed class ModeContext(string directory) : AssemblyLoadContext {
    protected override Assembly? Load(AssemblyName name) {
        string file = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
    }
}

static class Program {
    static void Main(string[] args) {
        MethodInfo[] probes = args.Take(2).Select(directory => {
            ModeContext context = new(directory);
            Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "YGOProbabilityCalculatorBlazor.dll"));
            return assembly.GetType("YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.ModeBenchmarkProbe")!.GetMethod("Measure")!;
        }).ToArray();
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[2]))!;
        Random random = new(207208);
        List<object> output = [];
        foreach (JsonElement fixture in fixtures[0].TryGetProperty("Unchanged", out _) ? fixtures : fixtures.Take(6)) {
            List<JsonElement>[] samples = [[], []];
            JsonElement[] firstCalls = new JsonElement[2];
            for (int trial = 0; trial < 25; trial++) {
                foreach (int side in Enumerable.Range(0, 2).OrderBy(_ => random.Next())) {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    string input = fixture.GetProperty(side == 0 ? "BaselineInput" : "Input").GetRawText();
                    string measured = (string)probes[side].Invoke(null, [input])!;
                    if (trial == 0) {
                        firstCalls[side] = JsonDocument.Parse(measured).RootElement.Clone();
                    }
                    if (trial >= 5) {
                        samples[side].Add(JsonDocument.Parse(measured).RootElement.Clone());
                    }
                }
            }
            bool unchanged = !fixture.TryGetProperty("Unchanged", out JsonElement flag) || flag.GetBoolean();
            foreach (string field in unchanged ? (fixture.TryGetProperty("Unchanged", out _)
                ? new[] { "Total", "Combos", "Groups", "Entries", "Cells", "Outcome", "Work" }
                : new[] { "Total", "Combos", "Groups", "Entries", "Cells" }) : Array.Empty<string>()) {
                string expected = samples[0][0].GetProperty(field).GetRawText();
                foreach (JsonElement value in samples.SelectMany(side => side)) {
                    if (value.GetProperty(field).GetRawText() != expected) {
                        throw new InvalidOperationException($"{fixture.GetProperty("Name")}/{field} parity");
                    }
                }
            }
            foreach (List<JsonElement> side in samples) {
                foreach (string field in new[] { "Total", "Combos", "Groups", "Work", "Entries", "Cells", "Outcome" }) {
                    if (side.Any(sample => sample.GetProperty(field).GetRawText() != side[0].GetProperty(field).GetRawText())) {
                        throw new InvalidOperationException($"{fixture.GetProperty("Name")}/{field} repeatability");
                    }
                }
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), Samples = samples, FirstCalls = firstCalls });
            File.WriteAllText(args[3], JsonSerializer.Serialize(output));
            Console.WriteLine(fixture.GetProperty("Name").GetString());
        }
    }
}
