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
        ProbeContext context = new(Path.GetFullPath(args[0]));
        Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[0], "YGOProbabilityCalculatorBlazor.dll")));
        MethodInfo probe = assembly.GetType("YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe")!.GetMethod("Measure")!;
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[1]))!;
        Random random = new(170224);
        List<object> output = [];
        bool baseline = args.Length > 3 && args[3] == "baseline";
        string? cold = args.Length > 3 && args[3].StartsWith("cold-") ? args[3][5..] : null;
        int trials = baseline || cold is not null ? 1 : 12;
        string[] sides = cold is not null ? [cold] : baseline ? ["fast", "exact", "primary"] : ["fast", "low", "high", "exact", "primary"];
        foreach (JsonElement fixture in fixtures) {
            Dictionary<string, List<JsonElement>> samples = sides.ToDictionary(side => side, _ => new List<JsonElement>());
            Dictionary<string, JsonElement> first = [];
            for (int trial = 0; trial < trials; trial++) {
                foreach (string side in sides.OrderBy(_ => random.Next())) {
                    JsonNode input = JsonNode.Parse(fixture.GetProperty("Input").GetRawText())!;
                    if (side == "exact" && input["Options"]!["Mode"]!.GetValue<int>() != 1) { input["Options"]!["Mode"] = 2; }
                    JsonObject request = new() { ["Input"] = input, ["MeasureCertificate"] = side != "primary", ["RefinementBudget"] = side == "low" ? 100000 : side == "high" ? 1000000 : 0 };
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    string text = (string)probe.Invoke(null, [request.ToJsonString()])!;
                    JsonElement measured = JsonDocument.Parse(text).RootElement.Clone();
                    if (trial == 0) { first[side] = measured; }
                    if (trial >= 2 || baseline || cold is not null) { samples[side].Add(measured); }
                }
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), First = first, Samples = samples });
            File.WriteAllText(args[2], JsonSerializer.Serialize(output));
            Console.WriteLine(fixture.GetProperty("Name").GetString() + ": " + string.Join(" / ", sides.Select(side => side + "=" + samples[side][0].GetProperty("Outcome").GetString() + ":" + samples[side][0].GetProperty("Milliseconds").GetDouble().ToString("F2"))));
        }
    }
}
