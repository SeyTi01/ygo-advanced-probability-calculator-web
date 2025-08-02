using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Shared;

public class JsonSerializer : ISerializer {
    private readonly JsonSerializerOptions _defaultOptions = new() { WriteIndented = true };

    public string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        System.Text.Json.JsonSerializer.Serialize(value, options ?? _defaultOptions);

    public T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(json, options ?? _defaultOptions);
}
