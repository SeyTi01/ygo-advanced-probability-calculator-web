using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Shared;

public class FileService(HttpClient httpClient) : IFileService {
    public async Task<string> ReadAllTextAsync(string path) {
        return await httpClient.GetStringAsync(path);
    }

    public Task WriteAllTextAsync(string path, string content) {
        throw new NotSupportedException("Writing files is not supported in Blazor WebAssembly");
    }

    public async Task<string[]> ReadAllLinesAsync(string path) {
        var content = await httpClient.GetStringAsync(path);
        return content.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}
