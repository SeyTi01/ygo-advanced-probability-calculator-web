using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Shared;

public class FileService(HttpClient httpClient) : IFileService {
    public async Task<string[]> ReadAllLinesAsync(IBrowserFile file) {
        var content = await ReadAllTextAsync(file);
        return content.Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    public async Task<string> ReadAllTextAsync(string path) {
        return await httpClient.GetStringAsync(path);
    }

    private static async Task<string> ReadAllTextAsync(IBrowserFile file) {
        using var streamReader = new StreamReader(file.OpenReadStream());
        return await streamReader.ReadToEndAsync();
    }
}
