using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Shared;

public class FileService(HttpClient httpClient) : IFileService {
    public async Task<string[]> ReadAllLinesAsync(IBrowserFile file) {
        using StreamReader streamReader = new(file.OpenReadStream());
        string content = await streamReader.ReadToEndAsync();

        return content.Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    public Task<string> ReadAllTextAsync(string path) => httpClient.GetStringAsync(path);
}
