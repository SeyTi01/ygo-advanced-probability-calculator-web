using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class LocalStorageService(IJSRuntime jsRuntime, ISerializer serializer) : ILocalStorageService {
    public async Task<T?> GetItemAsync<T>(string key) {
        var json = await jsRuntime.InvokeAsync<string>("localStorage.getItem", key);
        return serializer.Deserialize<T>(json);
    }

    public async Task SetItemAsync<T>(string key, T value) {
        var json = serializer.Serialize(value);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
    }
}
