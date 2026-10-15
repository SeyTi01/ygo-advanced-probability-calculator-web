// Opt-in paired Release comparison; compile this file in a temporary net10.0 console project
// with ImplicitUsings enabled (no packages). Build each test assembly with SearchPerformanceProbe.cs.
// Args: <dev-test-bin> <original-PR-test-bin> <candidate-test-bin> <SearchFeatureBenchmark.json> <output.json>
// Run with DOTNET_TieredCompilation=0. Eight warmups per side, then 24 interleaved samples;
// rotate execution order and collect outside the timed region. Do not run concurrent workloads.
// Outputs exact fractions, all rows, work/cell counters, allocation, and every timing sample.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
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
        JsonElement cases = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;
        List<object> output = [];
        foreach (JsonElement fixture in cases.EnumerateArray()) {
            string name = fixture.GetProperty("Name").GetString()!;
            if (name.StartsWith("search")) { continue; }
            Func<string>[] run = probes.Select(probe => (Func<string>)probe.Invoke(null, [fixture.GetProperty("Input").GetRawText()])!).ToArray();
            for (int warmup = 0; warmup < 8; warmup++) {
                for (int offset = 0; offset < 3; offset++) { run[(warmup + offset) % 3](); }
            }
            List<JsonElement>[] samples = [[], [], []];
            for (int trial = 0; trial < 24; trial++) {
                for (int offset = 0; offset < 3; offset++) {
                    int side = (trial + offset) % 3;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    samples[side].Add(JsonDocument.Parse(run[side]()).RootElement.Clone());
                }
            }
            output.Add(new { Name = name, Baseline = samples[0], OriginalPr = samples[1], Candidate = samples[2] });
            Console.WriteLine(name + ": " + string.Join(" / ", samples.Select(sample => sample.Select(item => item.GetProperty("Milliseconds").GetDouble())
                .Order().ElementAt(12).ToString("F2"))));
            File.WriteAllText(args[4], JsonSerializer.Serialize(output));
        }
    }
}
