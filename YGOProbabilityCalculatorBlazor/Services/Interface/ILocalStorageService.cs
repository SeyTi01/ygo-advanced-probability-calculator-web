namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ILocalStorageService {
    Task<T?> GetItemAsync<T>(string key);
    Task SetItemAsync<T>(string key, T value);
}
