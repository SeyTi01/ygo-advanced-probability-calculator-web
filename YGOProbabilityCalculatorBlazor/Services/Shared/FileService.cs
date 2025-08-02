using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Shared;

public class FileService : IFileService {
    public Task WriteAllTextAsync(string path, string content) =>
        File.WriteAllTextAsync(path, content);

    public Task<string> ReadAllTextAsync(string path) =>
        File.ReadAllTextAsync(path);

    public Task<string[]> ReadAllLinesAsync(string path) =>
        File.ReadAllLinesAsync(path);
}
