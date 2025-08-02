namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IFileService {
    Task WriteAllTextAsync(string path, string content);
    Task<string> ReadAllTextAsync(string path);
    Task<string[]> ReadAllLinesAsync(string path);
}
