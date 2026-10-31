namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ILocalStorageService {
    Task<string?> GetRawItemAsync(string key);

    Task SetItemAsync<T>(string key, T value);
}
