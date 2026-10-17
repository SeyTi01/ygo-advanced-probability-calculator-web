using System.Reflection;
using System.Text.Json.Nodes;

string assembly = Path.GetFullPath(args[0]);
Assembly app = Assembly.LoadFrom(assembly);
MethodInfo measure = app.GetType("YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe")!.GetMethod("Measure")!;
JsonArray corpus = JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();
JsonArray runs = [];
foreach (JsonNode fixture in corpus) {
    foreach (int mode in new[] { 0, 2 }) {
        JsonNode input = fixture["Input"]!.DeepClone();
        input["Options"] ??= new JsonObject();
        if (input["Options"]!["Mode"]?.GetValue<int>() != 1) {
            input["Options"]!["Mode"] = mode;
        }
        JsonObject request = new() { ["Input"] = input, ["MeasureCertificate"] = true };
        string output = (string)measure.Invoke(null, [request.ToJsonString()])!;
        JsonObject run = new() { ["name"] = fixture["Name"]!.GetValue<string>(), ["mode"] = mode, ["measurement"] = JsonNode.Parse(output) };
        runs.Add(run);
        Console.WriteLine($"{run["name"]}: {mode} {run["measurement"]!["Outcome"]}");
    }
}
File.WriteAllText(args[2], runs.ToJsonString());
