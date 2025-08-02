namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ISerializer {
    string Serialize<T>(T value);
    T? Deserialize<T>(string json);
}
