using System.Text.Json;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ISerializer {
    string Serialize<T>(T value, JsonSerializerOptions? options = null);
    T? Deserialize<T>(string json, JsonSerializerOptions? options = null);
}
